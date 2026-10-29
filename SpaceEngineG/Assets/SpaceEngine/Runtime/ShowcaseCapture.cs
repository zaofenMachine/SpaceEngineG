using System;
using System.Collections;
using System.IO;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceEngine.Runtime
{
    /// <summary>
    /// Art-direction capture route (--showcase). Frames composed views with and without the HUD
    /// so visual changes can be reviewed side by side. It asserts nothing about physics; the
    /// acceptance routes remain the evidence for correctness.
    /// </summary>
    public sealed class ShowcaseCapture:MonoBehaviour
    {
        UniverseApp app;string output;bool renderToTexture;Coroutine shooting;
        /// <summary>Shot names in route order. Each name is a composed view set up by <see cref="Setup"/>.</summary>
        public static readonly string[] Shots={
            "01-home-hud","02-details-hud","03-search-hud","04-home-clean",
            "05-ocean-crescent","06-ocean-three-quarter","07-ocean-limb-400km","08-ocean-sunset-surface","09-ocean-day-surface",
            "10-gas-giant","11-gas-giant-close","12-ice-giant","13-rocky","14-lava",
            "15-sky-galactic-centre","16-sky-along-plane","17-sky-high-latitude","18-star-close","19-star-behind-planet",
            "20-star-redgiant","20-star-bluemainsequence","20-star-whitedwarf","20-star-blackhole",
            "21-galaxy-oblique","22-galaxy-edge","23-galaxy-near","24-galaxy-hud","25-galactic-nucleus","26-nucleus-disk-edge",
            "27-ground-closeup","28-rocky-ground","29-ice-ground"};
        /// <summary>Names of the most recent <see cref="Shoot"/> request once it has finished.</summary>
        public static string Completed {get;private set;}

        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"--showcase")<0)yield break;
            bool quit=Array.IndexOf(args,"--quit-after-verification")>=0;
            int at=Array.IndexOf(args,"--evidence-path");
            output=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"Showcase");
            Directory.CreateDirectory(output);
            app=GetComponent<UniverseApp>();app.VerificationOwnsFlight=true;app.TimeRate=0;
            yield return Settle(30);
            foreach(string shot in Shots)yield return Run(shot);
            app.VerificationOwnsFlight=false;app.ShowHud=true;app.ReturnHome();
            Debug.Log("SPACE_ENGINE_SHOWCASE_DONE "+output);
            if(quit){yield return new WaitForSecondsRealtime(1);Application.Quit(0);}
        }

        /// <summary>
        /// Editor iteration entry (e.g. through Unity MCP in play mode): frames the named shots and
        /// renders the camera straight into a 1600x900 target, so the Game view size does not matter.
        /// The IMGUI HUD is not part of these captures.
        /// </summary>
        public void Shoot(string folder,string names)
        {
            app=GetComponent<UniverseApp>();output=folder;Directory.CreateDirectory(folder);Completed=null;
            // A repeated request (e.g. a remote call replayed across a domain reload) replaces the
            // running one; two interleaved routines would capture each other's views.
            if(shooting!=null)StopCoroutine(shooting);
            shooting=StartCoroutine(ShootRoutine(names.Split(',')));
        }
        IEnumerator ShootRoutine(string[] names)
        {
            app.VerificationOwnsFlight=true;app.TimeRate=0;renderToTexture=true;
            foreach(string name in names)yield return Run(name.Trim());
            renderToTexture=false;shooting=null;Completed=string.Join(",",names);
        }

        IEnumerator Run(string shot)
        {
            app.ShowHud=shot.EndsWith("-hud",StringComparison.Ordinal);
            bool framed=false;
            yield return Setup(shot,()=>framed=true);
            if(!framed)yield break;
            if(shot=="02-details-hud")app.Tools.ToggleDetails();
            if(shot=="03-search-hud")app.Tools.ToggleSearch();
            if(app.ShowHud)yield return Settle(20);
            yield return Capture(shot);
            app.Tools.ClosePanels();app.ShowHud=false;
        }

        IEnumerator Setup(string shot,Action framed)
        {
            int ocean=Find(PlanetKind.Oceanic);
            switch(shot)
            {
                case "01-home-hud":case "02-details-hud":case "03-search-hud":case "04-home-clean":
                    app.ReturnHome();yield return Settle(shot=="01-home-hud"?150:30);break;
                case "05-ocean-crescent":if(ocean<0)yield break;yield return Orbit(ocean,1.25,-.62,.08);yield return Settle(160);break;
                case "06-ocean-three-quarter":if(ocean<0)yield break;yield return Orbit(ocean,1.9,.45,.25);yield return Settle(120);break;
                case "07-ocean-limb-400km":if(ocean<0)yield break;yield return Limb(ocean,450000,55);yield return Settle(200);break;
                case "08-ocean-sunset-surface":if(ocean<0)yield break;yield return Surface(ocean,82,1.5f);yield return SettleTerrain();break;
                case "09-ocean-day-surface":if(ocean<0)yield break;yield return Surface(ocean,35,6f,.05);yield return SettleTerrain();break;
                case "10-gas-giant":case "11-gas-giant-close":
                {
                    int gas=Find(PlanetKind.GasGiant);if(gas<0)yield break;
                    if(shot=="10-gas-giant")yield return Orbit(gas,7.5,.35,.32);else yield return Orbit(gas,2.6,-.2,.12);
                    yield return Settle(120);break;
                }
                case "12-ice-giant":{int ice=Find(PlanetKind.IceGiant);if(ice<0)yield break;yield return Orbit(ice,3.2,.4,.2);yield return Settle(120);break;}
                case "13-rocky":{int rocky=Find(PlanetKind.Rocky);if(rocky<0)yield break;yield return Orbit(rocky,1.7,.5,.15);yield return Settle(140);break;}
                case "14-lava":{int lava=Find(PlanetKind.Lava);if(lava<0)yield break;yield return Orbit(lava,1.8,-.3,.1);yield return Settle(140);break;}
                case "15-sky-galactic-centre":case "16-sky-along-plane":case "17-sky-high-latitude":
                {
                    // Sky: from the home system toward the galactic plane and the galactic centre.
                    app.ReturnHome();
                    Vector3 toCentre=(-GalaxyModel.ToGalacticLy(app.System.Position,app.Settings.Galaxy)).Normalized.ToVector3();
                    Vector3 look=shot=="15-sky-galactic-centre"?toCentre:shot=="16-sky-along-plane"?Quaternion.AngleAxis(90,Vector3.up)*toCentre:
                        Quaternion.AngleAxis(-58,Vector3.Cross(toCentre,Vector3.up))*toCentre;
                    app.SetObserver(app.System.Position.Add(new Double3(0,8*PhysicalConstants.AstronomicalUnit,0)),Quaternion.LookRotation(look,Vector3.up));
                    yield return Settle(90);break;
                }
                case "18-star-close":app.ReturnHome();app.FrameStar();yield return Settle(90);break;
                case "19-star-behind-planet":
                {
                    // Night side, star just behind the limb: the atmosphere rim lights up against the glare.
                    if(ocean<0)yield break;
                    app.ReturnHome();app.FramePlanet(ocean,app.System.Planets[ocean].Radius*1.5);
                    double radius=app.System.Planets[ocean].Radius;
                    Double3 away=app.PlanetPosition(ocean).RelativeTo(app.System.Position).Normalized;
                    Double3 side=Double3.Cross(away,new Double3(0,1,0)).Normalized;
                    var viewpoint=app.PlanetPosition(ocean).Add((away+side*.255).Normalized*radius*4);
                    Double3 look=app.PlanetPosition(ocean).RelativeTo(viewpoint).Normalized;
                    app.SetObserver(viewpoint,Quaternion.LookRotation((look-side*.06).Normalized.ToVector3(),Vector3.up));
                    yield return Settle(120);break;
                }
                case "20-star-redgiant":case "20-star-bluemainsequence":case "20-star-whitedwarf":case "20-star-blackhole":
                {
                    var kind=(StellarKind)Enum.Parse(typeof(StellarKind),shot.Substring(8),true);
                    if(!FindSystem(kind,out long x,out long z))yield break;
                    app.LoadSystem(x,0,z);app.FrameStar();
                    if(kind==StellarKind.BlackHole)
                    {
                        var offset=new Double3(.25,.18,1).Normalized*app.System.StarRadius*28;
                        app.SetObserver(app.System.Position.Add(offset),Quaternion.LookRotation((-offset).ToVector3(),Vector3.up));
                    }
                    yield return Settle(120);break;
                }
                case "21-galaxy-oblique":case "24-galaxy-hud":app.ReturnHome();app.FrameGalaxy();yield return Settle(120);break;
                case "22-galaxy-edge":app.ReturnHome();app.FrameGalaxy(true);yield return Settle(100);break;
                case "23-galaxy-near":
                {
                    var close=GalaxyModel.FromGalacticLy(new Double3(0,22000,46000),app.Settings.Galaxy);
                    app.SetObserver(close,Quaternion.LookRotation(new Vector3(0,-22000,-46000).normalized,Vector3.up));
                    yield return Settle(100);break;
                }
                case "25-galactic-nucleus":app.ReturnHome();app.FrameNucleus();yield return Settle(100);break;
                case "26-nucleus-disk-edge":
                {
                    // Low inclination: the far side of the disk is lensed over the shadow.
                    app.ReturnHome();app.FrameNucleus();
                    Double3 offset=new Double3(0,.09,1).Normalized*app.NucleusRadius*34;
                    app.SetObserver(GalaxyModel.Center(app.Settings.Galaxy).Add(offset),Quaternion.LookRotation((-offset).ToVector3(),Vector3.up));
                    yield return Settle(100);break;
                }
                case "27-ground-closeup":if(ocean<0)yield break;yield return Surface(ocean,40,-24f,.05);yield return SettleTerrain();break;
                case "28-rocky-ground":{int rocky=Find(PlanetKind.Rocky);if(rocky<0)yield break;yield return Surface(rocky,55,-8f);yield return SettleTerrain();break;}
                case "29-ice-ground":{int ice=Find(PlanetKind.Ice);if(ice<0)yield break;yield return Surface(ice,60,-6f);yield return SettleTerrain();break;}
                default:Debug.LogWarning("Unknown showcase shot "+shot);yield break;
            }
            framed();
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
            string path=Path.Combine(output,name+".png");
            if(!renderToTexture){ScreenCapture.CaptureScreenshot(path);for(int i=0;i<3;i++)yield return null;yield break;}
            var camera=app.ViewCamera;
            var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var previous=RenderTexture.active;
            camera.targetTexture=target;
            // Size-dependent layers (galaxy radiance, starfield pixel scale) follow the target.
            app.RenderUniverse();
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};
            if(RenderPipeline.SupportsRenderRequest(camera,request))RenderPipeline.SubmitRenderRequest(camera,request);else camera.Render();
            RenderTexture.active=target;
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false,false);
            image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
            camera.targetTexture=null;RenderTexture.active=previous;
            File.WriteAllBytes(path,image.EncodeToPNG());
            Debug.Log("SHOWCASE_CAPTURE "+name+" observer="+app.Observer+" forward="+camera.transform.forward.ToString("F3"));
            Destroy(image);target.Release();Destroy(target);
            app.RenderUniverse();
            yield return null;
        }
    }
}
