using System;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceEngine.Runtime
{
    /// <summary>
    /// Overlay HUD on a 1600x900 canvas. Controls sit in compact translucent groups at the edges;
    /// engineering telemetry (geometry, sectors, frame time) is behind the F10 developer overlay.
    /// </summary>
    public sealed class ExplorerHud : MonoBehaviour
    {
        UniverseApp app;
        bool settingsOpen,helpOpen,seedFocused,systemCollapsed;
        string seedText;
        const string SeedControl="UniverseSeedInput";
        static readonly UnityEngine.Rect ToolbarArea=new UnityEngine.Rect(1000,8,580,40);
        static readonly UnityEngine.Rect TelemetryArea=new UnityEngine.Rect(1320,62,260,262);
        static readonly UnityEngine.Rect SettingsArea=new UnityEngine.Rect(1008,334,568,440);
        static readonly UnityEngine.Rect HelpArea=new UnityEngine.Rect(390,150,820,600);
        static readonly UnityEngine.Rect SeedField=new UnityEngine.Rect(1124,628,214,30);
        const float SystemX=20,SystemY=84,SystemWidth=236;
        public bool CapturesInput => app!=null&&app.ShowHud&&settingsOpen&&seedFocused;
        public bool PointerOverUI
        {
            get
            {
                if(app==null||!app.ShowHud||Mouse.current==null)return false;
                Vector2 point=Mouse.current.position.ReadValue();
                return PanelContains(new Vector2(point.x*1600/Math.Max(1,Screen.width),(Screen.height-point.y)*900/Math.Max(1,Screen.height)),
                    app.System?.Planets?.Length??0,settingsOpen,helpOpen,systemCollapsed);
            }
        }
        static float SystemHeight(int bodyCount,bool collapsed)=>collapsed?48:64+Math.Max(0,bodyCount)*28+38+24+3*30+12;
        public static bool PanelContains(Vector2 point,int bodyCount,bool settings,bool help)=>PanelContains(point,bodyCount,settings,help,false);
        static bool PanelContains(Vector2 point,int bodyCount,bool settings,bool help,bool collapsed)
        {
            return ToolbarArea.Contains(point)||TelemetryArea.Contains(point)||
                new UnityEngine.Rect(SystemX,SystemY,SystemWidth,SystemHeight(bodyCount,collapsed)).Contains(point)||
                (settings&&SettingsArea.Contains(point))||(help&&HelpArea.Contains(point));
        }
        void Awake(){app=GetComponent<UniverseApp>();seedText=app.Settings.UniverseSeed.ToString();}
        void Text(float x,float y,float w,string text,GUIStyle style=null,float h=30){HudSkin.Shadowed(new UnityEngine.Rect(x,y,w,h),text,style??HudSkin.Label);}
        void ClearTextFocus(){GUI.FocusControl(null);seedFocused=false;}
        public void CloseTransientPanels(){settingsOpen=helpOpen=seedFocused=false;}
        void OnGUI()
        {
            if(app==null||app.System==null||!app.ShowHud){seedFocused=false;return;}
            Matrix4x4 previousMatrix=GUI.matrix;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
            if(settingsOpen&&Event.current.type==EventType.MouseDown)
            {
                Vector2 pointer=Event.current.mousePosition;
                if(Mouse.current!=null){var physical=Mouse.current.position.ReadValue();pointer=new Vector2(physical.x*1600/Math.Max(1,Screen.width),(Screen.height-physical.y)*900/Math.Max(1,Screen.height));}
                if(!SeedField.Contains(pointer))ClearTextFocus();
            }
            var tools=app.GetComponent<ExplorerTools>();
            DrawHeader(tools);
            DrawSystem(tools);
            DrawTelemetry();
            DrawStatus();
            if(app.ShowDeveloperOverlay)DrawDeveloper();
            HudSkin.Fill(new UnityEngine.Rect(792,450,16,1),new Color(.6f,.9f,.95f,.45f));HudSkin.Fill(new UnityEngine.Rect(800,442,1,16),new Color(.6f,.9f,.95f,.45f));
            if(settingsOpen)DrawSettings();else seedFocused=false;
            if(helpOpen)DrawHelp();
            GUI.matrix=previousMatrix;
        }
        void DrawHeader(ExplorerTools tools)
        {
            Text(24,12,360,"SPACE ENGINE  G",HudSkin.Title);
            string place=app.GalaxyOverview?"棒旋银河 · 直径 "+(app.Settings.Galaxy.RadiusLy*2).ToString("N0")+" 光年":app.NucleusView?"银河中央核心":app.System.Name+" · "+ChineseText.StellarKind(app.System.Kind);
            Text(24,36,520,place,HudSkin.Small);
            Text(24,54,520,"种子 "+app.Settings.UniverseSeed+" · 第 "+app.Settings.WorldGenerationVersion+" 代 · "+app.Settings.Galaxy.ArmCount+" 条旋臂",HudSkin.Tiny);
            float x=1008;const float w=76,gap=6,y=14,h=28;
            if(HudSkin.Button(new UnityEngine.Rect(x,y,w,h),"搜索 F3",tools!=null&&tools.SearchOpen))tools?.ToggleSearch();x+=w+gap;
            if(HudSkin.Button(new UnityEngine.Rect(x,y,w,h),"详情 F2",tools!=null&&tools.ShowDetails))tools?.ToggleDetails();x+=w+gap;
            if(HudSkin.Button(new UnityEngine.Rect(x,y,w,h),"轨道 O",tools!=null&&tools.ShowOrbits))tools?.ToggleOrbits();x+=w+gap;
            if(HudSkin.Button(new UnityEngine.Rect(x,y,w,h),"标签 L",tools!=null&&tools.ShowLabels))tools?.ToggleLabels();x+=w+gap;
            if(HudSkin.Button(new UnityEngine.Rect(x,y,w,h),"书签 F6",tools!=null&&tools.BookmarksOpen))tools?.ToggleBookmarks();x+=w+gap;
            if(HudSkin.Button(new UnityEngine.Rect(x,y,w,h),"设置",settingsOpen)){settingsOpen=!settingsOpen;helpOpen=false;ClearTextFocus();}x+=w+gap;
            if(HudSkin.Button(new UnityEngine.Rect(x,y,w,h),"帮助",helpOpen)){helpOpen=!helpOpen;settingsOpen=false;ClearTextFocus();}
        }
        void DrawSystem(ExplorerTools tools)
        {
            int count=app.System.Planets.Length;
            // The galaxy overview is not about the local system; keep only its header.
            bool collapsed=systemCollapsed||app.GalaxyOverview;
            float height=SystemHeight(count,collapsed);
            HudSkin.Panel(new UnityEngine.Rect(SystemX,SystemY,SystemWidth,height),.58f);
            HudSkin.Fill(new UnityEngine.Rect(SystemX,SystemY+10,2,24),HudSkin.Accent*new Color(1,1,1,.8f));
            if(GUI.Button(new UnityEngine.Rect(SystemX,SystemY,SystemWidth,40),GUIContent.none,GUIStyle.none))systemCollapsed=!systemCollapsed;
            GUI.Label(new UnityEngine.Rect(SystemX+12,SystemY+5,200,24),"恒星系 · "+ChineseText.StellarKind(app.System.Kind),HudSkin.Label);
            GUI.Label(new UnityEngine.Rect(SystemX+SystemWidth-26,SystemY+5,20,24),collapsed?"+":"-",HudSkin.Small);
            GUI.Label(new UnityEngine.Rect(SystemX+12,SystemY+25,210,22),app.System.StarTemperature.ToString("N0")+" K · "+(app.System.StarMass/PhysicalConstants.SolarMass).ToString("F2")+" 倍太阳质量",HudSkin.Small);
            if(collapsed)return;
            float y=SystemY+52;
            for(int i=0;i<count;i++)
            {
                var p=app.System.Planets[i];
                bool selected=tools?.Target!=null?tools.Target.X==app.CellX&&tools.Target.Y==app.CellY&&tools.Target.Z==app.CellZ&&tools.Target.BodyIndex==i:i==app.SelectedPlanet;
                string[] parts=p.Name.Split(' ');string shortName=parts.Length>1?parts[parts.Length-1]:p.Name;
                float indent=p.IsMoon?16:0;
                if(HudSkin.Button(new UnityEngine.Rect(SystemX+10+indent,y,SystemWidth-20-indent,25),(p.IsMoon?"· ":"")+shortName+"   "+ChineseText.PlanetKind(p.Kind),selected,true))tools?.SelectCurrentBody(i);
                HudSkin.Fill(new UnityEngine.Rect(SystemX+SystemWidth-22,y+9,7,7),KindColor(p.Kind));
                y+=28;
            }
            y+=4;
            float half=(SystemWidth-26)/2;
            if(HudSkin.Button(new UnityEngine.Rect(SystemX+10,y,half,26),"观察恒星 T"))app.FrameStar();
            if(HudSkin.Button(new UnityEngine.Rect(SystemX+16+half,y,half,26),"邻近星系 N"))app.JumpToNeighbour();
            y+=36;
            GUI.Label(new UnityEngine.Rect(SystemX+12,y-2,200,22),"导航",HudSkin.Small);y+=20;
            if(HudSkin.Button(new UnityEngine.Rect(SystemX+10,y,half,26),"飞向目标 G")){if(tools!=null)tools.StartTargetFlight();else app.BeginDescent();}
            if(HudSkin.Button(new UnityEngine.Rect(SystemX+16+half,y,half,26),"上升 R"))app.BeginAscent();y+=30;
            if(HudSkin.Button(new UnityEngine.Rect(SystemX+10,y,half,26),"跳转观察 F")){if(tools!=null)tools.FrameTarget();else app.FramePlanet(app.SelectedPlanet,app.Settings.InitialAltitude);}
            if(HudSkin.Button(new UnityEngine.Rect(SystemX+16+half,y,half,26),"银河总览 M"))app.FrameGalaxy();y+=30;
            if(HudSkin.Button(new UnityEngine.Rect(SystemX+10,y,half,26),"返回起点"))app.ReturnHome();
            if(HudSkin.Button(new UnityEngine.Rect(SystemX+16+half,y,half,26),"银河核心 B"))app.FrameNucleus();
        }
        static Color KindColor(PlanetKind kind)
        {
            switch(kind)
            {
                case PlanetKind.Oceanic:return new Color(.3f,.62f,.95f);
                case PlanetKind.Desert:return new Color(.9f,.66f,.36f);
                case PlanetKind.Ice:return new Color(.78f,.9f,.98f);
                case PlanetKind.Lava:return new Color(1,.4f,.16f);
                case PlanetKind.GasGiant:return new Color(.86f,.7f,.5f);
                case PlanetKind.IceGiant:return new Color(.45f,.8f,.86f);
                default:return new Color(.66f,.63f,.6f);
            }
        }
        void DrawTelemetry()
        {
            var area=TelemetryArea;
            HudSkin.Panel(area,.42f);
            float x=area.x+16,w=area.width-30,y=area.y+10;
            string altitudeLabel=app.GalaxyOverview?"距银河中心":app.NucleusView?"距几何视界":app.StellarView?
                (app.System.Kind==StellarKind.BlackHole?"距几何视界":"距恒星表面")+" · "+app.System.Name:(app.Planet.IsLandable?"距地表高度":"距云顶高度")+" · "+app.Planet.Name;
            Text(x,y,w,altitudeLabel,HudSkin.Small);
            double displayDistance=app.GalaxyOverview?GalaxyModel.ToGalacticLy(app.Observer,app.Settings.Galaxy).Length*PhysicalConstants.LightYear:app.Altitude;
            Text(x,y+16,w,Distance(displayDistance),HudSkin.Number,42);
            Text(x,y+64,w,"速度",HudSkin.Small);Text(x+44,y+62,w-44,Distance(app.Speed)+" / 秒");
            Text(x,y+88,w,"巡航",HudSkin.Small);Text(x+44,y+86,w-44,Distance(app.CruiseSpeed)+" / 秒  · 滚轮");
            Text(x,y+112,w,"时间",HudSkin.Small);Text(x+44,y+110,w-44,(app.SimulationTime/86400).ToString("F3")+" 天  ×"+app.TimeRate.ToString("G4"));
            if(HudSkin.Button(new UnityEngine.Rect(x,y+146,104,26),app.TimeRate==0?"继续时间":"暂停时间",app.TimeRate==0))app.TimeRate=app.TimeRate==0?1:0;
            if(HudSkin.Button(new UnityEngine.Rect(x+110,y+146,104,26),"时间 ×100"))app.TimeRate=app.TimeRate>=1e6?1:Math.Max(1,app.TimeRate)*100;
            string context=app.GalaxyOverview?"本地恒星密度 "+app.LocalStellarDensity.ToString("G3")+" / 立方秒差距":app.StellarView?ChineseText.StellarKind(app.System.Kind)+" · 半径 "+Distance(app.System.StarRadius):ChineseText.PlanetKind(app.Planet.Kind)+" · 半径 "+Distance(app.Planet.Radius);
            Text(x,y+184,w,context,HudSkin.Small);
            Text(x,y+204,w,"F1 隐藏界面 · F10 开发信息",HudSkin.Tiny);
        }
        void DrawStatus()
        {
            Text(24,838,900,ChineseText.Display(app.Navigation));
            Text(24,860,1000,ChineseText.Display(app.Notice),HudSkin.Small);
        }
        void DrawDeveloper()
        {
            const float x=1196,w=390;
            Text(x,780,w,"帧间隔 "+app.FrameMilliseconds.ToString("F1")+" 毫秒 / "+(1000/Math.Max(1,app.FrameMilliseconds)).ToString("F0")+" 帧/秒",HudSkin.Small);
            Text(x,800,w,"几何 "+(app.ActiveTriangles+app.BodyTriangles).ToString("N0")+" 三角形 / 层级 "+app.MaxLod,HudSkin.Small);
            Text(x,820,w,app.Stars.Count+" 颗解析恒星 / 地表 "+app.Settings.TerrainMaterialResolution+"² × 6",HudSkin.Small);
            Text(x,840,w,"银河体积 "+app.GalacticLight.RenderedWidth+" × "+app.GalacticLight.RenderedHeight+" / "+app.GalacticLight.EffectiveSampleCount+" 步",HudSkin.Small);
            Text(x,860,w,"扇区 "+app.Observer.sectorX+" / "+app.Observer.sectorY+" / "+app.Observer.sectorZ+" · 相机相对 64 位",HudSkin.Small);
        }
        void Slider(float y,string label,ref float value,float min,float max,string format="F2")
        {
            var area=SettingsArea;
            GUI.Label(new UnityEngine.Rect(area.x+22,y,170,24),label,HudSkin.Label);
            value=GUI.HorizontalSlider(new UnityEngine.Rect(area.x+200,y+8,280,18),value,min,max);
            GUI.Label(new UnityEngine.Rect(area.x+490,y,64,24),value.ToString(format),HudSkin.Small);
        }
        void DrawSettings()
        {
            var area=SettingsArea;
            HudSkin.Panel(area,.9f);
            GUI.Label(new UnityEngine.Rect(area.x+22,area.y+14,500,26),"渲染与生成设置",HudSkin.Title);
            float y=area.y+52;
            Slider(y,"大气曝光",ref app.Settings.AtmosphereExposure,.1f,5);y+=32;
            Slider(y,"恒星亮度",ref app.Settings.StarBrightness,.25f,3);y+=32;
            Slider(y,"背景星空亮度",ref app.Settings.StarfieldBrightness,0,3);y+=32;
            float bloom=app.Settings.BloomIntensity;Slider(y,"泛光强度",ref bloom,0,2);
            if(bloom!=app.Settings.BloomIntensity){app.Settings.BloomIntensity=bloom;app.ApplyPostProcessing();}y+=32;
            if(app.GalaxyOverview)Slider(y,"银河总览视场",ref app.Settings.GalaxyOverviewFieldOfView,30,90,"F0");
            else Slider(y,"飞行视场",ref app.Settings.FieldOfView,40,100,"F0");
            y+=32;
            float budget=app.Settings.TerrainPatchBudget;Slider(y,"地形区块预算",ref budget,64,384,"F0");app.Settings.TerrainPatchBudget=(int)budget;
            foreach(var terrain in app.Terrains){terrain.MaximumVisiblePatches=app.Settings.TerrainPatchBudget;terrain.MaximumResidentPatches=app.Settings.TerrainPatchBudget*2;}
            y+=44;
            GUI.Label(new UnityEngine.Rect(area.x+22,SeedField.y+3,100,24),"宇宙种子",HudSkin.Label);
            GUI.SetNextControlName(SeedControl);seedText=GUI.TextField(SeedField,seedText,HudSkin.Input);
            seedFocused=GUI.GetNameOfFocusedControl()==SeedControl;
            if(HudSkin.Button(new UnityEngine.Rect(area.x+346,SeedField.y,200,30),"重新生成")&&int.TryParse(seedText,out int seed))
            {ClearTextFocus();app.Settings.WorldGenerationVersion=UniverseGenerator.GenerationVersion;app.Settings.UniverseSeed=seed;app.LoadSystem(app.CellX,app.CellY,app.CellZ);app.FramePlanet(0,app.Settings.InitialAltitude);}
            y=SeedField.y+44;
            if(HudSkin.Button(new UnityEngine.Rect(area.x+22,y,170,30),"保存飞行 F5"))app.SaveFlight();
            if(HudSkin.Button(new UnityEngine.Rect(area.x+200,y,170,30),"恢复飞行 F9"))app.LoadFlight();
            if(HudSkin.Button(new UnityEngine.Rect(area.x+378,y,168,30),"银河侧视"))app.FrameGalaxy(true);
            y+=40;
            if(HudSkin.Button(new UnityEngine.Rect(area.x+22,y,170,28),"开发信息 F10",app.ShowDeveloperOverlay))app.ShowDeveloperOverlay=!app.ShowDeveloperOverlay;
            GUI.Label(new UnityEngine.Rect(area.x+206,y+4,340,24),"高级参数在项目配置 UniverseSettings 中",HudSkin.Tiny);
        }
        void DrawHelp()
        {
            var area=HelpArea;
            HudSkin.Panel(area,.92f);
            GUI.Label(new UnityEngine.Rect(area.x+30,area.y+20,760,28),"飞行与探索帮助",HudSkin.Title);
            string[] lines={"右键拖动：自由观察；Alt + 右键拖动：绕目标观察", "W A S D：前后左右；Q / E：升降；Z / X：滚转",
                "滚轮或 + / -：设置巡航速度；Shift：加速 10 倍", "G：连续飞向选中目标并下降；R：持续上升",
                "F / Tab：跳转观察 / 切换天体；T：观察恒星", "F3：搜索；O：轨道；L：标签；F6：书签",
                "F2 / I：天体详情页；C：居中；Y：跟踪；V：恒星系总览", "M：银河总览；Home：返回起始恒星系；B：银河核心",
                "N：显式跳转到邻近的已生成恒星系", "Esc：取消导航；空格：暂停时间；F1：隐藏界面；F10：开发信息",
                "F5 / F9：保存 / 恢复；F7：截图；Ctrl + B：添加书签", "[ / ]：减慢 / 加快模拟时间",
                "气态巨行星和冰巨行星没有可供降落的固体表面。",
                "银河远景代表三维星群；自由飞行连续，跳转观察会立即改变位置。",
                "背景星空为未解析恒星层，随所在区域与银河辐射变化；黑洞显示史瓦西引力透镜（无吸积盘）。"};
            for(int i=0;i<lines.Length;i++)GUI.Label(new UnityEngine.Rect(area.x+30,area.y+66+i*32,760,28),lines[i],i>11?HudSkin.Small:HudSkin.Label);
        }
        public static string Distance(double value)
        {
            if(Math.Abs(value)>=PhysicalConstants.LightYear*.01)return (value/PhysicalConstants.LightYear).ToString("F3")+" 光年";
            if(Math.Abs(value)>=PhysicalConstants.AstronomicalUnit*.01)return (value/PhysicalConstants.AstronomicalUnit).ToString("F3")+" 天文单位";
            if(Math.Abs(value)>=1000)return (value/1000).ToString(Math.Abs(value)>1e7?"N0":"N2")+" 千米";
            return value.ToString("N2")+" 米";
        }
    }
}
