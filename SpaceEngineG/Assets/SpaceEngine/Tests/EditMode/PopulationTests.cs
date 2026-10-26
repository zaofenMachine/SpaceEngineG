using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceEngine.Core;

namespace SpaceEngine.Tests
{
    public class PopulationTests
    {
        [Test]
        public void PopulationVersionSeparatesNewHierarchyFromOldSaves()
        {
            Assert.That(UniverseGenerator.GenerationVersion, Is.EqualTo(2));
        }

        [Test]
        public void HomeAlwaysOffersLandableOceanAndBothGiantClasses()
        {
            for (int seed = -32; seed < 32; seed++)
            {
                var system = UniverseGenerator.GenerateSystem(seed, 0, 0, 0);
                Assert.That(system.Kind, Is.EqualTo(StellarKind.MainSequence));
                Assert.That(system.IsOccupied, Is.True);
                Assert.That(system.Planets[0].Kind, Is.EqualTo(PlanetKind.Oceanic));
                Assert.That(system.Planets[0].IsLandable, Is.True);
                Assert.That(system.Planets[3].Kind, Is.EqualTo(PlanetKind.GasGiant));
                Assert.That(system.Planets[4].Kind, Is.EqualTo(PlanetKind.IceGiant));
                Assert.That(system.Planets[5].Kind, Is.EqualTo(PlanetKind.Lava));
            }
        }

        [Test]
        public void GeneratedPopulationIncludesEveryStellarAndSurfaceClass()
        {
            var stars = new HashSet<StellarKind>();
            var planets = new HashSet<PlanetKind>();
            for (int i = 1; i <= 16384 && (stars.Count < 7 || planets.Count < 7); i++)
            {
                var system = UniverseGenerator.GenerateSystem(12486, i, i % 19, -i % 29);
                stars.Add(system.Kind);
                foreach (var planet in system.Planets) planets.Add(planet.Kind);
            }
            Assert.That(stars.Count, Is.EqualTo(Enum.GetValues(typeof(StellarKind)).Length));
            Assert.That(planets.Count, Is.EqualTo(Enum.GetValues(typeof(PlanetKind)).Length));
        }

        [Test]
        public void NewPopulationFieldsRemainBitwiseSeedDeterministic()
        {
            for (int seed = 1; seed <= 32; seed++)
            {
                var a = UniverseGenerator.GenerateSystem(seed, -342, 11, 785);
                var b = UniverseGenerator.GenerateSystem(seed, -342, 11, 785);
                Assert.That(a.Kind, Is.EqualTo(b.Kind));
                Assert.That(a.StarLuminosity, Is.EqualTo(b.StarLuminosity));
                Assert.That(a.GalacticDensity, Is.EqualTo(b.GalacticDensity));
                Assert.That(a.IsOccupied, Is.EqualTo(b.IsOccupied));
                for (int i = 0; i < a.Planets.Length; i++)
                {
                    var p = a.Planets[i]; var q = b.Planets[i];
                    Assert.That(p.Kind, Is.EqualTo(q.Kind)); Assert.That(p.ParentIndex, Is.EqualTo(q.ParentIndex));
                    Assert.That(p.RotationPeriod, Is.EqualTo(q.RotationPeriod)); Assert.That(p.AxialTilt, Is.EqualTo(q.AxialTilt));
                    Assert.That(p.RingInnerRadius, Is.EqualTo(q.RingInnerRadius)); Assert.That(p.RingOuterRadius, Is.EqualTo(q.RingOuterRadius));
                    Assert.That(p.AtmosphereTint, Is.EqualTo(q.AtmosphereTint));
                }
            }
        }

        [Test]
        public void MoonsHaveEarlierParentsClearRingsAndFitStableHillEnvelope()
        {
            for (int seed = 1; seed <= 256; seed++)
            {
                var system = UniverseGenerator.GenerateSystem(seed, seed, 0, -seed);
                for (int i = 0; i < system.Planets.Length; i++)
                {
                    var moon = system.Planets[i];
                    if (!moon.IsMoon) { Assert.That(moon.ParentIndex, Is.EqualTo(-1)); continue; }
                    Assert.That(moon.ParentIndex, Is.InRange(0, i - 1));
                    var parent = system.Planets[moon.ParentIndex];
                    Assert.That(parent.IsMoon, Is.False);
                    double periapsis = moon.SemiMajorAxis * (1 - moon.Eccentricity);
                    double apoapsis = moon.SemiMajorAxis * (1 + moon.Eccentricity);
                    Assert.That(periapsis, Is.GreaterThan(parent.RingOuterRadius + moon.Radius));
                    double hill = parent.SemiMajorAxis * (1 - parent.Eccentricity)
                        * Math.Pow(parent.Mass / (3 * system.StarMass), 1.0 / 3);
                    Assert.That(apoapsis, Is.LessThan(hill * 0.35));
                    double period = Orbit.Period(moon, parent.Mass);
                    Assert.That(moon.RotationPeriod, Is.EqualTo(period).Within(period * 1e-13));
                    Assert.That((Orbit.Evaluate(moon, parent.Mass, period) - Orbit.Evaluate(moon, parent.Mass, 0)).Length,
                        Is.LessThan(moon.SemiMajorAxis * 1e-12));
                }
            }
        }

        [Test]
        public void GiantRingsAndLandingRulesMatchBodyComposition()
        {
            var system = UniverseGenerator.GenerateSystem(12486, 0, 0, 0);
            foreach (var body in system.Planets)
            {
                if (body.Kind == PlanetKind.GasGiant || body.Kind == PlanetKind.IceGiant)
                {
                    Assert.That(body.IsLandable, Is.False); Assert.That(body.TerrainHeight, Is.Zero);
                    Assert.That(body.HasOcean, Is.False);
                    Assert.That(body.RingInnerRadius, Is.GreaterThan(body.Radius + body.AtmosphereHeight));
                    Assert.That(body.RingOuterRadius, Is.GreaterThan(body.RingInnerRadius));
                }
                else
                {
                    Assert.That(body.IsLandable, Is.True); Assert.That(body.TerrainHeight, Is.GreaterThan(0));
                    Assert.That(body.RingInnerRadius, Is.Zero); Assert.That(body.RingOuterRadius, Is.Zero);
                }
            }
        }

        [Test]
        public void BulkDensitiesRotationAndPrimaryPeriapsidesArePhysical()
        {
            for (int i = 1; i <= 256; i++)
            {
                var system = UniverseGenerator.GenerateSystem(42, i, 0, 3);
                double previousApoapsis = system.StarRadius;
                foreach (var body in system.Planets)
                {
                    double density = body.Mass / (4.0 / 3 * Math.PI * Math.Pow(body.Radius, 3));
                    Assert.That(density, Is.InRange(600, 6000));
                    Assert.That(body.RotationPeriod, Is.GreaterThan(0));
                    Assert.That(body.AxialTilt, Is.InRange(0, Math.PI));
                    Assert.That(body.TerrainHeight, Is.LessThan(body.Radius * 0.01));
                    if (body.IsMoon) continue;
                    Assert.That(body.SemiMajorAxis * (1 - body.Eccentricity), Is.GreaterThan(previousApoapsis));
                    previousApoapsis = body.SemiMajorAxis * (1 + body.Eccentricity);
                }
            }
        }

        [Test]
        public void CompactAndGiantStarsHaveDistinctPhysicalRadiiAndLuminosities()
        {
            var seen = new HashSet<StellarKind>();
            for (int i = 1; i <= 16384 && seen.Count < 7; i++)
            {
                var system = UniverseGenerator.GenerateSystem(12486, i, i % 19, -i % 29);
                if (!seen.Add(system.Kind)) continue;
                Assert.That(system.StarMass, Is.GreaterThan(0)); Assert.That(system.StarRadius, Is.GreaterThan(0));
                switch (system.Kind)
                {
                    case StellarKind.RedGiant:
                        Assert.That(system.StarRadius, Is.GreaterThan(10 * PhysicalConstants.SolarRadius)); break;
                    case StellarKind.WhiteDwarf:
                        Assert.That(system.StarRadius, Is.InRange(0.005 * PhysicalConstants.SolarRadius, 0.02 * PhysicalConstants.SolarRadius)); break;
                    case StellarKind.NeutronStar:
                        Assert.That(system.StarRadius, Is.InRange(10000, 14000)); break;
                    case StellarKind.BlackHole:
                        double horizon = 2 * PhysicalConstants.GravitationalConstant * system.StarMass / Math.Pow(PhysicalConstants.SpeedOfLight, 2);
                        Assert.That(system.StarRadius, Is.EqualTo(horizon).Within(horizon * 1e-12));
                        Assert.That(system.StarLuminosity, Is.Zero); continue;
                }
                double luminosity = 4 * Math.PI * Math.Pow(system.StarRadius, 2) * 5.670374419e-8 * Math.Pow(system.StarTemperature, 4);
                Assert.That(system.StarLuminosity, Is.EqualTo(luminosity).Within(luminosity * 1e-12));
            }
            Assert.That(seen.Count, Is.EqualTo(7));
        }

        [Test]
        public void AdmissionIsSeedStableAndMatchesGeneratedCandidateMetadata()
        {
            int occupied = 0;
            for (int x = -8; x <= 8; x++) for (int z = -8; z <= 8; z++)
            {
                bool admission = UniverseGenerator.IsCellOccupied(12486, x, 0, z);
                Assert.That(UniverseGenerator.IsCellOccupied(12486, x, 0, z), Is.EqualTo(admission));
                var candidate = UniverseGenerator.GenerateSystem(12486, x, 0, z);
                Assert.That(candidate.IsOccupied, Is.EqualTo(admission));
                StarSystemDefinition resolved;
                Assert.That(UniverseGenerator.TryGenerateSystem(12486, x, 0, z, out resolved), Is.EqualTo(admission));
                if (admission) { occupied++; Assert.That(resolved.Position, Is.EqualTo(candidate.Position)); }
                else Assert.That(resolved, Is.Null);
            }
            Assert.That(occupied, Is.GreaterThan(10)); Assert.That(occupied, Is.LessThan(150));
        }

        [Test]
        public void IntergalacticCellsAreEmptyButHomeRemainsAvailable()
        {
            for (int seed = -4; seed <= 4; seed++)
            {
                Assert.That(UniverseGenerator.IsCellOccupied(seed, 0, 0, 0), Is.True);
                Assert.That(UniverseGenerator.IsCellOccupied(seed, 100000, 100000, 100000), Is.False);
                Assert.That(UniverseGenerator.IsCellOccupied(seed, -100000, -100000, -100000), Is.False);
            }
        }

        [Test]
        public void ConfiguredGalaxyAdmissionAndCandidateMetadataUseTheSameDensity()
        {
            const int seed = 12486;
            var custom = GalaxyModel.Default(seed);
            custom.SolarRadiusLy = 21000;
            custom.DiskScaleLengthLy = 12000;
            custom.LocalNumberDensityPerCubicParsec = .8;
            int changedAdmission = 0, occupied = 0;
            for (int x = 1; x <= 64; x++)
            {
                bool admitted = UniverseGenerator.IsCellOccupied(seed, x, 0, 0, custom);
                var candidate = UniverseGenerator.GenerateSystem(seed, x, 0, 0, custom);
                double density = GalaxyModel.SampleDensity(GalaxyModel.ToGalacticLy(candidate.Position, custom), custom);
                Assert.That(candidate.GalacticDensity, Is.EqualTo(density));
                Assert.That(candidate.IsOccupied, Is.EqualTo(admitted));
                StarSystemDefinition resolved;
                Assert.That(UniverseGenerator.TryGenerateSystem(seed, x, 0, 0, custom, out resolved), Is.EqualTo(admitted));
                if (admitted)
                {
                    occupied++;
                    Assert.That(resolved.IsOccupied, Is.True);
                    Assert.That(resolved.GalacticDensity, Is.EqualTo(density));
                    Assert.That(resolved.Position, Is.EqualTo(candidate.Position));
                }
                else Assert.That(resolved, Is.Null);
                if (admitted != UniverseGenerator.IsCellOccupied(seed, x, 0, 0)) changedAdmission++;
            }
            Assert.That(changedAdmission, Is.GreaterThan(10), "The fixture must exercise a meaningful difference from default galactic density.");
            Assert.That(occupied, Is.InRange(30, 63));
        }
    }
}
