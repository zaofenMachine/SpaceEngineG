using System;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering.Tests
{
    public sealed class GalaxyVolumeTests
    {
        [Test]
        public void SmallCameraRotationInvalidatesTheVolumeWhileStationaryViewsStayCached()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("This cache regression submits a real small galactic volume target.");
            var root = new GameObject("Small galactic rotation fixture");
            var target = new RenderTexture(64, 64, 0);
            try
            {
                Camera camera = root.AddComponent<Camera>();
                target.Create(); camera.targetTexture = target;
                GalaxyDefinition galaxy = GalaxyModel.Default(42);
                Double3 location = new Double3(0, 105000, 125000);
                UniversalPosition observer = GalaxyModel.FromGalacticLy(location, galaxy);
                camera.transform.rotation = Quaternion.LookRotation((-location).ToVector3(), Vector3.up);
                var volume = root.AddComponent<GalaxyVolume>();
                volume.ResolutionScale = 1; volume.SampleCount = 32; volume.Configure(galaxy);
                volume.Tick(observer, camera);
                int initialRevision = volume.RenderRevision;
                volume.Tick(observer, camera);
                Assert.That(volume.RenderRevision, Is.EqualTo(initialRevision), "A stationary view must reuse its volume.");
                Quaternion previous = camera.transform.rotation;
                camera.transform.rotation = Quaternion.AngleAxis(.02f, Vector3.up) * previous;
                Assert.That(camera.transform.rotation.Equals(previous), Is.False, "Fixture must change the actual quaternion.");
                volume.Tick(observer, camera);
                Assert.That(volume.RenderRevision, Is.EqualTo(initialRevision + 1),
                    "A 0.02 degree rotation is visible under slow movement and must not inherit Quaternion.operator== tolerance.");
                volume.Tick(observer, camera);
                Assert.That(volume.RenderRevision, Is.EqualTo(initialRevision + 1), "The resulting stationary view remains cacheable.");
                volume.DetailStrength = .4f;
                volume.Tick(observer, camera);
                Assert.That(volume.RenderRevision, Is.EqualTo(initialRevision + 2), "Physical dust-detail controls must invalidate cached radiance.");
                volume.Tick(observer, camera);
                Assert.That(volume.RenderRevision, Is.EqualTo(initialRevision + 2));
                volume.Invalidate();
                volume.Tick(observer, camera);
                Assert.That(volume.RenderRevision, Is.EqualTo(initialRevision + 3), "Profiling must be able to force a real integration at an unchanged viewpoint.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void VolumeUsesIndependentOwnedMaterialsAndFiniteFullscreenGeometry()
        {
            var root = new GameObject("Volume lifecycle fixture");
            var source = new Material(Shader.Find("SpaceEngine/GalaxyVolume"));
            var composite = new Material(Shader.Find("SpaceEngine/GalaxyVolumeComposite"));
            try
            {
                var renderer = root.AddComponent<GalaxyVolume>();
                GalaxyDefinition galaxy = GalaxyModel.Default(42);
                renderer.Configure(galaxy, source, composite);
                Assert.That(renderer.Definition, Is.SameAs(galaxy));
                Assert.That(root.GetComponent<MeshRenderer>().sharedMaterial, Is.Not.SameAs(composite));
                Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.EqualTo(4));
                foreach (Vector3 vertex in mesh.vertices)
                    Assert.That(float.IsFinite(vertex.x) && float.IsFinite(vertex.y) && vertex.z == 0, Is.True);
                renderer.enabled = false;
                Assert.That(root.GetComponent<MeshRenderer>().enabled, Is.False,
                    "Disabled optical systems must not retain stale sky renderers in Editor or play mode.");
                renderer.Configure(GalaxyModel.Default(72), source, composite);
                Assert.That(renderer.Definition.Seed, Is.EqualTo(72));
                Assert.That(source.GetFloat("_Exposure"), Is.EqualTo(1), "Template materials are caller-owned.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(composite);
            }
        }

        [Test]
        public void DefinitionUploadRetainsAllGeometricScalesAndSeedSpecificStructure()
        {
            var material = new Material(Shader.Find("SpaceEngine/GalaxyVolume"));
            try
            {
                GalaxyDefinition galaxy = GalaxyModel.Default(123);
                galaxy.RadiusLy = 61000; galaxy.ThickDiskHeightLy = 4100;
                galaxy.ArmCount = 6; galaxy.BarAngleRadians = .9;
                GalaxyVolume.SetDefinition(material, galaxy);
                Assert.That(material.GetVector("_DiskScales").x, Is.EqualTo(61000));
                Assert.That(material.GetVector("_DiskScales").w, Is.EqualTo(4100));
                Assert.That(material.GetVector("_ArmScales").x, Is.EqualTo(6));
                Assert.That(material.GetVector("_CentralScales").z, Is.EqualTo(.9).Within(1e-6));
                Vector4 first = material.GetVector("_SeedOffset");
                GalaxyVolume.SetDefinition(material, GalaxyModel.Default(123));
                Assert.That(material.GetVector("_SeedOffset"), Is.EqualTo(first));
                GalaxyVolume.SetDefinition(material, GalaxyModel.Default(124));
                Assert.That(material.GetVector("_SeedOffset"), Is.Not.EqualTo(first));
                galaxy.LocalNumberDensityPerCubicParsec = 0;
                GalaxyVolume.SetDefinition(material, galaxy);
                Assert.That(material.GetFloat("_PopulationScale"), Is.Zero,
                    "A zero-density galaxy cannot retain visible stellar populations.");
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }

        [Test]
        public void NullGalaxyCannotLeaveAnAmbiguousRenderingConfiguration()
        {
            var root = new GameObject("Invalid volume fixture");
            try { Assert.Throws<ArgumentNullException>(() => root.AddComponent<GalaxyVolume>().Configure(null)); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
