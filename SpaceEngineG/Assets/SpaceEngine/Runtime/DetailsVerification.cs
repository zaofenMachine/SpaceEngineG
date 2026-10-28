using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Profiling;

namespace SpaceEngine.Runtime
{
    /// <summary>Actual science pages and version-compatible worlds, inspected without teleporting on selection.</summary>
    public sealed class DetailsVerification:MonoBehaviour
    {
        [Serializable] public sealed class BodySample
        {
            public string name,kind,caveats;public int generation,bodyIndex,parentIndex;
            public double radius,mass,density,gravity,escapeSpeed,period,parentDistance,stellarDistance,stellarFlux,equilibriumTemperature;
            public bool solid,surfaceTemperatureDefined;public double surfaceTemperature,referencePressure,gasFractionSum;
        }
        [Serializable] public sealed class Report
        {
            public string utc,version,buildGuid,gpu,api;public bool passed;public int errors;
            public long allocatedBytes;public double pageBuildMeanMs,pageBuildMaxMs;
            public List<string> checks=new List<string>(),screenshots=new List<string>(),runtimeErrors=new List<string>();
            public List<BodySample> bodies=new List<BodySample>();
        }
        UniverseApp app;Report report;string output;bool failed,quit;
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--verify-details")<0)yield break;
            app=GetComponent<UniverseApp>();app.VerificationOwnsFlight=true;app.TimeRate=0;app.ShowHud=true;
            quit=Array.IndexOf(args,"--quit-after-verification")>=0;int index=Array.IndexOf(args,"--evidence-path");
            output=index>=0&&index+1<args.Length?args[index+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../Artifacts/V050/Details"));
            Directory.CreateDirectory(output);report=new Report{utc=DateTime.UtcNow.ToString("O"),version=Application.version,buildGuid=Application.buildGUID,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};
            Application.logMessageReceived+=Log;
            yield return Frames(8);app.ReturnHome();app.SetAltitude(600000);yield return new WaitForEndOfFrame();
            Check(app.Terrains[0].ActivePatchCount>0,"A coroutine navigation replacement has real procedural coverage in the same rendered frame");
            yield return Capture("00-same-frame-world-reload");yield return WaitForTerrain();
            var home=app.Observer;
            for(int tab=0;tab<4;tab++)
            {app.Tools.SelectCurrentBody(0);Show(tab);yield return Capture((tab+1).ToString("D2")+"-ocean-"+new[]{"overview","orbit","environment","system"}[tab]);}
            Show(2);app.Tools.DetailsPage.ScrollTo(10000);yield return Capture("05-ocean-environment-bottom");
            var targets=new Dictionary<PlanetKind,CelestialTarget>();CelestialTarget vacuum=null,moon=null;
            app.Tools.RefreshCatalogue();
            foreach(var entry in app.Stars.Entries)
                for(int body=0;body<entry.System.Planets.Length;body++)
                {
                    var planet=entry.System.Planets[body];var target=new CelestialTarget{X=entry.X,Y=entry.Y,Z=entry.Z,System=entry.System,BodyIndex=body};
                    if(!targets.ContainsKey(planet.Kind))targets.Add(planet.Kind,target);
                    if(planet.IsMoon&&moon==null)moon=target;if(planet.IsLandable&&planet.AtmosphereHeight<=0&&vacuum==null)vacuum=target;
                }
            int captured=6;foreach(PlanetKind kind in Enum.GetValues(typeof(PlanetKind)))
            {
                if(!targets.TryGetValue(kind,out var target)){Check(false,"Every planet class has a real catalogue detail page: "+kind);continue;}
                app.Tools.Select(target);Show(2);Sample(target);Check(app.Observer.Equals(home),"Inspecting catalogue "+kind+" leaves the observer unchanged");
                yield return Capture((captured++).ToString("D2")+"-environment-"+kind.ToString().ToLowerInvariant());
            }
            Check(vacuum!=null&&moon!=null,"The actual catalogue contains a vacuum solid and a satellite");
            if(moon!=null){app.Tools.Select(moon);Show(1);yield return Capture("13-satellite-parent-orbit");}
            if(vacuum!=null){app.Tools.Select(vacuum);Show(2);Sample(vacuum);yield return Capture("14-vacuum-environment");}
            app.Tools.Select(new CelestialTarget{System=app.System,X=app.CellX,Y=app.CellY,Z=app.CellZ});Show(0);yield return Capture("15-star-details");

            string current=app.CaptureFlightState();app.Settings.WorldGenerationVersion=2;app.LoadSystem(0,0,0);app.FramePlanet(0,app.Settings.InitialAltitude);
            string legacy=app.CaptureFlightState();var legacyPosition=app.Observer;double legacyOrbit=app.Planet.SemiMajorAxis;
            Check(app.RestoreFlightState(current)&&app.Settings.WorldGenerationVersion==3,"Generation 3 state restores under its own physical generation rules");
            Check(app.RestoreFlightState(legacy)&&app.Settings.WorldGenerationVersion==2&&app.Observer.Equals(legacyPosition)&&app.Planet.SemiMajorAxis==legacyOrbit,"Generation 2 state retains its original exact position and orbital parameters");
            app.Tools.SelectCurrentBody(0);Show(2);yield return Frames(8);yield return Capture("16-legacy-world-environment");
            Check(app.RestoreFlightState(current),"Return to the new world after legacy inspection");
            app.LoadSystem(-96,0,11);app.FrameStar();app.Tools.SelectCurrentBody(0);Show(2);yield return Frames(8);Sample(app.Tools.Target);
            Check(app.System.Kind==StellarKind.BlackHole&&app.Planet.Kind==PlanetKind.Ice&&!app.Planet.HasOcean,"Zero-luminosity host generates frozen reservoirs rather than a warm liquid ocean");
            yield return Capture("17-black-hole-cold-world");
            app.Tools.Select(new CelestialTarget{System=app.System,X=app.CellX,Y=app.CellY,Z=app.CellZ});Show(0);yield return Capture("18-black-hole-details");
            app.RestoreFlightState(current);app.Tools.ClosePanels();
            if(report.bodies.Count>0)report.pageBuildMeanMs/=report.bodies.Count;
            report.allocatedBytes=Profiler.GetTotalAllocatedMemoryLong();report.passed=!failed&&report.errors==0;
            File.WriteAllText(Path.Combine(output,"details-verification.json"),JsonUtility.ToJson(report,true));Application.logMessageReceived-=Log;
            app.VerificationOwnsFlight=false;Debug.Log("SPACE_ENGINE_DETAILS_ACCEPTANCE "+(report.passed?"PASS ":"FAIL ")+output);
            if(quit){yield return new WaitForSecondsRealtime(1);Application.Quit(report.passed?0:2);}
        }
        void Show(int tab){app.Tools.ShowDetails=true;app.Tools.DetailsPage.SelectTab(tab);app.Tools.DetailsPage.ScrollTo(0);}
        IEnumerator Capture(string name,int tab=-1)
        {
            if(tab>=0)app.Tools.DetailsPage.SelectTab(tab);
            yield return Frames(3);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));report.screenshots.Add(name+".png");yield return Frames(5);
        }
        void Sample(CelestialTarget target)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();var doc=CelestialDetailsPage.CreateDocument(target,app.SimulationTime);double ms=watch.Elapsed.TotalMilliseconds;
            report.pageBuildMeanMs+=ms;report.pageBuildMaxMs=Math.Max(report.pageBuildMaxMs,ms);
            var science=PlanetScience.Evaluate(target.System,target.BodyIndex,app.SimulationTime);double gases=0;foreach(var gas in science.Climate.GasMoleFractions)gases+=gas.MoleFraction;
            Check(doc.Pages.Length==4&&doc.Title.Contains(target.Name),"A complete four-tab document is generated for "+target.Name);
            Check(!science.Climate.HasAtmosphere||Math.Abs(gases-1)<1e-12,"Mole fractions normalize for "+target.Name);
            Check(science.Geometry.HasSolidSurface||!science.Climate.SurfaceTemperatureK.HasValue,"Gas reference layers do not invent a solid surface temperature");
            report.bodies.Add(new BodySample{name=target.Name,kind=target.Body.Kind.ToString(),caveats=science.Caveats.ToString(),generation=target.System.GenerationVersion,
                bodyIndex=target.BodyIndex,parentIndex=target.Body.ParentIndex,radius=target.Radius,mass=target.Body.Mass,density=science.Geometry.MeanDensityKgM3,
                gravity=science.Geometry.GravityAtReferenceRadiusMps2,escapeSpeed=science.Geometry.EscapeSpeedAtReferenceRadiusMps,period=science.Orbit.PeriodSeconds,
                parentDistance=science.Orbit.InstantaneousDistanceMetres,stellarDistance=science.Orbit.StellarDistanceMetres,stellarFlux=science.Climate.IncidentStellarFluxWm2,
                equilibriumTemperature=science.Climate.EquilibriumTemperatureK,solid=target.Body.IsLandable,surfaceTemperatureDefined=science.Climate.SurfaceTemperatureK.HasValue,
                surfaceTemperature=science.Climate.SurfaceTemperatureK??0,referencePressure=science.Climate.ReferencePressurePascals,gasFractionSum=gases});
        }
        IEnumerator WaitForTerrain(){double until=Time.realtimeSinceStartupAsDouble+20;do{yield return null;}while(!app.Terrains[app.SelectedPlanet].IsSettled&&Time.realtimeSinceStartupAsDouble<until);}
        IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
        void Check(bool pass,string text){report.checks.Add((pass?"PASS ":"FAIL ")+text);if(!pass)failed=true;}
        void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){report.errors++;report.runtimeErrors.Add(text+"\n"+trace);}}
    }
}
