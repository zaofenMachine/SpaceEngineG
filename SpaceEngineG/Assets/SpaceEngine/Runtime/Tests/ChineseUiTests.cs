using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Runtime.Tests
{
    public sealed class ChineseUiTests
    {
        [Test]
        public void BundledFontProducesDistinctRealChineseGlyphMetrics()
        {
            Font font=ChineseText.Font;
            Assert.That(font.name,Does.Contain("NotoSansCJKsc"));
            Assert.That(font.dynamic,Is.True,"Font bytes must be embedded for on-demand CJK glyph rasterization.");
            font.RequestCharactersInTexture(ChineseText.GlyphProbe,24,FontStyle.Normal);
            var characters=new HashSet<char>();var rectangles=new HashSet<string>();
            foreach(char character in ChineseText.GlyphProbe)
            {
                if(!characters.Add(character))continue;
                Assert.That(font.GetCharacterInfo(character,out CharacterInfo info,24,FontStyle.Normal),Is.True,"Missing glyph: "+character);
                Assert.That(info.advance,Is.GreaterThan(0));
                Assert.That(info.maxX-info.minX,Is.GreaterThan(0));
                Assert.That(info.maxY-info.minY,Is.GreaterThan(0));
                rectangles.Add(info.uvBottomLeft.ToString("R")+info.uvTopRight.ToString("R"));
            }
            Assert.That(rectangles.Count,Is.EqualTo(characters.Count),"Missing characters must not all render the same tofu glyph.");
        }
        [Test]
        public void EveryGeneratedBodyAndStellarClassHasAChineseDisplayName()
        {
            foreach(PlanetKind kind in Enum.GetValues(typeof(PlanetKind)))
                Assert.That(ChineseText.PlanetKind(kind),Does.Match("[\\u4e00-\\u9fff]"));
            foreach(StellarKind kind in Enum.GetValues(typeof(StellarKind)))
                Assert.That(ChineseText.StellarKind(kind),Does.Match("[\\u4e00-\\u9fff]"));
            foreach(GalaxyComponent kind in Enum.GetValues(typeof(GalaxyComponent)))
                Assert.That(ChineseText.GalacticComponent(kind),Does.Match("[\\u4e00-\\u9fff]"));
        }
        [TestCase("FREE FLIGHT")]
        [TestCase("CONTINUOUS DESCENT")]
        [TestCase("CONTINUOUS ASCENT")]
        [TestCase("APPROACH CLOUD TOPS / NO SOLID SURFACE")]
        [TestCase("HOLDING ABOVE CLOUDS / NON-LANDABLE")]
        [TestCase("LANDED / TERRAIN FOLLOW")]
        [TestCase("Flight saved / F9 restore")]
        [TestCase("CENTRAL BLACK HOLE / geometric horizon; no relativistic lensing")]
        public void SimulationStatusIsLocalizedAtTheDisplayBoundary(string original)
        {
            string translated=ChineseText.Display(original);
            Assert.That(translated,Does.Match("[\\u4e00-\\u9fff]"));
            Assert.That(translated,Is.Not.EqualTo(original));
        }
        [Test]
        public void DynamicNoticesKeepAddressesButTranslateTheUserFacingMessage()
        {
            Assert.That(ChineseText.Display("Streamed SEG-TEST; observer position retained"),Is.EqualTo("已载入恒星系 SEG-TEST，观察者坐标保持不变"));
            Assert.That(ChineseText.Display("STELLAR REFERENCE / BlueMainSequence / no relativistic lensing"),Does.Contain("蓝色主序星"));
            Assert.That(ChineseText.Display("PARENT-RELATIVE MOON / Lava"),Is.EqualTo("母星相对卫星 / 熔岩天体"));
            Assert.That(ChineseText.Display("Restore failed: Invalid orientation"),Does.StartWith("恢复失败："));
            Assert.That(ChineseText.Display("已选中目标"),Is.EqualTo("已选中目标"));
        }
        [Test]
        public void PointerHitRegionsCoverHudAndOpenPanelsWithoutBlockingTheSpaceView()
        {
            Assert.That(ExplorerHud.PanelContains(new Vector2(1000,20),6,false,false),Is.True);
            Assert.That(ExplorerHud.PanelContains(new Vector2(1500,270),6,false,false),Is.True);
            Assert.That(ExplorerHud.PanelContains(new Vector2(800,450),6,false,false),Is.False);
            Assert.That(ExplorerHud.PanelContains(new Vector2(800,450),6,false,true),Is.True);
            Assert.That(ExplorerHud.PanelContains(new Vector2(1200,680),6,true,false),Is.True);
            Assert.That(ExplorerHud.PanelContains(new Vector2(1200,680),6,false,false),Is.False);
        }
    }
}
