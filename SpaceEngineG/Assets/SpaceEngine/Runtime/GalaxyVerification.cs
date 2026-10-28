using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Profiling;

namespace SpaceEngine.Runtime
{
    /// <summary>Actual galaxy and celestial-type captures, independent of the original continuous landing route.</summary>
    public sealed class GalaxyVerification:MonoBehaviour
    {
        [Serializable] public sealed class Stage
        {
            public string name,bodyKind,stellarKind,position;public double frameMs,clearance,starRadius,starMass,starLuminosity;
            public int terrainTriangles,celestialTriangles,lod,resolvedStars,galaxyKernels;
            public long allocatedBytes,materialMapBytes,totalMaterialMapBytes;public int materialResolution,volumeWidth,volumeHeight;
            public double coldApproachMeanFrameMs,coldApproachMaxFrameMs;
        }
        struct SourceCell
        {
            public long x,z;
            public int planetIndex;
        }
        [Serializable] public sealed class Report
        {
            public string utc,version,buildGuid,unity,gpu,api;public bool passed;public int errors,seed,generationVersion;
            public List<string> checks=new List<string>();public List<string> runtimeErrors=new List<string>();public List<Stage> stages=new List<Stage>();
            public double movingViewMeanFrameMs,movingViewP95FrameMs,movingViewMaxFrameMs;public int movingViewFrames,movingViewRecomputations;
            public string scope="Real screen frames and runtime assertions. Galaxy kernels are 3D population aggregates; only nearby admitted cells are resolved systems. Overview framing is explicit navigation, not a continuous intergalactic journey.";
        }
        UniverseApp app;Report report;string output;bool failed,quit;double coldMean,coldMax;
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();bool requested=Array.IndexOf(args,"--verify-galaxy")>=0;
            quit=Array.IndexOf(args,"--quit-after-verification")>=0;
#if UNITY_EDITOR
            if(UnityEditor.SessionState.GetBool("SpaceEngine.VerifyGalaxy",false)){requested=true;UnityEditor.SessionState.SetBool("SpaceEngine.VerifyGalaxy",false);}
#endif
            if(!requested)yield break;
            app=GetComponent<UniverseApp>();app.VerificationOwnsFlight=true;app.TimeRate=0;
            int at=Array.IndexOf(args,"--evidence-path");output=at>=0&&at+1<args.Length?args[at+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../Artifacts/V050/Galaxy"));
            Directory.CreateDirectory(output);
            report=new Report{utc=DateTime.UtcNow.ToString("O"),version=Application.version,buildGuid=Application.buildGUID,unity=Application.unityVersion,
                gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),seed=app.Settings.UniverseSeed,generationVersion=UniverseGenerator.GenerationVersion};
            Application.logMessageReceived+=Log;
            app.FrameGalaxy();yield return Settle(100);yield return Capture("01-galaxy-oblique");
            Check(app.Galaxy.ParticleCount==app.Settings.GalaxyParticleCount,"GPU galaxy field uses configured 3D population budget");
            Check(app.Stars.Count==0,"No fabricated resolved systems outside the stellar halo");
            var exact=app.Observer;app.StreamNearestSystem();Check(app.Observer.Equals(exact),"Empty-region streaming retains observer coordinates");
            app.FrameGalaxy(true);yield return Settle(100);yield return Capture("02-galaxy-edge");
            app.ReturnHome();
            app.SetObserver(UniversalPosition.Origin.Add(new Double3(0,50*PhysicalConstants.AstronomicalUnit,0)),Quaternion.LookRotation(Vector3.left,Vector3.up));
            app.Notice="SOLAR NEIGHBOURHOOD / looking into the galactic disk";
            yield return Settle(100);yield return Capture("03-disk-from-within");
            app.ReturnHome();
            for(int i=0;i<app.System.Planets.Length;i++)
            {
                var body=app.System.Planets[i];
                app.FramePlanet(i,Math.Max(5000000,body.Radius*2));
                app.Notice=(body.IsMoon?"PARENT-RELATIVE MOON / ":"PLANET / ")+body.Kind;
                coldMean=0;coldMax=0;
                for(int warmup=0;warmup<140;warmup++)
                {
                    yield return null;double interval=Time.unscaledDeltaTime*1000;
                    coldMean+=interval;coldMax=Math.Max(coldMax,interval);
                }
                coldMean/=140;
                yield return Capture((4+i).ToString("D2")+"-"+body.Kind.ToString().ToLowerInvariant()+(body.IsMoon?"-moon":""));
                if(!body.IsLandable)
                {
                    Check(!app.Terrains[i].gameObject.activeInHierarchy,"Gas body has no rendered solid terrain: "+body.Kind);
                    Check(app.Celestials[i].TriangleCount>1000,"Procedural clouds and rings render: "+body.Kind);
                }
                if(body.IsMoon)
                {
                    double relative=app.PlanetPosition(i).RelativeTo(app.PlanetPosition(body.ParentIndex)).Length;
                    Check(relative>body.SemiMajorAxis*(1-body.Eccentricity)-1&&relative<body.SemiMajorAxis*(1+body.Eccentricity)+1,"Moon world position uses parent-relative orbit");
                }
            }
            int gas=Array.FindIndex(app.System.Planets,b=>b.Kind==PlanetKind.GasGiant);
            if(gas>=0)
            {
                app.FramePlanet(gas,app.System.Planets[gas].Radius*3.5);app.BeginDescent();
                for(int i=0;i<240&&app.Altitude>app.Settings.GasCloudSafetyClearance+.04;i++){app.AdvanceRadialFlight(.15);app.ConstrainGround();yield return null;}
                for(int i=0;i<8&&!app.Navigation.StartsWith("HOLDING");i++){app.AdvanceRadialFlight(.15);yield return null;}
                yield return Settle(80);yield return Capture("13-cloud-top-approach");
                Check(app.Altitude>=app.Settings.GasCloudSafetyClearance-.01&&app.Altitude<app.Settings.GasCloudSafetyClearance+1,"Gas approach stops at configured cloud safety clearance");
                Check(!app.Navigation.Contains("LANDED"),"Gas cloud approach never claims a solid-surface landing");
            }
            app.FrameNucleus();yield return Settle(100);yield return Capture("14-central-horizon");
            Check(app.NucleusRadius>1e10&&app.NucleusRadius<2e10,"Central reference horizon uses configured mass and Schwarzschild radius");

            // Discover real admitted systems from this exact saved galaxy, rather than
            // substituting star parameters into the home system for the screenshots.
            var kinds=(StellarKind[])Enum.GetValues(typeof(StellarKind));
            var sources=new Dictionary<StellarKind,SourceCell>();
            SourceCell desertSource=default;bool foundDesert=false;int scanned=0;
            const int searchRadius=128;
            for(int x=-searchRadius;x<=searchRadius&&(sources.Count<kinds.Length||!foundDesert);x++)
            for(int z=-searchRadius;z<=searchRadius&&(sources.Count<kinds.Length||!foundDesert);z++)
            {
                if(UniverseGenerator.TryGenerateSystem(app.Settings.UniverseSeed,x,0,z,app.Settings.Galaxy,out var candidate))
                {
                    if(!sources.ContainsKey(candidate.Kind))sources.Add(candidate.Kind,new SourceCell{x=x,z=z});
                    if(!foundDesert)
                    {
                        int desert=Array.FindIndex(candidate.Planets,b=>b.Kind==PlanetKind.Desert);
                        if(desert>=0){desertSource=new SourceCell{x=x,z=z,planetIndex=desert};foundDesert=true;}
                    }
                }
                if(++scanned%256==0)yield return null;
            }
            Check(sources.Count==kinds.Length,"Deterministic admitted-system search covers all "+kinds.Length+" stellar kinds ("+scanned+" cells checked)");
            for(int i=0;i<kinds.Length;i++)
            {
                StellarKind kind=kinds[i];
                if(!sources.TryGetValue(kind,out SourceCell source)){Check(false,"Missing admitted system for "+kind);continue;}
                app.LoadSystem(source.x,0,source.z);app.FrameStar();
                Check(app.System.IsOccupied&&app.System.Kind==kind,"Rendered stellar source is admitted with its generated kind: "+kind);
                Check(app.System.StarRadius>0&&!double.IsNaN(app.System.StarRadius)&&!double.IsInfinity(app.System.StarRadius),"Generated stellar radius is positive and finite: "+kind);
                if(kind==StellarKind.BlackHole)
                {
                    Check(app.System.StarLuminosity==0,"Non-accreting stellar black hole emits zero photospheric luminosity");
                    double horizon=2*PhysicalConstants.GravitationalConstant*app.System.StarMass/
                        (PhysicalConstants.SpeedOfLight*PhysicalConstants.SpeedOfLight);
                    Check(Math.Abs(app.System.StarRadius/horizon-1)<1e-12,"Stellar black-hole radius is the generated mass's Schwarzschild radius");
                }
                else Check(app.System.StarLuminosity>0&&!double.IsNaN(app.System.StarLuminosity)&&!double.IsInfinity(app.System.StarLuminosity),"Emitting stellar source has positive finite luminosity: "+kind);
                yield return Settle(100);
                yield return Capture((15+i).ToString("D2")+"-star-"+kind.ToString().ToLowerInvariant());
            }
            Check(foundDesert,"Admitted-system search includes a generated desert planet");
            if(foundDesert)
            {
                app.LoadSystem(desertSource.x,0,desertSource.z);
                var desert=app.System.Planets[desertSource.planetIndex];
                app.FramePlanet(desertSource.planetIndex,Math.Max(5000000,desert.Radius*2));
                app.Notice="PLANET / "+desert.Kind+" / generated admitted-system desert";
                yield return Settle(140);yield return Capture("22-desert");
                Check(app.Planet.Kind==PlanetKind.Desert&&app.Planet.IsLandable,"Desert frame uses its procedural solid terrain");
            }
            app.FrameGalaxy();yield return Settle(30);
            app.Notice="GALACTIC VOLUME / rotating-view performance capture";
            var motionTimes=new List<double>();var motionObserver=app.Observer;var motionRotation=app.ViewCamera.transform.rotation;
            int beforeRevision=app.GalacticLight.RenderRevision;
            for(int i=0;i<120;i++)
            {
                app.SetObserver(motionObserver,Quaternion.AngleAxis((float)Math.Sin(i*.13)*5,Vector3.up)*motionRotation);
                yield return null;motionTimes.Add(Time.unscaledDeltaTime*1000);
            }
            report.movingViewFrames=motionTimes.Count;report.movingViewRecomputations=app.GalacticLight.RenderRevision-beforeRevision;
            motionTimes.Sort();double motionSum=0;foreach(double ms in motionTimes)motionSum+=ms;
            report.movingViewMeanFrameMs=motionSum/motionTimes.Count;report.movingViewP95FrameMs=motionTimes[(int)((motionTimes.Count-1)*.95)];
            report.movingViewMaxFrameMs=motionTimes[motionTimes.Count-1];
            Check(report.movingViewRecomputations>=118,"Moving-view benchmark recomputes the spatial volume instead of timing only cached frames");
            yield return Capture("23-galaxy-moving-view");
            Check(report.errors==0,"No error/exception logs during galaxy and body-type render expedition");
            report.passed=!failed&&report.errors==0;
            File.WriteAllText(Path.Combine(output,"galaxy-verification.json"),JsonUtility.ToJson(report,true));
            Application.logMessageReceived-=Log;app.VerificationOwnsFlight=false;app.ReturnHome();
            app.Notice=report.passed?"GALAXY AND CELESTIAL ACCEPTANCE PASSED":"ACCEPTANCE FAILED / inspect evidence";
            Debug.Log("SPACE_ENGINE_GALAXY_ACCEPTANCE "+(report.passed?"PASS":"FAIL")+" "+output);
            if(quit){yield return new WaitForSecondsRealtime(1);Application.Quit(report.passed?0:2);}
        }
        IEnumerator Settle(int count){for(int i=0;i<count;i++)yield return null;}
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
            long totalMaps=0;foreach(var terrain in app.Terrains)totalMaps+=terrain.MaterialMapBytes;
            report.stages.Add(new Stage{name=name,bodyKind=app.Planet.Kind.ToString(),stellarKind=app.System.Kind.ToString(),position=app.Observer.ToString(),frameMs=app.FrameMilliseconds,
                starRadius=app.System.StarRadius,starMass=app.System.StarMass,starLuminosity=app.System.StarLuminosity,
                clearance=app.Altitude,terrainTriangles=app.ActiveTriangles,celestialTriangles=app.BodyTriangles,lod=app.MaxLod,
                resolvedStars=app.Stars.Count,galaxyKernels=app.Galaxy.ParticleCount,allocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),
                materialMapBytes=app.Terrains[app.SelectedPlanet].MaterialMapBytes,materialResolution=app.Terrains[app.SelectedPlanet].MaterialMapReady?app.Terrains[app.SelectedPlanet].MaterialMapResolution:0,
                totalMaterialMapBytes=totalMaps,coldApproachMeanFrameMs=coldMean,coldApproachMaxFrameMs=coldMax,
                volumeWidth=app.GalacticLight.RenderedWidth,volumeHeight=app.GalacticLight.RenderedHeight});
            coldMean=0;coldMax=0;
            for(int i=0;i<4;i++)yield return null;
        }
        void Check(bool pass,string message){report.checks.Add((pass?"PASS ":"FAIL ")+message);if(!pass){failed=true;Debug.LogWarning(message);}}
        void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){report.errors++;report.runtimeErrors.Add(message+"\n"+trace);}}
    }
}
