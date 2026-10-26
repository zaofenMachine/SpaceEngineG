using System;
using System.Collections.Generic;
using System.IO;
using SpaceEngine.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Editor
{
    /// <summary>Actual GPU glyph rasterization, separate from static cmap/compilation checks.</summary>
    public static class ChineseGlyphPixelValidation
    {
        const int Width=1024,Height=320,Layer=27,Size=40,Columns=14;
        const string FontPath="Assets/SpaceEngine/Resources/Fonts/NotoSansCJKsc-Regular.otf";
        public static string ReportPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","Artifacts","V040","ChineseGlyphPixels","glyph-pixels.json"));
        [MenuItem("Space Engine/Validate Chinese Glyph Pixels")]
        public static void ValidateGlyphs()
        {
            var report=new Report{utcTimestamp=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,
                graphicsDevice=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),
                scope="Actual 1024x320 URP float GPU render of distinct CJK glyphs from the bundled OFL font. Each requested Chinese character must have a distinct atlas rectangle and visible cell pixels. This fixture verifies glyph rasterization, not the whole HUD layout."};
            GameObject root=null;Mesh mesh=null;Material material=null;RenderTexture target=null;
            var checks=new List<Check>();var glyphs=new List<Glyph>();
            string directory=Path.GetDirectoryName(ReportPath);Directory.CreateDirectory(directory);
            try
            {
                if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)throw new InvalidOperationException("中文像素验证需要真实图形设备。");
                AssetDatabase.ImportAsset(FontPath,ImportAssetOptions.ForceUpdate);
                var importer=(TrueTypeFontImporter)AssetImporter.GetAtPath(FontPath);
                Font font=ChineseText.Font;report.fontName=font.name;
                Add(checks,"Chinese font bytes are embedded and dynamic",importer.includeFontData&&importer.fontTextureCase==FontTextureCase.Dynamic,importer.includeFontData?1:0,1);
                font.RequestCharactersInTexture(ChineseText.GlyphProbe,Size,FontStyle.Normal);
                var unique=new List<char>();foreach(char character in ChineseText.GlyphProbe)if(!unique.Contains(character))unique.Add(character);
                var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();var atlasRects=new HashSet<string>();
                for(int i=0;i<unique.Count;i++)
                {
                    char character=unique[i];
                    if(!font.GetCharacterInfo(character,out CharacterInfo info,Size,FontStyle.Normal))throw new InvalidOperationException("缺少中文字形："+character);
                    int column=i%Columns,row=i/Columns;float x=32+column*68,y=260-row*64;
                    int offset=vertices.Count;
                    vertices.Add(new Vector3(x+info.minX,y+info.minY,0));vertices.Add(new Vector3(x+info.maxX,y+info.minY,0));
                    vertices.Add(new Vector3(x+info.maxX,y+info.maxY,0));vertices.Add(new Vector3(x+info.minX,y+info.maxY,0));
                    uv.Add(info.uvBottomLeft);uv.Add(info.uvBottomRight);uv.Add(info.uvTopRight);uv.Add(info.uvTopLeft);
                    triangles.AddRange(new[]{offset,offset+2,offset+1,offset,offset+3,offset+2});
                    atlasRects.Add(info.uvBottomLeft.ToString("R")+info.uvTopRight.ToString("R"));
                    glyphs.Add(new Glyph{character=character.ToString(),x=(int)x,y=(int)y,metricArea=(info.maxX-info.minX)*(info.maxY-info.minY)});
                }
                Add(checks,"Chinese characters have distinct actual atlas glyphs",atlasRects.Count==unique.Count,atlasRects.Count,unique.Count);
                foreach(Renderer existing in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if(existing.gameObject.layer==Layer&&existing.enabled)throw new InvalidOperationException("中文验证隔离层 27 已被占用。");
                root=new GameObject("Temporary Chinese glyph GPU validation"){hideFlags=HideFlags.HideAndDontSave};
                var cameraObject=new GameObject("Chinese glyph camera");cameraObject.transform.SetParent(root.transform);
                Camera camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<Layer;
                camera.transform.position=new Vector3(Width*.5f,Height*.5f,-10);camera.orthographic=true;camera.orthographicSize=Height*.5f;
                camera.aspect=(float)Width/Height;camera.nearClipPlane=.1f;camera.farClipPlane=100;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowMSAA=false;camera.allowHDR=true;
                var textObject=new GameObject("Actual Chinese glyph geometry"){layer=Layer};textObject.transform.SetParent(root.transform);
                mesh=new Mesh{name="CJK dynamic font glyph quads"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
                material=new Material(Shader.Find("Hidden/SpaceEngine/ChineseGlyphPixel"));material.SetTexture("_MainTex",font.material.mainTexture);
                ShaderUtil.CompilePass(material,0,true);textObject.AddComponent<MeshFilter>().sharedMesh=mesh;textObject.AddComponent<MeshRenderer>().sharedMaterial=material;
                target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){antiAliasing=1};target.Create();
                var request=new RenderPipeline.StandardRequest{destination=target};
                if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new InvalidOperationException("当前管线不支持中文像素验证。");
                RenderPipeline.SubmitRenderRequest(camera,request);RenderPipeline.SubmitRenderRequest(camera,request);
                Color[] values=Readback(target);
                int visible=0,shaped=0;foreach(Glyph glyph in glyphs)
                {
                    for(int y=Math.Max(0,glyph.y-12);y<Math.Min(Height,glyph.y+44);y++)
                        for(int x=Math.Max(0,glyph.x-4);x<Math.Min(Width,glyph.x+52);x++)
                            if(values[x+y*Width].maxColorComponent>.1f)glyph.litPixels++;
                    if(glyph.litPixels>16)visible++;
                    glyph.inkFraction=glyph.litPixels/(float)Math.Max(1,glyph.metricArea);
                    if(glyph.litPixels>16&&glyph.inkFraction<.9f)shaped++;
                }
                Add(checks,"Every Chinese glyph produces visible GPU ink",visible==glyphs.Count,visible,glyphs.Count);
                Add(checks,"Chinese glyph rasterization preserves shaped ink and transparent gaps",shaped==glyphs.Count,shaped,glyphs.Count);
                Save(values,Path.Combine(directory,"chinese-glyphs.png"));report.passed=true;foreach(Check check in checks)report.passed&=check.passed;
            }
            catch(Exception exception){report.exception=exception.ToString();report.passed=false;}
            finally
            {
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            }
            report.checks=checks.ToArray();report.glyphs=glyphs.ToArray();File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true));
            if(report.passed)Debug.Log("中文字体像素验证通过："+ReportPath);else Debug.LogError("中文字体像素验证失败："+ReportPath);
        }
        static Color[] Readback(RenderTexture target)
        {
            RenderTexture previous=RenderTexture.active;var texture=new Texture2D(Width,Height,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,Width,Height),0,0);texture.Apply(false);return texture.GetPixels();}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);}
        }
        static void Save(Color[] values,string path)
        {
            var texture=new Texture2D(Width,Height,TextureFormat.RGBA32,false,true);
            try{texture.SetPixels(values);texture.Apply(false);File.WriteAllBytes(path,texture.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(texture);}
        }
        static void Add(List<Check> checks,string name,bool passed,float measured,float threshold)=>checks.Add(new Check{name=name,passed=passed,measured=measured,threshold=threshold});
        [Serializable]sealed class Report{public string utcTimestamp,unityVersion,graphicsDevice,graphicsApi,fontName,scope,exception;public bool passed;public Check[] checks;public Glyph[] glyphs;}
        [Serializable]sealed class Check{public string name;public bool passed;public float measured,threshold;}
        [Serializable]sealed class Glyph{public string character;public int x,y,litPixels,metricArea;public float inkFraction;}
    }
}
