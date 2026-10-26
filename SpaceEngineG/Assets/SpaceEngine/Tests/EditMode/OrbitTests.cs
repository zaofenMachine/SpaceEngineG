using System;
using NUnit.Framework;
using SpaceEngine.Core;

namespace SpaceEngine.Tests
{
    public class OrbitTests
    {
        private static PlanetDefinition EarthOrbit(double eccentricity = 0) => new PlanetDefinition
        { SemiMajorAxis = PhysicalConstants.AstronomicalUnit, Mass = PhysicalConstants.EarthMass, Eccentricity = eccentricity };

        [TestCase(0.0)] [TestCase(0.2)] [TestCase(0.8)] [TestCase(0.95)] [TestCase(0.999999)]
        public void KeplerResidualRemainsSmallForCircularThroughHighlyEccentricOrbits(double eccentricity)
        {
            foreach (double mean in new[] { -Math.PI, -2.4, -0.00001, 0, 0.00001, 0.7, Math.PI, 11 * Math.PI })
            {
                double eccentric = Orbit.SolveEccentricAnomaly(mean, eccentricity);
                double residual = eccentric - eccentricity * Math.Sin(eccentric) - Math.IEEERemainder(mean, 2 * Math.PI);
                Assert.That(Math.Abs(residual), Is.LessThan(2e-13), $"e={eccentricity}, M={mean}");
            }
        }

        [Test]
        public void EarthScaleOrbitHasYearPeriodAndClosesExactly()
        {
            var planet = EarthOrbit(0.0167);
            double period = Orbit.Period(planet, PhysicalConstants.SolarMass);
            Assert.That(period / PhysicalConstants.SecondsPerDay, Is.InRange(365.0, 365.5));
            Assert.That((Orbit.Evaluate(planet, PhysicalConstants.SolarMass, period) - Orbit.Evaluate(planet, PhysicalConstants.SolarMass, 0)).Length, Is.LessThan(0.001));
        }

        [Test]
        public void EccentricOrbitReachesAnalyticPeriapsisAndApoapsis()
        {
            var planet = EarthOrbit(0.67);
            planet.Inclination = 0.42; planet.LongitudeAscendingNode = 1.17; planet.ArgumentPeriapsis = 2.7;
            double period = Orbit.Period(planet, PhysicalConstants.SolarMass);
            Assert.That(Orbit.Evaluate(planet, PhysicalConstants.SolarMass, 0).Length, Is.EqualTo(planet.SemiMajorAxis * 0.33).Within(0.001));
            Assert.That(Orbit.Evaluate(planet, PhysicalConstants.SolarMass, period / 2).Length, Is.EqualTo(planet.SemiMajorAxis * 1.67).Within(0.001));
        }

        [Test]
        public void OrbitalInclinationUsesUnityYUpAndSupportsNegativeTime()
        {
            var planet = EarthOrbit(); planet.Inclination = Math.PI / 2;
            double quarter = Orbit.Period(planet, PhysicalConstants.SolarMass) / 4;
            var positive = Orbit.Evaluate(planet, PhysicalConstants.SolarMass, quarter);
            var negative = Orbit.Evaluate(planet, PhysicalConstants.SolarMass, -quarter);
            Assert.That(positive.y, Is.EqualTo(planet.SemiMajorAxis).Within(0.001));
            Assert.That(Math.Abs(positive.z), Is.LessThan(0.001));
            Assert.That(negative.y, Is.EqualTo(-planet.SemiMajorAxis).Within(0.001));
        }

        [Test]
        public void NonEllipticAndInvalidPhysicalInputsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Orbit.SolveEccentricAnomaly(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Orbit.SolveEccentricAnomaly(0, -0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Orbit.Evaluate(EarthOrbit(), -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Orbit.Evaluate(EarthOrbit(), PhysicalConstants.SolarMass, double.NaN));
        }
    }
}
