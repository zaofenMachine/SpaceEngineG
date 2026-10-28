using System;
using System.Collections.Generic;
using System.Globalization;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Runtime
{
    /// <summary>A scrollable presentation of the science report. Selection callbacks never move an observer.</summary>
    public sealed class CelestialDetailsPage
    {
        public enum DataOrigin { Generated, Derived, Assumed, Estimated, NotModeled }
        public sealed class Datum
        {
            public readonly string Label, Value;
            public readonly DataOrigin Origin;
            public readonly int? RelatedBodyIndex;
            public Datum(string label,string value,DataOrigin origin,int? relatedBodyIndex=null)
            {Label=label;Value=value;Origin=origin;RelatedBodyIndex=relatedBodyIndex;}
        }
        public sealed class Section
        {
            public readonly string Title;
            public readonly List<Datum> Rows=new List<Datum>();
            public Section(string title){Title=title;}
            public void Add(string label,string value,DataOrigin origin,int? link=null)=>Rows.Add(new Datum(label,value,origin,link));
        }
        public sealed class Page
        {
            public readonly string Name,ModelNote;
            public readonly List<Section> Sections=new List<Section>();
            public Page(string name,string note){Name=name;ModelNote=note;}
            public Section Add(string title){var section=new Section(title);Sections.Add(section);return section;}
        }
        public sealed class Document
        {
            public string TargetId,Title,Subtitle,Warning;
            public int GenerationVersion;
            public readonly Page[] Pages={
                new Page("概览","模型与数据来源：质量与半径为生成参数；密度、重力与逃逸速度由其推导，不是观测值。"),
                new Page("轨道","模型与数据来源：母体两体开普勒模型；恒星日与太阳日分开，未计多体扰动、岁差和真实潮汐演化。"),
                new Page("环境","模型与数据来源：温度与大气是简化模型估计；成分与压强含种子假设，未求解三维天气及大气相变。"),
                new Page("系统与来源","模型与数据来源：目录、外观分类和种子来自确定性生成；选择相关天体只更换目标，不改变观察者位置。")};
        }

        static readonly CultureInfo Culture=CultureInfo.InvariantCulture;
        static readonly string[] GasNames={"氢气","氦气","氮气","二氧化碳","二氧化硫","水蒸气","氩气"};
        public int SelectedTab {get;private set;}
        public Vector2 ScrollPosition {get;private set;}
        GUIStyle body,small,title,heading,button,source;
        Document cached;
        StarSystemDefinition cachedSystem;
        string cachedId;
        double nextRefresh;
        readonly Color ink=new Color(.74f,.84f,.88f),cyan=new Color(.43f,.89f,.94f);

        public void SelectTab(int index)
        {
            if(index<0||index>3)throw new ArgumentOutOfRangeException(nameof(index));
            if(index!=SelectedTab){SelectedTab=index;ScrollPosition=Vector2.zero;}
        }
        public void ScrollTo(float verticalPixels)
        {ScrollPosition=new Vector2(0,Mathf.Max(0,verticalPixels));}
        public static string OriginText(DataOrigin origin)
        {
            switch(origin)
            {
                case DataOrigin.Generated:return "生成参数";
                case DataOrigin.Derived:return "公式推导";
                case DataOrigin.Assumed:return "模型假设";
                case DataOrigin.Estimated:return "模型估计";
                default:return "未建模";
            }
        }
        public static string Number(double? value,string unit,string format="G5")
        {
            if(!value.HasValue||!double.IsFinite(value.Value))return "未求解";
            return value.Value.ToString(format,Culture)+(string.IsNullOrEmpty(unit)?"":" "+unit);
        }
        public static string Temperature(double? kelvin)
        {
            if(!kelvin.HasValue||!double.IsFinite(kelvin.Value)||kelvin.Value<0)return "未求解";
            return Number(kelvin,"K","F1")+"（"+(kelvin.Value-273.15).ToString("F1",Culture)+" °C）";
        }
        public static string Duration(double? seconds)
        {
            if(!seconds.HasValue||!double.IsFinite(seconds.Value)||seconds.Value<=0)return "未定义";
            if(seconds.Value>=PhysicalConstants.JulianYear)return Number(seconds.Value/PhysicalConstants.JulianYear,"年","G5");
            if(seconds.Value>=PhysicalConstants.SecondsPerDay)return Number(seconds.Value/PhysicalConstants.SecondsPerDay,"天","G5");
            if(seconds.Value>=3600)return Number(seconds.Value/3600,"小时","G5");
            return Number(seconds,"秒","G5");
        }
        static string Distance(double? metres)=>metres.HasValue&&double.IsFinite(metres.Value)?ExplorerHud.Distance(metres.Value):"未求解";
        static string Angle(double radians)=>Number(radians*180/Math.PI,"度","F3");
        static string Pressure(double? pascals)=>!pascals.HasValue?"未求解":
            Number(pascals,"帕","G5")+"（"+Number(pascals.Value/100000,"bar","G4")+"）";
        static string GasName(GasSpecies species)
        {
            switch(species)
            {
                case GasSpecies.Hydrogen:return GasNames[0];case GasSpecies.Helium:return GasNames[1];
                case GasSpecies.Nitrogen:return GasNames[2];case GasSpecies.CarbonDioxide:return GasNames[3];
                case GasSpecies.SulfurDioxide:return GasNames[4];case GasSpecies.WaterVapor:return GasNames[5];
                case GasSpecies.Argon:return GasNames[6];default:return "其他气体";
            }
        }
        public static CelestialTarget RelatedTarget(CelestialTarget target,int bodyIndex)
        {
            if(target==null||target.System==null)throw new ArgumentNullException(nameof(target));
            if(bodyIndex < -1||bodyIndex>=target.System.Planets.Length)throw new ArgumentOutOfRangeException(nameof(bodyIndex));
            return new CelestialTarget{X=target.X,Y=target.Y,Z=target.Z,System=target.System,BodyIndex=bodyIndex};
        }
        static string Subclass(CelestialTarget target,PlanetScienceReport science)
        {
            if(target.IsStar)return target.System.Kind==StellarKind.BlackHole?"无吸积黑洞模型":
                target.System.Kind==StellarKind.WhiteDwarf||target.System.Kind==StellarKind.NeutronStar?"紧致星参考模型":"恒星光球模型";
            string prefix=target.Body.IsMoon?"卫星 · ":"";
            if(!science.Geometry.HasSolidSurface)return prefix+"1 bar 气体参考层 · 无固体地表";
            switch(target.Body.Kind)
            {
                case PlanetKind.Oceanic:return prefix+"海洋生成外观 · 固体地表";
                case PlanetKind.Desert:return prefix+"干旱生成外观 · 固体地表";
                case PlanetKind.Ice:return prefix+"冰封生成外观 · 固体地表";
                case PlanetKind.Lava:return prefix+"局部熔岩模型 · 固体地表";
                default:return prefix+"岩质生成模型 · 固体地表";
            }
        }

        public static Document CreateDocument(CelestialTarget target,double simulationTime)
        {
            if(target==null||target.System==null)throw new ArgumentNullException(nameof(target));
            var science=PlanetScience.Evaluate(target.System,target.BodyIndex,simulationTime);
            var geometry=science.Geometry;
            var document=new Document{TargetId=target.Id,GenerationVersion=science.GenerationVersion,
                Title=target.Name+" · "+(target.IsStar?ChineseText.StellarKind(target.System.Kind):ChineseText.PlanetKind(target.Body.Kind)),
                Subtitle=Subclass(target,science)+" · 第 "+science.GenerationVersion+" 代世界"};
            if(science.GenerationVersion<3)document.Warning="旧版世界保留原有轨道与外观，部分分类与热环境可能不一致；环境数据只供模型参考。";
            if((science.Caveats&ScienceCaveat.VisualClassClimateMismatch)!=0)
                document.Warning=(document.Warning??"")+" 外观分类与简化热模型不一致，请结合环境页解释。";
            if((science.Caveats&ScienceCaveat.StellarParametersInconsistent)!=0)
                document.Warning=(document.Warning??"")+" 恒星温度采用光度与半径推导值，旧参数存在差异。";
            var overview=document.Pages[0].Add("基本性质");
            overview.Add("参考半径",Distance(geometry.RadiusMetres),DataOrigin.Generated);
            overview.Add("直径",Distance(geometry.DiameterMetres),DataOrigin.Derived);
            overview.Add("质量",Number(geometry.MassKilograms,"千克","E4"),DataOrigin.Generated);
            overview.Add("平均密度",Number(geometry.MeanDensityKgM3,"千克/米³",geometry.MeanDensityKgM3>=1e7?"E4":"F2"),DataOrigin.Derived);
            overview.Add("参考半径处重力",Number(geometry.GravityAtReferenceRadiusMps2,"米/秒²","G5"),DataOrigin.Derived);
            overview.Add("逃逸速度（参考）",Number(geometry.EscapeSpeedAtReferenceRadiusMps,"米/秒","G5"),DataOrigin.Derived);
            overview.Add("参考位置",target.IsStar?(target.System.Kind==StellarKind.BlackHole?"几何事件视界":"恒星光球半径"):
                geometry.HasSolidSurface?"名义固体地表半径；局部地形高度另计":"1 bar 气体参考层；不代表可降落地表",DataOrigin.Assumed);
            if(target.IsStar)
            {
                AddStar(document,science);
            }
            else
            {
                AddPlanetOverview(document,target,science);
                AddOrbit(document,target,science);
                AddEnvironment(document,target,science);
            }
            AddSystem(document,target,science);
            return document;
        }
        static void AddStar(Document document,PlanetScienceReport science)
        {
            var star=science.Star;
            var section=document.Pages[0].Add("恒星光学参数");
            section.Add("质量 / 太阳",Number(star.MassSolarUnits,"倍太阳质量"),DataOrigin.Derived);
            section.Add("半径 / 太阳",Number(star.RadiusSolarUnits,"倍太阳半径"),DataOrigin.Derived);
            section.Add("总辐射光度",Number(star.BolometricLuminosityW,"瓦","E4"),DataOrigin.Generated);
            section.Add("光度 / 太阳",Number(star.LuminositySolarUnits,"倍太阳光度"),DataOrigin.Derived);
            section.Add("有效温度",star.IsNonAccretingBlackHole?"不适用：未建模吸积辐射":Temperature(star.EffectiveTemperatureK),
                star.IsNonAccretingBlackHole?DataOrigin.NotModeled:DataOrigin.Derived);
            section.Add("紧致星说明","重力与逃逸速度是参考半径处的牛顿量；不代表相对论环境的完整解。",DataOrigin.Assumed);
            var orbit=document.Pages[1].Add("恒星运动");
            orbit.Add("银河公转轨道","未建模；本模型保留恒星系在银河中的空间位置。",DataOrigin.NotModeled);
            orbit.Add("恒星自转与日长","未建模",DataOrigin.NotModeled);
            var environment=document.Pages[2].Add("恒星环境");
            environment.Add("行星气候参数","不适用；请选择行星或卫星查看。",DataOrigin.NotModeled);
            environment.Add("恒星光球 / 视界",star.IsNonAccretingBlackHole?"仅几何视界；没有吸积盘、喷流与相对论透镜。":"温度与光度为恒星生成模型参数；未求解恒星大气结构。",DataOrigin.Assumed);
            environment.Add("成分、气压与磁场","恒星内部组成、气压剖面和磁场未建模。",DataOrigin.NotModeled);
        }
        static void AddPlanetOverview(Document document,CelestialTarget target,PlanetScienceReport science)
        {
            var rotation=science.Rotation;var climate=science.Climate;
            var section=document.Pages[0].Add("自转与光照");
            section.Add("恒星日（自转周期）",Duration(rotation.SiderealPeriodSeconds),DataOrigin.Generated);
            section.Add("平均太阳日",rotation.SolarDayInfinite?"无有限太阳日：与恒星公转同步":
                rotation.SolarDaySeconds.HasValue?Duration(rotation.SolarDaySeconds):"未求解：卫星多层运动的平均太阳日",rotation.SolarDaySeconds.HasValue||rotation.SolarDayInfinite?DataOrigin.Derived:DataOrigin.NotModeled);
            section.Add("相对母体同步",rotation.SynchronousWithParent?"是（模型同步自转）":"否",DataOrigin.Derived);
            section.Add("轴倾角",Angle(rotation.AxialTiltRadians),DataOrigin.Generated);
            section.Add("赤道自转线速度",Number(rotation.EquatorialSpeedMps,"米/秒"),DataOrigin.Derived);
            section.Add("平衡温度",Temperature(climate.EquilibriumTemperatureK),DataOrigin.Estimated);
            section.Add("参考温度",Temperature(climate.ReferenceTemperatureK),DataOrigin.Estimated);
            section.Add("可接近区域",target.Body.IsLandable?"程序化地表；碰撞净空独立于本页选择操作":"可接近云顶；没有可供降落的固体表面",DataOrigin.Generated);
        }
        static void AddOrbit(Document document,CelestialTarget target,PlanetScienceReport science)
        {
            var orbit=science.Orbit;var section=document.Pages[1].Add("相对母体的轨道");
            string parent=orbit.ParentIndex<0?target.System.Name:target.System.Planets[orbit.ParentIndex].Name;
            section.Add("环绕母体",parent,DataOrigin.Generated,orbit.ParentIndex);
            section.Add("母体质量",Number(orbit.ParentMassKilograms,"千克","E4"),DataOrigin.Generated);
            section.Add("公转周期",Duration(orbit.PeriodSeconds),DataOrigin.Derived);
            section.Add("半长轴",Distance(orbit.SemiMajorAxisMetres),DataOrigin.Generated);
            section.Add("轨道偏心率",Number(orbit.Eccentricity,"","F5"),DataOrigin.Generated);
            section.Add("近心点 / 远心点",Distance(orbit.PeriapsisMetres)+" / "+Distance(orbit.ApoapsisMetres),DataOrigin.Derived);
            section.Add("当前距母体中心",Distance(orbit.InstantaneousDistanceMetres),DataOrigin.Derived);
            section.Add("当前相对母体速度",Number(orbit.InstantaneousSpeedMps,"米/秒"),DataOrigin.Derived);
            section.Add("当前距恒星中心",Distance(orbit.StellarDistanceMetres),DataOrigin.Derived);
            section.Add("距恒星范围（几何界限）",Distance(orbit.StellarDistanceMinimumBoundMetres)+" — "+Distance(orbit.StellarDistanceMaximumBoundMetres),DataOrigin.Derived);
            section=document.Pages[1].Add("轨道方向与自转");
            section.Add("轨道倾角",Angle(orbit.InclinationRadians),DataOrigin.Generated);
            section.Add("升交点经度",Angle(orbit.LongitudeAscendingNodeRadians),DataOrigin.Generated);
            section.Add("近心点幅角",Angle(orbit.ArgumentPeriapsisRadians),DataOrigin.Generated);
            section.Add("轨道参数历元","模拟时间零点；角度属于生成参考系，不是实测天球坐标。",DataOrigin.Assumed);
            section.Add("恒星日",Duration(science.Rotation.SiderealPeriodSeconds),DataOrigin.Generated);
            section.Add("太阳日说明",science.Rotation.SolarDayInfinite?"公转与自转同步，太阳方向不形成有限重复日长。":
                science.Rotation.SolarDaySeconds.HasValue?Duration(science.Rotation.SolarDaySeconds):"未求解卫星多层轨道的平均太阳日。",science.Rotation.SolarDaySeconds.HasValue||science.Rotation.SolarDayInfinite?DataOrigin.Derived:DataOrigin.NotModeled);
            if(science.Geometry.HasSolidSurface)
                section.Add("自转应用边界","数值为生成参数模型；固体地形及日照尚未进入自转体固连系。",DataOrigin.NotModeled);
        }
        static void AddEnvironment(Document document,CelestialTarget target,PlanetScienceReport science)
        {
            var climate=science.Climate;var section=document.Pages[2].Add("能量与温度");
            section.Add("当前恒星入射通量",Number(climate.IncidentStellarFluxWm2,"瓦/米²"),DataOrigin.Derived);
            section.Add("相对地球入射通量",Number(climate.StellarFluxEarthUnits,"倍"),DataOrigin.Derived);
            section.Add("邦德反照率",Number(climate.BondAlbedo,"","F3"),DataOrigin.Assumed);
            section.Add("热重分配假设","全球均匀辐射的零维近似；未求解昼夜之间的真实输热。",DataOrigin.Assumed);
            section.Add("辐射平衡温度",Temperature(climate.EquilibriumTemperatureK),DataOrigin.Estimated);
            section.Add("含内部热的有效温度",Temperature(climate.EffectiveTemperatureK),DataOrigin.Estimated);
            section.Add("内部热通量",Number(climate.IntrinsicHeatFluxWm2,"瓦/米²"),DataOrigin.Assumed);
            section.Add("潮汐热通量",Number(climate.TidalHeatFluxWm2,"瓦/米²"),DataOrigin.Estimated);
            section.Add("潮汐耗散参数 k₂/Q",Number(climate.TidalDissipationK2OverQ,""),
                climate.TidalDissipationK2OverQ.HasValue?DataOrigin.Assumed:DataOrigin.NotModeled);
            section.Add("灰大气红外光学厚度",Number(climate.InfraredOpticalDepth,"","G4"),DataOrigin.Assumed);
            section.Add("红外发射率",Number(climate.InfraredEmissivity,"","F3"),DataOrigin.Assumed);
            section.Add("地表温度估计",climate.HasSurfaceTemperatureEstimate?Temperature(climate.SurfaceTemperatureK):
                climate.IsOneBarReference?"不适用：气体天体无固体地表":"未求解",climate.HasSurfaceTemperatureEstimate?DataOrigin.Estimated:DataOrigin.NotModeled);
            bool hasRange=climate.HasSurfaceTemperatureEstimate&&climate.SurfaceTemperatureMinimumK.HasValue&&climate.SurfaceTemperatureMaximumK.HasValue;
            section.Add("轨道温度范围估计",hasRange?Temperature(climate.SurfaceTemperatureMinimumK)+" — "+Temperature(climate.SurfaceTemperatureMaximumK):
                "未求解；不代表昼夜或季节最高最低温",hasRange?DataOrigin.Estimated:DataOrigin.NotModeled);
            section.Add("参考层温度",Temperature(climate.ReferenceTemperatureK)+(climate.IsOneBarReference?" · 1 bar 气体层":""),DataOrigin.Estimated);
            section=document.Pages[2].Add("大气参考模型");
            section.Add("大气层",climate.HasAtmosphere?"设有简化气体层":"未设大气层",DataOrigin.Generated);
            section.Add("光学大气层高度",Distance(target.Body.AtmosphereHeight),DataOrigin.Generated);
            section.Add(climate.IsOneBarReference?"参考层压强（非地表）":"地表压强假设",
                climate.IsOneBarReference?Pressure(climate.ReferencePressurePascals):climate.HasAtmosphere?Pressure(climate.SurfacePressurePascals):"未设气体层",DataOrigin.Assumed);
            var gases=new List<string>();
            if(climate.GasMoleFractions!=null)foreach(var gas in climate.GasMoleFractions)
                gases.Add(GasName(gas.Species)+" "+Number(gas.MoleFraction*100,"%","F2"));
            section.Add("主要成分（摩尔比例）",gases.Count>0?string.Join("、",gases):climate.HasAtmosphere?"未求解":"未设气体层",DataOrigin.Assumed);
            section.Add("气体平均摩尔质量",climate.HasAtmosphere?Number(climate.MeanMolarMassKgPerMol*1000,"克/摩尔"):"不适用",DataOrigin.Derived);
            section.Add("参考层气体密度",climate.HasAtmosphere?Number(climate.AtmosphericDensityKgM3,"千克/米³"):"不适用",
                climate.HasAtmosphere&&climate.AtmosphericDensityKgM3.HasValue?DataOrigin.Estimated:DataOrigin.NotModeled);
            section.Add("密度标高",climate.HasAtmosphere?Distance(climate.ScaleHeightMetres):"不适用",
                climate.HasAtmosphere&&climate.ScaleHeightMetres.HasValue?DataOrigin.Estimated:DataOrigin.NotModeled);
            section.Add("红外质量吸收系数",Number(climate.InfraredMassAbsorptionM2Kg,"米²/千克"),DataOrigin.Assumed);
            section=document.Pages[2].Add("解释与边界");
            section.Add("温度含义","全局简化能量平衡与灰大气估计；不是当地实测温度，也不包含逐时天气。",DataOrigin.Assumed);
            section.Add("范围含义","温度范围采用轨道距离几何界限估计；不是实际昼夜、季节或天气的最高最低温。",DataOrigin.Assumed);
            section.Add("大气尺度","密度标高基于参考层的等温理想气体近似；它与光学大气层的设定高度不同。",DataOrigin.Assumed);
            section.Add("气体与相变","配比不等于化学演化结果；凝结、逃逸、云微物理与气候动力学尚未求解。",DataOrigin.NotModeled);
            if((science.Caveats&ScienceCaveat.StellarEclipsesAndPlanetshineOmitted)!=0)
                section.Add("卫星光照限制","未计母体遮食、母体反照光和热辐射。",DataOrigin.NotModeled);
            if((science.Caveats&ScienceCaveat.TidalApproximationUnavailable)!=0)
                section.Add("潮汐热适用范围","当前轨道未应用小偏心率同步潮汐近似；显示零附加热不代表没有真实潮汐加热。",DataOrigin.NotModeled);
            if(climate.IncidentStellarFluxWm2<=0)section.Add("没有恒星加热","当前恒星辐照为零；温度仅来自内部热与潮汐热的简化假设，不含吸积辐射。",DataOrigin.Assumed);
            if(target.Body.Kind==PlanetKind.Lava)section.Add("局部熔岩温度","发光地形代表局部火山热源；本模型未求解熔岩局部温度和热输运。",DataOrigin.NotModeled);
            if(climate.IsLegacyGeneration)section.Add("旧版环境提示","旧世界保留原有分类；这里的简化温度不能反向证明其海洋、冰或熔岩外观自洽。",DataOrigin.Assumed);
        }
        static void AddSystem(Document document,CelestialTarget target,PlanetScienceReport science)
        {
            var system=target.System;var section=document.Pages[3].Add("所属系统与相关天体");
            section.Add("恒星系",system.Name,DataOrigin.Generated,target.IsStar?(int?)null:-1);
            if(!target.IsStar)
            {
                int parent=target.Body.ParentIndex;
                section.Add("直接母体",parent<0?system.Name:system.Planets[parent].Name,DataOrigin.Generated,parent);
            }
            int count=0;
            for(int i=0;i<system.Planets.Length;i++)
            {
                var body=system.Planets[i];
                if(body.ParentIndex!=target.BodyIndex)continue;
                section.Add(target.IsStar?"环绕恒星的行星":"直属卫星",body.Name+" · "+ChineseText.PlanetKind(body.Kind),DataOrigin.Generated,i);count++;
            }
            if(count==0)section.Add(target.IsStar?"行星目录":"直属卫星","当前生成目录中没有此类成员",DataOrigin.Generated);
            section=document.Pages[3].Add("模型与数据来源");
            section.Add("世界生成版本","第 "+science.GenerationVersion+" 代",DataOrigin.Generated);
            section.Add("程序化地址",target.Id,DataOrigin.Generated);
            section.Add("天体 / 恒星系种子",(target.IsStar?system.Seed:target.Body.Seed)+" / "+system.Seed,DataOrigin.Generated);
            section.Add("数据性质","本天体来自确定性生成模型，不是观测目录记录。",DataOrigin.Generated);
            section.Add("公式依据","牛顿引力、两体开普勒轨道、辐射能量平衡与理想气体关系。",DataOrigin.Derived);
            section.Add("参考资料","JPL 天文常数、IAU 名义太阳常数；简化辐射平衡与同步潮汐近似。",DataOrigin.Derived);
            section.Add("热模型假设","种子派生反照率、内部热与大气参数；简化潮汐热和灰大气估计。",DataOrigin.Assumed);
            section.Add("未建模数据","真实三维气候、气体化学演化、磁场、生命与生态系统均未建模。",DataOrigin.NotModeled);
            if(!string.IsNullOrEmpty(document.Warning))section.Add("旧版兼容",document.Warning,DataOrigin.Assumed);
        }

        void Styles()
        {
            if(body!=null)return;
            body=new GUIStyle(GUI.skin.label){font=ChineseText.Font,fontSize=14,wordWrap=true};body.normal.textColor=ink;
            small=new GUIStyle(body){fontSize=12};small.normal.textColor=new Color(.54f,.69f,.75f);
            source=new GUIStyle(small){alignment=TextAnchor.UpperRight};
            title=new GUIStyle(body){fontSize=21,fontStyle=FontStyle.Bold};title.normal.textColor=Color.white;
            heading=new GUIStyle(body){fontSize=16,fontStyle=FontStyle.Bold};heading.normal.textColor=cyan;
            button=new GUIStyle(GUI.skin.button){font=ChineseText.Font,fontSize=13,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(10,8,3,3),wordWrap=true};
            button.normal.background=button.hover.background=button.active.background=Texture2D.whiteTexture;
            button.normal.textColor=ink;button.hover.textColor=cyan;button.active.textColor=Color.white;
        }
        static void Fill(Rect rect,Color color)
        {Color previous=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=previous;}
        bool Button(Rect rect,string text,bool active=false)
        {
            Color previous=GUI.backgroundColor;GUI.backgroundColor=active?new Color(.12f,.30f,.36f):new Color(.07f,.14f,.19f,.98f);
            bool clicked=GUI.Button(rect,text,button);GUI.backgroundColor=previous;return clicked;
        }
        float RowHeight(Datum row,float width)
        {
            float valueWidth=width-312;
            return Mathf.Max(34,Mathf.Max(body.CalcHeight(new GUIContent(row.Label),188),
                body.CalcHeight(new GUIContent(row.Value),valueWidth))+12);
        }
        public void Draw(Rect area,CelestialTarget target,double simulationTime,Action<CelestialTarget> selectTarget,Action close,Action openSearch)
        {
            if(target==null||target.System==null)return;
            Styles();
            bool targetChanged=cachedId!=target.Id||!ReferenceEquals(cachedSystem,target.System);
            if(cached==null||targetChanged||Time.unscaledTimeAsDouble>=nextRefresh)
            {
                cached=CreateDocument(target,simulationTime);cachedId=target.Id;cachedSystem=target.System;nextRefresh=Time.unscaledTimeAsDouble+.25;
                if(targetChanged)ScrollPosition=Vector2.zero;
            }
            Fill(area,new Color(.018f,.043f,.059f,.98f));
            GUI.Label(new Rect(area.x+18,area.y+12,area.width-145,32),cached.Title,title);
            if(Button(new Rect(area.xMax-112,area.y+13,94,29),"关闭 Esc")){close?.Invoke();return;}
            GUI.Label(new Rect(area.x+18,area.y+48,area.width-190,30),cached.Subtitle,small);
            if(Button(new Rect(area.xMax-158,area.y+47,140,29),"搜索其他天体")){openSearch?.Invoke();return;}
            float tabWidth=(area.width-60)/4;
            for(int i=0;i<cached.Pages.Length;i++)
                if(Button(new Rect(area.x+18+i*(tabWidth+8),area.y+86,tabWidth,32),cached.Pages[i].Name,i==SelectedTab))SelectTab(i);
            var page=cached.Pages[SelectedTab];
            var viewport=new Rect(area.x+18,area.y+132,area.width-36,area.height-192);
            float width=viewport.width-22,height=8;
            if(!string.IsNullOrEmpty(cached.Warning))height+=small.CalcHeight(new GUIContent(cached.Warning),width-12)+24;
            foreach(var section in page.Sections){height+=38;foreach(var row in section.Rows)height+=RowHeight(row,width);height+=12;}
            ScrollPosition=GUI.BeginScrollView(viewport,ScrollPosition,new Rect(0,0,width,Mathf.Max(viewport.height,height)));
            float y=8;
            if(!string.IsNullOrEmpty(cached.Warning))
            {
                float h=small.CalcHeight(new GUIContent(cached.Warning),width-12)+16;
                Fill(new Rect(0,y,width,h),new Color(.22f,.16f,.07f,.7f));GUI.Label(new Rect(6,y+6,width-12,h-6),cached.Warning,small);y+=h+8;
            }
            foreach(var section in page.Sections)
            {
                GUI.Label(new Rect(0,y,width,30),section.Title,heading);y+=36;
                foreach(var row in section.Rows)
                {
                    float h=RowHeight(row,width);GUI.Label(new Rect(0,y+4,188,h-8),row.Label,body);
                    var valueRect=new Rect(200,y,width-312,h-4);
                    if(row.RelatedBodyIndex.HasValue&&selectTarget!=null)
                    {if(Button(valueRect,row.Value+"  ›"))selectTarget(RelatedTarget(target,row.RelatedBodyIndex.Value));}
                    else GUI.Label(new Rect(valueRect.x,valueRect.y+4,valueRect.width,valueRect.height),row.Value,body);
                    GUI.Label(new Rect(width-104,y+6,100,h-8),OriginText(row.Origin),source);
                    Fill(new Rect(0,y+h-3,width,1),new Color(.13f,.24f,.29f,.35f));y+=h;
                }
                y+=14;
            }
            GUI.EndScrollView();
            Fill(new Rect(area.x+18,area.yMax-51,area.width-36,1),new Color(.18f,.32f,.38f,.8f));
            GUI.Label(new Rect(area.x+18,area.yMax-43,area.width-36,38),page.ModelNote,small);
        }
    }
}
