using System;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Terrain.Tests
{
    public sealed class TypedTerrainGeometryTests
    {
        static PlanetDefinition Body(PlanetKind kind) => new PlanetDefinition
        {
            Name = kind.ToString(), Kind = kind, Seed = 1234, Radius = 6371000,
            TerrainHeight = 12000, HasOcean = kind == PlanetKind.Oceanic,
            SurfaceTint = new Color(.4f, .35f, .3f)
        };

        [TestCase(PlanetKind.Rocky)]
        [TestCase(PlanetKind.Ice)]
        [TestCase(PlanetKind.Desert)]
        [TestCase(PlanetKind.Lava)]
        public void TypedPatchSamplesTheActualHeightFieldAndKeepsPhysicalNormals(PlanetKind kind)
        {
            var body = Body(kind);
            var key = new TerrainPatchKey(0, 8, 128, 128);
            var data = TerrainPatchMesh.Build(key, body, 8);
            CubeSphere.Bounds(key, out double u, out double v, out double span);
            for (int y = 0; y <= 8; y++) for (int x = 0; x <= 8; x++)
            {
                int index = y * 9 + x;
                Double3 direction = CubeSphere.Direction(key.Face, u + span * x / 8, v + span * y / 8);
                double actualRadius = (data.Anchor + Double3.FromVector3(data.Vertices[index])).Length;
                Assert.That(actualRadius, Is.EqualTo(body.Radius + TerrainNoise.Height(direction, body)).Within(.02));
                Assert.That(data.Normals[index].magnitude, Is.EqualTo(1).Within(1e-5));
                Assert.That((Double3.FromVector3(data.Normals[index]) - TerrainPatchMesh.SurfaceNormal(direction, body, .25)).Length,
                    Is.LessThan(1e-6));
            }
        }

        [Test]
        public void OnlyLavaPatchVerticesCarryFissureEmissionIncludingSkirts()
        {
            foreach (PlanetKind kind in new[] { PlanetKind.Oceanic, PlanetKind.Rocky, PlanetKind.Desert, PlanetKind.Ice, PlanetKind.Lava })
            {
                var body = Body(kind);
                var key = new TerrainPatchKey(0, 0, 0, 0);
                var data = TerrainPatchMesh.Build(key, body, 16);
                bool dark = false, emissive = false;
                foreach (Color color in data.Colors)
                {
                    Assert.That(color.a, Is.InRange(0f, 1f));
                    if (kind != PlanetKind.Lava) Assert.That(color.a, Is.Zero);
                    else { dark |= color.a < .01f; emissive |= color.a > .9f; }
                }
                if (kind == PlanetKind.Lava) Assert.That(dark && emissive, Is.True);
                int surfaceCount = 17 * 17;
                for (int edge = 0; edge < 64; edge++)
                {
                    int quad = 16 * 16 * 6 + edge * 12;
                    Assert.That(data.Colors[surfaceCount + edge], Is.EqualTo(data.Colors[data.Triangles[quad]]));
                }
            }
        }

        [TestCase(PlanetKind.Ice)]
        [TestCase(PlanetKind.Lava)]
        public void TypedTerrainPreservesCubeChartSeamNormalsAndDeterministicMesh(PlanetKind kind)
        {
            var body = Body(kind);
            Double3 a = CubeSphere.Direction(0, -1, .376);
            Double3 b = CubeSphere.Direction(4, 1, .376);
            Assert.That((TerrainPatchMesh.SurfaceNormal(a, body) - TerrainPatchMesh.SurfaceNormal(b, body)).Length, Is.LessThan(1e-12));
            var key = new TerrainPatchKey(3, 7, 27, 72);
            var first = TerrainPatchMesh.Build(key, body, 4);
            var second = TerrainPatchMesh.Build(key, body, 4);
            CollectionAssert.AreEqual(first.Vertices, second.Vertices);
            CollectionAssert.AreEqual(first.Normals, second.Normals);
            CollectionAssert.AreEqual(first.Colors, second.Colors);
        }

        [TestCase(PlanetKind.Lava, 1f)]
        [TestCase(PlanetKind.Ice, 0f)]
        public void TerrainSuppliesExplicitLavaFlagSeedWordsAndBoundedPlanetCoordinates(PlanetKind kind, float expectedFlag)
        {
            var gameObject = new GameObject("Lava shader coordinate test");
            var material = new Material(Shader.Find("SpaceEngine/Terrain"));
            try
            {
                var terrain = gameObject.AddComponent<PlanetTerrain>();
                terrain.PatchResolution = 4;
                terrain.MaximumLevel = 1;
                terrain.MaximumVisiblePatches = 24;
                terrain.BuildsPerFrame = 32;
                terrain.BuildBudgetMilliseconds = 1000;
                terrain.HorizonCulling = false;
                terrain.RefinementThreshold = 12;
                terrain.OrbitalMinimumLevel = 0;
                var body = Body(kind);
                body.Seed = unchecked((int)0xf123abcdu);
                terrain.Configure(body, material);
                terrain.Tick(new Double3(body.Radius * 3, 0, 0), .001, Vector3.zero, Vector3.right);
                int inspected = 0;
                foreach (var renderer in terrain.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!renderer.enabled) continue;
                    inspected++;
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Assert.That(properties.GetFloat("_IsLava"), Is.EqualTo(expectedFlag));
                    Vector4 seed = properties.GetVector("_LavaSeed");
                    Assert.That(seed.x, Is.EqualTo(0xabcdu));
                    Assert.That(seed.y, Is.EqualTo(0xf123u));
                    Vector4 anchor = properties.GetVector("_PatchAnchor");
                    Assert.That(new Vector3(anchor.x, anchor.y, anchor.z).magnitude, Is.InRange(.99f, 1.01f));
                    Assert.That(anchor.w, Is.EqualTo((float)(1 / body.Radius)));
                }
                Assert.That(inspected, Is.EqualTo(6), "Each root patch needs its own bounded anchor uniform.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
