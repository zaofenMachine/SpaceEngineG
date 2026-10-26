using System;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Terrain.Tests
{
    public sealed class TerrainGeometryTests
    {
        static PlanetDefinition Earth => new PlanetDefinition
        {
            Name = "Test Earth", Seed = 123456, Radius = 6371000, TerrainHeight = 12000,
            HasOcean = true, SurfaceTint = new Color(.3f, .4f, .2f)
        };

        [Test]
        public void FaceChartsMeetAtExactSharedDirections()
        {
            for (int i = 0; i <= 24; i++)
            {
                double t = -1 + i / 12.0;
                Assert.That((CubeSphere.Direction(0, -1, t) - CubeSphere.Direction(4, 1, t)).Length, Is.LessThan(1e-14));
                Assert.That((CubeSphere.Direction(0, 1, t) - CubeSphere.Direction(5, -1, t)).Length, Is.LessThan(1e-14));
                Assert.That((CubeSphere.Direction(2, 1, t) - CubeSphere.Direction(0, t, 1)).Length, Is.LessThan(1e-14));
            }
        }

        [Test]
        public void EveryFaceHasOutwardTrianglesAndValidSkirts()
        {
            const int resolution = 8;
            for (int face = 0; face < 6; face++)
            {
                var data = TerrainPatchMesh.Build(new TerrainPatchKey(face, 0, 0, 0), Earth, resolution);
                Assert.That(data.Vertices.Length, Is.EqualTo(81 + 32));
                Assert.That(data.Triangles.Length, Is.EqualTo(8 * 8 * 6 + 32 * 6));
                for (int i = 0; i < resolution * resolution * 6; i += 3)
                {
                    Vector3 a = data.Vertices[data.Triangles[i]];
                    Vector3 b = data.Vertices[data.Triangles[i + 1]];
                    Vector3 c = data.Vertices[data.Triangles[i + 2]];
                    Double3 absolute = data.Anchor + Double3.FromVector3(a);
                    Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a), absolute.Normalized.ToVector3()), Is.GreaterThan(0));
                }
                foreach (int index in data.Triangles) Assert.That(index, Is.InRange(0, data.Vertices.Length - 1));
                foreach (Vector3 normal in data.Normals) Assert.That(Math.Abs(normal.magnitude - 1), Is.LessThan(1e-5));
            }
        }

        [Test]
        public void SameSeedAndAddressBuildBitwiseIdenticalGeometry()
        {
            var key = new TerrainPatchKey(3, 7, 27, 72);
            var first = TerrainPatchMesh.Build(key, Earth, 8);
            var second = TerrainPatchMesh.Build(key, Earth, 8);
            CollectionAssert.AreEqual(first.Vertices, second.Vertices);
            CollectionAssert.AreEqual(first.Normals, second.Normals);
            CollectionAssert.AreEqual(first.Colors, second.Colors);
            CollectionAssert.AreEqual(first.Triangles, second.Triangles);
        }

        [Test]
        public void EarthSurfacePatchRetainsCentimeterCameraMotion()
        {
            var key = new TerrainPatchKey(0, 20, 524288, 524288);
            var data = TerrainPatchMesh.Build(key, Earth, 12);
            Double3 camera = data.Anchor + new Double3(2, 0, 0);
            Vector3 before = (data.Anchor - camera).ToVector3();
            Vector3 after = (data.Anchor - (camera + new Double3(.01, 0, 0))).ToVector3();
            Assert.That(Math.Abs((after.x - before.x) + .01), Is.LessThan(2e-6));
            Assert.That(data.Vertices[0].magnitude, Is.LessThan(20));
            Assert.That((data.Vertices[0] - data.Vertices[1]).magnitude, Is.GreaterThan(.1));
        }

        [Test]
        public void NormalsRemainContinuousAcrossCubeChartBoundary()
        {
            var a = CubeSphere.Direction(0, -1, .376);
            var b = CubeSphere.Direction(4, 1, .376);
            Assert.That((TerrainPatchMesh.SurfaceNormal(a, Earth) - TerrainPatchMesh.SurfaceNormal(b, Earth)).Length, Is.LessThan(1e-12));
        }

        [Test]
        public void NearSurfaceLodConvergesWithinBoundedResidentAndLeafBudgets()
        {
            var gameObject = new GameObject("Terrain LOD test");
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            try
            {
                var terrain = gameObject.AddComponent<PlanetTerrain>();
                terrain.PatchResolution = 4;
                terrain.BuildsPerFrame = 32;
                terrain.BuildBudgetMilliseconds = 1000;
                terrain.MaximumVisiblePatches = 128;
                terrain.MaximumResidentPatches = 256;
                var definition = Earth;
                terrain.Configure(definition, material);
                Double3 direction = new Double3(1, .123, .456).Normalized;
                Double3 camera = TerrainPatchMesh.SurfacePoint(direction, definition) + direction * 2;
                for (int i = 0; i < 16 && (i == 0 || terrain.PendingBuildCount > 0); i++)
                    terrain.Tick(camera, 1, Vector3.zero, Vector3.one.normalized);
                Assert.That(terrain.PendingBuildCount, Is.EqualTo(0));
                Assert.That(terrain.ActivePatchCount, Is.InRange(1, 128));
                Assert.That(terrain.MaxActiveLevel, Is.GreaterThanOrEqualTo(18));
                Assert.That(terrain.ResidentPatchCount, Is.LessThanOrEqualTo(256));
                Assert.That(terrain.BuiltThisFrame, Is.LessThanOrEqualTo(32));
                Assert.That(terrain.TriangleCount, Is.GreaterThan(0));
                terrain.Tick(direction * definition.Radius * 1000, .000001, Vector3.zero, Vector3.up);
                Assert.That(terrain.ActivePatchCount, Is.LessThan(16));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
