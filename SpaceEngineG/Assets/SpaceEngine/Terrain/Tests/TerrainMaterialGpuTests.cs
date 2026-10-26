using System;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceEngine.Terrain.Tests
{
    /// <summary>Readbacks compare production GPU geology with the actual unchanged CPU field.</summary>
    public sealed class TerrainMaterialGpuTests
    {
        [TestCase(9371)]
        [TestCase(-1873647123)]
        [TestCase(int.MinValue)]
        public void GpuSplitMixGradientNoiseMatchesCpuAcrossSignedSeedAndCoordinates(int seed)
        {
            RequireGpu();
            var directions = new Vector4[64];
            for (int i = 0; i < directions.Length; i++)
                directions[i] = new Vector4((i % 7 - 3) * .25f + .125f,
                    (i % 11 - 5) * .125f + .0625f, (i % 13 - 6) * .25f + .125f, 8);
            var planet = Planet(seed, PlanetKind.Oceanic);
            Vector2[] actual = Probe(planet, directions);
            double maxError = 0;
            for (int i = 0; i < directions.Length; i++)
            {
                Vector4 p = directions[i];
                double expected = TerrainNoise.Noise(new Double3(p.x * p.w, p.y * p.w, p.z * p.w), seed);
                Assert.That(float.IsNaN(actual[i].x) || float.IsNaN(actual[i].y) || float.IsInfinity(actual[i].x) || float.IsInfinity(actual[i].y),
                    Is.False, "GPU output must be finite, not a passing NaN comparison.");
                maxError = Math.Max(maxError, Math.Abs(expected - actual[i].y));
            }
            Assert.That(maxError, Is.LessThan(2e-6), "A different integer hash draws a different planet despite a sharp texture.");
        }

        [TestCase(PlanetKind.Oceanic)]
        [TestCase(PlanetKind.Desert)]
        [TestCase(PlanetKind.Rocky)]
        [TestCase(PlanetKind.Ice)]
        [TestCase(PlanetKind.Lava)]
        public void GpuHeightMapUsesActualCpuContinentsReliefCratersAndFissures(PlanetKind kind)
        {
            RequireGpu();
            var planet = Planet(-1873647123, kind);
            var random = new DeterministicRandom(719);
            var directions = new Vector4[128];
            for (int i = 0; i < directions.Length; i++)
            {
                double y = random.Range(-1.0, 1.0), angle = random.Range(0, Math.PI * 2), horizontal = Math.Sqrt(1 - y * y);
                directions[i] = new Vector4((float)(horizontal * Math.Cos(angle)), (float)y, (float)(horizontal * Math.Sin(angle)), 1);
            }
            Vector2[] actual = Probe(planet, directions);
            double maximum = 0, mean = 0;
            for (int i = 0; i < directions.Length; i++)
            {
                Vector4 p = directions[i];
                double error = Math.Abs(TerrainNoise.Height(new Double3(p.x, p.y, p.z), planet) - actual[i].x);
                Assert.That(float.IsNaN(actual[i].x) || float.IsInfinity(actual[i].x), Is.False, "Height probe returned nonfinite output.");
                maximum = Math.Max(maximum, error); mean += error;
            }
            TestContext.WriteLine($"{kind} GPU/CPU height error maximum={maximum:F6}m mean={mean / directions.Length:F6}m across {directions.Length} positions");
            // Float direction normalization at an Earth-sized radius introduces
            // sub-metre differences in the unchanged six-metre physical band.
            Assert.That(maximum, Is.LessThan(3), "Orbital material zero-contours must represent the CPU landing field.");
        }

        [Test]
        public void MaterialCubeChartsMeetAndHaveMipLevelsWithoutChangingGeometry()
        {
            RequireGpu();
            var planet = Planet(9371, PlanetKind.Oceanic);
            var key = new TerrainPatchKey(0, 8, 128, 129);
            var before = TerrainPatchMesh.Build(key, planet, 4);
            using (var map = new TerrainMaterialMap())
            {
                Assert.That(map.Bake(planet, 64), Is.True);
                Assert.That(map.Texture.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
                Assert.That(map.Texture.volumeDepth, Is.EqualTo(6));
                Assert.That(map.Texture.mipmapCount, Is.EqualTo(7));
                var request = AsyncGPUReadback.Request(map.Texture, 0);
                request.WaitForCompletion();
                Assert.That(request.hasError, Is.False);
                var face0 = request.GetData<float>(0);
                var face4 = request.GetData<float>(4);
                double edgeError = 0;
                for (int y = 0; y < 64; y++)
                    edgeError = Math.Max(edgeError, Math.Abs(face0[y * 64] - face4[y * 64 + 63]));
                Assert.That(edgeError, Is.LessThan(.1), "Neighbouring cube faces must evaluate identical shared edge directions.");
            }
            var after = TerrainPatchMesh.Build(key, planet, 4);
            CollectionAssert.AreEqual(before.Vertices, after.Vertices);
            CollectionAssert.AreEqual(before.Normals, after.Normals);
            CollectionAssert.AreEqual(before.SurfaceData, after.SurfaceData);
        }

        [Test]
        public void MaterialRawHeightChannelPreservesSeabedWhileVisibleGeometryClampsOceanDatum()
        {
            var planet = Planet(9371, PlanetKind.Oceanic);
            int negative = 0;
            for (int face = 0; face < 6; face++)
            {
                var mesh = TerrainPatchMesh.Build(new TerrainPatchKey(face, 0, 0, 0), planet, 4);
                for (int i = 0; i < 5 * 5; i++)
                {
                    if (mesh.SurfaceData[i].x >= 0) continue;
                    negative++;
                    Double3 position = mesh.Anchor + Double3.FromVector3(mesh.Vertices[i]);
                    Assert.That(Math.Abs(position.Length - planet.Radius), Is.LessThan(1));
                }
            }
            Assert.That(negative, Is.GreaterThan(0));
        }

        static PlanetDefinition Planet(int seed, PlanetKind kind) => new PlanetDefinition
        {
            Name = "GPU material parity", Seed = seed, Kind = kind, Radius = 6371000,
            TerrainHeight = 12000, HasOcean = kind == PlanetKind.Oceanic,
            SurfaceTint = new Color(.3f,.4f,.2f)
        };

        static void RequireGpu()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("This regression requires a real compute-capable GPU and texture readback.");
        }

        static Vector2[] Probe(PlanetDefinition planet, Vector4[] directions)
        {
            ComputeShader shader = null;
            ComputeBuffer input = null, output = null, integerOutput = null;
            try
            {
                shader = Object.Instantiate(Resources.Load<ComputeShader>("SpaceEngineTerrainSurface"));
                TerrainMaterialMap.Configure(shader, planet);
                int kernel = shader.FindKernel("ProbeSurface");
                input = new ComputeBuffer(directions.Length, sizeof(float) * 4);
                output = new ComputeBuffer(directions.Length, sizeof(float) * 2);
                input.SetData(directions);
                shader.SetInt("_ProbeCount", directions.Length);
                shader.SetBuffer(kernel, "_ProbeDirections", input);
                shader.SetBuffer(kernel, "_ProbeOutput", output);
                shader.Dispatch(kernel, (directions.Length + 63) / 64, 1, 1);
                var actual = new Vector2[directions.Length];
                output.GetData(actual);
                int integerKernel = shader.FindKernel("ProbeInteger");
                integerOutput = new ComputeBuffer(directions.Length, sizeof(uint) * 4);
                shader.SetBuffer(integerKernel, "_ProbeDirections", input);
                shader.SetBuffer(integerKernel, "_IntegerProbeOutput", integerOutput);
                shader.Dispatch(integerKernel, (directions.Length + 63) / 64, 1, 1);
                var integers = new uint[directions.Length * 4]; integerOutput.GetData(integers);
                for (int i = 0; i < directions.Length; i++)
                {
                    Vector4 p = directions[i];
                    long x=(long)Math.Floor(p.x*p.w),y=(long)Math.Floor(p.y*p.w)+1,z=(long)Math.Floor(p.z*p.w);
                    ulong expectedHash = DeterministicRandom.Hash(planet.Seed,x,y,z);
                    ulong hash = integers[i*4] | ((ulong)integers[i*4+1] << 32);
                    Assert.That(hash, Is.EqualTo(expectedHash), $"64-bit signed corner hash differs at ({x},{y},{z}) seed {planet.Seed}");
                    Assert.That(integers[i*4+2], Is.EqualTo((uint)(expectedHash % 12)), "Gradient table selection must use all64 hash bits.");
                }
                return actual;
            }
            finally
            {
                input?.Dispose(); output?.Dispose(); integerOutput?.Dispose();
                if (shader != null) Object.DestroyImmediate(shader);
            }
        }
    }
}
