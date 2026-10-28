using System;
using NUnit.Framework;
using SpaceEngine.Core;

namespace SpaceEngine.Tests
{
    public sealed class PlanetGenerationTests
    {
        [Test]
        public void NewGenerationPreservesTheOriginalRandomStreamAndTerrainParameters()
        {
            for(int i=0;i<64;i++)
            {
                var galaxy=GalaxyModel.Default(20261026);
                var old=UniverseGenerator.GenerateSystem(20261026,i,0,i%19,galaxy,2);
                var current=UniverseGenerator.GenerateSystem(20261026,i,0,i%19,galaxy,3);
                Assert.That(old.GenerationVersion,Is.EqualTo(2));Assert.That(current.GenerationVersion,Is.EqualTo(3));
                Assert.That(current.Position.Equals(old.Position),Is.True);Assert.That(current.Seed,Is.EqualTo(old.Seed));
                Assert.That(current.StarMass,Is.EqualTo(old.StarMass));Assert.That(current.StarLuminosity,Is.EqualTo(old.StarLuminosity));
                for(int body=0;body<old.Planets.Length;body++)
                {
                    var a=old.Planets[body];var b=current.Planets[body];
                    Assert.That(b.Seed,Is.EqualTo(a.Seed));Assert.That(b.Mass,Is.EqualTo(a.Mass));Assert.That(b.Radius,Is.EqualTo(a.Radius));
                    Assert.That(b.TerrainHeight,Is.EqualTo(a.TerrainHeight));Assert.That(b.Eccentricity,Is.EqualTo(a.Eccentricity));
                }
            }
        }
        [Test]
        public void EveryNewOceanWorldPassesItsOwnPressureAndWholeOrbitWaterScreen()
        {
            int ocean=0;var stars=new System.Collections.Generic.HashSet<StellarKind>();
            for(int i=0;i<2048;i++)
            {
                var system=UniverseGenerator.GenerateSystem(20261026,i,i%19,-i%29);stars.Add(system.Kind);
                foreach(var body in system.Planets)
                {
                    if(body.Kind!=PlanetKind.Oceanic)continue;ocean++;
                    int index=Array.IndexOf(system.Planets,body);var result=PlanetScience.Evaluate(system,index,123456);
                    Assert.That(result.Climate.LiquidWaterModelCompatible,Is.True,system.Name+" "+system.Kind+" "+result.Climate.SurfaceTemperatureMinimumK+".."+result.Climate.SurfaceTemperatureMaximumK);
                    Assert.That(result.Caveats.HasFlag(ScienceCaveat.VisualClassClimateMismatch),Is.False);
                }
            }
            Assert.That(ocean,Is.GreaterThan(1900));Assert.That(stars.Count,Is.EqualTo(7));
        }
        [Test]
        public void NonAccretingBlackHoleHasNoStellarHeatOrLiquidOceanAndNoSolidBodyAtmospheres()
        {
            var system=UniverseGenerator.GenerateSystem(20261026,-96,0,11);
            Assert.That(system.Kind,Is.EqualTo(StellarKind.BlackHole));Assert.That(system.StarLuminosity,Is.Zero);
            for(int i=0;i<system.Planets.Length;i++)
            {
                var body=system.Planets[i];var report=PlanetScience.Evaluate(system,i,0);
                Assert.That(body.HasOcean,Is.False);Assert.That(report.Climate.IncidentStellarFluxWm2,Is.Zero);
                if(body.IsLandable){Assert.That(body.AtmosphereHeight,Is.Zero);Assert.That(report.Climate.HasAtmosphere,Is.False);}
            }
        }
        [Test]
        public void VersionTwoStillContainsItsOriginalColdOceanRatherThanSilentlyMigratingIt()
        {
            var system=UniverseGenerator.GenerateSystem(20261026,-96,0,11,GalaxyModel.Default(20261026),2);
            Assert.That(system.Planets[0].Kind,Is.EqualTo(PlanetKind.Oceanic));Assert.That(system.Planets[0].HasOcean,Is.True);
            Assert.That(system.Planets[0].AtmosphereHeight,Is.GreaterThan(0));
            Assert.That(PlanetScience.Evaluate(system,0,0).Caveats.HasFlag(ScienceCaveat.LegacyClimateUncalibrated),Is.True);
        }
    }
}
