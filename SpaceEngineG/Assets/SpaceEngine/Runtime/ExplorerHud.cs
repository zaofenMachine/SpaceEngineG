using System;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Runtime
{
    public sealed class ExplorerHud : MonoBehaviour
    {
        UniverseApp app;
        GUIStyle small,body,title,button,number;
        bool settingsOpen,helpOpen;
        string seedText;
        readonly Color ink=new Color(.64f,.73f,.79f),cyan=new Color(.43f,.89f,.94f),muted=new Color(.34f,.47f,.55f);
        void Awake(){app=GetComponent<UniverseApp>();seedText=app.Settings.UniverseSeed.ToString();}
        void Styles()
        {
            if(body!=null)return;
            Font font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            body=new GUIStyle(GUI.skin.label){font=font,fontSize=14};body.normal.textColor=ink;
            small=new GUIStyle(body){fontSize=11};small.normal.textColor=muted;
            title=new GUIStyle(body){fontSize=21,fontStyle=FontStyle.Bold};title.normal.textColor=Color.white;
            number=new GUIStyle(body){fontSize=26,fontStyle=FontStyle.Normal};number.normal.textColor=cyan;
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=12,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(12,8,3,3)};
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

        void OnGUI()
        {
            if(app==null||app.System==null||!app.ShowHud)return;
            Styles();GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
            Rect(0,0,1600,78,new Color(.016f,.027f,.041f,.92f));Rect(0,77,1600,1,new Color(.18f,.32f,.38f,.75f));
            Text(30,14,330,"SPACE ENGINE  G",title);Text(32,45,500,"PROCEDURAL UNIVERSE  /  EXPLORER 01",small);
            Text(700,20,300,app.System.Name,body);Text(700,45,400,"SEED "+app.Settings.UniverseSeed+"   ·   GEN "+UniverseGenerator.GenerationVersion,small);
            if(Button(1230,23,140,settingsOpen?"CLOSE SETTINGS":"SETTINGS"))settingsOpen=!settingsOpen;
            if(Button(1380,23,185,helpOpen?"CLOSE CONTROLS":"FLIGHT CONTROLS"))helpOpen=!helpOpen;

            float listHeight=128+app.System.Planets.Length*30;
            Rect(26,110,260,listHeight,new Color(.018f,.032f,.047f,.86f));Rect(26,110,3,listHeight,cyan*.7f);
            Text(44,122,230,"LOCAL SYSTEM",small);
            Text(44,145,230,app.System.StarTemperature.ToString("N0")+" K  /  "+(app.System.StarMass/PhysicalConstants.SolarMass).ToString("F2")+" M☉");
            for(int i=0;i<app.System.Planets.Length;i++)
            {
                var p=app.System.Planets[i];
                GUI.color=i==app.SelectedPlanet?cyan:ink;
                if(Button(42,184+i*30,228,(i==app.SelectedPlanet?"●  ":"○  ")+p.Name.Split(' ')[1]+"    "+(p.Radius/1000).ToString("N0")+" km"))app.FramePlanet(i,app.Settings.InitialAltitude);
                GUI.color=Color.white;
            }
            if(Button(42,192+app.System.Planets.Length*30,228,"NEXT STAR SYSTEM     [N]"))app.JumpToNeighbour();

            Rect(1285,110,289,292,new Color(.018f,.032f,.047f,.86f));
            Text(1304,123,250,"SURFACE CLEARANCE",small);
            Text(1304,148,250,Distance(app.Altitude),number);
            Text(1304,193,250,"RELATIVE SPEED",small);Text(1304,215,250,Distance(app.Speed)+" / s");
            Text(1304,252,250,"SIMULATION TIME",small);Text(1304,273,250,(app.SimulationTime/86400).ToString("F3")+" days   ×"+app.TimeRate.ToString("G4"));
            if(Button(1304,311,118,"PAUSE / RUN"))app.TimeRate=app.TimeRate==0?1:0;
            if(Button(1430,311,124,"TIME × 100"))app.TimeRate=app.TimeRate>=1e6?1:Math.Max(1,app.TimeRate)*100;
            Text(1304,360,250,"FRAME  "+app.FrameMilliseconds.ToString("F1")+" ms  /  "+(1000/Math.Max(1,app.FrameMilliseconds)).ToString("F0")+" FPS",small);

            float navTop=110+listHeight+24;
            Rect(26,navTop,260,168,new Color(.018f,.032f,.047f,.86f));Text(44,navTop+11,240,"NAVIGATION",small);
            if(Button(42,navTop+40,228,"DESCEND TO SURFACE     [G]"))app.BeginDescent();
            if(Button(42,navTop+77,228,"ASCEND TO ORBIT             [R]"))app.BeginAscent();
            if(Button(42,navTop+114,228,"FRAME PLANET                    [F]"))app.FramePlanet(app.SelectedPlanet,app.Settings.InitialAltitude);

            Rect(0,818,1600,82,new Color(.016f,.027f,.041f,.94f));Rect(0,818,1600,1,new Color(.18f,.32f,.38f,.75f));
            Text(30,833,430,app.Navigation,body);Text(30,861,670,app.Notice,small);
            Text(700,831,440,"TERRAIN  "+app.ActivePatches+" patches  /  "+app.ActiveTriangles.ToString("N0")+" tris",body);
            Text(700,858,450,"LOD "+app.MaxLod+"   ·   BUILD "+app.BuildMilliseconds.ToString("F2")+" ms   ·   "+app.Stars.Count+" spatial stars",small);
            Text(1220,833,370,"SECTOR  "+app.Observer.sectorX+" / "+app.Observer.sectorY+" / "+app.Observer.sectorZ,small);
            Text(1220,856,370,"CAMERA-RELATIVE   ·   64-BIT LOCAL",small);
            Rect(792,448,16,1,new Color(.5f,.8f,.85f,.5f));Rect(800,440,1,16,new Color(.5f,.8f,.85f,.5f));
            if(settingsOpen)DrawSettings();
            if(helpOpen)DrawHelp();
        }

        void DrawSettings()
        {
            Rect(1010,425,564,375,new Color(.018f,.032f,.047f,.98f));Text(1030,441,500,"RENDER & GENERATION",title);
            Text(1030,486,210,"Atmosphere exposure");app.Settings.AtmosphereExposure=GUI.HorizontalSlider(new UnityEngine.Rect(1260,495,275,18),app.Settings.AtmosphereExposure,.1f,5);
            Text(1030,525,210,"Star exposure");app.Settings.StarBrightness=GUI.HorizontalSlider(new UnityEngine.Rect(1260,534,275,18),app.Settings.StarBrightness,.25f,3);
            Text(1030,564,210,"Field of view");app.Settings.FieldOfView=GUI.HorizontalSlider(new UnityEngine.Rect(1260,573,275,18),app.Settings.FieldOfView,40,100);app.ViewCamera.fieldOfView=app.Settings.FieldOfView;
            Text(1030,603,210,"Terrain budget  "+app.Settings.TerrainPatchBudget);app.Settings.TerrainPatchBudget=(int)GUI.HorizontalSlider(new UnityEngine.Rect(1260,612,275,18),app.Settings.TerrainPatchBudget,64,384);
            foreach(var terrain in app.Terrains){terrain.MaximumVisiblePatches=app.Settings.TerrainPatchBudget;terrain.MaximumResidentPatches=app.Settings.TerrainPatchBudget*2;}
            Text(1030,650,130,"Universe seed");seedText=GUI.TextField(new UnityEngine.Rect(1160,649,190,28),seedText);
            if(Button(1370,648,164,"REGENERATE")&&int.TryParse(seedText,out int seed))
            {app.Settings.UniverseSeed=seed;app.LoadSystem(app.CellX,app.CellY,app.CellZ);app.FramePlanet(0,app.Settings.InitialAltitude);}
            if(Button(1030,711,240,"SAVE FLIGHT   [F5]"))app.SaveFlight();
            if(Button(1290,711,264,"RESTORE FLIGHT   [F9]"))app.LoadFlight();
            Text(1030,758,510,"Full geometry / optical settings: Resources/UniverseSettings",small);
        }
        void DrawHelp()
        {
            Rect(390,190,820,560,new Color(.018f,.032f,.047f,.98f));Text(420,215,760,"FLIGHT CONTROLS",title);
            string[] lines={"RMB + mouse     Look freely", "W A S D     Forward / left / back / right", "Q / E     Down / up     ·     Z / C     Roll", "Mouse wheel     Adjust flight speed     ·     Shift     Boost ×10", "G     Continuous descent     ·     R     Continuous ascent", "F     Frame selected planet     ·     Tab     Next planet", "N     Explicit jump to next seeded star system", "Esc     Cancel navigation     ·     Space     Pause time", "F5 / F9     Save / restore flight     ·     F1     Toggle interface", "Free flight streams systems without changing observer coordinates.", "Navigation jumps are shortcuts; descent and free flight are continuous."};
            for(int i=0;i<lines.Length;i++)Text(420,265+i*36,760,lines[i],i>8?small:body);
        }
        public static string Distance(double value)
        {
            if(Math.Abs(value)>=PhysicalConstants.LightYear*.01)return (value/PhysicalConstants.LightYear).ToString("F3")+" ly";
            if(Math.Abs(value)>=PhysicalConstants.AstronomicalUnit*.01)return (value/PhysicalConstants.AstronomicalUnit).ToString("F3")+" AU";
            if(Math.Abs(value)>=1000)return (value/1000).ToString(Math.Abs(value)>1e7?"N0":"N2")+" km";
            return value.ToString("N2")+" m";
        }
    }
}
