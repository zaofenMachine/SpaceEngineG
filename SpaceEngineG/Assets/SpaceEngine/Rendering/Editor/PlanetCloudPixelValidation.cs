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
    /// <summary>Real URP pixel differentials against an independent opaque blue fixture.</summary>
    public static class PlanetCloudPixelValidation
    {
        const int Size = 512, TestLayer = 29;
        public static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
            "Artifacts", "V030", "CloudPixels", "cloud-pixels.json"));

        [MenuItem("Space Engine/Validate Planet Cloud Pixels")]
        public static void ValidatePixels()
        {
            var report = new Report
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                scope = "512x512 floating-point actual URP cloud pixels against an independent opaque unlit blue sphere fixture. This fixture isolates optical weather; it is not runtime planet geometry. Timings include synchronous readback and do not measure gameplay performance."
            };
            var checks = new List<Check>(); var timings = new List<float>();
            GameObject root = null; Material template = null, blue = null; Mesh fixtureMesh = null;
            RenderTexture target = null;
            string directory = Path.GetDirectoryName(ReportPath); Directory.CreateDirectory(directory);
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("A real graphics device is required.");
                foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (renderer.gameObject.layer == TestLayer && renderer.enabled) throw new InvalidOperationException("Cloud validation layer 29 is in use.");
                root = new GameObject("Temporary cloud pixel validation") { hideFlags = HideFlags.HideAndDontSave };
                var cameraObject = new GameObject("Isolated cloud camera"); cameraObject.transform.SetParent(root.transform);
                Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.cullingMask = 1 << TestLayer; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.nearClipPlane = .01f; camera.farClipPlane = 100; camera.fieldOfView = 48;
                camera.allowHDR = true; camera.allowMSAA = false;
                template = new Material(Shader.Find("SpaceEngine/PlanetCloud"));
                blue = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                blue.SetColor("_BaseColor", new Color(.025f, .12f, .26f));
                for (int pass = 0; pass < template.passCount; pass++) ShaderUtil.CompilePass(template, pass, true);
                var planet = new GameObject("Independent unlit occluder") { layer = TestLayer }; planet.transform.SetParent(root.transform);
                planet.transform.position = Vector3.forward * 3;
                fixtureMesh = CelestialMesh.Sphere(128, 64);
                planet.AddComponent<MeshFilter>().sharedMesh = fixtureMesh;
                planet.AddComponent<MeshRenderer>().sharedMaterial = blue;
                var weatherObject = new GameObject("Weather"); weatherObject.transform.SetParent(root.transform);
                PlanetCloudRenderer cloud = weatherObject.AddComponent<PlanetCloudRenderer>();
                var definition = new PlanetDefinition { Name = "GPU weather", Seed = 9371, Kind = PlanetKind.Oceanic, Radius = 6e6 };
                const double scale = 1 / 6e6;
                Double3 from = new Double3(0, 0, -18e6);
                Configure(cloud, definition, template);
                cloud.Tick(from, scale, Vector3.back, 0);
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
                target.Create();
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP StandardRequest is unavailable.");
                Capture(camera, target, request, timings);
                cloud.ShowRenderer = false; cloud.Tick(from, scale, Vector3.back, 0);
                Color[] clear = Capture(camera, target, request, timings); Save(clear, Path.Combine(directory, "01-cloud-disabled-control.png"));
                cloud.ShowRenderer = true; cloud.Tick(from, scale, Vector3.back, 0);
                Color[] baseline = Capture(camera, target, request, timings); Save(baseline, Path.Combine(directory, "02-seeded-cloud-weather.png"));
                Add(checks, "Weather changes actual lit planet pixels", Difference(clear, baseline) > .005f, Difference(clear, baseline), .005f);
                int diskPixels = 0, brightCloudPixels = 0; float peak = 0;
                for (int i = 0; i < baseline.Length; i++)
                {
                    peak = Mathf.Max(peak, baseline[i].maxColorComponent);
                    if (clear[i].maxColorComponent <= .0001f) continue;
                    diskPixels++;
                    if (baseline[i].maxColorComponent - clear[i].maxColorComponent > .05f) brightCloudPixels++;
                }
                float brightCoverage = brightCloudPixels / (float)Math.Max(1, diskPixels);
                Add(checks, "Default weather retains clear/thin terrain windows instead of a white blanket",
                    brightCoverage > .08f && brightCoverage < .65f, brightCoverage, .65f);
                Add(checks, "Default cloud radiance retains headroom before display clipping", peak < .98f, peak, .98f);
                Color[] repeat = Capture(camera, target, request, timings);
                Add(checks, "Same seed and explicit simulation time repeat pixel radiance", Difference(baseline, repeat) < .00001f, Difference(baseline, repeat), .00001f);
                definition.Seed++; Configure(cloud, definition, template); cloud.Tick(from, scale, Vector3.back, 0);
                Capture(camera, target, request, timings); Color[] different = Capture(camera, target, request, timings);
                Save(different, Path.Combine(directory, "03-different-seed.png"));
                Add(checks, "Different weather seed changes visible cloud geography", Difference(baseline, different) > .003f, Difference(baseline, different), .003f);
                definition.Seed--; Configure(cloud, definition, template); cloud.Tick(from, scale, Vector3.back, 86400 * 18);
                Capture(camera, target, request, timings); Color[] wind = Capture(camera, target, request, timings);
                Save(wind, Path.Combine(directory, "04-simulation-weather-advection.png"));
                Add(checks, "Explicit simulation time advects the weather field", Difference(baseline, wind) > .003f, Difference(baseline, wind), .003f);
                cloud.Tick(from, scale, Vector3.forward, 0); Color[] night = Capture(camera, target, request, timings);
                Save(night, Path.Combine(directory, "05-planet-shadow-control.png"));
                float dayNight = Mean(baseline) - Mean(night);
                Add(checks, "Analytic planet shadow removes cloud direct light", dayNight > .01f, dayNight, .01f);
                planet.transform.position = Vector3.forward * 1.5f; planet.transform.localScale = Vector3.one * .5f;
                cloud.Tick(from, scale * .5, Vector3.back, 0); Color[] rescaled = Capture(camera, target, request, timings);
                Save(rescaled, Path.Combine(directory, "06-render-scale-control.png"));
                Add(checks, "Weather angular appearance is invariant under render scale", Difference(baseline, rescaled) < .001f, Difference(baseline, rescaled), .001f);
                planet.transform.position = Vector3.forward * 3; planet.transform.localScale = Vector3.one;
                cloud.Coverage = 0; cloud.Tick(from, scale, Vector3.back, 0);
                Color[] noCoverage = Capture(camera, target, request, timings);
                Add(checks, "Zero configured coverage removes all optical weather", Difference(clear, noCoverage) < .00001f, Difference(clear, noCoverage), .00001f);
                cloud.Coverage = .42f; cloud.Tick(new Double3(0,0,-definition.Radius - 2), scale, Vector3.back, 0);
                Add(checks, "Thin layer is actually hidden before surface entry", !cloud.Visible && cloud.ObserverFade == 0, cloud.ObserverFade, 0);
                report.passed = true; foreach (Check check in checks) report.passed &= check.passed;
            }
            catch (Exception exception) { report.exception = exception.ToString(); report.passed = false; }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (template != null) UnityEngine.Object.DestroyImmediate(template);
                if (blue != null) UnityEngine.Object.DestroyImmediate(blue);
                if (fixtureMesh != null) UnityEngine.Object.DestroyImmediate(fixtureMesh);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
            report.checks = checks.ToArray(); report.submitAndReadbackMilliseconds = timings.ToArray();
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            if (report.passed) Debug.Log("Planet cloud GPU pixel validation passed: " + ReportPath);
            else Debug.LogError("Planet cloud GPU pixel validation failed: " + ReportPath);
        }

        static void Configure(PlanetCloudRenderer cloud, PlanetDefinition definition, Material template)
        {
            cloud.Configure(definition, template);
            foreach (Transform child in cloud.GetComponentsInChildren<Transform>()) child.gameObject.layer = TestLayer;
        }
        static Color[] Capture(Camera camera, RenderTexture target, RenderPipeline.StandardRequest request, List<float> timings)
        {
            var watch = Stopwatch.StartNew(); RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target; readback.ReadPixels(new Rect(0,0,Size,Size),0,0); readback.Apply(false);
                Color[] values = readback.GetPixels();
                foreach (Color value in values) if (!float.IsFinite(value.r) || !float.IsFinite(value.g) || !float.IsFinite(value.b))
                    throw new InvalidOperationException("Cloud pixels have non-finite radiance.");
                timings.Add((float)watch.Elapsed.TotalMilliseconds); return values;
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(readback); }
        }
        static void Save(Color[] values, string path)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            try
            {
                var display = new Color[values.Length];
                for (int i=0; i<values.Length; i++) display[i] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(values[i].r)),
                    Mathf.LinearToGammaSpace(Mathf.Clamp01(values[i].g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(values[i].b)),1);
                texture.SetPixels(display); texture.Apply(false); File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        static float Difference(Color[] a, Color[] b)
        {
            double sum = 0; for (int i=0; i<a.Length; i++) sum += Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);
            return (float)(sum / (a.Length * 3));
        }
        static float Mean(Color[] values)
        {
            double sum = 0; foreach(Color color in values) sum += (color.r + color.g + color.b) / 3;
            return (float)(sum / values.Length);
        }
        static void Add(List<Check> checks, string name, bool passed, float value, float threshold)
            => checks.Add(new Check { name=name, passed=passed, measured=value, threshold=threshold });
        [Serializable] sealed class Report
        {
            public string utcTimestamp, unityVersion, graphicsApi, graphicsDevice, scope, exception;
            public bool passed; public Check[] checks; public float[] submitAndReadbackMilliseconds;
        }
        [Serializable] sealed class Check { public string name; public bool passed; public float measured, threshold; }
    }
}
