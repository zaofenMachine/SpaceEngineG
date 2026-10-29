using System;
using System.Collections;
using System.IO;
using SpaceEngine.Core;
using UnityEngine;

namespace SpaceEngine.Runtime
{
    /// <summary>
    /// Art-direction capture route (--showcase). Frames composed views with and without the HUD
    /// so visual changes can be reviewed side by side. It asserts nothing about physics; the
    /// acceptance routes remain the evidence for correctness.
    /// </summary>
    public sealed class ShowcaseCapture:MonoBehaviour
    {
        UniverseApp app;string output;
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"--showcase")<0&&Array.IndexOf(args,"--showcase-diagnose")<0)yield break;
            bool quit=Array.IndexOf(args,"--quit-after-verification")>=0;
            int at=Array.IndexOf(args,"--evidence-path");
            output=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"Showcase");
            Directory.CreateDirectory(output);
            app=GetComponent<UniverseApp>();app.VerificationOwnsFlight=true;app.TimeRate=0;
            yield return Settle(30);
            if(Array.IndexOf(args,"--showcase-diagnose")>=0){yield return Diagnose();if(quit)Application.Quit(0);yield break;}

            // Interface review: default arrival view with the full HUD.
            app.ReturnHome();app.ShowHud=true;yield return Settle(150);yield return Capture("01-home-hud");
            app.Tools.ToggleDetails();yield return Settle(20);yield return Capture("02-details-hud");app.Tools.ClosePanels();
            app.Tools.ToggleSearch();yield return Settle(20);yield return Capture("03-search-hud");app.Tools.ClosePanels();
            app.ShowHud=false;yield return Capture("04-home-clean");

            int ocean=Find(PlanetKind.Oceanic);
            if(ocean>=0)
            {
                yield return Orbit(ocean,1.25,-.62,.08);yield return Settle(160);yield return Capture("05-ocean-crescent");
                yield return Orbit(ocean,1.9,.45,.25);yield return Settle(120);yield return Capture("06-ocean-three-quarter");
                yield return Limb(ocean,450000,55);yield return Settle(200);yield return Capture("07-ocean-limb-400km");
                yield return Surface(ocean,82,1.5f);yield return SettleTerrain();yield return Capture("08-ocean-sunset-surface");
                yield return Surface(ocean,35,6f,.05);yield return SettleTerrain();yield return Capture("09-ocean-day-surface");
            }
            int gas=Find(PlanetKind.GasGiant);
            if(gas>=0){yield return Orbit(gas,7.5,.35,.32);yield return Settle(120);yield return Capture("10-gas-giant");
                yield return Orbit(gas,2.6,-.2,.12);yield return Settle(120);yield return Capture("11-gas-giant-close");}
            int ice=Find(PlanetKind.IceGiant);
            if(ice>=0){yield return Orbit(ice,3.2,.4,.2);yield return Settle(120);yield return Capture("12-ice-giant");}
            int rocky=Find(PlanetKind.Rocky);
            if(rocky>=0){yield return Orbit(rocky,1.7,.5,.15);yield return Settle(140);yield return Capture("13-rocky");}
            int lava=Find(PlanetKind.Lava);
            if(lava>=0){yield return Orbit(lava,1.8,-.3,.1);yield return Settle(140);yield return Capture("14-lava");}

            // Sky: from the home system toward the galactic plane and the galactic centre.
            app.ReturnHome();
            Double3 home=GalaxyModel.ToGalacticLy(app.System.Position,app.Settings.Galaxy);
            Vector3 toCentre=(-home).Normalized.ToVector3();
            app.SetObserver(app.System.Position.Add(new Double3(0,8*PhysicalConstants.AstronomicalUnit,0)),Quaternion.LookRotation(toCentre,Vector3.up));
            yield return Settle(90);yield return Capture("15-sky-galactic-centre");
            app.SetObserver(app.System.Position.Add(new Double3(0,8*PhysicalConstants.AstronomicalUnit,0)),Quaternion.LookRotation(Quaternion.AngleAxis(90,Vector3.up)*toCentre,Vector3.up));
            yield return Settle(60);yield return Capture("16-sky-along-plane");
            app.SetObserver(app.System.Position.Add(new Double3(0,8*PhysicalConstants.AstronomicalUnit,0)),Quaternion.LookRotation(Quaternion.AngleAxis(-58,Vector3.Cross(toCentre,Vector3.up))*toCentre,Vector3.up));
            yield return Settle(60);yield return Capture("17-sky-high-latitude");

            // Home star from close range and from the home planet's distance.
            app.FrameStar();yield return Settle(90);yield return Capture("18-star-close");
            if(ocean>=0)
            {
                // Night side, star just behind the limb: the atmosphere rim lights up against the glare.
                app.FramePlanet(ocean,app.System.Planets[ocean].Radius*1.5);
                double radius=app.System.Planets[ocean].Radius;
                Double3 away=app.PlanetPosition(ocean).RelativeTo(app.System.Position).Normalized;
                Double3 side=Double3.Cross(away,new Double3(0,1,0)).Normalized;
                var viewpoint=app.PlanetPosition(ocean).Add((away+side*.255).Normalized*radius*4);
                Double3 look=app.PlanetPosition(ocean).RelativeTo(viewpoint).Normalized;
                app.SetObserver(viewpoint,Quaternion.LookRotation((look-side*.06).Normalized.ToVector3(),Vector3.up));
                yield return Settle(120);yield return Capture("19-star-behind-planet");
            }

            // Other stellar classes and the black hole.
            foreach(var kind in new[]{StellarKind.RedGiant,StellarKind.BlueMainSequence,StellarKind.WhiteDwarf,StellarKind.BlackHole})
            {
                if(!FindSystem(kind,out long x,out long z))continue;
                app.LoadSystem(x,0,z);app.FrameStar();
                if(kind==StellarKind.BlackHole)
                {
                    var offset=new Double3(.25,.18,1).Normalized*app.System.StarRadius*28;
                    app.SetObserver(app.System.Position.Add(offset),Quaternion.LookRotation((-offset).ToVector3(),Vector3.up));
                }
                yield return Settle(120);yield return Capture("20-star-"+kind.ToString().ToLowerInvariant());
            }

            // Galaxy from outside and the central black hole.
            app.ReturnHome();app.FrameGalaxy();yield return Settle(120);yield return Capture("21-galaxy-oblique");
            app.FrameGalaxy(true);yield return Settle(100);yield return Capture("22-galaxy-edge");
            var close=GalaxyModel.FromGalacticLy(new Double3(0,22000,46000),app.Settings.Galaxy);
            app.SetObserver(close,Quaternion.LookRotation(new Vector3(0,-22000,-46000).normalized,Vector3.up));
            yield return Settle(100);yield return Capture("23-galaxy-near");
            app.ShowHud=true;app.FrameGalaxy();yield return Settle(60);yield return Capture("24-galaxy-hud");
            app.ShowHud=false;app.FrameNucleus();yield return Settle(100);yield return Capture("25-galactic-nucleus");

            app.VerificationOwnsFlight=false;app.ShowHud=true;app.ReturnHome();
            Debug.Log("SPACE_ENGINE_SHOWCASE_DONE "+output);
            if(quit){yield return new WaitForSecondsRealtime(1);Application.Quit(0);}
        }

        /// <summary>Component isolation for a single composed view (night-side brightness, layer order).</summary>
        IEnumerator Diagnose()
        {
            app.ShowHud=false;int ocean=Find(PlanetKind.Oceanic);if(ocean<0)yield break;
            yield return Orbit(ocean,1.25,-.62,.08);yield return Settle(160);yield return Capture("d0-all");
            float coverage=app.Settings.CloudCoverage;foreach(var c in app.Clouds)c.Coverage=0;
            yield return Orbit(ocean,1.25,-.62,.08);yield return Settle(30);yield return Capture("d1-no-clouds");
            float exposure=app.Settings.AtmosphereExposure;app.Settings.AtmosphereExposure=0;
            yield return Settle(10);yield return Capture("d2-no-clouds-no-scattering");
            foreach(var c in app.Clouds)c.Coverage=coverage;
            yield return Settle(10);yield return Capture("d3-no-scattering");
            app.Settings.AtmosphereExposure=exposure;
        }

        int Find(PlanetKind kind)=>Array.FindIndex(app.System.Planets,b=>b.Kind==kind);

        /// <summary>Place the observer at a given distance (in radii) with an explicit phase: +1 sunward, -1 night side.</summary>
        IEnumerator Orbit(int body,double radii,double sunward,double elevation)
        {
            app.ReturnHome();app.FramePlanet(body,app.System.Planets[body].Radius*.5);
            var planet=app.PlanetPosition(body);
            Double3 toSun=app.System.Position.RelativeTo(planet).Normalized;
            Double3 side=Double3.Cross(toSun,new Double3(0,1,0)).Normalized;
            double lateral=Math.Sqrt(Math.Max(0,1-sunward*sunward));
            Double3 direction=(toSun*sunward+side*lateral+new Double3(0,elevation,0)).Normalized;
            Double3 offset=direction*app.System.Planets[body].Radius*(1+radii);
            app.SetObserver(planet.Add(offset),Quaternion.LookRotation((-offset).Normalized.ToVector3(),Vector3.up));
            yield return null;
        }

        /// <summary>Low orbit looking along the horizon, the sun at a given elevation angle behind the limb.</summary>
        IEnumerator Limb(int body,double altitude,double sunAngleDegrees)
        {
            app.ReturnHome();app.FramePlanet(body,altitude);
            var planet=app.PlanetPosition(body);double radius=app.System.Planets[body].Radius;
            Double3 toSun=app.System.Position.RelativeTo(planet).Normalized;
            Double3 side=Double3.Cross(toSun,new Double3(0,1,0)).Normalized;
            double a=sunAngleDegrees*Math.PI/180;
            Double3 up=(toSun*Math.Cos(a)+side*Math.Sin(a)).Normalized;
            app.SetObserver(planet.Add(up*(radius+altitude)),Quaternion.identity);
            Vector3 u=up.ToVector3();Vector3 forward=Vector3.ProjectOnPlane(toSun.ToVector3(),u).normalized;
            float dip=(float)(Math.Acos(radius/(radius+altitude))*180/Math.PI);
            Quaternion look=Quaternion.LookRotation(forward,u);
            app.SetObserver(app.Observer,Quaternion.AngleAxis(dip*.62f,look*Vector3.right)*look);
            yield return null;
        }

        /// <summary>Ground view with the sun at a given zenith angle, looking toward it with a slight upward tilt.</summary>
        IEnumerator Surface(int body,double sunZenithDegrees,float tiltDegrees,double maximumHeightFraction=1)
        {
            app.ReturnHome();app.FramePlanet(body,2000);
            var planet=app.PlanetPosition(body);double radius=app.System.Planets[body].Radius;
            Double3 toSun=app.System.Position.RelativeTo(planet).Normalized;
            Double3 side=Double3.Cross(toSun,new Double3(0,1,0)).Normalized;
            Double3 up=Double3.Zero;
            for(int i=0;i<160;i++)
            {
                double a=(sunZenithDegrees+(i%2==0?1:-1)*(i/2)*.6)*Math.PI/180;
                Double3 rotation=Double3.Cross(toSun,side).Normalized;
                Double3 candidate=(toSun*Math.Cos(a)+(side*Math.Cos(i*.7)+rotation*Math.Sin(i*.7))*Math.Sin(a)).Normalized;
                up=candidate;
                double height=TerrainNoise.Height(candidate,app.System.Planets[body]);
                if(height>0&&height<app.System.Planets[body].TerrainHeight*maximumHeightFraction)break;
            }
            app.SetObserver(planet.Add(up*(radius+50000)),Quaternion.identity);
            app.SetAltitude(2.5,true);
            Vector3 u=app.CameraFromPlanet.Normalized.ToVector3();
            Vector3 forward=Vector3.ProjectOnPlane(app.System.Position.RelativeTo(app.Observer).Normalized.ToVector3(),u).normalized;
            Quaternion look=Quaternion.LookRotation(forward,u);
            app.SetObserver(app.Observer,Quaternion.AngleAxis(-tiltDegrees,look*Vector3.right)*look);
            yield return null;
        }

        bool FindSystem(StellarKind kind,out long x,out long z)
        {
            for(int r=0;r<=128;r++)
            for(int i=-r;i<=r;i++)
            for(int j=-r;j<=r;j++)
            {
                if(Math.Max(Math.Abs(i),Math.Abs(j))!=r)continue;
                if(UniverseGenerator.TryGenerateSystem(app.Settings.UniverseSeed,i,0,j,app.Settings.Galaxy,out var candidate)&&candidate.Kind==kind)
                {x=i;z=j;return true;}
            }
            x=z=0;return false;
        }

        IEnumerator Settle(int count){for(int i=0;i<count;i++)yield return null;}
        /// <summary>Wait until the selected terrain has no pending patch builds (bounded), then report its state.</summary>
        IEnumerator SettleTerrain()
        {
            var terrain=app.Terrains[app.SelectedPlanet];int frames=0;
            for(;frames<1800&&(frames<120||terrain.PendingBuildCount>0||terrain.TransitioningPatchCount>0);frames++)yield return null;
            Debug.Log("SHOWCASE_TERRAIN frames="+frames+" lod="+terrain.MaxActiveLevel+" patches="+terrain.ActivePatchCount+" pending="+terrain.PendingBuildCount+" altitude="+app.Altitude.ToString("F1"));
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
            for(int i=0;i<3;i++)yield return null;
        }
    }
}
