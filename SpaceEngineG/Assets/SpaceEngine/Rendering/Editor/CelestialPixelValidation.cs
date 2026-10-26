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
    /// <summary>Actual GPU render/readback regressions for seeded clouds, ice coloration and ring shadows.</summary>
    public static class CelestialPixelValidation
    {
        const int TestLayer = 30;
        const int Size = 512;
        public static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
            "Artifacts", "V040", "CelestialPixels", "celestial-pixels.json"));

        [MenuItem("Space Engine/Validate Celestial Pixels")]
        public static void ValidatePixels()
        {
            var report = new PixelReport
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                scope = "512x512 floating-point URP GPU renders and readbacks. Seed repeatability, changing seeds, rotation, ice/gas distinction, ring coverage, analytic planet-shadow differential and terrain lava emission. PNGs are display-gamma previews. Timings include GPU submission and synchronous readback; they are not gameplay frame times."
            };
            var checks = new List<PixelCheck>();
            var timings = new List<float>();
            GameObject root = null;
            Material gas = null, ring = null, terrainMaterial = null;
            Mesh lavaFixtureMesh = null, denseLavaFixtureMesh = null;
            RenderTexture target = null;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            string imageDirectory = Path.GetDirectoryName(ReportPath);
            Directory.CreateDirectory(imageDirectory);
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Celestial pixel validation requires a real graphics API.");
                foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (renderer.gameObject.layer == TestLayer && renderer.enabled)
                        throw new InvalidOperationException("Layer 30 is already in use; GPU fixture isolation is unavailable.");
                gas = new Material(Shader.Find("SpaceEngine/GasGiant"));
                ring = new Material(Shader.Find("SpaceEngine/PlanetRing"));
                Compile(gas); Compile(ring);
                root = new GameObject("Temporary celestial pixel validation") { hideFlags = HideFlags.HideAndDontSave };
                var cameraObject = new GameObject("Isolated camera");
                cameraObject.transform.SetParent(root.transform);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << TestLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.nearClipPlane = .01f; camera.farClipPlane = 100;
                camera.fieldOfView = 48;
                camera.allowHDR = true; camera.allowMSAA = false;
                var body = new GameObject("Isolated body");
                body.transform.SetParent(root.transform);
                var celestial = body.AddComponent<CelestialBodyRenderer>();
                const double radius = 60000000;
                Double3 from = new Double3(0, 2.5, -6) * radius;
                Vector3 sun = new Vector3(-1, .3f, -1).normalized;
                camera.transform.rotation = Quaternion.LookRotation((-from).Normalized.ToVector3(), Vector3.up);
                var definition = new PlanetDefinition
                {
                    Name = "GPU seeded giant", Seed = 9371, Kind = PlanetKind.GasGiant,
                    Radius = radius, RotationPeriod = 36000, AxialTilt = .47,
                    SurfaceTint = new Color(.78f, .46f, .21f)
                };
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                    { name = "Celestial GPU regression target", antiAliasing = 1 };
                target.Create();
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new InvalidOperationException("Active pipeline does not support StandardRequest.");

                Configure(celestial, definition, gas, ring, from, sun, 0);
                Capture(camera, target, request, timings); // GPU resident renderer registration warmup.
                Color[] baseline = Capture(camera, target, request, timings);
                SavePng(baseline, Path.Combine(imageDirectory, "01-gas-seed-9371.png"));
                int lit = CountLit(baseline);
                Add(checks, "Generated clouds draw a visible lit disk", lit > 1000, lit, 1000);

                celestial.Tick(from, 1 / radius, sun, 0);
                Color[] repeat = Capture(camera, target, request, timings);
                float repeatDifference = Difference(baseline, repeat);
                Add(checks, "Same seed and time reproduce GPU radiance", repeatDifference < .00001f, repeatDifference, .00001f);

                definition.Seed = 9372;
                Configure(celestial, definition, gas, ring, from, sun, 0);
                Capture(camera, target, request, timings);
                Color[] differentSeed = Capture(camera, target, request, timings);
                SavePng(differentSeed, Path.Combine(imageDirectory, "02-gas-seed-9372.png"));
                float seedDifference = Difference(baseline, differentSeed);
                Add(checks, "Changing seed changes rendered cloud pattern", seedDifference > .0005f, seedDifference, .0005f);

                definition.Seed = 9371;
                Configure(celestial, definition, gas, ring, from, sun, definition.RotationPeriod * .25);
                Capture(camera, target, request, timings);
                Color[] rotated = Capture(camera, target, request, timings);
                SavePng(rotated, Path.Combine(imageDirectory, "03-gas-quarter-rotation.png"));
                float rotationDifference = Difference(baseline, rotated);
                Add(checks, "Simulation rotation visibly advects clouds and vortices", rotationDifference > .0003f, rotationDifference, .0003f);

                definition.Kind = PlanetKind.IceGiant;
                definition.SurfaceTint = new Color(.18f, .58f, .87f);
                Configure(celestial, definition, gas, ring, from, sun, 0);
                Capture(camera, target, request, timings);
                Color[] ice = Capture(camera, target, request, timings);
                SavePng(ice, Path.Combine(imageDirectory, "04-ice-giant.png"));
                Color iceMean = MeanLit(ice);
                Add(checks, "Ice giant has distinct blue cloud radiance", iceMean.b > iceMean.r + .03f, iceMean.b - iceMean.r, .03f);

                definition.Kind = PlanetKind.GasGiant;
                definition.SurfaceTint = new Color(.78f, .46f, .21f);
                definition.RingInnerRadius = radius * 1.3;
                definition.RingOuterRadius = radius * 2.4;
                Configure(celestial, definition, gas, ring, from, sun, 0);
                Capture(camera, target, request, timings);
                Color[] ringed = Capture(camera, target, request, timings);
                SavePng(ringed, Path.Combine(imageDirectory, "05-ringed-gas-giant.png"));
                int ringPixels = CountNewLit(baseline, ringed);
                Add(checks, "Particle rings add visible pixels beyond cloud disk", ringPixels > 500, ringPixels, 500);

                MeshRenderer[] renderers = celestial.GetComponentsInChildren<MeshRenderer>();
                renderers[0].enabled = false;
                renderers[1].sharedMaterial.SetFloat("_PlanetShadowStrength", 1);
                Color[] shadow = Capture(camera, target, request, timings);
                SavePng(shadow, Path.Combine(imageDirectory, "06-rings-analytic-shadow.png"));
                renderers[1].sharedMaterial.SetFloat("_PlanetShadowStrength", 0);
                Color[] noShadow = Capture(camera, target, request, timings);
                SavePng(noShadow, Path.Combine(imageDirectory, "07-rings-shadow-disabled-control.png"));
                float shadowDifference = Difference(shadow, noShadow);
                int shadowedPixels = CountDarker(shadow, noShadow);
                Add(checks, "Analytic planet shadow removes light from correct ring pixels", shadowDifference > .0001f && shadowedPixels > 50,
                    shadowedPixels, 50);

                definition.RingInnerRadius = definition.RingOuterRadius = 0;
                Double3 nearFrom = from.Normalized * (radius + 10000);
                Configure(celestial, definition, gas, ring, nearFrom, sun, 0);
                celestial.Tick(nearFrom, .001, sun, 0); // Ten render units above the physical reference, no near-clip ambiguity.
                Material cloudDetailMaterial = celestial.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                cloudDetailMaterial.SetFloat("_DetailStrength", 1);
                Capture(camera, target, request, timings);
                Color[] nearCloudDetail = Capture(camera, target, request, timings);
                SavePng(nearCloudDetail, Path.Combine(imageDirectory, "10-cloud-top-detail.png"));
                cloudDetailMaterial.SetFloat("_DetailStrength", 0);
                Color[] noCloudDetail = Capture(camera, target, request, timings);
                SavePng(noCloudDetail, Path.Combine(imageDirectory, "11-cloud-top-detail-disabled-control.png"));
                float cloudDetailDifference = Difference(nearCloudDetail, noCloudDetail);
                Add(checks, "Ten-kilometre cloud approach reveals actual procedural cell detail",
                    CountLit(nearCloudDetail) > 1000 && cloudDetailDifference > .001f, cloudDetailDifference, .001f);

                celestial.ShowRenderer = false;
                celestial.Tick(from, 1 / radius, sun, 0);
                terrainMaterial = new Material(Shader.Find("SpaceEngine/Terrain"));
                terrainMaterial.SetFloat("_AmbientFloor", 0);
                terrainMaterial.SetFloat("_SunIntensity", 0);
                terrainMaterial.SetFloat("_SpecularStrength", 0);
                terrainMaterial.SetFloat("_EmissionStrength", 1.5f);
                terrainMaterial.SetFloat("_IsLava", 0);
                terrainMaterial.SetVector("_PatchAnchor", new Vector4(0, 0, 1, 1));
                terrainMaterial.SetVector("_LavaSeed", new Vector4(9371, 0, 0, 0));
                terrainMaterial.SetVector("_SunDirection", camera.transform.forward);
                Compile(terrainMaterial);
                var lavaFixture = new GameObject("Nightside lava emission fixture") { layer = TestLayer };
                lavaFixture.transform.SetParent(root.transform);
                lavaFixture.transform.SetPositionAndRotation(camera.transform.forward * 3, camera.transform.rotation);
                lavaFixtureMesh = new Mesh { name = "Lava mask validation patch" };
                lavaFixtureMesh.vertices = new[]
                {
                    new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0)
                };
                lavaFixtureMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                lavaFixtureMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                var nonEmissive = new Color(.1f, .03f, .01f, 0);
                lavaFixtureMesh.colors = new[] { nonEmissive, nonEmissive, nonEmissive, nonEmissive };
                lavaFixtureMesh.RecalculateBounds();
                lavaFixture.AddComponent<MeshFilter>().sharedMesh = lavaFixtureMesh;
                lavaFixture.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
                Capture(camera, target, request, timings);
                Color[] darkTerrain = Capture(camera, target, request, timings);
                // All vertex alpha remains zero: the fissure field must be evaluated
                // per fragment and must not regress to interpolated triangle masks.
                terrainMaterial.SetFloat("_IsLava", 1);
                Capture(camera, target, request, timings);
                Color[] glowingTerrain = Capture(camera, target, request, timings);
                SavePng(glowingTerrain, Path.Combine(imageDirectory, "08-nightside-lava-emission.png"));
                float maximumRed = 0;
                int glowingFissurePixels = 0;
                foreach (Color value in glowingTerrain)
                {
                    maximumRed = Mathf.Max(maximumRed, value.r);
                    if (value.r > .1f && value.r > value.g * 5) glowingFissurePixels++;
                }
                Add(checks, "Procedural lava fissures emit red radiance without sunlight or vertex alpha",
                    CountLit(darkTerrain) == 0 && maximumRed > 1 && glowingFissurePixels > 500,
                    glowingFissurePixels, 500);

                denseLavaFixtureMesh = DenseLavaFixture(16);
                lavaFixture.GetComponent<MeshFilter>().sharedMesh = denseLavaFixtureMesh;
                Capture(camera, target, request, timings);
                Color[] denseLava = Capture(camera, target, request, timings);
                SavePng(denseLava, Path.Combine(imageDirectory, "12-lava-dense-grid-control.png"));
                float topologyDifference = Difference(glowingTerrain, denseLava);
                Add(checks, "Lava fissures remain invariant under mesh density and vertex-color changes",
                    topologyDifference < .001f, topologyDifference, .001f);
                terrainMaterial.SetFloat("_EmissionStrength", 0);
                Color[] disabledEmission = Capture(camera, target, request, timings);
                SavePng(disabledEmission, Path.Combine(imageDirectory, "09-lava-emission-disabled-control.png"));
                Add(checks, "Material emission strength zero removes nightside lava radiance",
                    CountLit(disabledEmission) == 0,
                    CountLit(disabledEmission), 0);
                report.passed = true;
                foreach (PixelCheck check in checks) report.passed &= check.passed;
            }
            catch (Exception exception)
            {
                report.exception = exception.ToString();
                report.passed = false;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (gas != null) UnityEngine.Object.DestroyImmediate(gas);
                if (ring != null) UnityEngine.Object.DestroyImmediate(ring);
                if (terrainMaterial != null) UnityEngine.Object.DestroyImmediate(terrainMaterial);
                if (lavaFixtureMesh != null) UnityEngine.Object.DestroyImmediate(lavaFixtureMesh);
                if (denseLavaFixtureMesh != null) UnityEngine.Object.DestroyImmediate(denseLavaFixtureMesh);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
            report.checks = checks.ToArray();
            report.submitAndReadbackMilliseconds = timings.ToArray();
            report.imageDirectory = imageDirectory;
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            if (report.passed) Debug.Log("Celestial GPU pixel validation passed: " + ReportPath);
            else Debug.LogError("Celestial GPU pixel validation failed: " + ReportPath);
        }

        public static void RunBatch()
        {
            ValidatePixels();
            EditorApplication.Exit(JsonUtility.FromJson<PixelReport>(File.ReadAllText(ReportPath)).passed ? 0 : 1);
        }

        static void Configure(CelestialBodyRenderer renderer, PlanetDefinition definition, Material gas, Material ring,
            Double3 from, Vector3 sun, double time)
        {
            renderer.Configure(definition, gas, ring);
            foreach (Transform child in renderer.GetComponentsInChildren<Transform>()) child.gameObject.layer = TestLayer;
            renderer.Tick(from, 1 / definition.Radius, sun, time);
        }

        static void Compile(Material material)
        {
            for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
        }

        static Mesh DenseLavaFixture(int segments)
        {
            int row = segments + 1;
            var vertices = new Vector3[row * row];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * segments * 6];
            for (int y = 0; y <= segments; y++)
            for (int x = 0; x <= segments; x++)
            {
                int index = y * row + x;
                vertices[index] = new Vector3(-1 + 2f * x / segments, -1 + 2f * y / segments, 0);
                normals[index] = Vector3.back;
                // Deliberately unrelated colors/alpha: the lava fragment field must be authoritative.
                colors[index] = new Color((x & 1) == 0 ? 1 : 0, (y & 1) == 0 ? 1 : 0, .8f, 1);
            }
            int cursor = 0;
            for (int y = 0; y < segments; y++)
            for (int x = 0; x < segments; x++)
            {
                int a = y * row + x, b = a + 1, c = a + row, d = c + 1;
                triangles[cursor++] = a; triangles[cursor++] = c; triangles[cursor++] = b;
                triangles[cursor++] = b; triangles[cursor++] = c; triangles[cursor++] = d;
            }
            var mesh = new Mesh { name = "Dense lava interpolation control" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.colors = colors; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Color[] Capture(Camera camera, RenderTexture target, RenderPipeline.StandardRequest request, List<float> timings)
        {
            var watch = Stopwatch.StartNew();
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply(false);
                Color[] values = readback.GetPixels();
                foreach (Color value in values)
                    if (!float.IsFinite(value.r) || !float.IsFinite(value.g) || !float.IsFinite(value.b))
                        throw new InvalidOperationException("Celestial GPU readback contains non-finite radiance.");
                timings.Add((float)watch.Elapsed.TotalMilliseconds);
                return values;
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        static void SavePng(Color[] linear, string path)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            try
            {
                var display = new Color[linear.Length];
                for (int i = 0; i < display.Length; i++)
                    display[i] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(linear[i].r)),
                        Mathf.LinearToGammaSpace(Mathf.Clamp01(linear[i].g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(linear[i].b)), 1);
                texture.SetPixels(display); texture.Apply(false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        static float Difference(Color[] a, Color[] b)
        {
            double total = 0;
            for (int i = 0; i < a.Length; i++) total += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return (float)(total / (a.Length * 3));
        }
        static int CountLit(Color[] colors) { int count = 0; foreach (Color color in colors) if (color.maxColorComponent > .005f) count++; return count; }
        static int CountNewLit(Color[] before, Color[] after)
        {
            int count = 0;
            for (int i = 0; i < before.Length; i++) if (before[i].maxColorComponent < .001f && after[i].maxColorComponent > .005f) count++;
            return count;
        }
        static int CountDarker(Color[] shadow, Color[] clear)
        {
            int count = 0;
            for (int i = 0; i < shadow.Length; i++) if (clear[i].maxColorComponent - shadow[i].maxColorComponent > .02f) count++;
            return count;
        }
        static Color MeanLit(Color[] colors)
        {
            Color sum = Color.black; int count = 0;
            foreach (Color color in colors) if (color.maxColorComponent > .005f) { sum += color; count++; }
            return sum / Math.Max(1, count);
        }
        static void Add(List<PixelCheck> checks, string name, bool passed, float value, float threshold)
            => checks.Add(new PixelCheck { name = name, passed = passed, measured = value, threshold = threshold });

        [Serializable] sealed class PixelReport
        {
            public string utcTimestamp, unityVersion, graphicsApi, graphicsDevice, scope, exception, imageDirectory;
            public bool passed;
            public float[] submitAndReadbackMilliseconds;
            public PixelCheck[] checks;
        }
        [Serializable] sealed class PixelCheck { public string name; public bool passed; public float measured, threshold; }
    }
}
