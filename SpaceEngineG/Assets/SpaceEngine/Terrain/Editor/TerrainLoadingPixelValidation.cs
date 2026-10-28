using System;
using System.Collections.Generic;
using System.IO;
using SpaceEngine.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace SpaceEngine.Terrain.Editor
{
    /// <summary>Controlled evidence for camera submission versus cold procedural geometry.</summary>
    public static class TerrainLoadingPixelValidation
    {
        const int Size=384,Layer=28;
        [Serializable] public sealed class Check
        {public string name;public bool passed;public double value,limit;public Check(string n,bool p,double v,double l){name=n;passed=p;value=v;limit=l;}}
        [Serializable] public sealed class Sample
        {
            public string name,direction;public int built,active,lod,pending,settledLod;
            public double firstBuildMilliseconds,firstSelectionMilliseconds,firstCenterLuminance,firstCenterDepthCoverage,settledCenterLuminance;
        }
        [Serializable] public sealed class Report
        {
            public bool passed;public string utc,gpu,api,error;
            public string scope="Actual seed20261026 origin planet and production terrain shader, cold Configure versus first Tick at600km, fixed camera, real URP color/depth readbacks. No atmosphere/cloud/HUD/static-sphere fallback. An unsubmitted-frame empty control diagnoses caller timing; it is expected to be empty. Tests origin landing direction, cube-chart edge and polar viewpoints using the default build/time budgets. This does not replace same-frame Runtime navigation or native video validation.";
            public List<Check> checks=new List<Check>();public List<Sample> samples=new List<Sample>();
        }
        public static string ReportPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"../..","Artifacts/V050/LoadingPixels/loading-pixels.json"));
        [MenuItem("Space Engine/Validate Terrain Loading Pixels")]
        public static void Validate()
        {
            var report=new Report{utc=DateTime.UtcNow.ToString("O"),gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};
            GameObject root=null;Material material=null,depthMaterial=null;Mesh depthMesh=null;RenderTexture target=null;
            string directory=Path.GetDirectoryName(ReportPath);Directory.CreateDirectory(directory);
            try
            {
                foreach(Renderer renderer in Object.FindObjectsByType<Renderer>())
                    if(renderer.enabled&&renderer.gameObject.layer==Layer)throw new InvalidOperationException("Fixture layer28 is in use.");
                root=new GameObject("Temporary cold terrain loading validation"){hideFlags=HideFlags.HideAndDontSave};
                var cameraObject=new GameObject("Loading camera");cameraObject.transform.SetParent(root.transform);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<Layer;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.nearClipPlane=.1f;camera.farClipPlane=16000000;camera.fieldOfView=60;camera.allowHDR=true;camera.allowMSAA=false;
                camera.depthTextureMode=DepthTextureMode.Depth|DepthTextureMode.DepthNormals;
                material=new Material(Shader.Find("SpaceEngine/Terrain"));material.SetFloat("_AmbientFloor",.12f);
                material.SetFloat("_SpecularStrength",0);
                target=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){antiAliasing=1};target.Create();
                var request=new RenderPipeline.StandardRequest{destination=target};
                var system=UniverseGenerator.GenerateSystem(20261026,0,0,0);var planet=system.Planets[0];
                Double3 sun=(-Orbit.Evaluate(planet,system.StarMass,0)).Normalized;
                Double3 side=Double3.Cross(sun,new Double3(0,1,0)).Normalized;if(side.Length<.1)side=new Double3(1,0,0);
                Double3 landing=(sun*.78+side*.46+new Double3(0,.40,0)).Normalized;
                for(int i=0;i<60;i++)
                {var d=(landing+new Double3(Math.Sin(i*2.39)*.35,Math.Cos(i*1.17)*.35,Math.Sin(i*1.71)*.35)).Normalized;if(TerrainNoise.Height(d,planet)>planet.TerrainHeight*.08){landing=d;break;}}
                depthMaterial=new Material(Shader.Find("Hidden/SpaceEngine/GroundDepthProbe"));
                depthMesh=new Mesh{name="Loading actual-depth triangle"};depthMesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(3,-1,0),new Vector3(-1,3,0)};
                depthMesh.triangles=new[]{0,1,2};depthMesh.bounds=new Bounds(Vector3.zero,Vector3.one*32000000);
                var depthObject=new GameObject("Loading camera depth occupancy"){layer=Layer};depthObject.transform.SetParent(root.transform);
                depthObject.AddComponent<MeshFilter>().sharedMesh=depthMesh;var probe=depthObject.AddComponent<MeshRenderer>();probe.sharedMaterial=depthMaterial;probe.enabled=false;
                var directions=new[]{landing,new Double3(1,1,.17).Normalized,new Double3(0,1,0)};
                var names=new[]{"origin-landing","cube-edge","polar"};
                for(int test=0;test<directions.Length;test++)
                {
                    Double3 direction=directions[test];Double3 from=TerrainPatchMesh.SurfacePoint(direction,planet)+direction*600000;
                    camera.transform.rotation=Quaternion.LookRotation((-direction).ToVector3(),Math.Abs(direction.y)>.95?Vector3.forward:Vector3.up);
                    var terrainObject=new GameObject("Cold terrain "+names[test]);terrainObject.transform.SetParent(root.transform);
                    var terrain=terrainObject.AddComponent<PlanetTerrain>();terrain.MaterialMapResolution=64;terrain.SunIntensity=1.5f;terrain.Configure(planet,material);
                    Color[] empty=Read(camera,target,request);Save(empty,Path.Combine(directory,(test+1)+"-a-unsubmitted-"+names[test]+".png"));
                    double emptyMean=CenterLuminance(empty);
                    report.checks.Add(new Check(names[test]+": Configure alone is the empty timing control",emptyMean<1e-7,emptyMean,1e-7));
                    terrain.Tick(from,.05,Vector3.zero,direction.ToVector3(),1f/30);SetLayer(terrain);
                    var sample=new Sample{name=names[test],direction=direction.ToString(),built=terrain.BuiltThisFrame,active=terrain.ActivePatchCount,lod=terrain.MaxActiveLevel,
                        pending=terrain.PendingBuildCount,firstBuildMilliseconds=terrain.LastBuildMilliseconds,firstSelectionMilliseconds=terrain.LastSelectionMilliseconds};
                    Color[] first=Read(camera,target,request);sample.firstCenterLuminance=CenterLuminance(first);
                    Save(first,Path.Combine(directory,(test+1)+"-b-first-tick-"+names[test]+".png"));
                    probe.enabled=true;Color[] depth=Read(camera,target,request);probe.enabled=false;
                    sample.firstCenterDepthCoverage=CenterOccupancy(depth);Save(depth,Path.Combine(directory,(test+1)+"-c-first-depth-"+names[test]+".png"));
                    report.checks.Add(new Check(names[test]+": First submitted frame contains visible generated terrain",sample.firstCenterLuminance>.01,sample.firstCenterLuminance,.01));
                    report.checks.Add(new Check(names[test]+": First submitted center writes opaque camera depth",sample.firstCenterDepthCoverage>.98,sample.firstCenterDepthCoverage,.98));
                    report.checks.Add(new Check(names[test]+": First build obeys configured count budget",terrain.BuiltThisFrame<=terrain.BuildsPerFrame,terrain.BuiltThisFrame,terrain.BuildsPerFrame));
                    for(int frame=0;frame<350&&!terrain.IsSettled;frame++)terrain.Tick(from,.05,Vector3.zero,direction.ToVector3(),.1f);
                    SetLayer(terrain);Color[] settled=Read(camera,target,request);sample.settledLod=terrain.MaxActiveLevel;sample.settledCenterLuminance=CenterLuminance(settled);
                    Save(settled,Path.Combine(directory,(test+1)+"-d-settled-"+names[test]+".png"));
                    report.checks.Add(new Check(names[test]+": Loading converges to finer real geometry",terrain.IsSettled&&sample.settledLod>=3,sample.settledLod,3));
                    report.samples.Add(sample);Object.DestroyImmediate(terrainObject);
                }
            }
            catch(Exception e){report.error=e.ToString();}
            finally
            {
                if(root!=null)Object.DestroyImmediate(root);if(material!=null)Object.DestroyImmediate(material);if(depthMaterial!=null)Object.DestroyImmediate(depthMaterial);
                if(depthMesh!=null)Object.DestroyImmediate(depthMesh);if(target!=null){target.Release();Object.DestroyImmediate(target);}
            }
            report.passed=string.IsNullOrEmpty(report.error)&&report.checks.Count==15&&report.checks.TrueForAll(check=>check.passed);
            File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true));
            if(report.passed)Debug.Log("SPACE_ENGINE_LOADING_PIXELS PASS "+ReportPath);else Debug.LogError("SPACE_ENGINE_LOADING_PIXELS FAIL "+ReportPath+" "+report.error);
        }
        static void SetLayer(PlanetTerrain terrain){foreach(Renderer renderer in terrain.GetComponentsInChildren<Renderer>())renderer.gameObject.layer=Layer;}
        static Color[] Read(Camera camera,RenderTexture target,RenderPipeline.StandardRequest request)
        {
            RenderPipeline.SubmitRenderRequest(camera,request);var previous=RenderTexture.active;RenderTexture.active=target;
            var texture=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);texture.ReadPixels(new Rect(0,0,Size,Size),0,0);texture.Apply(false);
            Color[] pixels=texture.GetPixels();Object.DestroyImmediate(texture);RenderTexture.active=previous;return pixels;
        }
        static double CenterLuminance(Color[] pixels)
        {double sum=0;for(int y=Size/2-48;y<Size/2+48;y++)for(int x=Size/2-48;x<Size/2+48;x++){Color p=pixels[y*Size+x];sum+=p.r*.2126+p.g*.7152+p.b*.0722;}return sum/(96*96);}
        static double CenterOccupancy(Color[] pixels)
        {int count=0;for(int y=Size/2-48;y<Size/2+48;y++)for(int x=Size/2-48;x<Size/2+48;x++)if(pixels[y*Size+x].r>.5)count++;return count/(double)(96*96);}
        static void Save(Color[] pixels,string path)
        {var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true);texture.SetPixels(pixels);texture.Apply(false);File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);}
    }
}
