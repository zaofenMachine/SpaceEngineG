using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SpaceEngine.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Editor
{
    /// <summary>
    /// Actual near-tangent color and DepthOnly masks versus an independent double
    /// ray/sphere reference. Low mesh density deliberately magnifies chord errors.
    /// </summary>
    public static class GasHorizonPixelValidation
    {
        const int Width = 512, Height = 256, TestLayer = 28;
        const double Radius = 68849000, Altitude = 10000;
        const float Fov = 6, Near = .000001f, Far = .1f;
        public static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
            "Artifacts", "V050", "GasHorizonPixels", "gas-horizon-pixels.json"));

        [MenuItem("Space Engine/Validate Gas Horizon Pixels")]
        public static void ValidatePixels()
        {
            var report = new Report
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                scope = "Real floating-point 512x256 URP near-tangent silhouettes at 10 km above a 68,849 km cloud reference. Color and explicit DepthOnly draws are compared to independent double-precision pixel-center ray/sphere intersections. 24x12 and 384x192 meshes must show the same physical silhouette; polygon chords cannot pass this check."
            };
            var checks = new List<Check>();
            string directory = Path.GetDirectoryName(ReportPath); Directory.CreateDirectory(directory);
            GameObject root = null; Material gas = null, ring = null, blue = null, diagnosticAlways = null, depthProbe = null;
            Mesh quad = null, probeQuad = null; RenderTexture target = null;
            Vector4 previousCamera = Shader.GetGlobalVector("_WorldSpaceCameraPos");
            Vector4 previousProjection = Shader.GetGlobalVector("_ProjectionParams");
            Matrix4x4 previousView = Shader.GetGlobalMatrix("unity_MatrixV");
            Matrix4x4 previousInverseView = Shader.GetGlobalMatrix("unity_MatrixInvV");
            Matrix4x4 previousViewProjection = Shader.GetGlobalMatrix("unity_MatrixVP");
            Matrix4x4 previousProjectionMatrix = Shader.GetGlobalMatrix("glstate_matrix_projection");
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("A real graphics device is required.");
                foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (renderer.gameObject.layer == TestLayer && renderer.enabled) throw new InvalidOperationException("Gas horizon layer 28 is in use.");
                root = new GameObject("Temporary analytic gas horizon validation") { hideFlags = HideFlags.HideAndDontSave };
                var cameraObject = new GameObject("Near-tangent validation camera"); cameraObject.transform.SetParent(root.transform);
                Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.cullingMask = 1 << TestLayer; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.nearClipPlane = Near; camera.farClipPlane = Far; camera.fieldOfView = Fov; camera.aspect = (float)Width / Height;
                camera.allowHDR = true; camera.allowMSAA = false;
                gas = new Material(Shader.Find("SpaceEngine/GasGiant")); ring = new Material(Shader.Find("SpaceEngine/PlanetRing"));
                blue = new Material(Shader.Find("Universal Render Pipeline/Unlit")); blue.SetColor("_BaseColor", Color.blue);
                blue.SetFloat("_Cull", (float)CullMode.Off);
                for (int pass = 0; pass < gas.passCount; pass++) ShaderUtil.CompilePass(gas, pass, true);
                for (int pass = 0; pass < blue.passCount; pass++) ShaderUtil.CompilePass(blue, pass, true);
                var body = new GameObject("Near-tangent physical reference"); body.transform.SetParent(root.transform);
                CelestialBodyRenderer celestial = body.AddComponent<CelestialBodyRenderer>();
                var definition = new PlanetDefinition
                { Name = "10 km gas horizon", Seed = 9371, Kind = PlanetKind.GasGiant, Radius = Radius, RotationPeriod = 36000, AxialTilt = .47 };
                Double3 from = new Double3(0, Radius + Altitude, 0);
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                { antiAliasing = 1, name = "Gas horizon float readback" }; target.Create();
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP StandardRequest is unavailable.");
                bool[] expected = ExpectedMask(0);
                Configure(celestial, definition, gas, ring, from, 24, 12);
                Capture(camera, target, request); // Register the replacement renderer before explicit pass draws.
                Capture(camera, target, request); Color[] coarse = Capture(camera, target, request);
                Save(coarse, Path.Combine(directory, "01-coarse-24x12-analytic-horizon.png"));
                int coarseError = MaskDifference(coarse, expected, false);
                Add(checks, "Coarse raster mesh preserves independent analytic near-horizon silhouette", coarseError <= 32, coarseError, 32);
                Configure(celestial, definition, gas, ring, from, 384, 192);
                Capture(camera, target, request); Color[] dense = Capture(camera, target, request);
                Save(dense, Path.Combine(directory, "02-dense-384x192-horizon-control.png"));
                int denseError = MaskDifference(dense, expected, false);
                Add(checks, "Production mesh preserves independent analytic near-horizon silhouette", denseError <= 32, denseError, 32);
                int meshDifference = BinaryDifference(coarse, dense);
                Add(checks, "Near-horizon coverage is independent of raster mesh density", meshDifference <= 8, meshDifference, 8);
                SaveMask(expected, Path.Combine(directory, "03-independent-double-ray-reference.png"));

                // Explicitly render the actual DepthOnly pass, then draw an
                // independent opaque plane through the expected sphere depth.
                // This fails if the depth pass retains the old polygon chords.
                Configure(celestial, definition, gas, ring, from, 24, 12);
                Capture(camera, target, request);
                const float planeDepth = .008f;
                float halfHeight = Mathf.Tan(Fov * Mathf.Deg2Rad * .5f) * planeDepth;
                float halfWidth = halfHeight * camera.aspect * 1.01f; halfHeight *= 1.01f;
                quad = new Mesh { name = "Independent depth-test plane" };
                quad.vertices = new[] { new Vector3(-halfWidth,-halfHeight,planeDepth), new Vector3(halfWidth,-halfHeight,planeDepth),
                    new Vector3(halfWidth,halfHeight,planeDepth), new Vector3(-halfWidth,halfHeight,planeDepth) };
                quad.triangles = new[] { 0,2,1,0,3,2 }; quad.RecalculateBounds();
                quad.colors = new[] { Color.blue, Color.blue, Color.blue, Color.blue };
                report.cpuClipOracle = new Vector4[4];
                Matrix4x4 gpuViewProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
                for (int i = 0; i < 4; i++) report.cpuClipOracle[i] = gpuViewProjection * new Vector4(quad.vertices[i].x, quad.vertices[i].y, quad.vertices[i].z, 1);
                // Independent plane-only controls identify camera/depth-state
                // setup failures separately from the production depth pass.
                Color[] clearZero = DiagnosticPlane(camera,target,quad,blue,0);
                Color[] clearOne = DiagnosticPlane(camera,target,quad,blue,1);
                Save(clearZero,Path.Combine(directory,"06-plane-only-clear-zero.png"));
                Save(clearOne,Path.Combine(directory,"07-plane-only-clear-one.png"));
                report.clearZeroBluePixels = BluePixels(clearZero);
                report.clearOneBluePixels = BluePixels(clearOne);
                Shader internalColor = Shader.Find("Hidden/Internal-Colored");
                if (internalColor != null)
                {
                    diagnosticAlways = new Material(internalColor);
                    diagnosticAlways.SetInt("_ZTest", (int)CompareFunction.Always);
                    diagnosticAlways.SetInt("_ZWrite", 0); diagnosticAlways.SetInt("_Cull", (int)CullMode.Off);
                    diagnosticAlways.SetInt("_SrcBlend", (int)BlendMode.One); diagnosticAlways.SetInt("_DstBlend", (int)BlendMode.Zero);
                    for (int pass = 0; pass < diagnosticAlways.passCount; pass++) ShaderUtil.CompilePass(diagnosticAlways,pass,true);
                    Color[] always = DiagnosticPlane(camera,target,quad,diagnosticAlways,1);
                    Save(always,Path.Combine(directory,"08-plane-only-ztest-always-control.png"));
                    report.alwaysBluePixels = BluePixels(always);
                }
                Add(checks,"Independent URP plane draws across the full viewport with engine-standard depth clear",
                    report.clearOneBluePixels == Width*Height,report.clearOneBluePixels,Width*Height);
                var command = new CommandBuffer { name = "Actual gas DepthOnly differential" };
                try
                {
                    command.SetRenderTarget(target);
                    command.SetViewport(new Rect(0,0,Width,Height));
                    // CoreUtils.ClearRenderTarget passes logical far depth 1 on
                    // every API; Unity handles reverse-Z internally for clears.
                    command.ClearRenderTarget(true, true, Color.red, 1);
                    SetupMatrices(command,camera);
                    MeshRenderer renderer = celestial.GetComponentInChildren<MeshRenderer>();
                    command.DrawRenderer(renderer, renderer.sharedMaterial, 0, renderer.sharedMaterial.FindPass("DepthOnly"));
                    command.DrawMesh(quad, Matrix4x4.identity, blue, 0, 0);
                    Graphics.ExecuteCommandBuffer(command);
                }
                finally { command.Release(); }
                Color[] depth = Readback(target); Save(depth, Path.Combine(directory, "04-depth-only-independent-plane.png"));
                bool[] expectedDepth = ExpectedMask(planeDepth);
                int depthError = MaskDifference(depth, expectedDepth, true);
                Add(checks, "Actual DepthOnly uses physical sphere depth against an independent opaque plane", depthError <= 32, depthError, 32);
                SaveMask(expectedDepth, Path.Combine(directory, "05-independent-depth-occlusion-reference.png"));

                // Exercise the real active Forward+/SSAO pipeline. Its normals
                // prepass fills _CameraDepthTexture through DepthNormalsOnly;
                // a material with just DepthOnly can pass the explicit draw
                // above while still leaving a false far-plane sky in gameplay.
                depthProbe = new Material(Shader.Find("Hidden/SpaceEngine/GasDepthProbe"));
                depthProbe.SetFloat("_DepthNormalization",Far);
                for(int pass=0;pass<depthProbe.passCount;pass++) ShaderUtil.CompilePass(depthProbe,pass,true);
                var probeObject = new GameObject("Read actual camera depth and normals") { layer=TestLayer };
                probeObject.transform.SetParent(root.transform,false);
                probeQuad = new Mesh { name="Camera depth/normal readback quad" };
                probeQuad.vertices = new[] { new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0) };
                probeQuad.triangles = new[] { 0,2,1,0,3,2 }; probeQuad.bounds = new Bounds(Vector3.zero,Vector3.one*100);
                probeObject.AddComponent<MeshFilter>().sharedMesh=probeQuad;
                probeObject.AddComponent<MeshRenderer>().sharedMaterial=depthProbe;
                camera.depthTextureMode |= DepthTextureMode.DepthNormals;
                Capture(camera,target,request); Color[] actualCamera = Capture(camera,target,request);
                Texture actualNormals = Shader.GetGlobalTexture("_CameraNormalsTexture");
                Texture actualDepth = Shader.GetGlobalTexture("_CameraDepthTexture");
                // RenderGraph can unbind/reset globals before SubmitRenderRequest
                // returns. Retain these observations, but do not mistake its
                // fallback white SRGB texture for the live camera attachments.
                report.postSubmitNormalGlobalFormat = actualNormals == null ? "unavailable" : actualNormals.graphicsFormat.ToString();
                report.postSubmitDepthGlobalFormat = actualDepth == null ? "unavailable" : actualDepth.graphicsFormat.ToString();
                ResolveNormalFormatOracle(report);
                bool snorm8 = report.normalFormatOracleVerified && report.urpDeviceSelectedNormalFormat == "R8G8B8A8_SNorm";
                // The official URP normals target prefers signed normalized
                // eight-bit channels. Compare to an independent round-to-nearest
                // CPU conversion to that measured format, not unquantized doubles.
                report.normalConversionToleranceUlp = snorm8 ? .6f : 0;
                report.normalQuantizationErrorBound = snorm8 ? .6f/127 : 0;
                report.normalConversionToleranceSource="Microsoft D3D FLOAT->SNORM conversion permits 0.6 ULP on the integer side; D3D12_FLOAT32_TO_INTEGER_TOLERANCE_IN_ULP=0.6f. https://github.com/microsoft/DirectX-Headers/blob/main/include/directx/d3d12.idl ; https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#3.2.3.4%20FLOAT%20-%3E%20SNORM";
                Save(actualCamera,Path.Combine(directory,"09-actual-camera-depth-normals.png"));
                int cameraMaskErrors = 0, normalSamples = 0;
                var normalDiagnostics = new List<NormalSample>();
                double depthErrorTotal=0,normalErrorTotal=0,quantizedErrorTotal=0,maximumNormalComponentError=0;
                double cameraTangent = Math.Tan(Fov*Math.PI/360);
                Double3 cameraOrigin = new Double3(0,1+Altitude/Radius,0);
                for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
                {
                    int pixel=x+y*Width;
                    bool hit=actualCamera[pixel].r<.99f;
                    if(hit!=expected[pixel])cameraMaskErrors++;
                    if(!hit||!expected[pixel])continue;
                    var ray=new Double3(((x+.5)/Width*2-1)*cameraTangent*Width/Height,
                        ((y+.5)/Height*2-1)*cameraTangent,1).Normalized;
                    double b=Double3.Dot(cameraOrigin,ray);
                    double discriminant=b*b-(cameraOrigin.SqrLength-1);
                    double t=-b-Math.Sqrt(Math.Max(0,discriminant));
                    Double3 normal=(cameraOrigin+ray*t).Normalized;
                    depthErrorTotal+=Math.Abs(actualCamera[pixel].r*Far-t*ray.z);
                    normalErrorTotal+=Math.Abs((actualCamera[pixel].g*2-1)-normal.y)+Math.Abs((actualCamera[pixel].b*2-1)-normal.z);
                    maximumNormalComponentError=Math.Max(maximumNormalComponentError,Math.Max(
                        Math.Abs((actualCamera[pixel].g*2-1)-normal.y),Math.Abs((actualCamera[pixel].b*2-1)-normal.z)));
                    double expectedY=normal.y,expectedZ=normal.z;
                    if(snorm8)
                    {
                        expectedY=Math.Round(Math.Max(-1,Math.Min(1,expectedY))*127,MidpointRounding.ToEven)/127;
                        expectedZ=Math.Round(Math.Max(-1,Math.Min(1,expectedZ))*127,MidpointRounding.ToEven)/127;
                    }
                    quantizedErrorTotal+=Math.Abs((actualCamera[pixel].g*2-1)-expectedY)+Math.Abs((actualCamera[pixel].b*2-1)-expectedZ);
                    if((x==64||x==192||x==320||x==448)&&(y==8||y==32||y==56||y==76))
                        normalDiagnostics.Add(new NormalSample
                        {
                            x=x,y=y,actualEncodedG=actualCamera[pixel].g,actualEncodedB=actualCamera[pixel].b,
                            actualNormalY=actualCamera[pixel].g*2-1,actualNormalZ=actualCamera[pixel].b*2-1,
                            analyticNormalY=(float)normal.y,analyticNormalZ=(float)normal.z,
                            quantizedNormalY=(float)expectedY,quantizedNormalZ=(float)expectedZ,
                            actualEyeDepth=actualCamera[pixel].r*Far,analyticEyeDepth=(float)(t*ray.z)
                        });
                    normalSamples++;
                }
                report.actualCameraDepthMeanError=(float)(depthErrorTotal/Math.Max(1,normalSamples));
                report.actualCameraNormalMeanError=(float)(normalErrorTotal/Math.Max(1,normalSamples));
                report.normalSamples=normalDiagnostics.ToArray();
                report.formatQuantizedNormalMeanError=(float)(quantizedErrorTotal/Math.Max(1,normalSamples));
                report.maximumNormalComponentError=(float)maximumNormalComponentError;
                report.maximumNormalConversionErrorUlp=(float)(maximumNormalComponentError*127);
                Add(checks,"Active URP camera depth prepass preserves physical near-horizon coverage",
                    cameraMaskErrors<=32,cameraMaskErrors,32);
                Add(checks,"Active URP depth/normal textures contain analytic sphere values rather than far sky",
                    report.normalFormatOracleVerified&&normalSamples>1000&&report.actualCameraDepthMeanError<.00001f&&
                    (snorm8?report.maximumNormalConversionErrorUlp<=report.normalConversionToleranceUlp:
                        report.maximumNormalComponentError<.002f),
                    snorm8?report.maximumNormalConversionErrorUlp:report.maximumNormalComponentError,
                    snorm8?report.normalConversionToleranceUlp:.002f);
                report.passed = true; foreach (Check check in checks) report.passed &= check.passed;
            }
            catch (Exception exception) { report.exception = exception.ToString(); report.passed = false; }
            finally
            {
                Shader.SetGlobalVector("_WorldSpaceCameraPos", previousCamera); Shader.SetGlobalVector("_ProjectionParams", previousProjection);
                Shader.SetGlobalMatrix("unity_MatrixV", previousView); Shader.SetGlobalMatrix("unity_MatrixInvV", previousInverseView);
                Shader.SetGlobalMatrix("unity_MatrixVP", previousViewProjection); Shader.SetGlobalMatrix("glstate_matrix_projection", previousProjectionMatrix);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (gas != null) UnityEngine.Object.DestroyImmediate(gas); if (ring != null) UnityEngine.Object.DestroyImmediate(ring);
                if (diagnosticAlways != null) UnityEngine.Object.DestroyImmediate(diagnosticAlways);
                if (depthProbe != null) UnityEngine.Object.DestroyImmediate(depthProbe);
                if (probeQuad != null) UnityEngine.Object.DestroyImmediate(probeQuad);
                if (blue != null) UnityEngine.Object.DestroyImmediate(blue); if (quad != null) UnityEngine.Object.DestroyImmediate(quad);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
            report.checks = checks.ToArray(); File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            if (report.passed) Debug.Log("Gas horizon GPU pixel validation passed: " + ReportPath);
            else Debug.LogError("Gas horizon GPU pixel validation failed: " + ReportPath);
        }

        static void Configure(CelestialBodyRenderer celestial, PlanetDefinition definition, Material gas, Material ring,
            Double3 from, int longitude, int latitude)
        {
            celestial.LongitudeSegments = longitude; celestial.LatitudeSegments = latitude;
            celestial.Configure(definition, gas, ring);
            foreach (Transform child in celestial.GetComponentsInChildren<Transform>()) child.gameObject.layer = TestLayer;
            celestial.AmbientFloor = 1; celestial.SunIntensity = 0; celestial.StormStrength = 0;
            celestial.Tick(from, 1 / Radius, Vector3.up, 0);
            Material appearance = celestial.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            appearance.SetColor("_LightCloudColor", Color.white); appearance.SetColor("_DarkCloudColor", Color.white);
        }
        static bool[] ExpectedMask(double planeDepth)
        {
            var result = new bool[Width * Height];
            double tangent = Math.Tan(Fov * Math.PI / 360), aspect = (double)Width / Height;
            Double3 origin = new Double3(0, 1 + Altitude / Radius, 0);
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                var ray = new Double3(((x + .5) / Width * 2 - 1) * tangent * aspect,
                    ((y + .5) / Height * 2 - 1) * tangent, 1).Normalized;
                double b = Double3.Dot(origin, ray), discriminant = b * b - (origin.SqrLength - 1);
                double t = discriminant >= 0 ? -b - Math.Sqrt(discriminant) : -1;
                result[x + y * Width] = t > 0 && (planeDepth <= 0 || t * ray.z < planeDepth);
            }
            return result;
        }
        static int MaskDifference(Color[] actual, bool[] expected, bool depth)
        {
            int count = 0;
            for (int i = 0; i < actual.Length; i++)
            {
                bool covered = depth ? actual[i].b < .5f : actual[i].r > .5f;
                if (covered != expected[i]) count++;
            }
            return count;
        }
        static int BinaryDifference(Color[] a, Color[] b)
        { int count = 0; for (int i=0; i<a.Length; i++) if ((a[i].r > .5f) != (b[i].r > .5f)) count++; return count; }
        static Color[] Capture(Camera camera, RenderTexture target, RenderPipeline.StandardRequest request)
        { RenderPipeline.SubmitRenderRequest(camera, request); return Readback(target); }
        static Color[] DiagnosticPlane(Camera camera,RenderTexture target,Mesh quad,Material material,float clearDepth)
        {
            var command = new CommandBuffer { name="Independent plane-only depth setup control" };
            try
            {
                command.SetRenderTarget(target); command.SetViewport(new Rect(0,0,Width,Height));
                command.ClearRenderTarget(true,true,Color.red,clearDepth); SetupMatrices(command,camera);
                command.DrawMesh(quad,Matrix4x4.identity,material,0,0); Graphics.ExecuteCommandBuffer(command);
            }
            finally { command.Release(); }
            return Readback(target);
        }
        static void SetupMatrices(CommandBuffer command,Camera camera)
        {
            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix,true);
            command.SetViewProjectionMatrices(view,camera.projectionMatrix);
            command.SetGlobalMatrix("unity_MatrixV",view); command.SetGlobalMatrix("unity_MatrixInvV",view.inverse);
            command.SetGlobalMatrix("unity_MatrixVP",projection*view); command.SetGlobalMatrix("glstate_matrix_projection",projection);
            command.SetGlobalVector("_WorldSpaceCameraPos",Vector4.zero);
            command.SetGlobalVector("_ProjectionParams",new Vector4(-1,Near,Far,1/Far));
        }
        static int BluePixels(Color[] values)
        { int count=0;foreach(Color value in values)if(value.b>.5f&&value.r<.5f)count++;return count; }
        static void ResolveNormalFormatOracle(Report report)
        {
            const BindingFlags instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var pipeline=GraphicsSettings.currentRenderPipeline;
            object renderer=pipeline.GetType().GetProperty("scriptableRenderer",instance)?.GetValue(pipeline);
            report.activeRendererMode=renderer?.GetType().GetProperty("renderingModeActual",instance)?.GetValue(renderer)?.ToString()??"unavailable";
            Type normalPass=Type.GetType("UnityEngine.Rendering.Universal.Internal.DepthNormalOnlyPass, Unity.RenderPipelines.Universal.Runtime");
            MethodInfo chooseFormat=normalPass?.GetMethod("GetGraphicsFormat",BindingFlags.Public|BindingFlags.Static);
            report.urpDeviceSelectedNormalFormat=chooseFormat?.Invoke(null,null)?.ToString()??"unavailable";
            // UniversalRendererRenderGraph.CreateCameraNormalsTexture uses this
            // exact hardware-selected method for Forward/ForwardPlus. Deferred
            // uses a GBuffer format and therefore is intentionally not inferred.
            report.normalFormatOracleVerified=(report.activeRendererMode=="Forward"||report.activeRendererMode=="ForwardPlus")&&chooseFormat!=null;
            report.normalFormatOracleSource="Active UniversalRenderer.renderingModeActual plus live DepthNormalOnlyPass.GetGraphicsFormat() device selection; UniversalRendererRenderGraph.CreateCameraNormalsTexture uses that method for non-deferred rendering. Post-submit global textures are not used as attachment evidence.";
        }
        static Color[] Readback(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active; var texture = new Texture2D(Width,Height,TextureFormat.RGBAFloat,false,true);
            try
            {
                RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,Width,Height),0,0); texture.Apply(false);
                Color[] values = texture.GetPixels();
                foreach (Color color in values) if (!float.IsFinite(color.r) || !float.IsFinite(color.g) || !float.IsFinite(color.b))
                    throw new InvalidOperationException("Gas horizon contains non-finite radiance.");
                return values;
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
        }
        static void SaveMask(bool[] mask, string path)
        { var colors = new Color[mask.Length]; for (int i=0; i<mask.Length; i++) colors[i] = mask[i] ? Color.white : Color.black; Save(colors,path); }
        static void Save(Color[] values, string path)
        {
            var texture = new Texture2D(Width,Height,TextureFormat.RGBA32,false,true);
            try { texture.SetPixels(values); texture.Apply(false); File.WriteAllBytes(path,texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        static void Add(List<Check> checks, string name, bool passed, float value, float threshold)
            => checks.Add(new Check { name=name, passed=passed, measured=value, threshold=threshold });
        [Serializable] sealed class Report
        {
            public string utcTimestamp,unityVersion,graphicsApi,graphicsDevice,scope,exception;
            public bool passed; public Check[] checks;
            public int clearZeroBluePixels,clearOneBluePixels,alwaysBluePixels;
            public float actualCameraDepthMeanError,actualCameraNormalMeanError;
            public string postSubmitNormalGlobalFormat,postSubmitDepthGlobalFormat,activeRendererMode;
            public string urpDeviceSelectedNormalFormat,normalFormatOracleSource;
            public bool normalFormatOracleVerified;
            public float normalQuantizationErrorBound,formatQuantizedNormalMeanError;
            public float normalConversionToleranceUlp,maximumNormalComponentError,maximumNormalConversionErrorUlp;
            public string normalConversionToleranceSource;
            public Vector4[] cpuClipOracle;
            public NormalSample[] normalSamples;
        }
        [Serializable] sealed class NormalSample
        {
            public int x,y;
            public float actualEncodedG,actualEncodedB,actualNormalY,actualNormalZ;
            public float analyticNormalY,analyticNormalZ,quantizedNormalY,quantizedNormalZ;
            public float actualEyeDepth,analyticEyeDepth;
        }
        [Serializable] sealed class Check { public string name; public bool passed; public float measured,threshold; }
    }
}
