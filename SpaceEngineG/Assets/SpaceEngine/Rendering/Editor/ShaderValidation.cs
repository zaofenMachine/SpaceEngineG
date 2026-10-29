using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Editor
{
    /// <summary>
    /// Synchronously compiles the actual active-API material passes, including both
    /// atmosphere modes. Rendered image acceptance is a separate play-mode check.
    /// </summary>
    public static class ShaderValidation
    {
        private static readonly string[] ShaderNames =
        {
            "SpaceEngine/Terrain", "SpaceEngine/Atmosphere",
            "SpaceEngine/StarBillboard", "SpaceEngine/Star",
            "SpaceEngine/GasGiant", "SpaceEngine/PlanetRing", "SpaceEngine/GalaxyField",
            "SpaceEngine/GalaxyVolume", "SpaceEngine/GalaxyVolumeComposite", "SpaceEngine/PlanetCloud", "SpaceEngine/OrbitOverlay",
            "SpaceEngine/Starfield", "SpaceEngine/StarGlow", "SpaceEngine/BlackHoleLens"
        };

        public static string ReportPath => Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "..", "Artifacts", "V050", "shaders.json"));

        [MenuItem("Space Engine/Validate Shaders")]
        public static void ValidateShaders()
        {
            var report = CreateReport();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            if (report.passed) Debug.Log("Space Engine shader validation passed: " + ReportPath);
            else Debug.LogError("Space Engine shader validation failed: " + ReportPath);
        }

        // Optional -executeMethod entry point. Run with a real graphics API;
        // -nographics is intentionally not accepted as GPU support evidence.
        public static void RunBatch()
        {
            ValidateShaders();
            var report = JsonUtility.FromJson<ShaderReport>(File.ReadAllText(ReportPath));
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        public static ShaderReport CreateReport()
        {
            var report = new ShaderReport
            {
                utcTimestamp = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsShaderLevel = SystemInfo.graphicsShaderLevel,
                graphicsAvailable = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                renderPipeline = GraphicsSettings.currentRenderPipeline == null
                    ? "Built-in" : GraphicsSettings.currentRenderPipeline.GetType().FullName,
                scope = "Active graphics API support, synchronous compilation of all material passes, and ShaderUtil diagnostics. Does not verify rendered pixel appearance.",
                passed = true
            };
            var entries = new List<ShaderEntry>();
            foreach (string shaderName in ShaderNames)
            {
                var entry = ValidateOne(shaderName);
                entries.Add(entry);
                report.passed &= entry.passed;
            }
            report.shaders = entries.ToArray();
            report.passed &= report.graphicsAvailable;
            return report;
        }

        private static ShaderEntry ValidateOne(string shaderName)
        {
            var entry = new ShaderEntry { name = shaderName };
            var passes = new List<PassEntry>();
            Shader shader = Shader.Find(shaderName);
            entry.found = shader != null;
            if (shader == null)
            {
                entry.exception = "Shader.Find returned null";
                entry.messages = Array.Empty<MessageEntry>();
                entry.passes = Array.Empty<PassEntry>();
                return entry;
            }

            entry.assetPath = AssetDatabase.GetAssetPath(shader);
            entry.supported = shader.isSupported;
            Material material = null;
            try
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                CompileMaterial(material, "default", passes);
                if (shaderName == "SpaceEngine/Atmosphere")
                {
                    material.EnableKeyword("_ATMOSPHERE_EXTINCTION");
                    CompileMaterial(material, "_ATMOSPHERE_EXTINCTION", passes);
                }
                if (shaderName == "SpaceEngine/Terrain" || shaderName == "SpaceEngine/GasGiant")
                {
                    material.EnableKeyword("_GBUFFER_NORMALS_OCT");
                    CompileMaterial(material, "_GBUFFER_NORMALS_OCT", passes);
                }
            }
            catch (Exception exception)
            {
                entry.exception = exception.ToString();
            }
            finally
            {
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }

            var messages = new List<MessageEntry>();
            bool errors = false;
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
            {
                messages.Add(new MessageEntry
                {
                    severity = message.severity.ToString(),
                    message = message.message,
                    details = message.messageDetails,
                    platform = message.platform.ToString(),
                    file = message.file,
                    line = message.line
                });
                errors |= message.severity == ShaderCompilerMessageSeverity.Error;
            }
            entry.messages = messages.ToArray();
            entry.passes = passes.ToArray();
            entry.passed = entry.found && entry.supported && !errors &&
                string.IsNullOrEmpty(entry.exception) && passes.Count > 0;
            foreach (var pass in passes) entry.passed &= pass.compiled;
            return entry;
        }

        private static void CompileMaterial(Material material, string variant, List<PassEntry> output)
        {
            for (int pass = 0; pass < material.passCount; pass++)
            {
                ShaderUtil.CompilePass(material, pass, true);
                output.Add(new PassEntry
                {
                    index = pass, name = material.GetPassName(pass), variant = variant,
                    compiled = ShaderUtil.IsPassCompiled(material, pass)
                });
            }
        }

        [Serializable]
        public sealed class ShaderReport
        {
            public string utcTimestamp;
            public string unityVersion;
            public string graphicsApi;
            public string graphicsDevice;
            public int graphicsShaderLevel;
            public bool graphicsAvailable;
            public string renderPipeline;
            public string scope;
            public bool passed;
            public ShaderEntry[] shaders;
        }

        [Serializable]
        public sealed class ShaderEntry
        {
            public string name;
            public string assetPath;
            public bool found;
            public bool supported;
            public bool passed;
            public string exception;
            public PassEntry[] passes;
            public MessageEntry[] messages;
        }

        [Serializable]
        public sealed class PassEntry
        {
            public int index;
            public string name;
            public string variant;
            public bool compiled;
        }

        [Serializable]
        public sealed class MessageEntry
        {
            public string severity;
            public string message;
            public string details;
            public string platform;
            public string file;
            public int line;
        }
    }
}
