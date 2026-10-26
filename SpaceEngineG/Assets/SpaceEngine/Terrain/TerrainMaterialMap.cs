using System;
using System.Diagnostics;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Terrain
{
    /// <summary>
    /// A seed-derived surface-height image, independent of the visible mesh LOD.
    /// The compute shader evaluates the unchanged CPU field, including its 64-bit
    /// hash. No painted coastline or unrelated color noise substitutes for geology.
    /// </summary>
    public sealed class TerrainMaterialMap : IDisposable
    {
        public RenderTexture Texture { get; private set; }
        public int Resolution { get; private set; }
        public double DispatchCpuMilliseconds { get; private set; }
        public long EstimatedBytes => Texture == null ? 0 : (long)Resolution * Resolution * 6 * 4 * 4 / 3;
        static ComputeShader baker;

        public bool Bake(PlanetDefinition planet, int requestedResolution)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports2DArrayTextures || !planet.IsLandable) return false;
            if (baker == null) baker = Resources.Load<ComputeShader>("SpaceEngineTerrainSurface");
            if (baker == null) return false;
            int resolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(requestedResolution, 64, 2048));
            Dispose();
            Resolution = resolution;
            Texture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                name = $"Seed {planet.Seed} surface height {resolution}x{resolution}x6",
                dimension = TextureDimension.Tex2DArray, volumeDepth = 6,
                enableRandomWrite = true, useMipMap = true, autoGenerateMips = false,
                filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4
            };
            if (!Texture.Create()) { Dispose(); return false; }
            var timer = Stopwatch.StartNew();
            int kernel = baker.FindKernel("BakeSurface");
            Configure(baker, planet);
            baker.SetInt("_MapResolution", resolution);
            baker.SetTexture(kernel, "_SurfaceOutput", Texture);
            baker.Dispatch(kernel, (resolution + 7) / 8, (resolution + 7) / 8, 6);
            Texture.GenerateMips();
            DispatchCpuMilliseconds = timer.Elapsed.TotalMilliseconds;
            return true;
        }

        /// <summary>Shared by the production baker and GPU/CPU parity tests.</summary>
        public static void Configure(ComputeShader shader, PlanetDefinition planet)
        {
            shader.SetInt("_TerrainSeed", planet.Seed);
            shader.SetInt("_TerrainKind", (int)planet.Kind);
            shader.SetInt("_TerrainHasOcean", planet.HasOcean ? 1 : 0);
            shader.SetFloat("_TerrainRadius", (float)planet.Radius);
            shader.SetFloat("_TerrainAmplitude", (float)planet.TerrainHeight);
            var centers = new Vector4[10];
            var strengths = new Vector4[10];
            var random = new DeterministicRandom(DeterministicRandom.Hash(planet.Seed, 719, 3121, 9973));
            for (int i = 0; i < centers.Length; i++)
            {
                double y = random.Range(-1.0, 1.0), phase = random.Range(0, Math.PI * 2);
                double horizontal = Math.Sqrt(1 - y * y), angularRadius = random.Range(.045, .16);
                centers[i] = new Vector4((float)(horizontal * Math.Cos(phase)), (float)y,
                    (float)(horizontal * Math.Sin(phase)), (float)(1 / (2 * (1 - Math.Cos(angularRadius)))));
                strengths[i] = new Vector4((float)random.Range(.55, 1.0), 0, 0, 0);
            }
            shader.SetVectorArray("_CraterCenters", centers);
            shader.SetVectorArray("_CraterStrengths", strengths);
        }

        public void Dispose()
        {
            if (Texture == null) return;
            Texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
            else UnityEngine.Object.DestroyImmediate(Texture);
            Texture = null;
        }
    }
}
