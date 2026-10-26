using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Editor
{
    /// <summary>
    /// Actual URP render/readback checks, separate from shader compilation and the
    /// explorer's route screenshots. Temporary test geometry is restricted to an
    /// otherwise unused camera layer and is destroyed after the check.
    /// </summary>
    public static class AtmospherePixelValidation
    {
        private const int TestLayer = 31;
        private const float Radius = 6371000f;
        private const float AtmosphereHeight = 100000f;

        [MenuItem("Space Engine/Validate Atmosphere Pixels")]
        public static void ValidatePixels()
        {
            var report = new PixelReport
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                colorSpace = QualitySettings.activeColorSpace.ToString(),
                scope = "64x64 floating-point GPU renders via RenderPipeline.SubmitRenderRequest. Checks sky/vacuum, scale invariance and opaque depth termination. Does not replace terrain or full-route image review."
            };
            GameObject root = null;
            Material atmosphereMaterial = null;
            Material terrainMaterial = null;
            Mesh fixtureMesh = null;
            RenderTexture target = null;
            var checks = new List<PixelCheck>();
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("A real graphics API is required; -nographics cannot run pixel checks.");
                if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                    throw new InvalidOperationException("ARGBFloat render targets are unsupported on this device.");
                // Do not silently share a test layer with live universe geometry.
                foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (renderer.gameObject.layer == TestLayer && renderer.enabled)
                        throw new InvalidOperationException("Layer 31 is already used by " + renderer.name + "; pixel isolation cannot be guaranteed.");

                var atmosphereShader = Shader.Find("SpaceEngine/Atmosphere");
                var fixtureShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (atmosphereShader == null || fixtureShader == null)
                    throw new InvalidOperationException("Atmosphere or the independent URP/Unlit fixture shader is unavailable.");
                atmosphereMaterial = new Material(atmosphereShader);
                ShaderUtil.CompilePass(atmosphereMaterial, 0, true);
                atmosphereMaterial.EnableKeyword("_ATMOSPHERE_EXTINCTION");
                ShaderUtil.CompilePass(atmosphereMaterial, 0, true);
                atmosphereMaterial.DisableKeyword("_ATMOSPHERE_EXTINCTION");
                Color fixtureColor = new Color(0.2f, 0.35f, 0.1f, 1f);
                // Material Color properties are converted from sRGB to linear
                // when bound to a shader in a linear-color-space project.
                Color expectedFixtureColor = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? fixtureColor.linear : fixtureColor;
                terrainMaterial = new Material(fixtureShader);
                terrainMaterial.SetColor("_BaseColor", fixtureColor);
                for (int pass = 0; pass < terrainMaterial.passCount; pass++)
                    ShaderUtil.CompilePass(terrainMaterial, pass, true);

                root = new GameObject("Temporary atmosphere pixel validation")
                {
                    layer = TestLayer, hideFlags = HideFlags.HideAndDontSave
                };
                var camera = root.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << TestLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = Radius * 4f;
                camera.fieldOfView = 50f;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                var atmosphere = root.AddComponent<AtmosphereRenderer>();
                atmosphere.Configure(camera, atmosphereMaterial);
                // Configure the real test atmosphere BEFORE disabling it; the
                // initial vacuum must not rely on uninitialized sphere geometry.
                atmosphere.UpdateAtmosphere(new Vector3(0f, -Radius - 2f, 0f), Radius,
                    Radius + AtmosphereHeight, Vector3.up, 1.6f);
                target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                {
                    name = "Atmosphere numerical pixel target", antiAliasing = 1
                };
                target.Create();
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new InvalidOperationException("The active render pipeline does not support StandardRequest.");

                atmosphere.enabled = false;
                Color vacuum = Capture(camera, target, request);
                checks.Add(Check("Vacuum renders black", vacuum.maxColorComponent < 0.0001f, vacuum, Color.black, 0.0001f));

                atmosphere.enabled = true;
                atmosphere.UpdateAtmosphere(new Vector3(0f, -Radius - 2f, 0f), Radius,
                    Radius + AtmosphereHeight, Vector3.up, 1.6f);
                Color sky = Capture(camera, target, request);
                checks.Add(Check("Terrestrial zenith scatters blue light", sky.b > sky.r + 0.005f && sky.b > 0.01f && sky.maxColorComponent < 0.6f,
                    sky, vacuum, 0.005f));

                atmosphere.enabled = false;
                Color disabledSky = Capture(camera, target, request);
                checks.Add(Check("Disabling the configured component removes visible scattering", disabledSky.maxColorComponent < 0.0001f,
                    disabledSky, Color.black, 0.0001f));
                atmosphere.enabled = true;

                const float scale = 0.001f;
                atmosphere.UpdateAtmosphere(new Vector3(0f, (-Radius - 2f) * scale, 0f), Radius * scale,
                    (Radius + AtmosphereHeight) * scale, Vector3.up, 1.6f);
                Color scaledSky = Capture(camera, target, request);
                checks.Add(Check("Render scale preserves optical appearance", Difference(sky, scaledSky) < 0.003f,
                    scaledSky, sky, 0.003f));

                // A nearby opaque patch looks upward, so there is no analytic
                // planet intersection to hide a missing camera depth texture.
                var fixture = new GameObject("Opaque depth termination fixture") { layer = TestLayer };
                fixture.transform.SetParent(root.transform, false);
                fixture.transform.localPosition = Vector3.forward * 10f;
                fixtureMesh = new Mesh { name = "Ten metre validation patch" };
                fixtureMesh.vertices = new[]
                {
                    new Vector3(-20,-20,0), new Vector3(20,-20,0),
                    new Vector3(20,20,0), new Vector3(-20,20,0)
                };
                fixtureMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                fixtureMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                fixtureMesh.colors = new[] { fixtureColor, fixtureColor, fixtureColor, fixtureColor };
                fixtureMesh.RecalculateBounds();
                fixture.AddComponent<MeshFilter>().sharedMesh = fixtureMesh;
                fixture.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;

                atmosphere.enabled = false;
                // The GPU resident renderer can register a newly created mesh on
                // its first submitted draw. Keep that image as evidence, then
                // sample the independent reference after one warmup request.
                Color fixtureInitialDraw = Capture(camera, target, request);
                report.fixtureRegistrationWarmupLinearRgb = Rgb(fixtureInitialDraw);
                Color clearPatch = Capture(camera, target, request);
                report.fixtureWarmupNote = Difference(fixtureInitialDraw, clearPatch) > 0.001f
                    ? "The first draw of the newly created renderer differed from the stable reference; one registration warmup draw was required."
                    : "The first draw already matched the subsequent independent reference.";
                checks.Add(Check("Independent URP unlit patch matches its linear base color", Difference(clearPatch, expectedFixtureColor) < 0.001f,
                    clearPatch, expectedFixtureColor, 0.001f));
                atmosphere.enabled = true;
                atmosphere.UpdateAtmosphere(new Vector3(0f, -Radius - 2f, 0f), Radius,
                    Radius + AtmosphereHeight, Vector3.up, 1.6f);
                Color hazedPatch = Capture(camera, target, request);
                float nearbyFogDifference = Difference(clearPatch, hazedPatch);
                checks.Add(Check("Camera depth limits measurable fog at a ten metre surface", nearbyFogDifference > 0.00001f && nearbyFogDifference < 0.003f,
                    hazedPatch, clearPatch, 0.003f));

                report.passed = true;
                foreach (var check in checks) report.passed &= check.passed;
            }
            catch (Exception exception)
            {
                report.exception = exception.ToString();
                report.passed = false;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (atmosphereMaterial != null) UnityEngine.Object.DestroyImmediate(atmosphereMaterial);
                if (terrainMaterial != null) UnityEngine.Object.DestroyImmediate(terrainMaterial);
                if (fixtureMesh != null) UnityEngine.Object.DestroyImmediate(fixtureMesh);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
            report.checks = checks.ToArray();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Artifacts", "V030", "atmosphere-pixels.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (report.passed) Debug.Log("Atmosphere GPU pixel validation passed: " + path);
            else Debug.LogError("Atmosphere GPU pixel validation failed: " + path);
        }

        private static Color Capture(Camera camera, RenderTexture target, RenderPipeline.StandardRequest request)
        {
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                RenderTexture.active = target;
                readback = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
                readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                readback.Apply(false);
                Color value = readback.GetPixel(target.width / 2, target.height / 2);
                if (float.IsNaN(value.r) || float.IsInfinity(value.r) ||
                    float.IsNaN(value.g) || float.IsInfinity(value.g) ||
                    float.IsNaN(value.b) || float.IsInfinity(value.b))
                    throw new InvalidOperationException("GPU readback contains non-finite radiance.");
                return value;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
        private static PixelCheck Check(string name, bool passed, Color actual, Color reference, float tolerance)
            => new PixelCheck { name = name, passed = passed, actualLinearRgb = Rgb(actual), referenceLinearRgb = Rgb(reference), tolerance = tolerance };
        private static float[] Rgb(Color color) => new[] { color.r, color.g, color.b };

        [Serializable]
        private sealed class PixelReport
        {
            public string utcTimestamp, unityVersion, graphicsApi, graphicsDevice, colorSpace, scope, exception;
            public string fixtureWarmupNote;
            public float[] fixtureRegistrationWarmupLinearRgb;
            public bool passed;
            public PixelCheck[] checks;
        }
        [Serializable]
        private sealed class PixelCheck
        {
            public string name;
            public bool passed;
            public float[] actualLinearRgb, referenceLinearRgb;
            public float tolerance;
        }
    }
}
