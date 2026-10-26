using System;
using NUnit.Framework;
using SpaceEngine.Core;

namespace SpaceEngine.Tests
{
    public class GalaxyTests
    {
        [Test]
        public void HomeIsSolarRadiusAndGalacticCenterMapsToItsUniversalAnchor()
        {
            GalaxyDefinition g = GalaxyModel.Default(42);
            Double3 home = GalaxyModel.ToGalacticLy(UniversalPosition.Origin, g);
            Assert.That(home.x, Is.EqualTo(g.SolarRadiusLy));
            Assert.That(home.y, Is.Zero); Assert.That(home.z, Is.Zero);
            Assert.That(GalaxyModel.FromGalacticLy(home, g).Equals(UniversalPosition.Origin), Is.True);
            Assert.That(GalaxyModel.FromGalacticLy(Double3.Zero, g).Equals(GalaxyModel.Center(g)), Is.True);
            Assert.That(GalaxyModel.ToGalacticLy(GalaxyModel.Center(g), g).Length, Is.LessThan(1e-10));
        }

        [Test]
        public void GalacticConversionHasScaleAwarePrecisionAndFlightKeepsMillimetres()
        {
            GalaxyDefinition g = GalaxyModel.Default(123);
            var nearby = UniversalPosition.Origin.Add(new Double3(1234567.89, 0.007, -0.001));
            var roundTrip = GalaxyModel.FromGalacticLy(GalaxyModel.ToGalacticLy(nearby, g), g);
            Assert.That(Math.Abs(roundTrip.RelativeTo(nearby).x), Is.LessThan(65536), "Light-year doubles near the solar radius quantize to tens of km.");
            Assert.That(Math.Abs(roundTrip.RelativeTo(nearby).y), Is.LessThan(1e-12));
            Assert.That(Math.Abs(roundTrip.RelativeTo(nearby).z), Is.LessThan(1e-12));
            Double3 galactic = new Double3(-120000.12345, 71543.12345, 8301.23123);
            Double3 restored = GalaxyModel.ToGalacticLy(GalaxyModel.FromGalacticLy(galactic, g), g);
            Assert.That((galactic - restored).Length, Is.LessThan(1e-9));
            UniversalPosition far = GalaxyModel.FromGalacticLy(galactic, g);
            Assert.That(far.Add(new Double3(0.001, 0, 0)).RelativeTo(far).x, Is.EqualTo(0.001).Within(0.0001));
        }

        [Test]
        public void LocalDensityIsPhysicalConfiguredNumberPerCubicParsec()
        {
            GalaxyDefinition g = GalaxyModel.Default(17);
            Assert.That(GalaxyModel.SampleDensity(new Double3(g.SolarRadiusLy, 0, 0), g), Is.EqualTo(0.1).Within(1e-14));
            g.LocalNumberDensityPerCubicParsec = 0.25;
            Assert.That(GalaxyModel.SampleDensity(new Double3(g.SolarRadiusLy, 0, 0), g), Is.EqualTo(0.25).Within(1e-14));
            GalaxyDensity components = GalaxyModel.SampleDensityComponents(new Double3(g.SolarRadiusLy, 0, 0), g);
            Assert.That(components.Total, Is.EqualTo(components.ThinDisk + components.ThickDisk + components.SpiralArms + components.Bulge + components.Bar + components.StellarHalo));
        }

        [Test]
        public void DiskFallsOffRadiallyAndVerticallyWithFiniteHaloBoundary()
        {
            GalaxyDefinition g = GalaxyModel.Default(456);
            GalaxyDensity inner = GalaxyModel.SampleDensityComponents(new Double3(10000, 0, 0), g);
            GalaxyDensity outer = GalaxyModel.SampleDensityComponents(new Double3(40000, 0, 0), g);
            GalaxyDensity high = GalaxyModel.SampleDensityComponents(new Double3(10000, 10000, 0), g);
            Assert.That(inner.ThinDisk, Is.GreaterThan(outer.ThinDisk * 20));
            Assert.That(inner.ThinDisk, Is.GreaterThan(high.ThinDisk * 1000));
            Assert.That(high.ThickDisk / inner.ThickDisk, Is.GreaterThan(high.ThinDisk / inner.ThinDisk));
            Assert.That(GalaxyModel.SampleDensityComponents(new Double3(g.RadiusLy, 0, 0), g).ThinDisk, Is.Zero);
            Assert.That(GalaxyModel.SampleDensity(new Double3(g.StellarHaloRadiusLy, 0, 0), g), Is.Zero);
            Assert.That(GalaxyModel.SampleDensity(new Double3(0, g.StellarHaloRadiusLy * 2, 0), g), Is.Zero);
        }

        [Test]
        public void SpiralArmsHaveSeededPhaseAndMeasurableDensityContrast()
        {
            GalaxyDefinition g = GalaxyModel.Default(991);
            const double radius = 30000;
            double angle = GalaxyModel.ArmAngle(radius, g);
            Double3 onArm = new Double3(radius * Math.Cos(angle), 0, radius * Math.Sin(angle));
            double gapAngle = angle + Math.PI / g.ArmCount;
            Double3 betweenArms = new Double3(radius * Math.Cos(gapAngle), 0, radius * Math.Sin(gapAngle));
            GalaxyDensity arm = GalaxyModel.SampleDensityComponents(onArm, g);
            GalaxyDensity gap = GalaxyModel.SampleDensityComponents(betweenArms, g);
            Assert.That(arm.SpiralArms, Is.GreaterThan(gap.SpiralArms * 100));
            Assert.That(arm.Total, Is.GreaterThan(gap.Total * 1.4));
            Assert.That(GalaxyModel.ArmAngle(radius, GalaxyModel.Default(992)), Is.Not.EqualTo(angle));
        }

        [Test]
        public void BarDensityFollowsItsConfiguredOrientation()
        {
            GalaxyDefinition g = GalaxyModel.Default(13);
            double radius = g.BarLengthLy * 0.7;
            Double3 along = new Double3(radius * Math.Cos(g.BarAngleRadians), 0, radius * Math.Sin(g.BarAngleRadians));
            Double3 across = new Double3(-radius * Math.Sin(g.BarAngleRadians), 0, radius * Math.Cos(g.BarAngleRadians));
            Assert.That(GalaxyModel.SampleDensityComponents(along, g).Bar, Is.GreaterThan(0));
            Assert.That(GalaxyModel.SampleDensityComponents(across, g).Bar, Is.Zero);
        }

        [Test]
        public void RepresentativesAreDeterministicDifferentSeedsDifferAndCountPreservesPrefix()
        {
            GalaxyDefinition g = GalaxyModel.Default(-273);
            GalaxyParticle[] a = GalaxyModel.GenerateRepresentatives(g, 256);
            GalaxyParticle[] b = GalaxyModel.GenerateRepresentatives(g, 512);
            GalaxyParticle[] other = GalaxyModel.GenerateRepresentatives(GalaxyModel.Default(-274), 256);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.That(a[i].PositionLy.Equals(b[i].PositionLy), Is.True);
                Assert.That(a[i].Color, Is.EqualTo(b[i].Color));
                Assert.That(a[i].Luminosity, Is.EqualTo(b[i].Luminosity));
                Assert.That(a[i].SizeLy, Is.EqualTo(b[i].SizeLy));
                Assert.That(a[i].Component, Is.EqualTo(b[i].Component));
                Assert.That(a[i].PositionLy.Equals(other[i].PositionLy), Is.False);
            }
        }

        [Test]
        public void AllSixComponentsOccupyThreeDimensionsAndRemainWithinTheirBounds()
        {
            GalaxyDefinition g = GalaxyModel.Default(33);
            GalaxyParticle[] particles = GalaxyModel.GenerateRepresentatives(g, 12000);
            var positiveHeight = new int[6]; var negativeHeight = new int[6]; var counts = new int[6];
            double[] heightSquares = new double[6];
            foreach (GalaxyParticle particle in particles)
            {
                Double3 p = particle.PositionLy;
                int component = (int)particle.Component;
                counts[component]++; heightSquares[component] += p.y * p.y;
                if (p.y > 0) positiveHeight[component]++;
                if (p.y < 0) negativeHeight[component]++;
                Assert.That(p.Length, Is.LessThan(g.StellarHaloRadiusLy));
                Assert.That(particle.Luminosity, Is.GreaterThan(0)); Assert.That(particle.SizeLy, Is.GreaterThan(0));
                if (particle.Component <= GalaxyComponent.SpiralArms)
                {
                    Assert.That(Math.Sqrt(p.x * p.x + p.z * p.z), Is.LessThan(g.RadiusLy));
                    double h = particle.Component == GalaxyComponent.ThickDisk ? g.ThickDiskHeightLy : g.ThinDiskHeightLy;
                    Assert.That(Math.Abs(p.y), Is.LessThanOrEqualTo(8 * h));
                }
                if (particle.Component == GalaxyComponent.Bulge)
                    Assert.That(Math.Sqrt(p.x * p.x + p.z * p.z), Is.LessThan(g.BulgeRadiusLy));
            }
            for (int i = 0; i < counts.Length; i++)
            {
                Assert.That(counts[i], Is.GreaterThan(100), ((GalaxyComponent)i).ToString());
                Assert.That(positiveHeight[i], Is.GreaterThan(30)); Assert.That(negativeHeight[i], Is.GreaterThan(30));
            }
            Assert.That(heightSquares[(int)GalaxyComponent.ThickDisk] / counts[(int)GalaxyComponent.ThickDisk],
                Is.GreaterThan(heightSquares[(int)GalaxyComponent.ThinDisk] / counts[(int)GalaxyComponent.ThinDisk] * 5));
        }

        [Test]
        public void ConfigurableVeryNarrowAndVeryBroadDiscsGenerateBoundedSamples()
        {
            GalaxyDefinition g = GalaxyModel.Default(112);
            foreach (double scale in new[] { 1.0, 1e12 })
            {
                g.DiskScaleLengthLy = scale;
                GalaxyParticle[] particles = GalaxyModel.GenerateRepresentatives(g, 1024);
                foreach (GalaxyParticle particle in particles)
                {
                    Assert.That(double.IsNaN(particle.PositionLy.Length), Is.False);
                    Assert.That(particle.PositionLy.Length, Is.LessThan(g.StellarHaloRadiusLy));
                }
            }
        }

        [Test]
        public void CentralMassMustBePositiveAndFiniteBeforeSaveSettingsAreAccepted()
        {
            foreach (double mass in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                GalaxyDefinition g = GalaxyModel.Default(5);
                g.CentralMassSolarMasses = mass;
                Assert.Throws<ArgumentOutOfRangeException>(() => GalaxyModel.SampleDensity(Double3.Zero, g));
                Assert.Throws<ArgumentOutOfRangeException>(() => GalaxyModel.GenerateRepresentatives(g, 1));
            }
        }

        [Test]
        public void InvalidConfigurationAndNegativeCountsFailExplicitly()
        {
            GalaxyDefinition g = GalaxyModel.Default(0);
            Assert.Throws<ArgumentOutOfRangeException>(() => GalaxyModel.GenerateRepresentatives(g, -1));
            Assert.That(GalaxyModel.GenerateRepresentatives(g, 0).Length, Is.Zero);
            g.ArmCount = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => GalaxyModel.GenerateRepresentatives(g, 1));
            g.ArmCount = 4; g.ThinDiskHeightLy = double.NaN;
            Assert.Throws<ArgumentOutOfRangeException>(() => GalaxyModel.SampleDensity(Double3.Zero, g));
        }
    }
}
