using System;
using System.Linq;
using NUnit.Framework;
using SpaceEngine.Core;

namespace SpaceEngine.Tests
{
    public class PlanetScienceTests
    {
        static PlanetDefinition Earth(double eccentricity = 0) => new PlanetDefinition
        {
            Name = "Earth reference", Seed = 12345, Kind = PlanetKind.Oceanic,
            Radius = PhysicalConstants.EarthRadius, Mass = PhysicalConstants.EarthMass,
            SemiMajorAxis = PhysicalConstants.AstronomicalUnit, Eccentricity = eccentricity,
            RotationPeriod = 86164.09054, AxialTilt = .4091, AtmosphereHeight = 100000, HasOcean = true
        };
        static StarSystemDefinition Sun(params PlanetDefinition[] bodies) => new StarSystemDefinition
        {
            Name = "Solar reference", GenerationVersion = 3, Kind = StellarKind.MainSequence,
            StarMass = PhysicalConstants.SolarMass, StarRadius = PhysicalConstants.SolarRadius,
            StarLuminosity = PlanetScience.SolarLuminosityW, StarTemperature = 5772, Planets = bodies
        };
        static PlanetDefinition Moon(PlanetDefinition parent, double eccentricity = .01)
        {
            var moon = new PlanetDefinition { Name = "Reference moon", Seed = 56, Kind = PlanetKind.Rocky,
                ParentIndex = 0, Radius = 1737400, Mass = 7.342e22, SemiMajorAxis = 384400000,
                Eccentricity = eccentricity, RotationPeriod = 1 };
            moon.RotationPeriod = Orbit.Period(moon, parent.Mass); return moon;
        }
        static void Relative(double actual, double expected, double tolerance = 1e-11)
            => Assert.That(actual, Is.EqualTo(expected).Within(Math.Abs(expected) * tolerance + 1e-12));

        [Test]
        public void EarthGeometryUsesMassAndRadiusWithKnownTerrestrialScale()
        {
            var science = PlanetScience.Evaluate(Sun(Earth()), 0, 0);
            Assert.That(science.Geometry.MeanDensityKgM3, Is.EqualTo(5513.4).Within(1));
            Assert.That(science.Geometry.GravityAtReferenceRadiusMps2, Is.EqualTo(9.8203).Within(.001));
            Assert.That(science.Geometry.EscapeSpeedAtReferenceRadiusMps, Is.EqualTo(11186.2).Within(2));
            Relative(science.Geometry.DiameterMetres, 12742000);
            Assert.That(science.Geometry.HasSolidSurface, Is.True);
            Assert.That(science.Geometry.ReferenceKind, Is.EqualTo(BodyReferenceKind.SolidSurface));
        }

        [Test]
        public void SolarIrradianceAndZeroGreenhouseEarthEquilibriumHaveIndependentReferenceValues()
        {
            double flux = PlanetScience.StellarFlux(PlanetScience.SolarLuminosityW, PhysicalConstants.AstronomicalUnit);
            Assert.That(flux, Is.EqualTo(1361.1665).Within(.001));
            Assert.That(PlanetScience.EquilibriumTemperature(flux, .3), Is.EqualTo(254.586).Within(.1));
            Assert.That(PlanetScience.EquilibriumTemperature(flux, 0), Is.EqualTo(278.33).Within(.1));
            Assert.That(PlanetScience.EquilibriumTemperature(flux, 1), Is.Zero);
            Relative(PlanetScience.StellarFlux(PlanetScience.SolarLuminosityW, 2 * PhysicalConstants.AstronomicalUnit), flux / 4);
        }

        [Test]
        public void EccentricOrbitUsesTrueParentKeplerPeriodAndVisVivaAtBothApsides()
        {
            var body = Earth(.3); body.Inclination = .4; body.ArgumentPeriapsis = 1.3; body.LongitudeAscendingNode = .8;
            var system = Sun(body);
            double period = Orbit.Period(body, system.StarMass);
            var peri = PlanetScience.Evaluate(system, 0, 0).Orbit;
            var apo = PlanetScience.Evaluate(system, 0, period / 2).Orbit;
            Relative(peri.PeriodSeconds, period);
            Relative(peri.InstantaneousDistanceMetres, body.SemiMajorAxis * .7);
            Relative(apo.InstantaneousDistanceMetres, body.SemiMajorAxis * 1.3);
            Relative(peri.InstantaneousSpeedMps / apo.InstantaneousSpeedMps, 1.3 / .7);
            double mu = PhysicalConstants.GravitationalConstant * (system.StarMass + body.Mass);
            Relative(peri.InstantaneousSpeedMps * peri.InstantaneousSpeedMps / 2 - mu / peri.InstantaneousDistanceMetres,
                -mu / (2 * body.SemiMajorAxis));
            double t = period * .23, dt = .1;
            var numericalVelocity = (Orbit.Evaluate(body, system.StarMass, t + dt) - Orbit.Evaluate(body, system.StarMass, t - dt)) / (2 * dt);
            Relative(PlanetScience.Evaluate(system, 0, t).Orbit.InstantaneousSpeedMps, numericalVelocity.Length, 1e-7);
        }

        [Test]
        public void SatelliteUsesItsParentMassButStarlightFollowsTheWholeParentChain()
        {
            var earth = Earth(); var moon = Moon(earth, 0); var system = Sun(earth, moon);
            var result = PlanetScience.Evaluate(system, 1, 0);
            Assert.That(result.Orbit.ParentIndex, Is.Zero);
            Relative(result.Orbit.ParentMassKilograms, earth.Mass);
            Assert.That(result.Orbit.PeriodSeconds / 86400, Is.EqualTo(27.28).Within(.1));
            Relative(result.Orbit.InstantaneousDistanceMetres, moon.SemiMajorAxis);
            Relative(result.Orbit.StellarDistanceMetres, earth.SemiMajorAxis + moon.SemiMajorAxis);
            Relative(result.Climate.IncidentStellarFluxWm2,
                PlanetScience.StellarFlux(system.StarLuminosity, earth.SemiMajorAxis + moon.SemiMajorAxis));
            Assert.That(result.Climate.StellarFluxEarthUnits, Is.InRange(.99, 1));
            Assert.That(result.Orbit.HierarchyDepth, Is.EqualTo(2));
            Assert.That(result.Rotation.SynchronousWithParent, Is.True);
            Assert.That(result.Rotation.SolarDaySeconds, Is.Null);
            Assert.That(result.Rotation.SolarDayInfinite, Is.False);
            Assert.That(result.Caveats.HasFlag(ScienceCaveat.SolarDayRequiresMultipleOrbits), Is.True);
        }

        [Test]
        public void NestedMoonBoundsContainEverySampledActualStellarPosition()
        {
            var earth = Earth(.05); var moon = Moon(earth, .02);
            var submoon = Moon(moon, .01); submoon.ParentIndex = 1; submoon.SemiMajorAxis = 20000000;
            submoon.RotationPeriod = Orbit.Period(submoon, moon.Mass);
            var system = Sun(earth, moon, submoon);
            for (int i = 0; i < 20; i++)
            {
                var report = PlanetScience.Evaluate(system, 2, i * 86400 * 17);
                Assert.That(report.Orbit.HierarchyDepth, Is.EqualTo(3));
                Assert.That(report.Orbit.StellarDistanceMetres, Is.InRange(report.Orbit.StellarDistanceMinimumBoundMetres,
                    report.Orbit.StellarDistanceMaximumBoundMetres));
            }
        }

        [Test]
        public void GalacticTranslationDoesNotLosePrecisionOrChangeAnyLocalPhysicalQuantity()
        {
            var system = Sun(Earth(.1)); var near = PlanetScience.Evaluate(system, 0, 123456);
            system.Position = new UniversalPosition(long.MaxValue - 1, long.MinValue + 1, 1234, new Double3(12, 34, 56));
            var far = PlanetScience.Evaluate(system, 0, 123456);
            Assert.That(far.Orbit.PositionFromStarMetres.Equals(near.Orbit.PositionFromStarMetres), Is.True);
            Assert.That(far.Climate.IncidentStellarFluxWm2, Is.EqualTo(near.Climate.IncidentStellarFluxWm2));
        }

        [Test]
        public void SurfaceAndEffectiveTemperaturesObeyEnergyBalanceRatherThanLinearTemperatureAddition()
        {
            var body = Earth(); var climate = PlanetScience.EvaluateClimate(body, 1000, 8);
            double absorbed = 1000 * (1 - climate.BondAlbedo) / 4;
            Relative(PlanetScience.StefanBoltzmann * Math.Pow(climate.EquilibriumTemperatureK, 4), absorbed);
            Relative(PlanetScience.StefanBoltzmann * Math.Pow(climate.EffectiveTemperatureK, 4),
                absorbed + climate.IntrinsicHeatFluxWm2 + 8);
            Relative(Math.Pow(climate.SurfaceTemperatureK.Value, 4) * (1 - climate.InfraredEmissivity / 2),
                Math.Pow(climate.EffectiveTemperatureK, 4));
            Assert.That(climate.SurfaceTemperatureK.Value, Is.GreaterThan(climate.EffectiveTemperatureK));
        }

        [TestCase(1)] [TestCase(9371)] [TestCase(-987654321)]
        public void InverseClimateHelperPlacesTheSameSeedAtTheRequestedMeanSurfaceTemperature(int seed)
        {
            var body = Earth(); body.Seed = seed;
            double flux = PlanetScience.RequiredStellarFluxForSurfaceTemperature(body, 292);
            Assert.That(flux, Is.GreaterThan(0));
            var result = PlanetScience.EvaluateClimate(body, flux);
            Assert.That(result.SurfaceTemperatureK.Value, Is.EqualTo(292).Within(1e-10));
            Assert.That(result.LiquidWaterModelCompatible, Is.True);
            double withHeat = PlanetScience.RequiredStellarFluxForSurfaceTemperature(body, 292, 12);
            Assert.That(withHeat, Is.LessThan(flux));
            Assert.That(PlanetScience.EvaluateClimate(body, withHeat, 12).SurfaceTemperatureK.Value, Is.EqualTo(292).Within(1e-10));
        }

        [Test]
        public void ClimatePresetsAreDeterministicAndDoNotChangeAuthoritativePlanetData()
        {
            var body = Earth(); double mass = body.Mass, radius = body.Radius, axis = body.SemiMajorAxis;
            var first = PlanetScience.EvaluateClimate(body, 1200); var second = PlanetScience.EvaluateClimate(body, 1200);
            Assert.That(first.BondAlbedo, Is.EqualTo(second.BondAlbedo));
            Assert.That(first.ReferencePressurePascals, Is.EqualTo(second.ReferencePressurePascals));
            Assert.That(first.GasMoleFractions.Select(g => g.MoleFraction), Is.EqualTo(second.GasMoleFractions.Select(g => g.MoleFraction)));
            Assert.That(body.Mass, Is.EqualTo(mass)); Assert.That(body.Radius, Is.EqualTo(radius)); Assert.That(body.SemiMajorAxis, Is.EqualTo(axis));
            body.Seed++;
            Assert.That(PlanetScience.EvaluateClimate(body, 1200).BondAlbedo, Is.Not.EqualTo(first.BondAlbedo));
        }

        [TestCase(PlanetKind.Oceanic)] [TestCase(PlanetKind.Rocky)] [TestCase(PlanetKind.Desert)]
        [TestCase(PlanetKind.Ice)] [TestCase(PlanetKind.Lava)] [TestCase(PlanetKind.GasGiant)] [TestCase(PlanetKind.IceGiant)]
        public void EveryAtmosphereHasNormalizedMoleFractionsAndConsistentIdealGasProperties(PlanetKind kind)
        {
            var body = Earth(); body.Kind = kind;
            var climate = PlanetScience.EvaluateClimate(body, 1100);
            Assert.That(climate.GasMoleFractions.Sum(g => g.MoleFraction), Is.EqualTo(1).Within(1e-14));
            Assert.That(climate.GasMoleFractions.All(g => g.MoleFraction >= 0 && g.MoleFraction <= 1), Is.True);
            double molar = climate.GasMoleFractions.Sum(g => g.MoleFraction * PlanetScience.MolarMass(g.Species));
            Relative(climate.MeanMolarMassKgPerMol, molar);
            Relative(climate.AtmosphericDensityKgM3.Value, climate.ReferencePressurePascals * molar /
                (PlanetScience.MolarGasConstant * climate.ReferenceTemperatureK));
            Relative(climate.ScaleHeightMetres.Value, PlanetScience.MolarGasConstant * climate.ReferenceTemperatureK /
                (molar * PhysicalConstants.GravitationalConstant * body.Mass / (body.Radius * body.Radius)));
        }

        [Test]
        public void GiantsUseOneBarReferenceAndHaveNoSolidSurfacePressureOrTemperature()
        {
            var body = Earth(); body.Kind = PlanetKind.GasGiant;
            var report = PlanetScience.Evaluate(Sun(body), 0, 0);
            Assert.That(report.Geometry.HasSolidSurface, Is.False);
            Assert.That(report.Geometry.ReferenceKind, Is.EqualTo(BodyReferenceKind.AtmosphereReferenceRadius));
            Assert.That(report.Climate.IsOneBarReference, Is.True);
            Assert.That(report.Climate.ReferencePressurePascals, Is.EqualTo(100000));
            Assert.That(report.Climate.SurfacePressurePascals, Is.Null);
            Assert.That(report.Climate.SurfaceTemperatureK, Is.Null);
            Assert.That(report.Climate.SurfaceTemperatureMinimumK, Is.Null);
            Assert.That(report.Climate.SurfaceTemperatureMaximumK, Is.Null);
            Assert.Throws<ArgumentException>(() => PlanetScience.RequiredStellarFluxForSurfaceTemperature(body, 292));
        }

        [Test]
        public void AirlessSolidHasZeroPressureAndNoFictionalGasDensityOrScaleHeight()
        {
            var body = Earth(); body.AtmosphereHeight = 0;
            var result = PlanetScience.EvaluateClimate(body, 1000);
            Assert.That(result.SurfacePressurePascals, Is.EqualTo(0));
            Assert.That(result.HasAtmosphere, Is.False);
            Assert.That(result.GasMoleFractions, Is.Empty);
            Assert.That(result.AtmosphericDensityKgM3, Is.Null);
            Assert.That(result.ScaleHeightMetres, Is.Null);
            Assert.That(result.InfraredOpticalDepth, Is.Zero);
            Assert.That(result.SurfaceTemperatureK.Value, Is.EqualTo(result.EffectiveTemperatureK));
        }

        [Test]
        public void BlackHoleHasZeroStarlightAndZeroStellarHeatingTemperatureWithoutInventedWarmOcean()
        {
            var body = Earth(); body.Kind = PlanetKind.Ice; body.HasOcean = false; body.AtmosphereHeight = 0;
            var system = Sun(body); system.Kind = StellarKind.BlackHole; system.StarLuminosity = 0; system.StarTemperature = 0;
            system.StarRadius = 2 * PhysicalConstants.GravitationalConstant * system.StarMass /
                (PhysicalConstants.SpeedOfLight * PhysicalConstants.SpeedOfLight);
            var result = PlanetScience.Evaluate(system, 0, 0);
            Assert.That(result.Climate.IncidentStellarFluxWm2, Is.Zero);
            Assert.That(result.Climate.EquilibriumTemperatureK, Is.Zero);
            Assert.That(result.Star.EffectiveTemperatureK, Is.Zero);
            Assert.That(result.Star.IsNonAccretingBlackHole, Is.True);
            Assert.That(result.Climate.SurfaceTemperatureK.Value, Is.LessThan(50));
            Assert.That(result.Caveats.HasFlag(ScienceCaveat.NoStellarHeating), Is.True);
            Assert.That(result.Climate.LiquidWaterModelCompatible, Is.False);
            Assert.That(PlanetScience.Evaluate(system, -1, 0).Geometry.ReferenceKind, Is.EqualTo(BodyReferenceKind.GeometricHorizon));
        }

        [Test]
        public void SmallEccentricitySynchronousTidalHeatIsPositiveAndScalesAsESquared()
        {
            var earth = Earth(); var moon = Moon(earth, .01); var system = Sun(earth, moon);
            var first = PlanetScience.Evaluate(system, 1, 0);
            Assert.That(first.Climate.TidalDissipationK2OverQ.HasValue, Is.True);
            Assert.That(first.Climate.TidalHeatFluxWm2, Is.GreaterThan(0));
            moon.Eccentricity *= 2;
            var second = PlanetScience.Evaluate(system, 1, 0);
            Relative(second.Climate.TidalHeatFluxWm2, first.Climate.TidalHeatFluxWm2 * 4);
            moon.Eccentricity = 0;
            Assert.That(PlanetScience.Evaluate(system, 1, 0).Climate.TidalHeatFluxWm2, Is.Zero);
            moon.Eccentricity = .01; moon.RotationPeriod *= 1.2;
            var nonSynchronous = PlanetScience.Evaluate(system, 1, 0);
            Assert.That(nonSynchronous.Climate.TidalDissipationK2OverQ, Is.Null);
            Assert.That(nonSynchronous.Climate.TidalHeatFluxWm2, Is.Zero);
            Assert.That(nonSynchronous.Caveats.HasFlag(ScienceCaveat.TidalApproximationUnavailable), Is.True);
        }

        [Test]
        public void PrimarySolarDayDistinguishesSiderealAndSynchronousCases()
        {
            var body = Earth(); var system = Sun(body);
            var rotation = PlanetScience.Evaluate(system, 0, 0).Rotation;
            Assert.That(rotation.SolarDaySeconds.Value, Is.EqualTo(86400).Within(1));
            body.RotationPeriod = Orbit.Period(body, system.StarMass); body.AxialTilt = 0;
            rotation = PlanetScience.Evaluate(system, 0, 0).Rotation;
            Assert.That(rotation.SolarDayInfinite, Is.True); Assert.That(rotation.SolarDaySeconds, Is.Null);
            body.RotationPeriod = 200 * 86400; body.AxialTilt = Math.PI;
            rotation = PlanetScience.Evaluate(system, 0, 0).Rotation;
            Assert.That(rotation.SolarDaySeconds.Value, Is.LessThan(body.RotationPeriod));
        }

        [Test]
        public void LegacyTemperatureConflictIsReportedWithoutMutatingOrRetconningTheSavedWorld()
        {
            var body = Earth(); body.SemiMajorAxis *= 8;
            var system = Sun(body); system.GenerationVersion = 2;
            var report = PlanetScience.Evaluate(system, 0, 0);
            Assert.That(report.Caveats.HasFlag(ScienceCaveat.LegacyClimateUncalibrated), Is.True);
            Assert.That(report.Caveats.HasFlag(ScienceCaveat.VisualClassClimateMismatch), Is.True);
            Assert.That(report.Climate.IsLegacyGeneration, Is.True);
            Assert.That(body.Kind, Is.EqualTo(PlanetKind.Oceanic)); Assert.That(body.HasOcean, Is.True);
        }

        [Test]
        public void InvalidBodiesTimesAndParentCyclesAreRejectedRatherThanReturningNonFiniteData()
        {
            var earth = Earth(); var system = Sun(earth);
            Assert.Throws<ArgumentOutOfRangeException>(() => PlanetScience.Evaluate(system, 5, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlanetScience.Evaluate(system, 0, double.NaN));
            earth.Radius = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => PlanetScience.Evaluate(system, 0, 0));
            earth.Radius = PhysicalConstants.EarthRadius; earth.ParentIndex = 0;
            Assert.Throws<ArgumentException>(() => PlanetScience.Evaluate(system, 0, 0));
            earth.ParentIndex = 4;
            Assert.Throws<ArgumentException>(() => PlanetScience.Evaluate(system, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlanetScience.EquilibriumTemperature(1000, 1.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlanetScience.StellarFlux(-1, 1));
        }

        [Test]
        public void StarOnlyReportNeedsNoPlanetArrayAndDoesNotInventOrbitRotationOrAtmosphere()
        {
            var system = Sun(); system.Planets = null;
            var result = PlanetScience.Evaluate(system, -1, 0);
            Assert.That(result.IsStar, Is.True);
            Assert.That(result.Orbit, Is.Null); Assert.That(result.Rotation, Is.Null); Assert.That(result.Climate, Is.Null);
            Assert.That(result.Star.LuminositySolarUnits, Is.EqualTo(1));
            Assert.That(result.Geometry.ReferenceKind, Is.EqualTo(BodyReferenceKind.StellarPhotosphere));
        }
    }
}
