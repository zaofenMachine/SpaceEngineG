using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SpaceEngine.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace SpaceEngine.Rendering.Editor
{
    /// <summary>
    /// Native-resolution and physical-detail controls rendered by the production shader.
    /// A double-resolution quadrature reference prevents mere sharpening/noise from passing.
    /// </summary>
    public static class GalaxyClarityValidation
    {
        const int Size = 512;
        public static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
            "Artifacts", "V040", "GalaxyClarity", "galaxy-clarity.json"));

        [MenuItem("Space Engine/Validate Galaxy Clarity")]
        public static void ValidateClarity()
        {
            var report = new Report
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                scope = "Production volume shader GPU readbacks at 512 and 1024 pixels. Native/half-resolution and fine-field-disabled controls share camera, seed, exposure and geometry. A 2x image/2x quadrature reference tests convergence; near-arm and interior observers test real spatial detail. Timings include texture allocation/submission and synchronized readback, not GPU timestamp queries or gameplay FPS. Shader caches may already be warm from previous menus."
            };
            string directory = Path.GetDirectoryName(ReportPath); Directory.CreateDirectory(directory);
            var checks = new List<Check>(); var timings = new List<Timing>();
            GameObject root = null; RenderTexture cameraTarget = null;
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("A real GPU is required.");
                root = new GameObject("Galactic clarity readback fixture");
                Camera camera = root.AddComponent<Camera>(); camera.enabled = false;
                camera.fieldOfView = 35; camera.nearClipPlane = .1f; camera.farClipPlane = 16000000;
                cameraTarget = Target(Size); camera.targetTexture = cameraTarget;
                var definition = GalaxyModel.Default(20261026);
                var volume = root.AddComponent<GalaxyVolume>(); volume.Configure(definition);
                volume.SampleCount = 128; volume.InteriorSampleCount = 224; volume.ResolutionScale = 1;
                Double3 overview = new Double3(0, 105000, 125000);
                Color[] native = Capture(volume, camera, definition, overview, -overview, timings, "first-native-allocation-integration-readback");
                Save(native, Size, Path.Combine(directory, "01-native-physical-detail.png"));
                Add(checks, "Default native target retains actual camera pixel dimensions",
                    volume.RenderedWidth == Size && volume.RenderedHeight == Size, volume.RenderedWidth, Size);

                volume.ResolutionScale = .5f;
                Color[] halfRaw = Capture(volume, camera, definition, overview, -overview, timings, "half-resolution-integration");
                Color[] half = Upsample(halfRaw, Size / 2, Size);
                Save(half, Size, Path.Combine(directory, "02-half-resolution-same-field-control.png"));
                volume.ResolutionScale = 1; volume.DetailStrength = 0;
                Color[] noDetail = Capture(volume, camera, definition, overview, -overview, timings, "native-no-fine-field");
                Save(noDetail, Size, Path.Combine(directory, "03-native-fine-field-disabled-control.png"));
                volume.ResolutionScale = .5f; volume.SampleCount = 96;
                Color[] oldStyle = Upsample(Capture(volume, camera, definition, overview, -overview, timings, "v030-style-resolution-field-control"), Size / 2, Size);
                Save(oldStyle, Size, Path.Combine(directory, "04-v030-style-half-res-no-fine-field.png"));

                volume.ResolutionScale = 1; volume.SampleCount = 256; volume.DetailStrength = 1;
                camera.targetTexture = null; cameraTarget.Release(); UnityEngine.Object.DestroyImmediate(cameraTarget);
                cameraTarget = Target(Size * 2); camera.targetTexture = cameraTarget;
                Color[] reference = Downsample(Capture(volume, camera, definition, overview, -overview, timings, "1024-image-256-step-reference"), Size * 2, Size);
                Save(reference, Size, Path.Combine(directory, "05-double-resolution-quadrature-reference.png"));
                float nativeError = Difference(native, reference), halfError = Difference(half, reference);
                Add(checks, "Native pixels converge more closely to the 2x reference than half-resolution reconstruction",
                    nativeError < halfError * .94f, nativeError / Mathf.Max(halfError, 1e-8f), .94f);
                float nativeDetail = HighFrequency(native, Size), smoothDetail = HighFrequency(noDetail, Size);
                Add(checks, "Fine physical field supplies resolved structure beyond the coarse-density control",
                    nativeDetail > smoothDetail * 1.05f && Difference(native, noDetail) > .0003f,
                    nativeDetail / Mathf.Max(smoothDetail, 1e-8f), 1.05f);
                Add(checks, "Native fine structure remains close to the higher-quality spatial reference",
                    nativeError < .025f, nativeError, .025f);
                Add(checks, "No isolated bright noise speckles replace continuous galactic structure",
                    Isolated(native, Size) < 20, Isolated(native, Size), 20);

                camera.targetTexture = null; cameraTarget.Release(); UnityEngine.Object.DestroyImmediate(cameraTarget);
                cameraTarget = Target(Size); camera.targetTexture = cameraTarget;
                volume.SampleCount = 128;
                for (int sample = 0; sample < 3; sample++)
                {
                    volume.Invalidate();
                    Capture(volume, camera, definition, overview, -overview, timings, "forced-native-reintegration-" + sample);
                }
                // Approach the same seeded logarithmic arm in actual galactic coordinates.
                // The angular feature scale changes; this is not a fixed zoomed background.
                double angle = GalaxyModel.ArmAngle(24000, definition);
                var armTarget = new Double3(Math.Cos(angle) * 24000, 0, Math.Sin(angle) * 24000);
                Double3 near = armTarget + new Double3(0, 6500, 9500);
                camera.fieldOfView = 45; volume.SampleCount = 192; volume.InteriorSampleCount = 256;
                Color[] nearDetail = Capture(volume, camera, definition, near, armTarget - near, timings, "near-physical-arm");
                Save(nearDetail, Size, Path.Combine(directory, "06-near-arm-physical-observer.png"));
                volume.DetailStrength = 0;
                Color[] nearSmooth = Capture(volume, camera, definition, near, armTarget - near, timings, "near-arm-no-fine-field");
                Save(nearSmooth, Size, Path.Combine(directory, "07-near-arm-no-fine-field-control.png"));
                float nearSharpness = HighFrequency(nearDetail, Size), nearSmoothness = HighFrequency(nearSmooth, Size);
                Add(checks, "Approaching a real arm resolves filament structure rather than magnifying the broad cloudy field",
                    nearSharpness > nearSmoothness * 1.08f && Difference(nearDetail, nearSmooth) > .001f,
                    nearSharpness / Mathf.Max(nearSmoothness, 1e-8f), 1.08f);
                volume.DetailStrength = 1; volume.SampleCount = 384; volume.InteriorSampleCount = 448;
                Color[] nearReference = Capture(volume, camera, definition, near, armTarget - near, timings, "near-arm-double-quadrature");
                float nearConvergence = Difference(nearDetail, nearReference);
                Add(checks, "Nearby physical fine field converges as the ray budget doubles", nearConvergence < .035f, nearConvergence, .035f);

                Double3 interior = new Double3(definition.SolarRadiusLy, 20, 0);
                volume.SampleCount = 128; volume.InteriorSampleCount = 224; camera.fieldOfView = 60;
                Color[] inside = Capture(volume, camera, definition, interior, new Double3(-1, 0, 0), timings, "embedded-disk-fine-dust");
                Save(inside, Size, Path.Combine(directory, "08-interior-near-field-dust.png"));
                Add(checks, "Embedded observer uses the increased near-dust sampling budget", volume.EffectiveSampleCount == 224, volume.EffectiveSampleCount, 224);
                volume.DetailStrength = 0;
                Color[] insideCoarse = Capture(volume, camera, definition, interior, new Double3(-1, 0, 0), timings, "embedded-disk-no-fine-field");
                Save(insideCoarse, Size, Path.Combine(directory, "09-interior-no-fine-field-control.png"));
                float interiorFrequency = HighFrequency(inside, Size), interiorCoarse = HighFrequency(insideCoarse, Size);
                Add(checks, "Interior dust contains spatial filaments beyond the old coarse cloud bands",
                    interiorFrequency > interiorCoarse * 1.04f, interiorFrequency / Mathf.Max(interiorCoarse, 1e-8f), 1.04f);
                volume.DetailStrength = 1; volume.InteriorSampleCount = 448;
                Color[] interiorReference = Capture(volume, camera, definition, interior, new Double3(-1, 0, 0), timings, "embedded-disk-double-quadrature");
                float interiorError = Difference(inside, interiorReference);
                Add(checks, "Embedded fine-dust view converges under doubled near-field quadrature", interiorError < .035f, interiorError, .035f);
                report.passed = true; foreach (Check check in checks) report.passed &= check.passed;
            }
            catch (Exception exception) { report.exception = exception.ToString(); report.passed = false; }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (cameraTarget != null) { cameraTarget.Release(); UnityEngine.Object.DestroyImmediate(cameraTarget); }
            }
            report.checks = checks.ToArray(); report.timings = timings.ToArray();
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            if (report.passed) Debug.Log("Galaxy clarity GPU validation passed: " + ReportPath);
            else Debug.LogError("Galaxy clarity GPU validation failed: " + ReportPath);
        }

        static RenderTexture Target(int size)
        {
            var target = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create(); return target;
        }
        static Color[] Capture(GalaxyVolume volume, Camera camera, GalaxyDefinition definition, Double3 observer,
            Double3 direction, List<Timing> timings, string name)
        {
            camera.transform.rotation = Quaternion.LookRotation(direction.ToVector3().normalized, Vector3.up);
            var watch = Stopwatch.StartNew();
            volume.Tick(GalaxyModel.FromGalacticLy(observer, definition), camera);
            Color[] pixels = Read(volume.RadianceTexture);
            timings.Add(new Timing { name = name, milliseconds = (float)watch.Elapsed.TotalMilliseconds,
                width = volume.RenderedWidth, height = volume.RenderedHeight, samples = volume.EffectiveSampleCount });
            return pixels;
        }
        static Color[] Read(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply(false);
                Color[] pixels = texture.GetPixels();
                foreach (Color pixel in pixels)
                    if (!float.IsFinite(pixel.r) || !float.IsFinite(pixel.g) || !float.IsFinite(pixel.b)) throw new InvalidOperationException("Non-finite volume radiance.");
                return pixels;
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
        }
        static Color[] Upsample(Color[] source,int sourceSize,int size)
        {
            var result = new Color[size * size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float sx=(x+.5f)*sourceSize/size-.5f,sy=(y+.5f)*sourceSize/size-.5f;
                int ax=Mathf.FloorToInt(sx),ay=Mathf.FloorToInt(sy);
                Color a=source[Mathf.Clamp(ay,0,sourceSize-1)*sourceSize+Mathf.Clamp(ax,0,sourceSize-1)];
                Color b=source[Mathf.Clamp(ay,0,sourceSize-1)*sourceSize+Mathf.Clamp(ax+1,0,sourceSize-1)];
                Color c=source[Mathf.Clamp(ay+1,0,sourceSize-1)*sourceSize+Mathf.Clamp(ax,0,sourceSize-1)];
                Color d=source[Mathf.Clamp(ay+1,0,sourceSize-1)*sourceSize+Mathf.Clamp(ax+1,0,sourceSize-1)];
                result[y*size+x]=Color.Lerp(Color.Lerp(a,b,sx-ax),Color.Lerp(c,d,sx-ax),sy-ay);
            }
            return result;
        }
        static Color[] Downsample(Color[] source,int sourceSize,int size)
        {
            int ratio=sourceSize/size;var result=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                Color sum=Color.black;
                for(int dy=0;dy<ratio;dy++)for(int dx=0;dx<ratio;dx++)sum+=source[(y*ratio+dy)*sourceSize+x*ratio+dx];
                result[y*size+x]=sum/(ratio*ratio);
            }
            return result;
        }
        static void Save(Color[] pixels,int size,string path)
        {
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
            try { var display=new Color[pixels.Length];for(int i=0;i<pixels.Length;i++)display[i]=pixels[i].gamma;
                texture.SetPixels(display);texture.Apply(false);File.WriteAllBytes(path,texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        static float Difference(Color[] a,Color[] b)
        { double total=0;for(int i=0;i<a.Length;i++)total+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return(float)(total/(a.Length*3)); }
        static float HighFrequency(Color[] pixels,int size)
        {
            double result=0;int count=0;
            for(int y=2;y<size-2;y++)for(int x=2;x<size-2;x++)
            {
                float center=pixels[y*size+x].grayscale;
                if(center<.002f||center>.7f)continue;
                float blur=(pixels[(y-2)*size+x].grayscale+pixels[(y+2)*size+x].grayscale+
                    pixels[y*size+x-2].grayscale+pixels[y*size+x+2].grayscale)*.25f;
                result+=Math.Abs(center-blur);count++;
            }
            return(float)(result/Math.Max(1,count));
        }
        static int Isolated(Color[] pixels,int size)
        {
            int result=0;for(int y=1;y<size-1;y++)for(int x=1;x<size-1;x++)
            {
                if(pixels[y*size+x].maxColorComponent<.04f)continue;
                bool isolated=true;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    if((dx!=0||dy!=0)&&pixels[(y+dy)*size+x+dx].maxColorComponent>.005f)isolated=false;
                if(isolated)result++;
            }
            return result;
        }
        static void Add(List<Check> checks,string name,bool passed,float measured,float threshold)
            =>checks.Add(new Check{name=name,passed=passed,measured=measured,threshold=threshold});
        [Serializable] sealed class Report {public string utcTimestamp,unityVersion,graphicsApi,graphicsDevice,scope,exception;public bool passed;public Check[] checks;public Timing[] timings;}
        [Serializable] sealed class Check {public string name;public bool passed;public float measured,threshold;}
        [Serializable] sealed class Timing {public string name;public float milliseconds;public int width,height,samples;}
    }
}
