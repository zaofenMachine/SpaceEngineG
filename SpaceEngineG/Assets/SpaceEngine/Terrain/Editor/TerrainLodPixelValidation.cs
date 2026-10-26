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
    /// <summary>Real URP frames of one parent and its exact four-child replacement.</summary>
    public static class TerrainLodPixelValidation
    {
        const int Size=512,Layer=28;
        public static string ReportPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"../..",
            "Artifacts/V040/TerrainLodPixels/terrain-lod-pixels.json"));
        [Serializable] public sealed class Check {public string name;public bool passed;public double value,limit;
            public Check(string n,bool p,double v,double l){name=n;passed=p;value=v;limit=l;}}
        [Serializable] public sealed class Report
        {
            public bool passed;public string utc,gpu,graphicsApi,error;
            public string scope="Actual Earth-scale CPU terrain, one L14 parent and four L15 children, production surface map/material/Forward+ depth-normal pass. Fixed camera checks morph endpoints, tiny morph steps, stable hold, reversal and depth. Full continuous player descent video remains a separate acceptance.";
            public Check[] checks;public double parentChildColorDifference,fullDetailDifference,tinyStepDifference,holdDifference,returnDifference;
            public double parentChildDepthDifference,morphDepthDifference;
        }
        [MenuItem("Space Engine/Validate Terrain LOD Pixels")]
        public static void Validate()
        {
            var report=new Report{utc=DateTime.UtcNow.ToString("O"),gpu=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString()};
            var checks=new List<Check>();var meshes=new List<Mesh>();
            GameObject root=null;Material material=null,probeMaterial=null;TerrainMaterialMap map=null;RenderTexture target=null;
            string directory=Path.GetDirectoryName(ReportPath);Directory.CreateDirectory(directory);
            try
            {
                foreach(Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if(r.enabled&&r.gameObject.layer==Layer)throw new InvalidOperationException("Fixture layer28 is in use.");
                root=new GameObject("Temporary parent-child LOD GPU fixture"){hideFlags=HideFlags.HideAndDontSave};
                var cameraObject=new GameObject("LOD camera");cameraObject.transform.SetParent(root.transform);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<Layer;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.nearClipPlane=.1f;camera.farClipPlane=10000;camera.fieldOfView=60;camera.allowHDR=true;camera.allowMSAA=false;
                camera.depthTextureMode=DepthTextureMode.Depth|DepthTextureMode.DepthNormals;
                material=new Material(Shader.Find("SpaceEngine/Terrain"));material.SetFloat("_AmbientFloor",.12f);
                material.SetFloat("_SunIntensity",1.2f);material.SetFloat("_SpecularStrength",0);
                var planet=UniverseGenerator.GenerateSystem(20261026,0,0,0).Planets[1];
                var key=new TerrainPatchKey(0,14,8192,8192);
                var payloads=new TerrainPatchMesh[5];var renderers=new MeshRenderer[5];var keys=new TerrainPatchKey[5];keys[0]=key;
                payloads[0]=TerrainPatchMesh.Build(key,planet,12);
                for(int i=0;i<4;i++)
                {keys[i+1]=key.Child(i);payloads[i+1]=TerrainPatchMesh.Build(keys[i+1],planet,12);payloads[i+1].SetParent(keys[i+1],payloads[0]);}
                Double3 up=CubeSphere.CenterDirection(key),tangent=Double3.Cross(up,new Double3(0,1,0)).Normalized;
                Double3 from=payloads[0].Anchor+up*600-tangent*650;
                camera.transform.rotation=Quaternion.LookRotation((payloads[0].Anchor-from).ToVector3(),up.ToVector3());
                map=new TerrainMaterialMap();if(!map.Bake(planet,1024))throw new InvalidOperationException("Material map bake failed.");
                var block=new MaterialPropertyBlock();
                for(int i=0;i<5;i++)
                {
                    var p=payloads[i];var mesh=new Mesh{name="Actual parent morph "+keys[i]};meshes.Add(mesh);
                    mesh.vertices=p.Vertices;mesh.normals=p.Normals;mesh.colors=p.Colors;mesh.uv=p.UV;mesh.uv2=p.SurfaceData;
                    mesh.SetUVs(2,new List<Vector4>(p.ParentData));mesh.SetUVs(3,new List<Vector3>(p.ParentNormals));mesh.SetUVs(4,new List<Vector4>(p.ParentColors));
                    mesh.triangles=p.Triangles;mesh.bounds=p.MorphBounds;
                    var tile=new GameObject(mesh.name){layer=Layer};tile.transform.SetParent(root.transform);
                    tile.transform.position=(p.Anchor-from).ToVector3();tile.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=tile.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderers[i]=renderer;
                    Double3 anchor=p.Anchor/planet.Radius;
                    block.Clear();block.SetVector("_PatchAnchor",new Vector4((float)anchor.x,(float)anchor.y,(float)anchor.z,(float)(1/planet.Radius)));
                    block.SetVector("_PatchDetailAnchor",new Vector4((float)(p.Anchor.x%16384),(float)(p.Anchor.y%16384),(float)(p.Anchor.z%16384),0));
                    block.SetTexture("_SurfaceHeightMap",map.Texture);block.SetFloat("_SurfaceMapReady",1);block.SetFloat("_MaterialMapResolution",map.Resolution);
                    block.SetFloat("_TerrainRadius",(float)planet.Radius);block.SetFloat("_TerrainAmplitude",(float)planet.TerrainHeight);
                    block.SetFloat("_PlanetKind",(int)planet.Kind);block.SetFloat("_HasOcean",planet.HasOcean?1:0);block.SetColor("_SurfaceTint",planet.SurfaceTint);
                    uint seed=unchecked((uint)planet.Seed);block.SetVector("_LavaSeed",new Vector4(seed&65535,seed>>16,0,0));
                    block.SetFloat("_RenderUnitsPerMetre",1);block.SetVector("_SunDirection",(up+tangent*.4).Normalized.ToVector3());
                    block.SetFloat("_LodMorph",1);CubeSphere.Bounds(keys[i],out _,out _,out double span);
                    block.SetFloat("_TerrainSampleMetres",(float)(planet.Radius*span/12));renderer.SetPropertyBlock(block);
                    renderer.enabled=i==0;
                }
                target=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){antiAliasing=1};target.Create();
                var request=new RenderPipeline.StandardRequest{destination=target};
                Color[] Capture()=>Read(camera,target,request);
                void Children(float morph)
                {
                    renderers[0].enabled=false;
                    for(int i=1;i<5;i++)
                    {
                        renderers[i].enabled=true;renderers[i].GetPropertyBlock(block);block.SetFloat("_LodMorph",morph);
                        CubeSphere.Bounds(keys[i],out _,out _,out double span);
                        block.SetFloat("_TerrainSampleMetres",(float)(planet.Radius*span/12)*(2-morph));renderers[i].SetPropertyBlock(block);
                    }
                }
                Capture();Color[] parent=Capture();Save(parent,Path.Combine(directory,"01-parent.png"));
                Children(0);Color[] endpoint=Capture();Save(endpoint,Path.Combine(directory,"02-children-parent-endpoint.png"));
                Children(.001f);Color[] tiny=Capture();
                Children(.25f);Color[] quarter=Capture();Save(quarter,Path.Combine(directory,"03-morph-quarter.png"));
                Children(.5f);Save(Capture(),Path.Combine(directory,"04-morph-half.png"));
                Children(1);Color[] fine=Capture();Save(fine,Path.Combine(directory,"05-children-full-detail.png"));
                Color[] hold=Capture();Children(.25f);Color[] returned=Capture();
                report.parentChildColorDifference=Difference(parent,endpoint);report.fullDetailDifference=Difference(endpoint,fine);
                report.tinyStepDifference=Difference(endpoint,tiny);report.holdDifference=Difference(fine,hold);report.returnDifference=Difference(quarter,returned);
                int lit=0;bool finite=true;
                foreach(Color p in parent){if(p.maxColorComponent>.002)lit++;finite&=!float.IsNaN(p.r)&&!float.IsInfinity(p.r)&&!float.IsNaN(p.g)&&!float.IsInfinity(p.g)&&!float.IsNaN(p.b)&&!float.IsInfinity(p.b);}
                checks.Add(new Check("Fixture renders nonempty finite real terrain",finite&&lit>Size*Size*.15,lit,Size*Size*.15));
                checks.Add(new Check("Child t0 reproduces parent surface and material",report.parentChildColorDifference<.002,report.parentChildColorDifference,.002));
                double tinyLimit=report.fullDetailDifference*.02+.0002;
                checks.Add(new Check("Tiny morph step is continuous rather than full-detail replacement",report.tinyStepDifference<tinyLimit,report.tinyStepDifference,tinyLimit));
                checks.Add(new Check("Full child endpoint retains additional visible geometry and shading",report.fullDetailDifference>1e-5,report.fullDetailDifference,1e-5));
                checks.Add(new Check("Stopped representation gives identical repeated GPU frames",report.holdDifference<1e-7,report.holdDifference,1e-7));
                checks.Add(new Check("Reversing morph returns the identical representation",report.returnDifference<1e-7,report.returnDifference,1e-7));
                probeMaterial=new Material(Shader.Find("Hidden/SpaceEngine/GroundDepthProbe"));
                var probeMesh=new Mesh{name="Depth readback triangle"};meshes.Add(probeMesh);
                probeMesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(3,-1,0),new Vector3(-1,3,0)};probeMesh.triangles=new[]{0,1,2};
                probeMesh.bounds=new Bounds(Vector3.zero,Vector3.one*32000000);
                var probeObject=new GameObject("Actual morph camera-depth probe"){layer=Layer};probeObject.transform.SetParent(root.transform);
                probeObject.AddComponent<MeshFilter>().sharedMesh=probeMesh;probeObject.AddComponent<MeshRenderer>().sharedMaterial=probeMaterial;
                for(int i=0;i<5;i++)renderers[i].enabled=i==0;
                Color[] parentDepth=Capture();Children(0);Color[] childDepth=Capture();Children(1);Color[] fineDepth=Capture();
                Save(childDepth,Path.Combine(directory,"06-parent-endpoint-scene-depth.png"));Save(fineDepth,Path.Combine(directory,"07-full-detail-scene-depth.png"));
                report.parentChildDepthDifference=Difference(parentDepth,childDepth);report.morphDepthDifference=Difference(childDepth,fineDepth);
                checks.Add(new Check("Depth-normal prepass uses identical parent endpoint",report.parentChildDepthDifference<.00002,report.parentChildDepthDifference,.00002));
                checks.Add(new Check("Depth texture follows geometric morph instead of fixed child shape",report.morphDepthDifference>1e-7,report.morphDepthDifference,1e-7));
            }
            catch(Exception e){report.error=e.ToString();}
            finally
            {
                if(root!=null)Object.DestroyImmediate(root);foreach(Mesh m in meshes)Object.DestroyImmediate(m);
                if(material!=null)Object.DestroyImmediate(material);if(probeMaterial!=null)Object.DestroyImmediate(probeMaterial);
                map?.Dispose();if(target!=null){target.Release();Object.DestroyImmediate(target);}
            }
            report.checks=checks.ToArray();report.passed=string.IsNullOrEmpty(report.error)&&checks.Count==8&&checks.TrueForAll(c=>c.passed);
            File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true));
            if(report.passed)Debug.Log("SPACE_ENGINE_TERRAIN_LOD_PIXELS PASS "+ReportPath);
            else Debug.LogError("SPACE_ENGINE_TERRAIN_LOD_PIXELS FAIL "+ReportPath+" "+report.error);
        }
        static Color[] Read(Camera camera,RenderTexture target,RenderPipeline.StandardRequest request)
        {
            RenderPipeline.SubmitRenderRequest(camera,request);var previous=RenderTexture.active;RenderTexture.active=target;
            var read=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);read.ReadPixels(new Rect(0,0,Size,Size),0,0);read.Apply(false);
            Color[] pixels=read.GetPixels();Object.DestroyImmediate(read);RenderTexture.active=previous;return pixels;
        }
        static double Difference(Color[] a,Color[] b)
        {
            double sum=0;int count=0;
            for(int y=80;y<Size-80;y++)for(int x=80;x<Size-80;x++)
            {int i=y*Size+x;sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);count+=3;}
            return sum/Math.Max(1,count);
        }
        static void Save(Color[] pixels,string path)
        {
            var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true);texture.SetPixels(pixels);texture.Apply(false);
            File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);
        }
    }
}
