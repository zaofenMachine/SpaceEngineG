using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SpaceEngine.Core;
using SpaceEngine.Terrain;
using UnityEngine;
using UnityEngine.Profiling;

namespace SpaceEngine.Runtime
{
    /// <summary>Real UI views and a wall-time descent/hold/ascent/return route, separate from fast throughput tests.</summary>
    public sealed class ExplorerVerification:MonoBehaviour
    {
        [Serializable] public sealed class Frame
        {
            public double elapsed,clearance,frameMs,renderedHeightError;public string phase;
            public int lod,pending,transitions,switches,geometryHash;public bool settled;
        }
        [Serializable] public sealed class Report
        {
            public string utc,version,buildGuid,gpu,api,scope="Actual game UI and wall-time continuous flight. Video capture, when used, adds overhead. No GPU timestamp measurement.";
            public bool passed;public int errors,stationaryFrames,stationaryChanges;public double holdSeconds,returnedPositionError;
            public List<string> checks=new List<string>(),screenshots=new List<string>(),runtimeErrors=new List<string>();
            public List<Frame> frames=new List<Frame>();public long allocatedBytes;
        }
        UniverseApp app;Report report;string output;bool failed,quit,record;
        VerificationVideoRecorder video;
        double start,nextSample;readonly List<TerrainPatchState> states=new List<TerrainPatchState>();
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();bool requested=Array.IndexOf(args,"--verify-explorer")>=0;
            record=Array.IndexOf(args,"--record-descent")>=0;quit=Array.IndexOf(args,"--quit-after-verification")>=0;
#if UNITY_EDITOR
            if(UnityEditor.SessionState.GetBool("SpaceEngine.VerifyExplorer",false)){requested=true;UnityEditor.SessionState.SetBool("SpaceEngine.VerifyExplorer",false);}
            if(UnityEditor.SessionState.GetBool("SpaceEngine.RecordDescent",false)){record=true;UnityEditor.SessionState.SetBool("SpaceEngine.RecordDescent",false);}
#endif
            if(!requested&&!record)yield break;
            app=GetComponent<UniverseApp>();app.VerificationOwnsFlight=true;app.TimeRate=0;app.ShowHud=true;
            int index=Array.IndexOf(args,"--evidence-path");output=index>=0&&index+1<args.Length?args[index+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../Artifacts/V050/Explorer"));
            Directory.CreateDirectory(output);report=new Report{utc=DateTime.UtcNow.ToString("O"),version=Application.version,buildGuid=Application.buildGUID,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};
            Application.logMessageReceived+=Log;start=Time.realtimeSinceStartupAsDouble;
            int encoderIndex=Array.IndexOf(args,"--video-encoder"),videoIndex=Array.IndexOf(args,"--video-output");
            if(encoderIndex>=0||videoIndex>=0)
            {
                bool valid=encoderIndex>=0&&encoderIndex+1<args.Length&&videoIndex>=0&&videoIndex+1<args.Length;
                string videoError=null;
                if(valid){video=gameObject.AddComponent<VerificationVideoRecorder>();valid=video.Begin(args[encoderIndex+1],args[videoIndex+1],output,out videoError);}
                Check(valid,"Native game framebuffer recorder starts: "+(videoError??"complete encoder/output arguments"));
            }
            if(requested)yield return ToolsRoute();
            if(record)yield return DescentRoute();
            if(video!=null)
            {yield return video.Finish();Check(video.Result!=null&&video.Result.passed,"Native framebuffer pipe finalizes successfully");}
            report.allocatedBytes=Profiler.GetTotalAllocatedMemoryLong();report.passed=!failed&&report.errors==0;
            File.WriteAllText(Path.Combine(output,"explorer-verification.json"),JsonUtility.ToJson(report,true));
            Application.logMessageReceived-=Log;app.VerificationOwnsFlight=false;
            app.Notice=report.passed?"探索功能与地形连续性验收通过":"验收失败，请检查记录";
            Debug.Log("SPACE_ENGINE_EXPLORER_ACCEPTANCE "+(report.passed?"PASS ":"FAIL ")+output);
            if(quit){yield return new WaitForSecondsRealtime(1);Application.Quit(report.passed?0:2);}
        }
        IEnumerator ToolsRoute()
        {
            app.ReturnHome();app.Tools.ClosePanels();app.Tools.SelectCurrentBody(0);yield return WaitForTerrain();yield return Capture("01-chinese-home");
            Check(ChineseText.Font!=null&&ChineseText.Font.HasCharacter('搜')&&ChineseText.Font.HasCharacter('轨'),"Bundled CJK font resolves real search/orbit glyphs");
            var exact=app.Observer;app.Tools.ToggleSearch();app.Tools.Filter.Query="海洋";app.Tools.Filter.LandableOnly=true;app.Tools.RefreshResults();
            Check(app.Tools.Results.Count>0,"Chinese object search finds generated landable ocean worlds");
            foreach(var target in app.Tools.Results)Check(!target.IsStar&&target.Body.Kind==PlanetKind.Oceanic&&target.Body.IsLandable,"Search result satisfies its real generated type");
            yield return Capture("02-search-filter-cn");
            app.Tools.SelectCurrentBody(1);Check(app.Observer.Equals(exact),"Selecting another body does not teleport");app.Tools.ClosePanels();
            app.Tools.CenterOnTarget();Check(app.Observer.Equals(exact),"Centering changes direction while retaining astronomical position");
            app.Tools.ShowDetails=true;yield return Capture("03-target-information-cn");app.Tools.ShowDetails=false;
            app.Tools.FrameSystem();yield return Frames(12);Check(app.Tools.Orbits.VisibleSegmentCount>100,"Actual parent-relative Kepler paths render in the system overview");yield return Capture("04-orbital-system-cn");
            app.Tools.ToggleOrbits();yield return Frames(2);Check(app.Tools.Orbits.VisibleSegmentCount==0,"Orbit toggle hides actual geometry");app.Tools.ToggleOrbits();
            var bookmarkScope=app.Tools.UseTemporaryBookmarkStore(Path.Combine(output,"test-bookmarks.json"));
            int bookmark=app.Tools.Bookmarks.Count;exact=app.Observer;app.Tools.AddBookmark("验收：轨道总览");
            app.Tools.ToggleBookmarks();yield return Capture("05-bookmarks-cn");app.ReturnHome();
            Check(app.Tools.RestoreBookmark(bookmark)&&app.Observer.Equals(exact),"Bookmark restores the exact SI observer");app.Tools.RemoveBookmark(bookmark);bookmarkScope.Dispose();
            double speed=app.SpeedMultiplier;app.AdjustSpeed(FlightSpeedControl.WheelNotches(120));Check(app.SpeedMultiplier>speed*1.5,"120-unit wheel notch visibly changes cruise speed");
            app.AdjustSpeed(FlightSpeedControl.WheelNotches(1));Check(app.SpeedMultiplier>speed*2.4,"One-unit device wheel changes the same speed scale");app.SpeedMultiplier=speed;
            app.ReturnHome();app.Tools.ClosePanels();
        }
        IEnumerator DescentRoute()
        {
            app.ReturnHome();app.Tools.ClosePanels();app.Tools.ShowOrbits=false;app.Tools.ShowLabels=false;app.TimeRate=0;
            app.SetAltitude(600000);yield return Capture("05b-same-frame-home-reload");yield return WaitForTerrain();yield return Frames(30);yield return Capture("06-descent-start");
            app.Notice="连续实录：下降 → 静止 → 上升 → 原路下降";app.BeginDescent();
            yield return FlyUntil(()=>!app.RadialNavigationActive,48,"first-descent");yield return WaitForTerrain();
            Check(app.Navigation.StartsWith("LANDED")&&Math.Abs(app.Altitude-2)<.05,"Real-time descent reaches a landed state without placement shortcuts");
            var landed=app.Observer;yield return StableHold(4,"first-hold");yield return Capture("07-first-surface-hold");
            app.BeginAscent();yield return FlyUntil(()=>app.Altitude>=2000,25,"ascent");app.StopNavigation();yield return WaitForTerrain();yield return Capture("08-continuous-ascent");
            app.BeginDescent();yield return FlyUntil(()=>!app.RadialNavigationActive,30,"return-descent");yield return WaitForTerrain();
            report.returnedPositionError=app.Observer.RelativeTo(landed).Length;Check(report.returnedPositionError<.01,"Returning on the same radial path restores the same centimetre-scale position");
            yield return StableHold(4,"second-hold");yield return Capture("09-return-surface-hold");
            Check(report.stationaryChanges==0&&report.stationaryFrames>100,"Settled terrain remains unchanged throughout both real stationary holds");
        }
        IEnumerator FlyUntil(Func<bool> complete,double timeout,string phase)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+timeout;
            while(!complete()&&Time.realtimeSinceStartupAsDouble<deadline)
            {
                app.AdvanceRadialFlight(Math.Min(.1,Time.unscaledDeltaTime));app.ConstrainGround();yield return null;Sample(phase);
            }
            Check(complete(),"Continuous phase completes before timeout: "+phase);
        }
        IEnumerator WaitForTerrain()
        {
            double deadline=Time.realtimeSinceStartupAsDouble+18;
            do{yield return null;Sample("settling");}while(!app.Terrains[app.SelectedPlanet].IsSettled&&Time.realtimeSinceStartupAsDouble<deadline);
            Check(app.Terrains[app.SelectedPlanet].IsSettled,"Geometry builds and parent-child morphs both settle");
        }
        IEnumerator StableHold(double seconds,string phase)
        {
            int hash=GeometryHash();double begin=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-begin<seconds)
            {
                yield return null;report.stationaryFrames++;var terrain=app.Terrains[app.SelectedPlanet];
                if(!terrain.IsSettled||terrain.LodSwitchesThisFrame!=0||GeometryHash()!=hash)report.stationaryChanges++;
                Sample(phase);
            }
            report.holdSeconds+=Time.realtimeSinceStartupAsDouble-begin;
        }
        int GeometryHash()
        {
            app.Terrains[app.SelectedPlanet].CopyActivePatchStates(states);int h=17;
            unchecked{foreach(var state in states){h=h*31+state.Key.GetHashCode();h=h*31+state.Morph.GetHashCode();}}return h;
        }
        void Sample(string phase)
        {
            double now=Time.realtimeSinceStartupAsDouble;if(now<nextSample)return;nextSample=now+.1;
            var terrain=app.Terrains[app.SelectedPlanet];double error=0;
            if(terrain.TrySampleRenderedSurface(app.CameraFromPlanet.Normalized,out Double3 surface,out Vector3 normal,out float height))error=surface.Length-app.Planet.Radius-app.SurfaceHeight(app.CameraFromPlanet.Normalized);
            report.frames.Add(new Frame{elapsed=now-start,phase=phase,clearance=app.Altitude,frameMs=Time.unscaledDeltaTime*1000,lod=app.MaxLod,pending=terrain.PendingBuildCount,
                transitions=terrain.TransitioningPatchCount,switches=terrain.LodSwitchesThisFrame,settled=terrain.IsSettled,geometryHash=GeometryHash(),renderedHeightError=error});
        }
        IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
        IEnumerator Capture(string name)
        {yield return new WaitForEndOfFrame();if(name=="05b-same-frame-home-reload")Check(app.Terrains[0].ActivePatchCount>0,"The first EOF after replacing the world contains submitted procedural terrain");video?.MarkScreenshot(name+".png");ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));report.screenshots.Add(name+".png");yield return Frames(4);}
        void Check(bool passed,string message){report.checks.Add((passed?"PASS ":"FAIL ")+message);if(!passed){failed=true;Debug.LogWarning(message);}}
        void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){report.errors++;report.runtimeErrors.Add(text+"\n"+trace);}}
    }
}
