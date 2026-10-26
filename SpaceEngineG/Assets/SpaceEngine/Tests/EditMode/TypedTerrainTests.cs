using System;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Tests
{
    public sealed class TypedTerrainTests
    {
        static PlanetDefinition Body(PlanetKind kind, int seed = 1234) => new PlanetDefinition
        {
            Kind = kind, Seed = seed, Radius = PhysicalConstants.EarthRadius,
            TerrainHeight = 12000, HasOcean = kind == PlanetKind.Oceanic,
            SurfaceTint = new Color(.4f, .35f, .3f)
        };

        static Double3 Direction(int i, int count)
        {
            double y = 1 - 2 * (i + .5) / count;
            double azimuth = i * Math.PI * (3 - Math.Sqrt(5));
            double radius = Math.Sqrt(1 - y * y);
            return new Double3(radius * Math.Cos(azimuth), y, radius * Math.Sin(azimuth));
        }

        [Test]
        public void OceanHeightRegressionKeepsExistingLandingGeometry()
        {
            var ocean = Body(PlanetKind.Oceanic);
            Assert.That(TerrainNoise.Height(new Double3(1, .23, -.71), ocean), Is.EqualTo(156.63900907907706).Within(1e-8));
            Assert.That(TerrainNoise.Height(new Double3(-.6, .54, .2), ocean), Is.EqualTo(-3531.919988030159).Within(1e-8));
            Assert.That(TerrainNoise.Height(new Double3(.07, -1, .43), ocean), Is.EqualTo(-603.0733708196369).Within(1e-8));
        }

        [Test]
        public void EveryKindRetainsSeededBoundedMetreHeightAndNonLavaHasNoEmission()
        {
            foreach (PlanetKind kind in Enum.GetValues(typeof(PlanetKind)))
            for (int seed = -2; seed <= 2; seed++)
            {
                var body = Body(kind, seed);
                for (int i = 0; i < 128; i++)
                {
                    Double3 direction = Direction(i, 128);
                    double height = TerrainNoise.Height(direction, body);
                    Assert.That(height, Is.InRange(-body.TerrainHeight, body.TerrainHeight));
                    Assert.That(TerrainNoise.Height(direction, body), Is.EqualTo(height));
                    if (!body.IsLandable) Assert.That(height, Is.Zero);
                    Color color = TerrainNoise.SurfaceColor(direction, height, body);
                    if (kind != PlanetKind.Lava) Assert.That(color.a, Is.Zero);
                    else Assert.That(color.a, Is.InRange(0f, 1f));
                }
            }
        }

        [Test]
        public void RockyCraterFieldChangesActualHeightsWithBowlsAndRaisedRims()
        {
            var rocky = Body(PlanetKind.Rocky);
            var uncratered = Body(PlanetKind.Desert);
            double minimum = 0, maximum = 0;
            for (int i = 0; i < 4096; i++)
            {
                Double3 direction = Direction(i, 4096);
                double difference = TerrainNoise.Height(direction, rocky) - TerrainNoise.Height(direction, uncratered);
                minimum = Math.Min(minimum, difference); maximum = Math.Max(maximum, difference);
            }
            Assert.That(minimum, Is.LessThan(-rocky.TerrainHeight * .05));
            Assert.That(maximum, Is.GreaterThan(rocky.TerrainHeight * .015));
        }

        [Test]
        public void IcePaletteIsBrightBlueAndDesertHasHeightDependentStrata()
        {
            var ice = Body(PlanetKind.Ice);
            Color frozen = TerrainNoise.SurfaceColor(new Double3(1, .1, .2), 2000, ice);
            Assert.That(frozen.b, Is.GreaterThanOrEqualTo(frozen.r));
            Assert.That((frozen.r + frozen.g + frozen.b) / 3, Is.GreaterThan(.8f));
            var desert = Body(PlanetKind.Desert);
            float minimum = 1, maximum = 0;
            for (int i = 0; i < 32; i++)
            {
                Color strata = TerrainNoise.SurfaceColor(new Double3(1, .1, .2), i * 130, desert);
                Assert.That(strata.r, Is.GreaterThan(strata.b * 1.8f));
                minimum = Math.Min(minimum, strata.r); maximum = Math.Max(maximum, strata.r);
            }
            Assert.That(maximum - minimum, Is.GreaterThan(.25f));
        }

        [Test]
        public void LavaHasDarkCrustBrightFissuresAndGeometricTroughs()
        {
            var lava = Body(PlanetKind.Lava);
            var unfractured = Body(PlanetKind.Desert);
            bool crust = false, molten = false, trough = false;
            for (int i = 0; i < 256; i++)
            {
                Double3 direction = Direction(i, 256);
                double height = TerrainNoise.Height(direction, lava);
                Color color = TerrainNoise.SurfaceColor(direction, height, lava);
                if (color.a < .01f) { crust = true; Assert.That(color.r, Is.LessThan(.07f)); }
                if (color.a > .9f)
                {
                    molten = true; Assert.That(color.r, Is.GreaterThan(.85f));
                    Assert.That(color.r, Is.GreaterThan(color.g * 4));
                    trough |= TerrainNoise.Height(direction, unfractured) - height > 100;
                }
            }
            Assert.That(crust && molten && trough, Is.True);
        }
    }
}
