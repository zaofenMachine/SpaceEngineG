using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using SpaceEngine.Core;

namespace SpaceEngine.Runtime.Tests
{
    public sealed class CelestialDetailsPageTests
    {
        const int Seed=20261026;
        static CelestialTarget Target(PlanetKind kind)
        {
            for(int seed=Seed;seed<Seed+100;seed++)
            {
                var system=UniverseGenerator.GenerateSystem(seed,0,0,0);
                for(int i=0;i<system.Planets.Length;i++)if(system.Planets[i].Kind==kind)
                    return new CelestialTarget{System=system,BodyIndex=i,X=123,Y=-456,Z=789};
            }
            throw new InvalidOperationException("No generated fixture for "+kind);
        }
        static IEnumerable<CelestialDetailsPage.Datum> Rows(CelestialDetailsPage.Page page)
        {foreach(var section in page.Sections)foreach(var row in section.Rows)yield return row;}
        static CelestialDetailsPage.Datum Find(CelestialDetailsPage.Document document,int page,string label)
        {
            foreach(var row in Rows(document.Pages[page]))if(row.Label==label)return row;
            Assert.Fail("Missing detail: "+label);return null;
        }
        static double NumericPrefix(CelestialDetailsPage.Datum row)
            =>double.Parse(row.Value.Split(' ')[0],CultureInfo.InvariantCulture);

        [TestCase(PlanetKind.Rocky)]
        [TestCase(PlanetKind.Oceanic)]
        [TestCase(PlanetKind.Desert)]
        [TestCase(PlanetKind.Ice)]
        [TestCase(PlanetKind.Lava)]
        [TestCase(PlanetKind.GasGiant)]
        [TestCase(PlanetKind.IceGiant)]
        public void EveryGeneratedPlanetKindHasFourCompleteChinesePages(PlanetKind kind)
        {
            var target=Target(kind);var document=CelestialDetailsPage.CreateDocument(target,12345);
            Assert.That(document.Title,Does.Contain(target.Name).And.Contain(ChineseText.PlanetKind(kind)));
            Assert.That(document.Pages.Length,Is.EqualTo(4));
            CollectionAssert.AreEqual(new[]{"概览","轨道","环境","系统与来源"},Array.ConvertAll(document.Pages,page=>page.Name));
            foreach(var page in document.Pages)
            {
                Assert.That(page.ModelNote,Does.StartWith("模型与数据来源："));
                Assert.That(page.Sections.Count,Is.GreaterThan(0));int count=0;
                foreach(var row in Rows(page))
                {
                    count++;Assert.That(row.Label,Is.Not.Empty);Assert.That(row.Value,Is.Not.Empty);
                    Assert.That(row.Value,Does.Not.Contain("NaN").And.Not.Contain("Infinity"));
                }
                Assert.That(count,Is.GreaterThanOrEqualTo(7));
            }
        }
        [Test]
        public void EarthReferenceFieldsKeepDensityGravityAndEscapeSpeedInTheirCorrectRows()
        {
            var target=Target(PlanetKind.Rocky);target.Body.Radius=PhysicalConstants.EarthRadius;target.Body.Mass=PhysicalConstants.EarthMass;
            var document=CelestialDetailsPage.CreateDocument(target,0);
            Assert.That(NumericPrefix(Find(document,0,"平均密度")),Is.InRange(5400,5600));
            Assert.That(NumericPrefix(Find(document,0,"参考半径处重力")),Is.InRange(9.7,10));
            Assert.That(NumericPrefix(Find(document,0,"逃逸速度（参考）")),Is.InRange(11000,12000));
            Assert.That(Find(document,0,"质量").Origin,Is.EqualTo(CelestialDetailsPage.DataOrigin.Generated));
            Assert.That(Find(document,0,"参考半径处重力").Origin,Is.EqualTo(CelestialDetailsPage.DataOrigin.Derived));
        }
        [TestCase(PlanetKind.GasGiant)]
        [TestCase(PlanetKind.IceGiant)]
        public void GasReferencesNeverPresentOneBarPressureAsASolidSurface(PlanetKind kind)
        {
            var document=CelestialDetailsPage.CreateDocument(Target(kind),0);
            Assert.That(document.Subtitle,Does.Contain("1 bar").And.Contain("无固体地表"));
            Assert.That(Find(document,2,"参考层压强（非地表）").Value,Does.Contain("bar"));
            Assert.That(Find(document,2,"地表温度估计").Value,Does.Contain("不适用"));
            Assert.That(Find(document,2,"地表温度估计").Origin,Is.EqualTo(CelestialDetailsPage.DataOrigin.NotModeled));
            Assert.That(Find(document,0,"可接近区域").Value,Does.Contain("没有可供降落的固体表面"));
        }
        [Test]
        public void V2LegacyWarningDoesNotSilentlyReclassifyTheSavedPlanet()
        {
            var system=UniverseGenerator.GenerateSystem(Seed,0,0,0,GalaxyModel.Default(Seed),UniverseGenerator.LegacyGenerationVersion);
            var target=new CelestialTarget{System=system,BodyIndex=0};var kind=target.Body.Kind;
            double axis=target.Body.SemiMajorAxis,mass=target.Body.Mass;var document=CelestialDetailsPage.CreateDocument(target,0);
            Assert.That(document.GenerationVersion,Is.EqualTo(2));Assert.That(document.Warning,Does.Contain("旧版世界"));
            Assert.That(Find(document,2,"旧版环境提示").Value,Does.Contain("不能反向证明"));
            Assert.That(target.Body.Kind,Is.EqualTo(kind));Assert.That(target.Body.SemiMajorAxis,Is.EqualTo(axis));Assert.That(target.Body.Mass,Is.EqualTo(mass));
        }
        [Test]
        public void RelatedTargetSelectionPreservesRemoteSystemAddressAndDoesNotMutateTheOriginalTarget()
        {
            var target=Target(PlanetKind.Oceanic);target.X=900719925474099;target.Y=-14500000000;target.Z=67108865;
            int original=target.BodyIndex;var star=CelestialDetailsPage.RelatedTarget(target,-1);
            Assert.That(star.System,Is.SameAs(target.System));Assert.That(star.BodyIndex,Is.EqualTo(-1));
            Assert.That(star.X,Is.EqualTo(target.X));Assert.That(star.Y,Is.EqualTo(target.Y));Assert.That(star.Z,Is.EqualTo(target.Z));
            Assert.That(target.BodyIndex,Is.EqualTo(original));Assert.That(star,Is.Not.SameAs(target));
            Assert.Throws<ArgumentOutOfRangeException>(()=>CelestialDetailsPage.RelatedTarget(target,target.System.Planets.Length));
        }
        [Test]
        public void MoonPageLinksToItsActualParentAndStarPageListsOnlyDirectPrimaryBodies()
        {
            var system=UniverseGenerator.GenerateSystem(Seed,0,0,0);int moon=Array.FindIndex(system.Planets,p=>p.IsMoon);
            Assert.That(moon,Is.GreaterThanOrEqualTo(0));
            var document=CelestialDetailsPage.CreateDocument(new CelestialTarget{System=system,BodyIndex=moon},0);
            Assert.That(Find(document,1,"环绕母体").RelatedBodyIndex,Is.EqualTo(system.Planets[moon].ParentIndex));
            Assert.That(Find(document,3,"直接母体").RelatedBodyIndex,Is.EqualTo(system.Planets[moon].ParentIndex));
            document=CelestialDetailsPage.CreateDocument(new CelestialTarget{System=system,BodyIndex=-1},0);
            int links=0;foreach(var row in Rows(document.Pages[3]))if(row.RelatedBodyIndex.HasValue)
            {links++;Assert.That(system.Planets[row.RelatedBodyIndex.Value].ParentIndex,Is.EqualTo(-1));}
            Assert.That(links,Is.EqualTo(Array.FindAll(system.Planets,p=>!p.IsMoon).Length));
        }
        [Test]
        public void TemperatureAndNullFormattingDoNotTurnUnknownDataIntoZero()
        {
            Assert.That(CelestialDetailsPage.Number(null,"帕"),Is.EqualTo("未求解"));
            Assert.That(CelestialDetailsPage.Number(double.NaN,"帕"),Is.EqualTo("未求解"));
            Assert.That(CelestialDetailsPage.Number(0,"帕"),Is.EqualTo("0 帕"));
            Assert.That(CelestialDetailsPage.Temperature(null),Is.EqualTo("未求解"));
            Assert.That(CelestialDetailsPage.Temperature(273.15),Does.Contain("0.0 °C"));
            Assert.That(CelestialDetailsPage.Temperature(-1),Is.EqualTo("未求解"));
            Assert.That(CelestialDetailsPage.Duration(double.PositiveInfinity),Is.EqualTo("未定义"));
        }
        [Test]
        public void ClimateSeparatesModelAssumptionsFromTemperatureEstimatesAndUnknownWeather()
        {
            var document=CelestialDetailsPage.CreateDocument(Target(PlanetKind.Oceanic),0);
            Assert.That(Find(document,2,"邦德反照率").Origin,Is.EqualTo(CelestialDetailsPage.DataOrigin.Assumed));
            Assert.That(Find(document,2,"主要成分（摩尔比例）").Origin,Is.EqualTo(CelestialDetailsPage.DataOrigin.Assumed));
            Assert.That(Find(document,2,"辐射平衡温度").Origin,Is.EqualTo(CelestialDetailsPage.DataOrigin.Estimated));
            Assert.That(Find(document,2,"气体与相变").Origin,Is.EqualTo(CelestialDetailsPage.DataOrigin.NotModeled));
            Assert.That(Find(document,3,"数据性质").Value,Does.Contain("不是观测目录"));
        }
        [Test]
        public void StellarPageKeepsUnsupportedPlanetClimateAndRotationExplicitlyUnmodeled()
        {
            var system=UniverseGenerator.GenerateSystem(Seed,0,0,0);
            var document=CelestialDetailsPage.CreateDocument(new CelestialTarget{System=system,BodyIndex=-1},0);
            Assert.That(document.Title,Does.Contain(ChineseText.StellarKind(system.Kind)));
            Assert.That(Find(document,0,"有效温度").Origin,Is.EqualTo(CelestialDetailsPage.DataOrigin.Derived));
            Assert.That(Find(document,1,"恒星自转与日长").Origin,Is.EqualTo(CelestialDetailsPage.DataOrigin.NotModeled));
            Assert.That(Find(document,2,"行星气候参数").Value,Does.Contain("不适用"));
            Assert.That(document.GenerationVersion,Is.EqualTo(UniverseGenerator.GenerationVersion));
            Assert.That(document.Warning??"",Does.Not.Contain("旧版世界"));
        }
        [Test]
        public void TabChangesResetScrollAndRejectNonexistentPages()
        {
            var page=new CelestialDetailsPage();page.SelectTab(3);Assert.That(page.SelectedTab,Is.EqualTo(3));
            Assert.That(page.ScrollPosition.sqrMagnitude,Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(()=>page.SelectTab(-1));Assert.Throws<ArgumentOutOfRangeException>(()=>page.SelectTab(4));
        }
    }
}
