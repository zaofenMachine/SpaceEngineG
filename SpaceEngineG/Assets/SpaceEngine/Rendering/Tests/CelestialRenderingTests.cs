using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Rendering.Tests
{
    public sealed class CelestialRenderingTests
    {
        [Test]
        public void CloudReferenceMeshIsClosedAndAllTrianglesFaceOutward()
        {
            Mesh mesh = CelestialMesh.Sphere(32, 16);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(2 + 32 * 15));
                Assert.That(mesh.triangles.Length / 3, Is.EqualTo(2 * 32 * 15));
                var edges = new Dictionary<ulong, int>();
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c), Is.GreaterThan(0));
                    for (int j = 0; j < 3; j++) AddEdge(edges, triangles[i + j], triangles[i + (j + 1) % 3]);
                }
                foreach (int count in edges.Values) Assert.That(count, Is.EqualTo(2), "Cloud sphere must have no cracks or boundary edges.");
                foreach (Vector3 vertex in vertices) Assert.That(vertex.magnitude, Is.EqualTo(1).Within(1e-6));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void RingAnnulusHasOnlyItsTwoPhysicalBoundaryLoops()
        {
            Mesh mesh = CelestialMesh.Ring(1.3f, 2.4f, 64, 4);
            try
            {
                Assert.That(mesh.triangles.Length / 3, Is.EqualTo(64 * 4 * 2));
                var edges = new Dictionary<ulong, int>();
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Assert.That(Vector3.Cross(b - a, c - a).y, Is.GreaterThan(0));
                    for (int j = 0; j < 3; j++) AddEdge(edges, triangles[i + j], triangles[i + (j + 1) % 3]);
                }
                int boundary = 0;
                foreach (int count in edges.Values)
                {
                    Assert.That(count, Is.InRange(1, 2));
                    if (count == 1) boundary++;
                }
                Assert.That(boundary, Is.EqualTo(128));
                foreach (Vector3 vertex in vertices)
                {
                    Assert.That(vertex.y, Is.Zero);
                    Assert.That(vertex.magnitude, Is.InRange(1.3f - 1e-6f, 2.4f + 1e-6f));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void IdenticalResolutionBuildsIdenticalSphereAndRingGeometry()
        {
            Mesh a = CelestialMesh.Sphere(48, 24), b = CelestialMesh.Sphere(48, 24);
            Mesh c = CelestialMesh.Ring(1.3f, 2.7f, 64, 8), d = CelestialMesh.Ring(1.3f, 2.7f, 64, 8);
            try
            {
                CollectionAssert.AreEqual(a.vertices, b.vertices);
                CollectionAssert.AreEqual(a.triangles, b.triangles);
                CollectionAssert.AreEqual(c.vertices, d.vertices);
                CollectionAssert.AreEqual(c.triangles, d.triangles);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
                UnityEngine.Object.DestroyImmediate(c); UnityEngine.Object.DestroyImmediate(d);
            }
        }

        [Test]
        public void RingRejectsGeometryInsidePlanetOrInvertedEdges()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CelestialMesh.Ring(.9f, 2, 32, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => CelestialMesh.Ring(2, 1.3f, 32, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => CelestialMesh.Sphere(2, 4));
        }

        [Test]
        public void GasHasCloudGeometryAndLandableBodiesHaveNoCloudSurface()
        {
            WithRenderer((renderer, gas, ring) =>
            {
                renderer.Configure(Definition(PlanetKind.GasGiant), gas, ring);
                Assert.That(renderer.IsGasBody, Is.True);
                Assert.That(renderer.TriangleCount, Is.GreaterThan(0));
                Assert.That(renderer.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(1));
                renderer.Configure(Definition(PlanetKind.Ice), gas, ring);
                Assert.That(renderer.IsGasBody, Is.False);
                Assert.That(renderer.TriangleCount, Is.Zero);
                Assert.That(renderer.GetComponentsInChildren<MeshFilter>().Length, Is.Zero);
            });
        }

        [Test]
        public void CameraRelativeSpherePreservesAngularSizeAcrossRenderScale()
        {
            WithRenderer((renderer, gas, ring) =>
            {
                var definition = Definition(PlanetKind.GasGiant);
                renderer.Configure(definition, gas, ring);
                Double3 camera = new Double3(2e8, 3e8, -1e8);
                renderer.Tick(camera, .001, Vector3.right, 0);
                var child = renderer.GetComponentInChildren<MeshFilter>().transform;
                Assert.That((child.position - (-(camera * .001)).ToVector3()).magnitude, Is.LessThan(.1));
                float before = child.localScale.x / child.position.magnitude;
                renderer.Tick(camera, .0000001, Vector3.right, 0);
                float after = child.localScale.x / child.position.magnitude;
                Assert.That(after, Is.EqualTo(before).Within(1e-7));
            });
        }

        [Test]
        public void RotationUsesSimulationTimeAndRingPlaneUsesAxialTilt()
        {
            WithRenderer((renderer, gas, ring) =>
            {
                var definition = Definition(PlanetKind.GasGiant);
                definition.AxialTilt = .47;
                definition.RingInnerRadius = definition.Radius * 1.3;
                definition.RingOuterRadius = definition.Radius * 2.4;
                renderer.Configure(definition, gas, ring);
                renderer.Tick(new Double3(0, 0, definition.Radius * 4), .001, Vector3.right, definition.RotationPeriod * .25);
                Assert.That(renderer.RotationDegrees, Is.EqualTo(90).Within(1e-4));
                Assert.That(renderer.HasRings, Is.True);
                Transform ringTransform = renderer.GetComponentsInChildren<MeshFilter>()[1].transform;
                Assert.That(Vector3.Angle(ringTransform.up, Vector3.up), Is.EqualTo(.47 * Mathf.Rad2Deg).Within(.001));
                renderer.Tick(new Double3(0, 0, definition.Radius * 4), .001, Vector3.right, definition.RotationPeriod * 1000000000.25);
                Assert.That(renderer.RotationDegrees, Is.EqualTo(90).Within(.01));
            });
        }

        [Test]
        public void SeededMaterialsAreRepeatableAndDoNotMutateTemplate()
        {
            WithRenderer((renderer, gas, ring) =>
            {
                float templateSeed = gas.GetFloat("_Seed");
                var definition = Definition(PlanetKind.GasGiant);
                renderer.Configure(definition, gas, ring);
                renderer.Tick(new Double3(0, 0, 1e8), .001, Vector3.right, 1);
                Material first = renderer.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                float seed = first.GetFloat("_Seed"), bands = first.GetFloat("_BandCount");
                renderer.Configure(definition, gas, ring);
                renderer.Tick(new Double3(0, 0, 1e8), .001, Vector3.right, 1);
                Material second = renderer.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                Assert.That(second.GetFloat("_Seed"), Is.EqualTo(seed));
                Assert.That(second.GetFloat("_BandCount"), Is.EqualTo(bands));
                Assert.That(gas.GetFloat("_Seed"), Is.EqualTo(templateSeed));
                definition.Seed++;
                renderer.Configure(definition, gas, ring);
                renderer.Tick(new Double3(0, 0, 1e8), .001, Vector3.right, 1);
                Assert.That(renderer.GetComponentInChildren<MeshRenderer>().sharedMaterial.GetFloat("_Seed"), Is.Not.EqualTo(seed));
            });
        }

        [Test]
        public void VisibilityAndInvalidRenderScaleRemoveActualRenderers()
        {
            WithRenderer((renderer, gas, ring) =>
            {
                renderer.Configure(Definition(PlanetKind.GasGiant), gas, ring);
                renderer.Tick(new Double3(0, 0, 1e8), 1, Vector3.right, 0);
                Assert.That(renderer.TriangleCount, Is.GreaterThan(0));
                renderer.ShowRenderer = false;
                renderer.Tick(new Double3(0, 0, 1e8), 1, Vector3.right, 0);
                Assert.That(renderer.GetComponentInChildren<MeshRenderer>().enabled, Is.False);
                Assert.That(renderer.TriangleCount, Is.Zero);
                renderer.ShowRenderer = true;
                renderer.Tick(new Double3(0, 0, 1e8), double.NaN, Vector3.right, 0);
                Assert.That(renderer.GetComponentInChildren<MeshRenderer>().enabled, Is.False);
            });
        }

        [Test]
        public void DisabledGalaxyFieldHidesItsActualRendererInEditorAndTick()
        {
            var root = new GameObject("Galaxy visibility test");
            var material = new Material(Shader.Find("SpaceEngine/GalaxyField"));
            try
            {
                var field = root.AddComponent<GalaxyField>();
                Assert.Throws<ArgumentNullException>(() => field.Configure(null, 1000, material));
                Assert.Throws<ArgumentNullException>(() => field.Configure(GalaxyModel.Default(123), 1000, null));
                field.Configure(GalaxyModel.Default(123), 1000, material);
                field.Tick(UniversalPosition.Origin);
                Assert.That(root.GetComponent<MeshRenderer>().enabled, Is.True);
                field.enabled = false;
                Assert.That(root.GetComponent<MeshRenderer>().enabled, Is.False);
                field.Tick(UniversalPosition.Origin);
                Assert.That(root.GetComponent<MeshRenderer>().enabled, Is.False);
                field.enabled = true;
                field.Tick(UniversalPosition.Origin);
                Assert.That(root.GetComponent<MeshRenderer>().enabled, Is.True);
                field.ShowField = false;
                field.Tick(UniversalPosition.Origin);
                Assert.That(root.GetComponent<MeshRenderer>().enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static PlanetDefinition Definition(PlanetKind kind) => new PlanetDefinition
        {
            Name = "Renderer test", Seed = 9371, Kind = kind, Radius = 6e7,
            RotationPeriod = 36000, SurfaceTint = new Color(.8f, .5f, .25f)
        };

        static void WithRenderer(Action<CelestialBodyRenderer, Material, Material> action)
        {
            var root = new GameObject("Celestial test");
            var gas = new Material(Shader.Find("SpaceEngine/GasGiant"));
            var ring = new Material(Shader.Find("SpaceEngine/PlanetRing"));
            try
            {
                var renderer = root.AddComponent<CelestialBodyRenderer>();
                renderer.LongitudeSegments = 48; renderer.LatitudeSegments = 24;
                renderer.RingAngularSegments = 64; renderer.RingRadialSegments = 4;
                action(renderer, gas, ring);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(gas);
                UnityEngine.Object.DestroyImmediate(ring);
            }
        }

        static void AddEdge(Dictionary<ulong, int> edges, int a, int b)
        {
            ulong key = ((ulong)(uint)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }
    }
}
