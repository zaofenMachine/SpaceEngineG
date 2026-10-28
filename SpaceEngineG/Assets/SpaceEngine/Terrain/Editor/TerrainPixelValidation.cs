using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SpaceEngine.Core;
using SpaceEngine.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace SpaceEngine.Terrain.Editor
{
    /// <summary>GPU controls isolate surface sampling from mesh tessellation and vertex colors.</summary>
    public static class TerrainPixelValidation
    {
        const int Size = 512, Layer = 28;
        public static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
            "Artifacts", "V050", "TerrainPixels", "terrain-pixels.json"));

        [MenuItem("Space Engine/Validate Ground Terrain Pixels")]
        public static void ValidateGroundPixels() => ValidateGroundPixels(false);
        [MenuItem("Space Engine/Validate Ground Atmosphere Pixels")]
        public static void ValidateGroundAtmospherePixels() => ValidateGroundPixels(true);
        static void ValidateGroundPixels(bool includeAtmosphere)
        {
            var checks = new List<Check>();
            GameObject root = null; Material material = null,airMaterial=null,depthMaterial=null;
            Mesh depthMesh=null; RenderTexture target = null;
            var report = new GroundReport
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"), graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                scope = "Actual seed20261026 origin planet, CPU-generated terrain meshes, planetary double anchors, camera-relative translations,2metre clearance and near-horizon view. Isolated terrain without atmosphere/HUD. Checker residual compares visible material with the same mesh/vertex-lighting control;2mm motion and2x render scale cover material precision. This supplements the complete player flight route."
            };
            if(includeAtmosphere) report.scope="Actual seed20261026 planet terrain at2m clearance with production AtmosphereRenderer. Reads camera-depth occupancy against the same opaque terrain and measures atmosphere-only horizontal row discontinuity. Includes the5 material precision checks; player screenshots still verify full composition.";
            string directory = null;
            try
            {
                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.gameObject.layer == Layer && r.enabled) throw new InvalidOperationException("Fixture layer28 is already in use.");
                material = new Material(Shader.Find("SpaceEngine/Terrain"));
                string phase = material.HasProperty("_RenderUnitsPerMetre") ? "CameraRelativeGradients" : "OriginalDirectionNormals";
                if(includeAtmosphere) phase=material.FindPass("DepthNormalsOnly")>=0?"DepthNormalsPresent":"MissingDepthNormals";
                directory = Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Artifacts","V050",
                    includeAtmosphere?"GroundAtmospherePixels":"GroundPixels",phase));
                Directory.CreateDirectory(directory);
                root = new GameObject("Temporary actual planetary ground fixture") { hideFlags = HideFlags.HideAndDontSave };
                var cameraObject = new GameObject("Actual ground camera") { layer=Layer }; cameraObject.transform.SetParent(root.transform);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << Layer;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.nearClipPlane = .1f; camera.farClipPlane = 16000000; camera.fieldOfView = 60;
                camera.allowHDR = true; camera.allowMSAA = false;
                material.SetFloat("_AmbientFloor",.035f); material.SetFloat("_SpecularStrength",.12f);
                material.SetFloat("_EmissionStrength",0);
                var system = UniverseGenerator.GenerateSystem(20261026,0,0,0);
                var planet = system.Planets[0];
                Double3 sun = (-Orbit.Evaluate(planet,system.StarMass,0)).Normalized;
                Double3 side = Double3.Cross(sun,new Double3(0,1,0)).Normalized;
                if (side.Length < .1) side = new Double3(1,0,0);
                Double3 direction = (sun*.78 + side*.46 + new Double3(0,.40,0)).Normalized;
                for (int i=0;i<60;i++)
                {
                    Double3 candidate = (direction + new Double3(Math.Sin(i*2.39)*.35,Math.Cos(i*1.17)*.35,Math.Sin(i*1.71)*.35)).Normalized;
                    if (TerrainNoise.Height(candidate,planet) > planet.TerrainHeight*.08) { direction=candidate;break; }
                }
                Double3 from = direction * (planet.Radius + TerrainPatchMesh.SurfaceHeight(direction,planet) + 2);
                Vector3 up = direction.ToVector3();
                Vector3 tangent = Vector3.ProjectOnPlane(sun.ToVector3(),up).normalized;
                camera.transform.rotation = Quaternion.LookRotation((tangent-up*.12f).normalized,up);
                var terrainObject = new GameObject("Actual seeded terrain"); terrainObject.transform.SetParent(root.transform);
                var terrain = terrainObject.AddComponent<PlanetTerrain>();
                terrain.PatchResolution=12;terrain.MaximumLevel=20;terrain.MaximumVisiblePatches=256;
                terrain.MaximumResidentPatches=1024;terrain.BuildsPerFrame=32;terrain.BuildBudgetMilliseconds=30;
                terrain.MaterialMapResolution=1024;terrain.SunIntensity=1.5f;terrain.SunColor=Color.white;
                terrain.Configure(planet,material);
                for (int frame=0;frame<400;frame++)
                {
                    terrain.Tick(from,1,Vector3.zero,sun.ToVector3(),.1f);
                    if (terrain.IsSettled && terrain.MaxActiveLevel>=20) break;
                }
                foreach (Renderer renderer in terrain.GetComponentsInChildren<Renderer>()) renderer.gameObject.layer=Layer;
                report.planetRadiusMetres=planet.Radius;report.terrainSeed=planet.Seed;
                report.cameraFromPlanet=from.ToString();report.maximumLod=terrain.MaxActiveLevel;report.triangles=terrain.TriangleCount;
                target = new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
                    { antiAliasing=1,name="Actual near-ground GPU regression" };target.Create();
                var request = new RenderPipeline.StandardRequest { destination=target };
                Capture(camera,target,request);
                Color[] ground = Capture(camera,target,request);SaveAt(ground,Path.Combine(directory,"01-ground-2m.png"));
                var block = new MaterialPropertyBlock();
                foreach (Renderer renderer in terrain.GetComponentsInChildren<Renderer>())
                { renderer.GetPropertyBlock(block);block.SetFloat("_SurfaceMapReady",0);renderer.SetPropertyBlock(block); }
                Color[] control=Capture(camera,target,request);SaveAt(control,Path.Combine(directory,"02-identical-mesh-vertex-control.png"));
                report.materialCheckerResidual=CheckerResidual(ground,control);report.controlCheckerResidual=CheckerResidual(control,control);
                checks.Add(new Check("Actual near-ground mesh reaches fine geometry",terrain.MaxActiveLevel>=20 && terrain.TriangleCount>1000,terrain.TriangleCount,1000));
                checks.Add(new Check("Near-ground radiance contains no NaN or infinity",Finite(ground),Finite(ground)?1:0,1));
                double checkerLimit=report.controlCheckerResidual*3+.002;
                checks.Add(new Check("Material avoids planetary-direction quantization noise",report.materialCheckerResidual<checkerLimit,report.materialCheckerResidual,checkerLimit));
                Double3 moved=from+Double3.FromVector3(camera.transform.right)*.002;
                terrain.Tick(moved,1,Vector3.zero,sun.ToVector3());
                Color[] translated=Capture(camera,target,request);SaveAt(translated,Path.Combine(directory,"03-camera-shifted-2mm.png"));
                report.twoMillimetreDifference=Difference(ground,translated);
                checks.Add(new Check("Two-millimetre camera translation remains visually continuous",report.twoMillimetreDifference<.015,report.twoMillimetreDifference,.015));
                terrain.Tick(from,2,Vector3.zero,sun.ToVector3());
                camera.nearClipPlane=.2f;camera.farClipPlane=32000000;
                Color[] scaled=Capture(camera,target,request);SaveAt(scaled,Path.Combine(directory,"04-render-scale-times-two.png"));
                report.renderScaleDifference=Difference(ground,scaled);
                checks.Add(new Check("Render scale retains physical material frequencies",report.renderScaleDifference<.005,report.renderScaleDifference,.005));
                if(includeAtmosphere)
                {
                    terrain.Tick(from,1,Vector3.zero,sun.ToVector3());
                    camera.nearClipPlane=.1f;camera.farClipPlane=16000000;
                    airMaterial=new Material(Shader.Find("SpaceEngine/Atmosphere"));
                    var atmosphere=cameraObject.AddComponent<AtmosphereRenderer>();
                    atmosphere.Configure(camera,airMaterial);
                    atmosphere.UpdateAtmosphere((-from).ToVector3(),(float)planet.Radius,
                        (float)(planet.Radius+planet.AtmosphereHeight),sun.ToVector3(),1.6f);
                    Capture(camera,target,request);
                    Color[] haze=Capture(camera,target,request);SaveAt(haze,Path.Combine(directory,"05-actual-ground-atmosphere.png"));
                    depthMaterial=new Material(Shader.Find("Hidden/SpaceEngine/GroundDepthProbe"));
                    depthMaterial.SetPass(0);
                    depthMesh=Chart(1);depthMesh.bounds=new Bounds(Vector3.zero,Vector3.one*32000000);
                    var probeObject=new GameObject("Actual camera depth readback") { layer=Layer };
                    probeObject.transform.SetParent(root.transform);
                    probeObject.AddComponent<MeshFilter>().sharedMesh=depthMesh;
                    probeObject.AddComponent<MeshRenderer>().sharedMaterial=depthMaterial;
                    Capture(camera,target,request);
                    Color[] depth=Capture(camera,target,request);SaveAt(depth,Path.Combine(directory,"06-scene-depth-occupancy.png"));
                    int expected=0,occupied=0;
                    for(int y=30;y<Size-30;y++)for(int x=40;x<Size-40;x++)
                    {
                        int i=y*Size+x;if(control[i].maxColorComponent<.005f)continue;
                        expected++;if(depth[i].r>.5f)occupied++;
                    }
                    report.opaqueDepthCoverageFraction=occupied/(double)Math.Max(1,expected);
                    report.atmosphereMaximumHorizontalStep=MaximumAtmosphereRowStep(haze,ground,control);
                    checks.Add(new Check("Actual terrain populates camera depth used by atmosphere",report.opaqueDepthCoverageFraction>.98,report.opaqueDepthCoverageFraction,.98));
                    checks.Add(new Check("Near-ground atmosphere has no reference-sea-level horizontal band",report.atmosphereMaximumHorizontalStep<.01,report.atmosphereMaximumHorizontalStep,.01));
                }
            }
            catch (Exception error) { report.error=error.ToString(); }
            finally
            {
                if (root!=null) Object.DestroyImmediate(root);
                if (material!=null) Object.DestroyImmediate(material);
                if (airMaterial!=null) Object.DestroyImmediate(airMaterial);
                if (depthMaterial!=null) Object.DestroyImmediate(depthMaterial);
                if (depthMesh!=null) Object.DestroyImmediate(depthMesh);
                if (target!=null) { target.Release();Object.DestroyImmediate(target); }
            }
            if (directory==null) directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Artifacts","V050","GroundPixels","Error"));
            Directory.CreateDirectory(directory);report.checks=checks.ToArray();
            report.passed=string.IsNullOrEmpty(report.error)&&checks.Count==(includeAtmosphere?7:5)&&checks.TrueForAll(c=>c.passed);
            string path=Path.Combine(directory,"ground-pixels.json");string json=JsonUtility.ToJson(report,true);File.WriteAllText(path,json);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(directory),"ground-pixels.json"),json);
            if(report.passed)Debug.Log("SPACE_ENGINE_GROUND_PIXELS PASS "+path);
            else Debug.LogError("SPACE_ENGINE_GROUND_PIXELS FAIL "+path+" "+report.error);
        }

        [MenuItem("Space Engine/Validate Terrain Pixels")]
        public static void ValidatePixels()
        {
            var checks = new List<Check>();
            GameObject root = null;
            Material material = null;
            Mesh coarse = null, dense = null;
            RenderTexture target = null;
            TerrainMaterialMap map = null;
            var report = new Report
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                scope = "512x512 floating-point URP shader renders/readbacks of an isolated flat chart. Controls compare 2 versus 8192 triangles, identical vertex colors, seed changes and metre-scale ground material. This isolates material correctness; it is not a substitute for real planet/player screenshots. Bake timing includes GPU completion/readback and fixture overhead, not gameplay frame time."
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            try
            {
                if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Terrain pixel validation needs a real compute-capable GPU.");
                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.gameObject.layer == Layer && r.enabled) throw new InvalidOperationException("Fixture layer 28 is already in use.");
                root = new GameObject("Temporary terrain material GPU fixture") { hideFlags = HideFlags.HideAndDontSave };
                var cameraObject = new GameObject("Material camera");
                cameraObject.transform.SetParent(root.transform);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.cullingMask = 1 << Layer;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.nearClipPlane = .01f; camera.farClipPlane = 100; camera.fieldOfView = 48;
                camera.allowHDR = true; camera.allowMSAA = false;
                var chart = new GameObject("Fixed geological chart") { layer = Layer };
                chart.transform.SetParent(root.transform); chart.transform.position = new Vector3(0, 0, 3);
                var filter = chart.AddComponent<MeshFilter>();
                var renderer = chart.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                material = new Material(Shader.Find("SpaceEngine/Terrain"));
                material.SetPass(0);
                if (ShaderUtil.ShaderHasError(material.shader)) throw new InvalidOperationException("Terrain shader compilation failed.");
                renderer.sharedMaterial = material;
                material.SetVector("_SunDirection", new Vector4(-.4f,.25f,-1,0));
                material.SetColor("_SunColor", Color.white); material.SetFloat("_SunIntensity", 1.3f);
                material.SetFloat("_AmbientFloor", .025f); material.SetFloat("_SpecularStrength", .05f);
                material.SetFloat("_EmissionStrength", 0); material.SetFloat("_IsLava", 0);
                material.SetFloat("_MaterialDetailStrength", .65f); material.SetFloat("_SurfaceNormalStrength", .7f);
                material.SetFloat("_OceanRoughness", .12f); material.SetFloat("_OceanSpecularStrength", .8f);
                material.SetVector("_PatchAnchor", new Vector4(0,0,-1,1));
                material.SetFloat("_RenderUnitsPerMetre",1f/6371000);
                material.SetFloat("_CameraAltitudeMetres", 1000000);
                material.SetFloat("_HasOcean", 1); material.SetFloat("_PlanetKind", (int)PlanetKind.Oceanic);
                material.SetFloat("_TerrainRadius", 6371000); material.SetFloat("_TerrainAmplitude", 12000);
                material.SetFloat("_MaterialMapResolution", 1024); material.SetFloat("_SurfaceMapReady", 1);
                coarse = Chart(1); dense = Chart(64); filter.sharedMesh = coarse;
                target = new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)
                    { antiAliasing = 1, name = "Terrain material GPU target" };
                target.Create();
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("Active pipeline lacks StandardRequest.");
                var planet = new PlanetDefinition
                {
                    Name = "Fixed chart geology", Seed = 9371, Kind = PlanetKind.Oceanic,
                    Radius = 6371000, TerrainHeight = 12000, HasOcean = true,
                    SurfaceTint = new Color(.3f,.4f,.2f)
                };
                map = new TerrainMaterialMap();
                var bakeWatch = Stopwatch.StartNew();
                if (!map.Bake(planet,1024)) throw new InvalidOperationException("GPU surface bake was unavailable.");
                var readback = AsyncGPUReadback.Request(map.Texture, 0); readback.WaitForCompletion();
                if (readback.hasError) throw new InvalidOperationException("Surface map GPU readback failed.");
                report.bakeAndReadbackMilliseconds = bakeWatch.Elapsed.TotalMilliseconds;
                report.materialMapBytes = map.EstimatedBytes;
                material.SetTexture("_SurfaceHeightMap", map.Texture);
                Capture(camera,target,request); // Renderer/URP registration warmup.
                Color[] baseline = Capture(camera,target,request);
                Save(baseline,"01-two-triangles-seed-9371.png");
                int visible = Lit(baseline);
                checks.Add(new Check("Seed height and biomes draw a visible material",visible > 30000,visible,30000));
                filter.sharedMesh = dense;
                Capture(camera,target,request);
                Color[] densePixels = Capture(camera,target,request);
                Save(densePixels,"02-8192-triangles-same-seed.png");
                double lodDifference = Difference(baseline,densePixels);
                checks.Add(new Check("Material does not inherit a 2-triangle vertex color grid",lodDifference < .003,lodDifference,.003));
                material.SetFloat("_SurfaceMapReady",0);
                Color[] vertexControl = Capture(camera,target,request);
                Save(vertexControl,"03-vertex-color-control.png");
                double materialDifference = Difference(densePixels,vertexControl);
                checks.Add(new Check("Per-pixel geological material differs from identical vertex colors",materialDifference > .025,materialDifference,.025));
                material.SetFloat("_SurfaceMapReady",1);
                planet.Seed = 9372; map.Bake(planet,1024); material.SetTexture("_SurfaceHeightMap",map.Texture);
                Color[] seedControl = Capture(camera,target,request);
                Save(seedControl,"04-seed-9372-same-geometry.png");
                double seedDifference = Difference(densePixels,seedControl);
                checks.Add(new Check("Changing geological seed changes visible coast and biomes",seedDifference > .012,seedDifference,.012));
                material.SetVector("_PatchAnchor", new Vector4(0,0,-1,1f/6371000));
                material.SetFloat("_RenderUnitsPerMetre",1);
                material.SetFloat("_CameraAltitudeMetres",2); material.SetFloat("_HasOcean",0);
                material.SetFloat("_PlanetKind", (int)PlanetKind.Desert);
                material.SetFloat("_MaterialDetailStrength",1);
                Color[] ground = Capture(camera,target,request);
                Save(ground,"05-ground-physical-material.png");
                material.SetFloat("_MaterialDetailStrength",0);
                Color[] groundControl = Capture(camera,target,request);
                Save(groundControl,"06-ground-detail-disabled-control.png");
                double groundDifference = Difference(ground,groundControl);
                checks.Add(new Check("Physical metre-scale ground detail survives without vertex color variation",groundDifference > .002,groundDifference,.002));
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                map?.Dispose();
                if (root != null) Object.DestroyImmediate(root);
                if (coarse != null) Object.DestroyImmediate(coarse);
                if (dense != null) Object.DestroyImmediate(dense);
                if (material != null) Object.DestroyImmediate(material);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
            report.checks = checks.ToArray();
            report.passed = string.IsNullOrEmpty(report.error) && checks.Count == 5 && checks.TrueForAll(c => c.passed);
            File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true));
            if (report.passed) Debug.Log("SPACE_ENGINE_TERRAIN_PIXELS PASS " + ReportPath);
            else Debug.LogError("SPACE_ENGINE_TERRAIN_PIXELS FAIL " + ReportPath + " " + report.error);
        }

        static Mesh Chart(int divisions)
        {
            int side = divisions + 1;
            var vertices = new Vector3[side*side]; var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length]; var heights = new Vector2[vertices.Length];
            for (int y=0;y<side;y++) for (int x=0;x<side;x++)
            {
                int i=y*side+x; vertices[i]=new Vector3(x*2f/divisions-1,y*2f/divisions-1,0);
                normals[i]=Vector3.back; colors[i]=new Color(.7f,.08f,.45f,0); heights[i]=new Vector2(500,0);
            }
            var triangles = new int[divisions*divisions*6]; int cursor=0;
            for (int y=0;y<divisions;y++) for (int x=0;x<divisions;x++)
            {
                int i=y*side+x;
                triangles[cursor++]=i;triangles[cursor++]=i+side+1;triangles[cursor++]=i+1;
                triangles[cursor++]=i;triangles[cursor++]=i+side;triangles[cursor++]=i+side+1;
            }
            var mesh=new Mesh { name=$"Material chart {divisions}x{divisions}" };
            mesh.vertices=vertices;mesh.normals=normals;mesh.colors=colors;mesh.uv2=heights;mesh.triangles=triangles;
            mesh.RecalculateBounds();return mesh;
        }
        static Color[] Capture(Camera camera,RenderTexture target,RenderPipeline.StandardRequest request)
        {
            RenderPipeline.SubmitRenderRequest(camera,request);
            var previous=RenderTexture.active;RenderTexture.active=target;
            var read=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
            read.ReadPixels(new Rect(0,0,Size,Size),0,0);read.Apply(false);
            Color[] pixels=read.GetPixels();Object.DestroyImmediate(read);RenderTexture.active=previous;return pixels;
        }
        static double Difference(Color[] a,Color[] b)
        {
            double sum=0;int count=0;
            for(int y=40;y<Size-40;y++) for(int x=40;x<Size-40;x++)
            {
                int i=y*Size+x;if(a[i].maxColorComponent<.002f && b[i].maxColorComponent<.002f)continue;
                sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);count+=3;
            }
            return sum/Math.Max(1,count);
        }
        static int Lit(Color[] pixels) { int n=0;foreach(Color p in pixels)if(p.maxColorComponent>.002f)n++;return n; }
        static void Save(Color[] pixels,string name)
        { SaveAt(pixels,Path.Combine(Path.GetDirectoryName(ReportPath),name)); }
        static void SaveAt(Color[] pixels,string path)
        {
            var display=new Texture2D(Size,Size,TextureFormat.RGBA32,false,false);
            var gamma=new Color[pixels.Length];for(int i=0;i<gamma.Length;i++)gamma[i]=pixels[i].gamma;
            display.SetPixels(gamma);display.Apply(false);
            File.WriteAllBytes(path,display.EncodeToPNG());
            Object.DestroyImmediate(display);
        }
        [Serializable] sealed class Report
        {
            public string utcTimestamp,unityVersion,graphicsApi,graphicsDevice,scope,error;
            public bool passed;public double bakeAndReadbackMilliseconds;public long materialMapBytes;public Check[] checks;
        }
        static bool Finite(Color[] pixels)
        { foreach(Color p in pixels) if(float.IsNaN(p.r)||float.IsNaN(p.g)||float.IsNaN(p.b)||float.IsInfinity(p.r)||float.IsInfinity(p.g)||float.IsInfinity(p.b))return false;return true; }
        static double CheckerResidual(Color[] pixels,Color[] coverage)
        {
            double sum=0;int count=0;
            for(int y=30;y<Size/2;y++)for(int x=40;x<Size-41;x++)
            {
                int i=y*Size+x;Color a=pixels[i],b=pixels[i+1],c=pixels[i+Size],d=pixels[i+Size+1];
                if(Math.Min(Math.Min(coverage[i].maxColorComponent,coverage[i+1].maxColorComponent),
                    Math.Min(coverage[i+Size].maxColorComponent,coverage[i+Size+1].maxColorComponent))<.005f)continue;
                sum+=Math.Abs(a.r+d.r-b.r-c.r)+Math.Abs(a.g+d.g-b.g-c.g)+Math.Abs(a.b+d.b-b.b-c.b);count+=3;
            }
            return sum/Math.Max(1,count);
        }
        static double MaximumAtmosphereRowStep(Color[] haze,Color[] clear,Color[] coverage)
        {
            double maximum=0,previous=0;bool previousValid=false;
            for(int y=35;y<Size-35;y++)
            {
                double sum=0;int count=0;
                for(int x=40;x<Size-40;x++)
                {
                    int i=y*Size+x;if(coverage[i].maxColorComponent<.005f)continue;
                    sum+=(haze[i].r-clear[i].r+haze[i].g-clear[i].g+haze[i].b-clear[i].b)/3;count++;
                }
                bool valid=count>(Size-80)*.98;
                if(valid&&previousValid)maximum=Math.Max(maximum,Math.Abs(sum/count-previous));
                previous=count>0?sum/count:0;previousValid=valid;
            }
            return maximum;
        }
        [Serializable] sealed class GroundReport
        {
            public string utcTimestamp,graphicsApi,graphicsDevice,scope,error,cameraFromPlanet;
            public bool passed;public int terrainSeed,maximumLod,triangles;public double planetRadiusMetres;
            public double materialCheckerResidual,controlCheckerResidual,twoMillimetreDifference,renderScaleDifference;public Check[] checks;
            public double opaqueDepthCoverageFraction,atmosphereMaximumHorizontalStep;
        }
        [Serializable] sealed class Check
        {
            public string name;public bool passed;public double measured,threshold;
            public Check(string name,bool passed,double measured,double threshold)
            {this.name=name;this.passed=passed;this.measured=measured;this.threshold=threshold;}
        }
    }
}
