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
    /// <summary>Actual integrated-radiance readbacks and an independent opaque-depth fixture.</summary>
    public static class GalaxyPixelValidation
    {
        const int Size = 256;
        const int TestLayer = 29;
        public static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
            "Artifacts", "V050", "GalaxyPixels", "galaxy-pixels.json"));

        [MenuItem("Space Engine/Validate Galaxy Pixels")]
        public static void ValidatePixels()
        {
            var report = new Report
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                scope = "256x256 half-float observer-dependent galaxy volume readbacks plus floating-point URP opaque-depth fixture. All viewpoints integrate the same bounded seeded 3D density. Submission/readback timings include synchronization and are not gameplay performance."
            };
            var checks = new List<Check>(); var timings = new List<float>();
            string directory = Path.GetDirectoryName(ReportPath); Directory.CreateDirectory(directory);
            GameObject root = null, fixture = null;
            RenderTexture target = null;
            Material volumeSource = null, compositeSource = null, fixtureMaterial = null;
            Mesh fixtureMesh = null;
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("A real graphics device is required.");
                // The composite dims with a sunlit sky; a value left over from play mode is not part of this fixture.
                Shader.SetGlobalFloat("_SkyDaylight", 0);
                foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (renderer.gameObject.layer == TestLayer) throw new InvalidOperationException("Galaxy test layer is already in use.");
                volumeSource = new Material(Shader.Find("SpaceEngine/GalaxyVolume"));
                compositeSource = new Material(Shader.Find("SpaceEngine/GalaxyVolumeComposite"));
                for (int pass = 0; pass < volumeSource.passCount; pass++) ShaderUtil.CompilePass(volumeSource, pass, true);
                for (int pass = 0; pass < compositeSource.passCount; pass++) ShaderUtil.CompilePass(compositeSource, pass, true);
                root = new GameObject("Galaxy GPU optical fixture") { layer = TestLayer };
                Camera camera = root.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << TestLayer; camera.fieldOfView = 50; camera.nearClipPlane = .1f; camera.farClipPlane = 16000000;
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                target.Create(); camera.targetTexture = target;
                GalaxyVolume volume = root.AddComponent<GalaxyVolume>();
                GalaxyDefinition galaxy = GalaxyModel.Default(20261026);
                volume.Configure(galaxy, volumeSource, compositeSource); volume.ResolutionScale = 1; volume.SampleCount = 96;
                Double3 oblique = new Double3(0, 105000, 125000);
                Color[] overview = Capture(volume, camera, galaxy, oblique, -oblique, timings);
                Save(overview, Path.Combine(directory, "01-volume-oblique.png"));
                Add(checks, "Bounded galaxy produces continuous luminous structure in exterior view", CountLit(overview) > 800, CountLit(overview), 800);
                int revision = volume.RenderRevision;
                volume.Tick(GalaxyModel.FromGalacticLy(oblique, galaxy), camera);
                Color[] repeat = Read(volume.RadianceTexture);
                Add(checks, "Unchanged observer reuses deterministic radiance without another GPU integration",
                    Difference(overview, repeat) == 0 && volume.RenderRevision == revision, volume.RenderRevision - revision, 0);

                volume.SampleCount = 192;
                Color[] high = Capture(volume, camera, galaxy, oblique, -oblique, timings);
                Save(high, Path.Combine(directory, "02-volume-high-sample-reference.png"));
                float convergence = Difference(overview, high);
                Add(checks, "96-step overview converges toward the independent 192-step budget", convergence < .035f, convergence, .035f);
                volume.SampleCount = 96;
                Double3 edge = new Double3(0, 5000, 160000);
                Color[] edgeView = Capture(volume, camera, galaxy, edge, -edge, timings);
                Save(edgeView, Path.Combine(directory, "03-volume-edge.png"));
                float edgeThickness = VerticalMoment(edgeView), overviewThickness = VerticalMoment(overview);
                Add(checks, "Edge-on view has a thinner projected stellar disk than the oblique observer",
                    edgeThickness < overviewThickness * .8f, edgeThickness / Mathf.Max(overviewThickness, .001f), .8f);

                Double3 inside = new Double3(galaxy.SolarRadiusLy, 0, 0);
                Color[] dusty = Capture(volume, camera, galaxy, inside, new Double3(-1, 0, 0), timings);
                Save(dusty, Path.Combine(directory, "04-volume-inside-dust.png"));
                double offBand = 0, band = 0; int offBandCount = 0, bandCount = 0;
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    Color value = dusty[y * Size + x];
                    float light = (value.r + value.g + value.b) / 3;
                    if (y < Size / 8 || y >= Size * 7 / 8) { offBand += light; offBandCount++; }
                    if (y >= Size * 3 / 8 && y < Size * 5 / 8) { band += light; bandCount++; }
                }
                float offBandMean = (float)(offBand / Math.Max(1, offBandCount));
                float bandMean = (float)(band / Math.Max(1, bandCount));
                Add(checks, "Interior display retains a dark off-plane night sky instead of a gray luminous screen",
                    offBandMean < .01f, offBandMean, .01f);
                Add(checks, "Galactic band stays visibly luminous while the off-plane night sky remains dark",
                    bandMean > .025f && bandMean > offBandMean * 8, bandMean / Mathf.Max(.00001f, offBandMean), 8);
                volume.DustStrength = 0;
                Color[] clear = Capture(volume, camera, galaxy, inside, new Double3(-1, 0, 0), timings);
                Save(clear, Path.Combine(directory, "05-volume-inside-no-dust-control.png"));
                float attenuation = Mean(clear) - Mean(dusty);
                Add(checks, "Seeded dust attenuates the interior disk and core", attenuation > .002f, attenuation, .002f);
                int darkened = 0; for (int i = 0; i < clear.Length; i++) if (clear[i].b - dusty[i].b > .05f) darkened++;
                Add(checks, "Dust lane changes a spatial area rather than only isolated stellar points", darkened > 300, darkened, 300);

                volume.DustStrength = 1.35f;
                galaxy = GalaxyModel.Default(20261027); volume.Configure(galaxy, volumeSource, compositeSource);
                Color[] changedSeed = Capture(volume, camera, galaxy, oblique, -oblique, timings);
                Save(changedSeed, Path.Combine(directory, "06-volume-different-seed.png"));
                float seedDifference = Difference(overview, changedSeed);
                Add(checks, "Seed changes three-dimensional arm phase and dust structures", seedDifference > .002f, seedDifference, .002f);
                Double3 translated = new Double3(85000, 30000, 25000);
                Color[] translatedView = Capture(volume, camera, galaxy, translated, -translated, timings);
                Save(translatedView, Path.Combine(directory, "07-volume-translated-observer.png"));
                float parallax = Difference(changedSeed, translatedView);
                Add(checks, "Moving the galactic observer changes perspective rather than replaying a fixed background", parallax > .005f, parallax, .005f);
                Double3 far = new Double3(0, 0, 200000);
                Color[] empty = Capture(volume, camera, galaxy, far, new Double3(0, 0, 1), timings);
                Add(checks, "Rays pointing away from the finite outer halo remain black", Mean(empty) < .00001f, Mean(empty), .00001f);
                volume.Exposure = 0;
                Color[] disabled = Capture(volume, camera, galaxy, oblique, -oblique, timings);
                Add(checks, "Zero optical exposure removes galactic emission", Mean(disabled) == 0, Mean(disabled), 0);

                // Actual URP depth compositing: the same integrated texture must not cover
                // an opaque independent green fixture in front of the far-depth sky draw.
                volume.Exposure = 1;
                Color[] depthBackground = Capture(volume, camera, galaxy, oblique, -oblique, timings);
                camera.transform.rotation = Quaternion.identity;
                fixture = new GameObject("Independent opaque depth fixture") { layer = TestLayer };
                fixtureMesh = new Mesh { name = "Opaque depth fixture plane" };
                fixtureMesh.vertices = new[] { new Vector3(-.2f,-.2f,3), new Vector3(.2f,-.2f,3), new Vector3(.2f,.2f,3), new Vector3(-.2f,.2f,3) };
                fixtureMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; fixtureMesh.RecalculateBounds();
                fixture.AddComponent<MeshFilter>().sharedMesh = fixtureMesh;
                fixtureMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                fixtureMaterial.SetColor("_BaseColor", new Color(.05f,.3f,.08f,1));
                fixture.AddComponent<MeshRenderer>().sharedMaterial = fixtureMaterial;
                var request = new RenderPipeline.StandardRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(camera, request); RenderPipeline.SubmitRenderRequest(camera, request);
                Color[] composited = Read(target);
                Color center = composited[(Size / 2) * Size + Size / 2];
                Color expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(.05f,.3f,.08f,1).linear : new Color(.05f,.3f,.08f,1);
                float depthError = Mathf.Abs(center.r - expected.r) + Mathf.Abs(center.g - expected.g) + Mathf.Abs(center.b - expected.b);
                Save(composited, Path.Combine(directory, "08-opaque-depth-control.png"));
                Add(checks, "Far-depth volume respects real URP opaque geometry", depthError < .002f, depthError, .002f);
                double backgroundError = 0; int backgroundSamples = 0, backgroundLit = 0;
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (Mathf.Abs(x - Size / 2) < 24 && Mathf.Abs(y - Size / 2) < 24) continue;
                    int index = y * Size + x;
                    backgroundError += Mathf.Abs(composited[index].r - depthBackground[index].r) +
                        Mathf.Abs(composited[index].g - depthBackground[index].g) + Mathf.Abs(composited[index].b - depthBackground[index].b);
                    backgroundSamples++;
                    if (composited[index].maxColorComponent > .01f) backgroundLit++;
                }
                float compositeError = (float)(backgroundError / Math.Max(1, backgroundSamples * 3));
                Add(checks, "URP composite preserves the actual integrated volume outside the opaque fixture",
                    compositeError < .002f && backgroundLit > 500, compositeError, .002f);
                report.passed = true; foreach (Check check in checks) report.passed &= check.passed;
            }
            catch (Exception exception) { report.exception = exception.ToString(); report.passed = false; }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                if (fixtureMesh != null) UnityEngine.Object.DestroyImmediate(fixtureMesh);
                if (fixtureMaterial != null) UnityEngine.Object.DestroyImmediate(fixtureMaterial);
                if (volumeSource != null) UnityEngine.Object.DestroyImmediate(volumeSource);
                if (compositeSource != null) UnityEngine.Object.DestroyImmediate(compositeSource);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
            report.checks = checks.ToArray(); report.submitAndReadbackMilliseconds = timings.ToArray();
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            if (report.passed) Debug.Log("Galaxy GPU pixel validation passed: " + ReportPath);
            else Debug.LogError("Galaxy GPU pixel validation failed: " + ReportPath);
        }

        static Color[] Capture(GalaxyVolume volume, Camera camera, GalaxyDefinition galaxy, Double3 observer, Double3 direction, List<float> timings)
        {
            camera.transform.rotation = Quaternion.LookRotation(direction.ToVector3().normalized, Vector3.up);
            var watch = Stopwatch.StartNew();
            volume.Tick(GalaxyModel.FromGalacticLy(observer, galaxy), camera);
            Color[] pixels = Read(volume.RadianceTexture);
            timings.Add((float)watch.Elapsed.TotalMilliseconds);
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
                    if (!float.IsFinite(pixel.r) || !float.IsFinite(pixel.g) || !float.IsFinite(pixel.b)) throw new InvalidOperationException("Non-finite galactic radiance.");
                return pixels;
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
        }
        static void Save(Color[] pixels, string path)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            try
            {
                var display = new Color[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) display[i] = pixels[i].gamma;
                texture.SetPixels(display); texture.Apply(false); File.WriteAllBytes(path,texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        static int CountLit(Color[] pixels) { int result=0;foreach(Color pixel in pixels)if(pixel.maxColorComponent>.01f)result++;return result; }
        static float Mean(Color[] pixels) { double sum=0;foreach(Color pixel in pixels)sum+=(pixel.r+pixel.g+pixel.b)/3;return(float)(sum/pixels.Length); }
        static float Difference(Color[] a,Color[] b) { double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return(float)(sum/(a.Length*3)); }
        static float VerticalMoment(Color[] pixels)
        {
            double sum=0,total=0;for(int y=0;y<Size;y++)for(int x=0;x<Size;x++){float light=pixels[y*Size+x].grayscale;sum+=light*(y-Size*.5)*(y-Size*.5);total+=light;}return(float)(sum/Math.Max(1e-12,total));
        }
        static void Add(List<Check> checks,string name,bool passed,float measured,float threshold) => checks.Add(new Check { name=name,passed=passed,measured=measured,threshold=threshold });
        [Serializable] sealed class Report { public string utcTimestamp,unityVersion,graphicsApi,graphicsDevice,scope,exception;public bool passed;public Check[] checks;public float[] submitAndReadbackMilliseconds; }
        [Serializable] sealed class Check { public string name;public bool passed;public float measured,threshold; }
    }
}
