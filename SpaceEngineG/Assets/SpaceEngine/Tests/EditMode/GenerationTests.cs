using NUnit.Framework;
using SpaceEngine.Core;

namespace SpaceEngine.Tests
{
    public class GenerationTests
    {
        [Test]
        public void SplitMixStreamMatchesPublishedAlgorithmReferenceVector()
        {
            var random = new DeterministicRandom(0);
            Assert.That(random.NextUInt64(), Is.EqualTo(0xe220a8397b1dcdafUL));
            Assert.That(random.NextUInt64(), Is.EqualTo(0x6e789e6aa1b965f4UL));
        }

        [Test]
        public void IdenticalSeedAndCellProduceIdenticalSystemAndAllPlanetParameters()
        {
            var a = UniverseGenerator.GenerateSystem(12486, -12, 61, 730);
            var b = UniverseGenerator.GenerateSystem(12486, -12, 61, 730);
            Assert.That(a.Name, Is.EqualTo(b.Name)); Assert.That(a.Seed, Is.EqualTo(b.Seed));
            Assert.That(a.Position.Equals(b.Position), Is.True);
            Assert.That(a.StarMass, Is.EqualTo(b.StarMass)); Assert.That(a.StarRadius, Is.EqualTo(b.StarRadius));
            Assert.That(a.StarTemperature, Is.EqualTo(b.StarTemperature)); Assert.That(a.Planets.Length, Is.EqualTo(b.Planets.Length));
            for (int i = 0; i < a.Planets.Length; i++)
            {
                PlanetDefinition p = a.Planets[i], q = b.Planets[i];
                Assert.That(p.Name, Is.EqualTo(q.Name)); Assert.That(p.Seed, Is.EqualTo(q.Seed));
                Assert.That(p.Radius, Is.EqualTo(q.Radius)); Assert.That(p.Mass, Is.EqualTo(q.Mass));
                Assert.That(p.SemiMajorAxis, Is.EqualTo(q.SemiMajorAxis)); Assert.That(p.Eccentricity, Is.EqualTo(q.Eccentricity));
                Assert.That(p.Inclination, Is.EqualTo(q.Inclination)); Assert.That(p.LongitudeAscendingNode, Is.EqualTo(q.LongitudeAscendingNode));
                Assert.That(p.ArgumentPeriapsis, Is.EqualTo(q.ArgumentPeriapsis)); Assert.That(p.MeanAnomalyAtEpoch, Is.EqualTo(q.MeanAnomalyAtEpoch));
                Assert.That(p.AtmosphereHeight, Is.EqualTo(q.AtmosphereHeight)); Assert.That(p.TerrainHeight, Is.EqualTo(q.TerrainHeight));
                Assert.That(p.HasOcean, Is.EqualTo(q.HasOcean)); Assert.That(p.SurfaceTint, Is.EqualTo(q.SurfaceTint));
            }
        }

        [Test]
        public void UniverseSeedAndAll64CellBitsAffectGeneration()
        {
            var a = UniverseGenerator.GenerateSystem(1, 11, 2, 3);
            var differentSeed = UniverseGenerator.GenerateSystem(2, 11, 2, 3);
            var differentCell = UniverseGenerator.GenerateSystem(1, 11 + (1L << 32), 2, 3);
            Assert.That(a.Seed, Is.Not.EqualTo(differentSeed.Seed));
            Assert.That(a.Seed, Is.Not.EqualTo(differentCell.Seed));
            Assert.That(a.Position.Equals(differentSeed.Position), Is.False);
            Assert.That(a.Position.Equals(differentCell.Position), Is.False);
        }

        [Test]
        public void OriginSystemIsAtOriginAndNeighbourCellsAreAstronomicalDistancesAway()
        {
            var origin = UniverseGenerator.GenerateSystem(42, 0, 0, 0);
            Assert.That(origin.Position.Equals(UniversalPosition.Origin), Is.True);
            var neighbour = UniverseGenerator.GenerateSystem(42, 1, 0, 0);
            Double3 delta = neighbour.Position.RelativeTo(origin.Position);
            Assert.That(delta.x / PhysicalConstants.LightYear, Is.InRange(3.2, 4.8));
            Assert.That(System.Math.Abs(delta.y / PhysicalConstants.LightYear), Is.LessThan(0.8));
            Assert.That(System.Math.Abs(delta.z / PhysicalConstants.LightYear), Is.LessThan(0.8));
        }

        [Test]
        public void ManyGeneratedSystemsHaveValidNonCrossingOrbitsAndLandingCandidates()
        {
            for (int seed = -16; seed < 16; seed++)
            {
                var system = UniverseGenerator.GenerateSystem(seed, seed, -seed, 2);
                Assert.That(system.Planets.Length, Is.InRange(4, 7));
                Assert.That(system.Planets[0].HasOcean, Is.True);
                Assert.That(system.Planets[0].AtmosphereHeight, Is.GreaterThan(0));
                double previousApoapsis = system.StarRadius;
                foreach (PlanetDefinition p in system.Planets)
                {
                    Assert.That(p.Radius, Is.GreaterThan(1e6)); Assert.That(p.Mass, Is.GreaterThan(0));
                    Assert.That(p.SemiMajorAxis * (1 - p.Eccentricity), Is.GreaterThan(previousApoapsis));
                    Assert.That(p.TerrainHeight, Is.LessThan(p.Radius * 0.01));
                    Assert.That(Orbit.Evaluate(p, system.StarMass, 0).Length, Is.GreaterThan(system.StarRadius));
                    previousApoapsis = p.SemiMajorAxis * (1 + p.Eccentricity);
                }
            }
        }
    }
}
