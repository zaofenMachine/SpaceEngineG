using System;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Rendering.Tests
{
    public sealed class PlanetCloudRenderingTests
    {
        [Test]
        public void WeatherLayerOnlyExistsForOceanicAndNeverCreatesCollider()
        {
            WithCloud((cloud, template) =>
            {
                cloud.Configure(Definition(PlanetKind.Oceanic), template);
                Assert.That(cloud.HasCloudLayer, Is.True);
                Assert.That(cloud.GetComponentsInChildren<Collider>().Length, Is.Zero);
                cloud.Configure(Definition(PlanetKind.Rocky), template);
                Assert.That(cloud.HasCloudLayer, Is.False);
                Assert.That(cloud.GetComponentsInChildren<MeshFilter>().Length, Is.Zero);
            });
        }

        [Test]
        public void WeatherRadiusAndCameraRelativePositionSharePhysicalRenderScale()
        {
            WithCloud((cloud, template) =>
            {
                var definition = Definition(PlanetKind.Oceanic);
                cloud.Configure(definition, template);
                Double3 from = new Double3(2e7, -1e7, -2e7);
                cloud.Tick(from, .0001, Vector3.back, 0);
                Transform shell = cloud.GetComponentInChildren<MeshFilter>().transform;
                Assert.That((shell.position + (from * .0001).ToVector3()).magnitude, Is.LessThan(.001));
                Assert.That(shell.localScale.x, Is.EqualTo((definition.Radius + cloud.EffectiveHeightMeters) * .0001).Within(.001));
                float angularRadius = shell.localScale.x / shell.position.magnitude;
                cloud.Tick(from, .0000001, Vector3.back, 0);
                Assert.That(shell.localScale.x / shell.position.magnitude, Is.EqualTo(angularRadius).Within(1e-7));
            });
        }

        [Test]
        public void LayerFadesBeforeEntryAndStaysHiddenAtGroundAndWhenDisabled()
        {
            WithCloud((cloud, template) =>
            {
                var definition = Definition(PlanetKind.Oceanic);
                cloud.Configure(definition, template);
                cloud.Tick(new Double3(0, 0, definition.Radius * 3), .001, Vector3.back, 0);
                Assert.That(cloud.Visible, Is.True);
                cloud.Tick(new Double3(0, 0, definition.Radius + cloud.CloudHeightMeters * 1.5), .001, Vector3.back, 0);
                Assert.That(cloud.ObserverFade, Is.InRange(.01f, .99f));
                cloud.Tick(new Double3(0, 0, definition.Radius + cloud.CloudHeightMeters), .001, Vector3.back, 0);
                Assert.That(cloud.Visible, Is.False);
                cloud.Tick(new Double3(0, 0, definition.Radius + 2), .001, Vector3.back, 0);
                Assert.That(cloud.Visible, Is.False);
                cloud.Tick(new Double3(0, 0, definition.Radius * 3), .001, Vector3.back, 0);
                cloud.enabled = false;
                Assert.That(cloud.Visible, Is.False);
                cloud.Tick(new Double3(0, 0, definition.Radius * 3), .001, Vector3.back, 0);
                Assert.That(cloud.Visible, Is.False);
            });
        }

        [Test]
        public void SeedAndWeatherClockAreExplicitAndTemplateRemainsUnmodified()
        {
            WithCloud((cloud, template) =>
            {
                var definition = Definition(PlanetKind.Oceanic);
                cloud.Configure(definition, template);
                cloud.Tick(new Double3(0, 0, definition.Radius * 3), .001, Vector3.back, 86400);
                Material first = cloud.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                float seed = first.GetFloat("_Seed"), phase = first.GetFloat("_WindPhase");
                Assert.That(phase, Is.EqualTo(cloud.WindRate).Within(1e-6));
                Assert.That(template.GetFloat("_WindPhase"), Is.Zero);
                cloud.Configure(definition, template);
                cloud.Tick(new Double3(0, 0, definition.Radius * 3), .001, Vector3.back, 86400);
                Material second = cloud.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                Assert.That(second.GetFloat("_Seed"), Is.EqualTo(seed));
                Assert.That(second.GetFloat("_WindPhase"), Is.EqualTo(phase));
                definition.Seed++;
                cloud.Configure(definition, template);
                cloud.Tick(new Double3(0, 0, definition.Radius * 3), .001, Vector3.back, 0);
                Assert.That(cloud.GetComponentInChildren<MeshRenderer>().sharedMaterial.GetFloat("_Seed"), Is.Not.EqualTo(seed));
            });
        }

        [Test]
        public void InvalidCoordinatesAndZeroCoverageHideActualShell()
        {
            WithCloud((cloud, template) =>
            {
                var definition = Definition(PlanetKind.Oceanic);
                cloud.Configure(definition, template);
                cloud.Tick(new Double3(0, 0, definition.Radius * 3), double.NaN, Vector3.back, 0);
                Assert.That(cloud.Visible, Is.False);
                cloud.Coverage = 0;
                cloud.Tick(new Double3(0, 0, definition.Radius * 3), .001, Vector3.back, 0);
                Assert.That(cloud.Visible, Is.False);
                Assert.That(cloud.TriangleCount, Is.Zero);
            });
        }

        static PlanetDefinition Definition(PlanetKind kind) => new PlanetDefinition
        { Name = "Weather test", Seed = 9371, Kind = kind, Radius = 6e6, RotationPeriod = 86400 };

        static void WithCloud(Action<PlanetCloudRenderer, Material> action)
        {
            var root = new GameObject("Temporary cloud tests");
            var template = new Material(Shader.Find("SpaceEngine/PlanetCloud"));
            try
            {
                var renderer = root.AddComponent<PlanetCloudRenderer>();
                renderer.LongitudeSegments = 48; renderer.LatitudeSegments = 24;
                action(renderer, template);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(template); }
        }
    }
}
