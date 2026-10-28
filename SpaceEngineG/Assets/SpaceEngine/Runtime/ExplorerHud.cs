using System;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceEngine.Runtime
{
    public sealed class ExplorerHud : MonoBehaviour
    {
        UniverseApp app;
        GUIStyle small,body,title,button,number,input;
        bool settingsOpen,helpOpen,seedFocused;
        string seedText;
        const string SeedControl="UniverseSeedInput";
        readonly Color ink=new Color(.72f,.80f,.84f),cyan=new Color(.43f,.89f,.94f),muted=new Color(.48f,.61f,.68f);
        public bool CapturesInput => app!=null&&app.ShowHud&&settingsOpen&&seedFocused;
        public bool PointerOverUI
        {
            get
            {
                if(app==null||!app.ShowHud||Mouse.current==null)return false;
                Vector2 point=Mouse.current.position.ReadValue();
                return PanelContains(new Vector2(point.x*1600/Math.Max(1,Screen.width),(Screen.height-point.y)*900/Math.Max(1,Screen.height)),
                    app.System?.Planets?.Length??0,settingsOpen,helpOpen);
            }
        }
        public static bool PanelContains(Vector2 point,int bodyCount,bool settings,bool help)
        {
            float listHeight=164+Math.Max(0,bodyCount)*30;
            return new UnityEngine.Rect(0,0,1600,78).Contains(point)||new UnityEngine.Rect(0,818,1600,82).Contains(point)||
                new UnityEngine.Rect(26,110,260,listHeight).Contains(point)||new UnityEngine.Rect(26,110+listHeight+24,260,242).Contains(point)||
                new UnityEngine.Rect(1285,110,289,372).Contains(point)||
                (settings&&new UnityEngine.Rect(1010,425,564,375).Contains(point))||
                (help&&new UnityEngine.Rect(390,190,820,560).Contains(point));
        }
        void Awake(){app=GetComponent<UniverseApp>();seedText=app.Settings.UniverseSeed.ToString();}
        void Styles()
        {
            if(body!=null)return;
            Font font=ChineseText.Font;
            body=new GUIStyle(GUI.skin.label){font=font,fontSize=14};body.normal.textColor=ink;
            small=new GUIStyle(body){fontSize=12};small.normal.textColor=muted;
            title=new GUIStyle(body){fontSize=21,fontStyle=FontStyle.Bold};title.normal.textColor=Color.white;
            number=new GUIStyle(body){fontSize=26,fontStyle=FontStyle.Normal};number.normal.textColor=cyan;
            input=new GUIStyle(GUI.skin.textField){font=font,fontSize=14,padding=new RectOffset(8,8,3,3)};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=13,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(12,8,3,3)};
            button.normal.textColor=ink;button.hover.textColor=cyan;button.active.textColor=Color.white;
            button.normal.background=Texture2D.whiteTexture;button.hover.background=Texture2D.whiteTexture;button.active.background=Texture2D.whiteTexture;
        }
        void Rect(float x,float y,float w,float h,Color color){Color old=GUI.color;GUI.color=color;GUI.DrawTexture(new UnityEngine.Rect(x,y,w,h),Texture2D.whiteTexture);GUI.color=old;}
        void Text(float x,float y,float w,string text,GUIStyle style=null){GUI.Label(new UnityEngine.Rect(x,y,w,35),text,style??body);}
        bool Button(float x,float y,float w,string label)
        {
            Color previous=GUI.backgroundColor;GUI.backgroundColor=new Color(.08f,.14f,.18f,.94f);
            bool clicked=GUI.Button(new UnityEngine.Rect(x,y,w,30),label,button);GUI.backgroundColor=previous;return clicked;
        }
        void ClearTextFocus(){GUI.FocusControl(null);seedFocused=false;}
        public void CloseTransientPanels(){settingsOpen=helpOpen=seedFocused=false;}
        void OnGUI()
        {
            if(app==null||app.System==null||!app.ShowHud){seedFocused=false;return;}
            Styles();Matrix4x4 previousMatrix=GUI.matrix;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
            if(settingsOpen&&Event.current.type==EventType.MouseDown)
            {
                Vector2 pointer=Event.current.mousePosition;
                if(Mouse.current!=null){var physical=Mouse.current.position.ReadValue();pointer=new Vector2(physical.x*1600/Math.Max(1,Screen.width),(Screen.height-physical.y)*900/Math.Max(1,Screen.height));}
                if(!new UnityEngine.Rect(1160,649,190,28).Contains(pointer))ClearTextFocus();
            }
            var tools=app.GetComponent<ExplorerTools>();
            Rect(0,0,1600,78,new Color(.016f,.027f,.041f,.94f));Rect(0,77,1600,1,new Color(.18f,.32f,.38f,.75f));
            Text(30,12,350,"SPACE ENGINE  G",title);Text(32,45,430,"程序化宇宙 / 自由探索 04",small);
            Text(475,14,460,app.GalaxyOverview?"棒旋银河 / 直径 "+(app.Settings.Galaxy.RadiusLy*2).ToString("N0")+" 光年":app.NucleusView?"银河中央核心":app.System.Name);
            Text(475,45,460,"种子 "+app.Settings.UniverseSeed+" · 第 "+app.Settings.WorldGenerationVersion+" 代生成器 · "+app.Settings.Galaxy.ArmCount+" 条旋臂",small);
            if(Button(950,7,145,"搜索 [F3]"))tools?.ToggleSearch();
            if(Button(1103,7,145,"轨道 [O]"))tools?.ToggleOrbits();
            if(Button(1256,7,145,"标签 [L]"))tools?.ToggleLabels();
            if(Button(1409,7,156,"书签 [F6]"))tools?.ToggleBookmarks();
            if(Button(1230,42,140,settingsOpen?"关闭设置":"设置")){settingsOpen=!settingsOpen;ClearTextFocus();}
            if(Button(1380,42,185,helpOpen?"关闭帮助":"飞行帮助")){helpOpen=!helpOpen;ClearTextFocus();}

            float listHeight=164+app.System.Planets.Length*30;
            Rect(26,110,260,listHeight,new Color(.018f,.032f,.047f,.90f));Rect(26,110,3,listHeight,cyan*.7f);
            Text(44,121,230,"本地参考系 / "+ChineseText.StellarKind(app.System.Kind),small);
            Text(44,146,230,app.System.StarTemperature.ToString("N0")+" K / "+(app.System.StarMass/PhysicalConstants.SolarMass).ToString("F2")+" 倍太阳质量");
            for(int i=0;i<app.System.Planets.Length;i++)
            {
                var p=app.System.Planets[i];
                bool targetSelected=tools?.Target!=null?tools.Target.X==app.CellX&&tools.Target.Y==app.CellY&&tools.Target.Z==app.CellZ&&tools.Target.BodyIndex==i:i==app.SelectedPlanet;
                GUI.color=targetSelected?cyan:ink;
                string[] parts=p.Name.Split(' ');string shortName=parts.Length>1?parts[parts.Length-1]:p.Name;
                string label=(targetSelected?"● ":"○ ")+(p.IsMoon?"卫星 ":"")+shortName+"  "+ChineseText.PlanetKind(p.Kind);
                if(Button(42,184+i*30,228,label))tools?.SelectCurrentBody(i);
                GUI.color=Color.white;
            }
            if(Button(42,192+app.System.Planets.Length*30,228,"观察恒星 [T]"))app.FrameStar();
            if(Button(42,228+app.System.Planets.Length*30,228,"跳转邻近恒星系 [N]"))app.JumpToNeighbour();

            Rect(1285,110,289,372,new Color(.018f,.032f,.047f,.90f));
            string altitudeLabel=app.GalaxyOverview?"距银河中心":app.NucleusView?"距几何视界":app.StellarView?
                "参考 "+app.System.Name+" · 距恒星表面":"参考 "+app.Planet.Name+" · "+(app.Planet.IsLandable?"距地表高度":"距云顶高度");
            Text(1304,123,250,altitudeLabel,small);
            double displayDistance=app.GalaxyOverview?GalaxyModel.ToGalacticLy(app.Observer,app.Settings.Galaxy).Length*PhysicalConstants.LightYear:app.Altitude;
            Text(1304,148,250,Distance(displayDistance),number);
            Text(1304,193,250,"实际相对速度",small);Text(1304,214,250,Distance(app.Speed)+" / 秒");
            Text(1304,247,250,"待命巡航速度 · 滚轮设置",small);Text(1304,268,250,Distance(app.CruiseSpeed)+" / 秒");
            Text(1304,302,250,"模拟时间",small);Text(1304,323,250,(app.SimulationTime/86400).ToString("F3")+" 天   ×"+app.TimeRate.ToString("G4"));
            if(Button(1304,363,118,app.TimeRate==0?"继续时间":"暂停时间"))app.TimeRate=app.TimeRate==0?1:0;
            if(Button(1430,363,124,"时间 × 100"))app.TimeRate=app.TimeRate>=1e6?1:Math.Max(1,app.TimeRate)*100;
            Text(1304,405,250,app.GalaxyOverview?"本地密度 "+app.LocalStellarDensity.ToString("G3")+" 恒星/立方秒差距":app.StellarView?ChineseText.StellarKind(app.System.Kind)+" / 半径 "+Distance(app.System.StarRadius):ChineseText.PlanetKind(app.Planet.Kind)+" / 半径 "+Distance(app.Planet.Radius),small);
            Text(1304,443,250,"帧间隔 "+app.FrameMilliseconds.ToString("F1")+" 毫秒 / "+(1000/Math.Max(1,app.FrameMilliseconds)).ToString("F0")+" 帧/秒",small);

            float navTop=110+listHeight+24;
            Rect(26,navTop,260,242,new Color(.018f,.032f,.047f,.90f));Text(44,navTop+11,240,"导航",small);
            if(Button(42,navTop+40,228,"连续飞向目标 [G]"))
            {if(tools!=null)tools.StartTargetFlight();else app.BeginDescent();}
            if(Button(42,navTop+77,228,"持续上升至轨道 [R]"))app.BeginAscent();
            if(Button(42,navTop+114,228,"跳转观察 [F]")){if(tools!=null)tools.FrameTarget();else app.FramePlanet(app.SelectedPlanet,app.Settings.InitialAltitude);}
            if(Button(42,navTop+151,228,"银河总览 [M]"))app.FrameGalaxy();
            if(Button(42,navTop+188,108,"返回起点"))app.ReturnHome();
            if(Button(158,navTop+188,112,"银河核心 [B]"))app.FrameNucleus();

            Rect(0,818,1600,82,new Color(.016f,.027f,.041f,.96f));Rect(0,818,1600,1,new Color(.18f,.32f,.38f,.75f));
            Text(30,832,650,ChineseText.Display(app.Navigation));Text(30,861,670,ChineseText.Display(app.Notice),small);
            Text(700,831,510,"几何 "+(app.ActiveTriangles+app.BodyTriangles).ToString("N0")+" 三角形 / 层级 "+app.MaxLod);
            Text(700,858,510,app.GalaxyOverview?"三维体积 / "+app.GalacticLight.RenderedWidth+" × "+app.GalacticLight.RenderedHeight+" / "+app.Settings.GalaxyVolumeSamples+" 步积分":app.Stars.Count+" 颗解析恒星 / 地表 "+app.Settings.TerrainMaterialResolution+"² × 6 + 像素细节",small);
            Text(1220,833,370,"扇区 "+app.Observer.sectorX+" / "+app.Observer.sectorY+" / "+app.Observer.sectorZ,small);
            Text(1220,856,370,"相机相对坐标 · 64 位局部精度",small);
            Rect(792,448,16,1,new Color(.5f,.8f,.85f,.5f));Rect(800,440,1,16,new Color(.5f,.8f,.85f,.5f));
            if(settingsOpen)DrawSettings();else seedFocused=false;
            if(helpOpen)DrawHelp();
            GUI.matrix=previousMatrix;
        }
        void DrawSettings()
        {
            Rect(1010,425,564,375,new Color(.018f,.032f,.047f,.98f));Text(1030,441,500,"渲染与生成设置",title);
            Text(1030,486,210,"大气曝光");app.Settings.AtmosphereExposure=GUI.HorizontalSlider(new UnityEngine.Rect(1260,495,275,18),app.Settings.AtmosphereExposure,.1f,5);
            Text(1030,525,210,"恒星亮度");app.Settings.StarBrightness=GUI.HorizontalSlider(new UnityEngine.Rect(1260,534,275,18),app.Settings.StarBrightness,.25f,3);
            Text(1030,564,210,app.GalaxyOverview?"银河总览视场":"飞行视场");
            if(app.GalaxyOverview)app.Settings.GalaxyOverviewFieldOfView=GUI.HorizontalSlider(new UnityEngine.Rect(1260,573,275,18),app.Settings.GalaxyOverviewFieldOfView,30,90);
            else app.Settings.FieldOfView=GUI.HorizontalSlider(new UnityEngine.Rect(1260,573,275,18),app.Settings.FieldOfView,40,100);
            Text(1030,603,210,"地形区块预算 "+app.Settings.TerrainPatchBudget);app.Settings.TerrainPatchBudget=(int)GUI.HorizontalSlider(new UnityEngine.Rect(1260,612,275,18),app.Settings.TerrainPatchBudget,64,384);
            foreach(var terrain in app.Terrains){terrain.MaximumVisiblePatches=app.Settings.TerrainPatchBudget;terrain.MaximumResidentPatches=app.Settings.TerrainPatchBudget*2;}
            Text(1030,650,130,"宇宙种子");GUI.SetNextControlName(SeedControl);seedText=GUI.TextField(new UnityEngine.Rect(1160,649,190,28),seedText,input);
            seedFocused=GUI.GetNameOfFocusedControl()==SeedControl;
            if(Button(1370,648,164,"重新生成")&&int.TryParse(seedText,out int seed))
            {ClearTextFocus();app.Settings.WorldGenerationVersion=UniverseGenerator.GenerationVersion;app.Settings.UniverseSeed=seed;app.LoadSystem(app.CellX,app.CellY,app.CellZ);app.FramePlanet(0,app.Settings.InitialAltitude);}
            if(Button(1030,711,240,"保存飞行 [F5]"))app.SaveFlight();
            if(Button(1290,711,264,"恢复飞行 [F9]"))app.LoadFlight();
            if(Button(1030,755,240,"银河侧视"))app.FrameGalaxy(true);
            Text(1290,761,264,"高级参数可在项目配置中调整",small);
        }
        void DrawHelp()
        {
            Rect(390,190,820,560,new Color(.018f,.032f,.047f,.98f));Text(420,209,760,"飞行与探索帮助",title);
            string[] lines={"右键拖动：自由观察；Alt + 右键拖动：绕目标观察", "W A S D：前后左右；Q / E：升降；Z / X：滚转",
                "滚轮或 + / -：设置巡航速度；Shift：加速 10 倍", "G：连续飞向选中目标并下降；R：持续上升",
                "F / Tab：跳转观察 / 切换天体；T：观察恒星", "F3：搜索；O：轨道；L：标签；F6：书签",
                "F2 / I：天体详情页；C：居中；Y：跟踪；V：恒星系总览", "M：银河总览；Home：返回起始恒星系；B：银河核心",
                "N：显式跳转到邻近的已生成恒星系", "Esc：取消导航；空格：暂停时间；F1：隐藏界面",
                "F5 / F9：保存 / 恢复；F7：截图；Ctrl + B：添加书签", "[ / ]：减慢 / 加快模拟时间",
                "气态巨行星和冰巨行星没有可供降落的固体表面。",
                "银河远景代表三维星群；自由飞行连续，跳转观察会立即改变位置。", "黑洞目前显示几何视界，尚未模拟广义相对论透镜。"};
            for(int i=0;i<lines.Length;i++)Text(420,255+i*30,760,lines[i],i>11?small:body);
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
