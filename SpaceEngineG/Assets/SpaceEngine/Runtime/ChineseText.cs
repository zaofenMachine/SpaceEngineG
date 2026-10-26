using System;
using System.Collections.Generic;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Runtime
{
    /// <summary>Chinese UI strings and the redistributable embedded static CJK font.</summary>
    public static class ChineseText
    {
        public const string FontResourcePath = "Fonts/NotoSansCJKsc-Regular";
        public const string GlyphProbe = "宇宙银河恒星行星地表轨道标签书签搜索暂停继续视场设置种子巡航速度目标选择定位跟踪保存恢复中文输入";
        static Font font;
        static readonly SpaceEngine.Core.StellarKind[] stellarKinds=(SpaceEngine.Core.StellarKind[])Enum.GetValues(typeof(SpaceEngine.Core.StellarKind));
        static readonly SpaceEngine.Core.PlanetKind[] planetKinds=(SpaceEngine.Core.PlanetKind[])Enum.GetValues(typeof(SpaceEngine.Core.PlanetKind));
        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.Load<Font>(FontResourcePath);
                if (font == null) throw new InvalidOperationException("缺少捆绑的 Noto Sans CJK SC 字体：" + FontResourcePath);
                return font;
            }
        }
        public static string PlanetKind(SpaceEngine.Core.PlanetKind kind)
        {
            switch (kind)
            {
                case SpaceEngine.Core.PlanetKind.Rocky: return "岩石行星";
                case SpaceEngine.Core.PlanetKind.Oceanic: return "海洋行星";
                case SpaceEngine.Core.PlanetKind.Desert: return "沙漠行星";
                case SpaceEngine.Core.PlanetKind.Ice: return "冰封行星";
                case SpaceEngine.Core.PlanetKind.Lava: return "熔岩天体";
                case SpaceEngine.Core.PlanetKind.GasGiant: return "气态巨行星";
                case SpaceEngine.Core.PlanetKind.IceGiant: return "冰巨行星";
                default: return "未知天体";
            }
        }
        public static string StellarKind(SpaceEngine.Core.StellarKind kind)
        {
            switch (kind)
            {
                case SpaceEngine.Core.StellarKind.RedDwarf: return "红矮星";
                case SpaceEngine.Core.StellarKind.MainSequence: return "主序星";
                case SpaceEngine.Core.StellarKind.BlueMainSequence: return "蓝色主序星";
                case SpaceEngine.Core.StellarKind.RedGiant: return "红巨星";
                case SpaceEngine.Core.StellarKind.WhiteDwarf: return "白矮星";
                case SpaceEngine.Core.StellarKind.NeutronStar: return "中子星";
                case SpaceEngine.Core.StellarKind.BlackHole: return "黑洞";
                default: return "未知恒星";
            }
        }
        public static string GalacticComponent(GalaxyComponent component)
        {
            switch (component)
            {
                case GalaxyComponent.ThinDisk: return "薄盘";
                case GalaxyComponent.ThickDisk: return "厚盘";
                case GalaxyComponent.SpiralArms: return "旋臂";
                case GalaxyComponent.Bulge: return "核球";
                case GalaxyComponent.Bar: return "银河棒";
                case GalaxyComponent.StellarHalo: return "恒星晕";
                default: return "银河区域";
            }
        }
        static readonly Dictionary<string,string> messages = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            {"FREE FLIGHT","自由飞行"},{"CONTINUOUS DESCENT","持续下降"},{"CONTINUOUS ASCENT","持续上升"},
            {"APPROACH CLOUD TOPS / NO SOLID SURFACE","正在接近云顶 / 没有固体地表"},
            {"HOLDING ABOVE CLOUDS / NON-LANDABLE","云顶悬停 / 不可降落"},{"LANDED / TERRAIN FOLLOW","已着陆 / 地形跟随"},
            {"RMB look  /  WASD fly  /  scroll speed","右键拖动观察 / WASD 飞行 / 滚轮设置巡航速度"},
            {"No resolved system in local volume. Home returns to the inhabited disk.","附近没有可解析恒星系。按 Home 返回银河盘内的起始星系。"},
            {"Explicit navigation jump / new deterministic system","已显式跳转至新的程序化恒星系"},
            {"Home system / generation v2","起始恒星系 / 第 2 代生成器"},
            {"GALACTIC EDGE / spatial disk thickness","银河侧视 / 观察盘面厚度"},
            {"GALAXY OVERVIEW / M to frame / Home to return","银河总览 / M 重新定位 / Home 返回起点"},
            {"CENTRAL BLACK HOLE / geometric horizon; no relativistic lensing","中央黑洞 / 几何视界，尚未模拟相对论透镜"},
            {"Flight saved / F9 restore","飞行状态已保存 / F9 恢复"},{"Flight restored","飞行状态已恢复"},
            {"No generation v2 saved flight","尚无第 2 代生成器的飞行存档"},
            {"Generation v1 save retained; open it with the 0.1.0 build.","第 1 代存档已保留，请使用 0.1.0 版本读取。"},
            {"SOLAR NEIGHBOURHOOD / looking into the galactic disk","太阳邻域 / 朝向银河盘观察"},
            {"PLANET / Desert / generated admitted-system desert","行星 / 沙漠行星 / 已生成目录中的沙漠天体"},
            {"GALACTIC VOLUME / rotating-view performance capture","银河体积 / 旋转视角性能采集"},
            {"GALAXY AND CELESTIAL ACCEPTANCE PASSED","银河与天体自动验收通过"},
            {"ACCEPTANCE FAILED / inspect evidence","自动验收失败 / 请查看验收证据"},
            {"AUTOMATED ACCEPTANCE / continuous flight from 0.03 ly to terrain","自动验收 / 从 0.03 光年外连续飞抵地表"},
            {"ACCEPTANCE PASSED / evidence saved","自动验收通过 / 证据已保存"},
            {"ACCEPTANCE FAILED / inspect runtime-verification.json","自动验收失败 / 请查看运行时验证报告"}
        };
        /// <summary>Translate at the display boundary, preserving simulation/save contracts.</summary>
        public static string Display(string value)
        {
            if(string.IsNullOrEmpty(value))return "";
            if(value[0]>='\u2e80')return value;
            if(messages.TryGetValue(value,out string translated))return translated;
            const string entered="Entered local frame of ",streamed="Streamed ",retained="; observer position retained";
            if(value.StartsWith(entered,StringComparison.Ordinal))return "已进入本地参考系："+value.Substring(entered.Length);
            if(value.StartsWith(streamed,StringComparison.Ordinal)&&value.EndsWith(retained,StringComparison.Ordinal))
                return "已载入恒星系 "+value.Substring(streamed.Length,value.Length-streamed.Length-retained.Length)+"，观察者坐标保持不变";
            const string stellar="STELLAR REFERENCE / ";
            if(value.StartsWith(stellar,StringComparison.Ordinal))return "恒星参考 / "+DisplayKind(value.Substring(stellar.Length).Split('/')[0].Trim())+" / 尚未模拟相对论透镜";
            if(value.StartsWith("Restore failed:",StringComparison.Ordinal))return "恢复失败：存档无效或无法读取，详细错误见日志";
            const string planet="PLANET / ",moon="PARENT-RELATIVE MOON / ";
            if(value.StartsWith(planet,StringComparison.Ordinal))return "行星 / "+DisplayKind(value.Substring(planet.Length));
            if(value.StartsWith(moon,StringComparison.Ordinal))return "母星相对卫星 / "+DisplayKind(value.Substring(moon.Length));
            return DisplayKind(value);
        }
        static string DisplayKind(string value)
        {
            foreach(SpaceEngine.Core.StellarKind kind in stellarKinds)if(kind.ToString()==value)return StellarKind(kind);
            foreach(SpaceEngine.Core.PlanetKind kind in planetKinds)if(kind.ToString()==value)return PlanetKind(kind);
            return value;
        }
    }
}
