using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Profiling;

namespace SpaceEngine.Runtime
{
    /// <summary>Actual rendered flight with independent assertions and screenshots. Runs only when requested.</summary>
    public sealed class FlightVerification : MonoBehaviour
    {
        [Serializable] public sealed class Sample
        {
            public string stage;public double clearance,frameMs,terrainBuildMs;public int lod,patches,triangles;
            public long allocatedBytes,materialMapBytes;public int materialResolution;public double materialBakeCpuMilliseconds;public string coordinate;
        }
        [Serializable] public sealed class Report
        {
            public string utc,unity,gpu,graphicsApi,operatingSystem;
            public string applicationVersion,buildGuid;public int seed,generationVersion;
            public bool passed;public string failure;
            public int width,height,routeSteps,errors;public double minClearance,maxFrameMs,meanFrameMs,p50FrameMs,p95FrameMs;
            public double routeStepSeconds=.15;
            public string routeScope="Fixed virtual navigation steps; wall-frame timing measures rendering/streaming throughput rather than elapsed travel time.";
            public string performanceScope="Actual Unity unscaled frame interval, includes Editor or player overhead; screenshot warmup excluded. No GPU timestamp query.";
            public List<Sample> samples=new List<Sample>();public List<string> assertions=new List<string>();public List<string> runtimeErrors=new List<string>();
        }
        UniverseApp app;Report report;string output;bool requested,quit;readonly List<double> frameTimes=new List<double>();
        IEnumerator Start()
        {
            app=GetComponent<UniverseApp>();
            var args=Environment.GetCommandLineArgs();requested=Array.IndexOf(args,"--verify-flight")>=0;quit=Array.IndexOf(args,"--quit-after-verification")>=0;
            int index=Array.IndexOf(args,"--evidence-path");
            output=index>=0&&index+1<args.Length?args[index+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../Artifacts/V050/Flight"));
#if UNITY_EDITOR
            if(UnityEditor.SessionState.GetBool("SpaceEngine.VerifyFlight",false))
            {requested=true;UnityEditor.SessionState.SetBool("SpaceEngine.VerifyFlight",false);}
#endif
            if(!requested)yield break;
            Directory.CreateDirectory(output);
            report=new Report{utc=DateTime.UtcNow.ToString("O"),unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,
                graphicsApi=SystemInfo.graphicsDeviceType.ToString(),operatingSystem=SystemInfo.operatingSystem,width=Screen.width,height=Screen.height,minClearance=double.MaxValue,
                applicationVersion=Application.version,buildGuid=Application.buildGUID,seed=app.Settings.UniverseSeed,generationVersion=UniverseGenerator.GenerationVersion};
            Application.logMessageReceived+=OnLog;
            app.VerificationOwnsFlight=true;app.TimeRate=0;
            app.FramePlanet(0,app.Settings.InitialAltitude);
            // The only setup placement is the initial interstellar start. The complete descent below retains state.
            app.SetAltitude(PhysicalConstants.LightYear*.03);
            app.Notice="AUTOMATED ACCEPTANCE / continuous flight from 0.03 ly to terrain";
            yield return Settle(45);
            yield return Capture("01-interstellar");
            app.BeginDescent();
            bool orbit=false,approach=false,low=false;
            for(int step=0;step<1600&&app.Altitude>app.Settings.MinimumClearance+.04;step++)
            {
                double previous=app.Altitude;
                app.AdvanceRadialFlight(.15);
                app.ConstrainGround();
                yield return null;
                if(app.Altitude>previous+.01){Fail("Descent clearance increased unexpectedly.");break;}
                report.routeSteps++;
                report.minClearance=Math.Min(report.minClearance,app.Altitude);
                frameTimes.Add(Time.unscaledDeltaTime*1000);
                if(!orbit&&app.Altitude<app.Planet.Radius*2)
                {orbit=true;yield return Settle(90);yield return Capture("02-orbit");}
                if(!approach&&app.Altitude<200000)
                {approach=true;yield return Settle(110);yield return Capture("03-atmosphere");}
                if(!low&&app.Altitude<1500)
                {low=true;yield return Settle(140);yield return Capture("04-low-altitude");}
            }
            // Finish the navigation state as well as crossing its distance tolerance.
            // Capturing immediately at +0.007 m could leave the HUD showing a stale descent speed.
            for(int i=0;i<8&&!app.Navigation.StartsWith("LANDED");i++)
            {
                app.AdvanceRadialFlight(.15);app.ConstrainGround();yield return null;
                report.routeSteps++;report.minClearance=Math.Min(report.minClearance,app.Altitude);
                frameTimes.Add(Time.unscaledDeltaTime*1000);
            }
            Check(app.Navigation.StartsWith("LANDED")&&app.Speed==0,"Descent completes its landed navigation state and stops reported motion");
            yield return SettleTerrain();
            yield return Capture("05-surface");
            Check(app.Altitude>=app.Settings.MinimumClearance-.001&&app.Altitude<app.Settings.MinimumClearance+.1,"Continuous route reaches terrain clearance without penetration");
            Check(orbit&&approach&&low,"All interstellar / orbit / atmosphere / low-altitude milestones were crossed");
            Check(app.MaxLod>=17,"Surface terrain refines to LOD >=17 on an Earth-size planet");
            Check(app.ActiveTriangles>1000,"Procedural terrain geometry is rendered");
            Check(app.Terrains[app.SelectedPlanet].IsSettled,"Surface geometry and LOD morphs converge before the final capture");
            Check(app.ActivePatches<=app.Settings.TerrainPatchBudget*app.System.Planets.Length+24,"Terrain active patches stay within aggregate streaming budgets");
            var basePosition=app.Observer;
            app.SetObserver(basePosition.Add(new Double3(.01,0,0)),app.ViewCamera.transform.rotation);
            double cmError=Math.Abs(app.Observer.RelativeTo(basePosition).x-.01);
            Check(cmError<.0001,"One-centimetre observer movement remains measurable at the current astronomical coordinate");
            // Cross a stellar cell boundary through streaming, retaining observer coordinate exactly.
            var neighbourEntry=app.NearestCatalogueSystem();
            var neighbour=neighbourEntry.System;
            app.SetObserver(neighbour.Position.Add(new Double3(0,0,PhysicalConstants.AstronomicalUnit*2)),app.ViewCamera.transform.rotation);
            var streamPosition=app.Observer;app.StreamNearestSystem();
            Check(app.CellX==neighbourEntry.X&&app.CellY==neighbourEntry.Y&&app.CellZ==neighbourEntry.Z&&app.Observer.Equals(streamPosition),"Nearest occupied-system streaming changes system and retains observer coordinates");
            app.FramePlanet(0,app.Settings.InitialAltitude);yield return Settle(120);yield return Capture("06-second-system");
            Check(app.System.Seed==neighbour.Seed,"Second system matches deterministic generator");
            Check(report.errors==0,"No error / exception log during rendered acceptance route");
            Finish();
            Application.logMessageReceived-=OnLog;
            app.VerificationOwnsFlight=false;
            app.Notice=report.passed?"ACCEPTANCE PASSED / evidence saved":"ACCEPTANCE FAILED / inspect runtime-verification.json";
            if(quit){yield return new WaitForSecondsRealtime(1);Application.Quit(report.passed?0:2);}
        }
        IEnumerator Settle(int frames){for(int i=0;i<frames;i++)yield return null;}
        IEnumerator SettleTerrain()
        {
            double deadline=Time.realtimeSinceStartupAsDouble+18;
            for(int i=0;i<30;i++)yield return null;
            while(!app.Terrains[app.SelectedPlanet].IsSettled&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            string path=Path.Combine(output,name+".png");
            ScreenCapture.CaptureScreenshot(path);
            report.samples.Add(new Sample{stage=name,clearance=app.Altitude,frameMs=app.FrameMilliseconds,terrainBuildMs=app.BuildMilliseconds,
                lod=app.MaxLod,patches=app.ActivePatches,triangles=app.ActiveTriangles,allocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),coordinate=app.Observer.ToString(),
                materialMapBytes=app.Terrains[app.SelectedPlanet].MaterialMapBytes,
                materialResolution=app.Terrains[app.SelectedPlanet].MaterialMapReady?app.Terrains[app.SelectedPlanet].MaterialMapResolution:0,
                materialBakeCpuMilliseconds=app.Terrains[app.SelectedPlanet].LastMaterialBakeCpuMilliseconds});
            for(int i=0;i<3;i++)yield return null;
        }
        void Check(bool passed,string description){report.assertions.Add((passed?"PASS ":"FAIL ")+description);if(!passed)Fail(description);}
        void Fail(string message){report.failure=(report.failure??"")+message+"\n";Debug.LogWarning("Flight acceptance: "+message);}
        void OnLog(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){report.errors++;report.runtimeErrors.Add(message+"\n"+stack);}}
        void Finish()
        {
            frameTimes.Sort();double sum=0;foreach(double t in frameTimes)sum+=t;
            if(frameTimes.Count>0){report.meanFrameMs=sum/frameTimes.Count;report.p50FrameMs=frameTimes[frameTimes.Count/2];report.p95FrameMs=frameTimes[(int)((frameTimes.Count-1)*.95)];report.maxFrameMs=frameTimes[frameTimes.Count-1];}
            report.passed=string.IsNullOrEmpty(report.failure)&&report.errors==0;
            File.WriteAllText(Path.Combine(output,"runtime-verification.json"),JsonUtility.ToJson(report,true));
            Debug.Log("SPACE_ENGINE_ACCEPTANCE "+(report.passed?"PASS":"FAIL")+" "+output);
        }
    }
}
