using System;
using System.IO;
using SpaceEngine.Core;
using SpaceEngine.Rendering;
using SpaceEngine.Terrain;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace SpaceEngine.Runtime
{
    /// <summary>Simulation coordinates stay in SI. The Unity camera is always at the render origin.</summary>
    public sealed class UniverseApp : MonoBehaviour
    {
        public UniverseSettings Settings;
        public StarSystemDefinition System { get; private set; }
        public UniversalPosition Observer { get; private set; }
        public double SimulationTime { get; set; }
        public double TimeRate { get; set; } = 1;
        public double SpeedMultiplier { get; set; } = 1;
        public double Speed { get; private set; }
        public double Altitude { get; private set; }
        public double RenderScale { get; private set; }
        public int SelectedPlanet { get; private set; }
        public long CellX { get; private set; }
        public long CellY { get; private set; }
        public long CellZ { get; private set; }
        public Camera ViewCamera { get; private set; }
        public PlanetTerrain[] Terrains { get; private set; }
        public StarCatalogueRenderer Stars { get; private set; }
        public GalaxyField Galaxy { get; private set; }
        public GalaxyVolume GalacticLight {get;private set;}
        public PlanetCloudRenderer[] Clouds {get;private set;}
        public CelestialBodyRenderer[] Celestials {get;private set;}
        public bool GalaxyOverview {get;private set;}
        public bool NucleusView {get;private set;}
        public bool StellarView {get;private set;}
        public string SavePath => Path.Combine(Application.persistentDataPath,"flight-v2.json");
        public double LocalStellarDensity => GalaxyModel.SampleDensity(GalaxyModel.ToGalacticLy(Observer,Settings.Galaxy),Settings.Galaxy);
        public int BodyTriangles {get{int total=0;if(Celestials!=null)foreach(var c in Celestials)total+=c.TriangleCount;if(Clouds!=null)foreach(var c in Clouds)total+=c.TriangleCount;return total;}}
        public string Navigation { get; private set; } = "FREE FLIGHT";
        public double FrameMilliseconds { get; private set; }
        public bool VerificationOwnsFlight;
        public bool ShowHud=true;
        public string Notice="RMB look  /  WASD fly  /  scroll speed";
        public PlanetDefinition Planet => System.Planets[SelectedPlanet];
        public Double3 CameraFromPlanet => Observer.RelativeTo(PlanetPosition(SelectedPlanet));
        public int ActiveTriangles { get {int total=0; if(Terrains!=null)foreach(var t in Terrains)if(t.gameObject.activeInHierarchy)total+=t.TriangleCount;return total;} }
        public int ActivePatches { get {int total=0; if(Terrains!=null)foreach(var t in Terrains)if(t.gameObject.activeInHierarchy)total+=t.ActivePatchCount;return total;} }
        public int MaxLod => Terrains==null||!Terrains[SelectedPlanet].gameObject.activeInHierarchy?0:Terrains[SelectedPlanet].MaxActiveLevel;
        public double BuildMilliseconds { get {double total=0;if(Terrains!=null)foreach(var t in Terrains)if(t.gameObject.activeInHierarchy)total+=t.LastBuildMilliseconds;return total;} }

        AtmosphereRenderer atmosphere;
        Material terrainMaterial,starMaterial,starBillboard,atmosphereMaterial,galaxyMaterial,gasMaterial,ringMaterial,nucleusMaterial;
        Material galaxyVolumeMaterial,galaxyCompositeMaterial,cloudMaterial;
        GameObject nucleusObject;
        GameObject starObject;
        Mesh starMesh;
        Quaternion orientation;
        double targetAltitude;
        bool radialNavigation;
        double nextStreamTime;
        Vector3 landingDirection;

        void Awake()
        {
            Application.runInBackground=true;
            QualitySettings.vSyncCount=0;
            Application.targetFrameRate=120;
            Settings=Settings!=null?Settings:Resources.Load<UniverseSettings>("UniverseSettings");
            if(Settings==null) Settings=ScriptableObject.CreateInstance<UniverseSettings>();
            else Settings=Instantiate(Settings);
            if(Settings.Galaxy==null)Settings.Galaxy=GalaxyModel.Default(Settings.UniverseSeed);
            Settings.Galaxy.Seed=Settings.UniverseSeed;
            terrainMaterial=Resolve(Settings.TerrainMaterial,"SpaceEngine/Terrain");
            starMaterial=Resolve(Settings.StarMaterial,"SpaceEngine/Star");
            starBillboard=Resolve(Settings.StarBillboardMaterial,"SpaceEngine/StarBillboard");
            starBillboard.SetFloat("_CoreSize",Settings.StarCoreRadius);
            atmosphereMaterial=Resolve(Settings.AtmosphereMaterial,"SpaceEngine/Atmosphere");
            galaxyMaterial=Resolve(Settings.GalaxyMaterial,"SpaceEngine/GalaxyField");
            gasMaterial=Resolve(Settings.GasGiantMaterial,"SpaceEngine/GasGiant");
            ringMaterial=Resolve(Settings.RingMaterial,"SpaceEngine/PlanetRing");
            galaxyVolumeMaterial=Resolve(Settings.GalaxyVolumeMaterial,"SpaceEngine/GalaxyVolume");
            galaxyCompositeMaterial=Resolve(Settings.GalaxyVolumeCompositeMaterial,"SpaceEngine/GalaxyVolumeComposite");
            cloudMaterial=Resolve(Settings.PlanetCloudMaterial,"SpaceEngine/PlanetCloud");
            terrainMaterial.SetFloat("_AmbientFloor",Settings.AmbientLight);
            RenderSettings.skybox=null; RenderSettings.fog=false; RenderSettings.ambientLight=Color.black;
            var cameraObject=new GameObject("Observer camera - always local origin");
            cameraObject.tag="MainCamera";
            ViewCamera=cameraObject.AddComponent<Camera>(); ViewCamera.clearFlags=CameraClearFlags.SolidColor;
            ViewCamera.backgroundColor=new Color(.0002f,.0003f,.0008f); ViewCamera.nearClipPlane=.1f; ViewCamera.farClipPlane=16000000;
            ViewCamera.fieldOfView=Settings.FieldOfView; ViewCamera.allowHDR=true;
            atmosphere=cameraObject.AddComponent<AtmosphereRenderer>();
            atmosphere.Settings=JsonUtility.FromJson<AtmosphereSettings>(JsonUtility.ToJson(Settings.Atmosphere));atmosphere.Configure(ViewCamera,atmosphereMaterial);
            Stars=new GameObject("Streamed star catalogue").AddComponent<StarCatalogueRenderer>();
            Galaxy=new GameObject("Three dimensional galactic population").AddComponent<GalaxyField>();
            Galaxy.Configure(Settings.Galaxy,Settings.GalaxyParticleCount,galaxyMaterial);
            GalacticLight=new GameObject("Finite galactic emission and dust volume").AddComponent<GalaxyVolume>();
            GalacticLight.Configure(Settings.Galaxy,galaxyVolumeMaterial,galaxyCompositeMaterial);
            starObject=new GameObject("Procedural stellar photosphere");
            starMesh=CreateStarMesh(); starObject.AddComponent<MeshFilter>().sharedMesh=starMesh;
            starObject.AddComponent<MeshRenderer>().sharedMaterial=starMaterial;
            nucleusMaterial=new Material(starMaterial);nucleusMaterial.SetColor("_BaseColor",Color.black);nucleusMaterial.SetFloat("_Intensity",0);
            nucleusObject=new GameObject("Central black hole - Schwarzschild reference horizon");
            nucleusObject.AddComponent<MeshFilter>().sharedMesh=starMesh;nucleusObject.AddComponent<MeshRenderer>().sharedMaterial=nucleusMaterial;
            LoadSystem(0,0,0);
            FramePlanet(0,Settings.InitialAltitude);
            gameObject.AddComponent<ExplorerHud>();
            gameObject.AddComponent<FlightVerification>();
            gameObject.AddComponent<GalaxyVerification>();
        }

        Material Resolve(Material source,string shaderName)
        {
            if(source!=null)return new Material(source);
            Shader shader=Shader.Find(shaderName);
            if(shader==null)throw new InvalidOperationException("Missing required shader: "+shaderName+". Run Space Engine/Setup Explorer Scene.");
            return new Material(shader);
        }

        public void LoadSystem(long x,long y,long z)
        {
            if(Terrains!=null)foreach(var terrain in Terrains)Destroy(terrain.gameObject);
            if(Celestials!=null)foreach(var celestial in Celestials)Destroy(celestial.gameObject);
            if(Clouds!=null)foreach(var cloud in Clouds)Destroy(cloud.gameObject);
            Settings.Galaxy.Seed=Settings.UniverseSeed;
            if(Galaxy!=null&&(Galaxy.GeneratedSeed!=Settings.UniverseSeed||!ReferenceEquals(Galaxy.Definition,Settings.Galaxy)))
                Galaxy.Configure(Settings.Galaxy,Settings.GalaxyParticleCount,galaxyMaterial);
            if(GalacticLight!=null&&!ReferenceEquals(GalacticLight.Definition,Settings.Galaxy))
                GalacticLight.Configure(Settings.Galaxy,galaxyVolumeMaterial,galaxyCompositeMaterial);
            CellX=x;CellY=y;CellZ=z;
            System=UniverseGenerator.GenerateSystem(Settings.UniverseSeed,x,y,z,Settings.Galaxy);
            SelectedPlanet=0; radialNavigation=false;
            Terrains=new PlanetTerrain[System.Planets.Length];
            Celestials=new CelestialBodyRenderer[Terrains.Length];
            Clouds=new PlanetCloudRenderer[Terrains.Length];
            for(int i=0;i<Terrains.Length;i++)
            {
                var terrain=new GameObject(System.Planets[i].Name).AddComponent<PlanetTerrain>();
                terrain.PatchResolution=Settings.TerrainResolution;terrain.MaximumLevel=Settings.TerrainMaxLevel;
                terrain.MaterialMapResolution=Settings.TerrainMaterialResolution;terrain.MaterialDetailStrength=Settings.TerrainMaterialDetail;
                terrain.OceanRoughness=Settings.OceanRoughness;terrain.OceanSpecularStrength=Settings.OceanSpecularStrength;
                terrain.SurfaceNormalStrength=Settings.SurfaceNormalStrength;
                terrain.MaximumVisiblePatches=Settings.TerrainPatchBudget;terrain.MaximumResidentPatches=Settings.TerrainPatchBudget*2;
                terrain.BuildsPerFrame=Settings.MeshBuildsPerFrame;terrain.BuildBudgetMilliseconds=Settings.MeshBuildBudgetMs;
                terrain.RefinementThreshold=Settings.TerrainRefinement;
                terrain.OrbitalMinimumLevel=Settings.OrbitalMinimumLod;terrain.OrbitalDetailMinimumDegrees=Settings.OrbitalDetailMinimumDegrees;
                terrain.SkirtDepthFactor=Settings.SkirtDepthFactor;terrain.MinimumSkirtDepth=Settings.MinimumSkirtDepth;terrain.NormalSampleDistance=Settings.NormalSampleDistance;
                terrain.Configure(System.Planets[i],terrainMaterial); Terrains[i]=terrain;
                var celestial=new GameObject("Clouds and rings - "+System.Planets[i].Name).AddComponent<CelestialBodyRenderer>();
                celestial.BandContrast=Settings.GasBandContrast;celestial.StormStrength=Settings.GasStormStrength;celestial.RingOpticalDepth=Settings.RingOpticalDepth;
                celestial.LongitudeSegments=Settings.GasLongitudeSegments;celestial.LatitudeSegments=Settings.GasLatitudeSegments;celestial.RingAngularSegments=Settings.RingAngularSegments;
                celestial.Configure(System.Planets[i],gasMaterial,ringMaterial);Celestials[i]=celestial;
                var cloud=new GameObject("Weather - "+System.Planets[i].Name).AddComponent<PlanetCloudRenderer>();
                cloud.Coverage=Settings.CloudCoverage;cloud.Opacity=Settings.CloudOpacity;cloud.CloudHeightMeters=Settings.CloudHeightMeters;
                cloud.WeatherScale=Settings.CloudWeatherScale;cloud.DetailStrength=Settings.CloudDetailStrength;cloud.WindRate=Settings.CloudWindRate;
                cloud.Configure(System.Planets[i],cloudMaterial);Clouds[i]=cloud;
            }
            Stars.Generate(Settings.UniverseSeed,x,y,z,Settings.StarCellRadius,starBillboard,Settings.Galaxy);
            starMaterial.SetColor("_BaseColor",System.Kind==StellarKind.BlackHole?Color.black:StarCatalogueRenderer.TemperatureColor(System.StarTemperature));
            starMaterial.SetFloat("_Seed",System.Seed);
        }

        public Double3 BodyOffset(int index)
        {
            Double3 sum=Double3.Zero;int cursor=index;
            for(int depth=0;depth<System.Planets.Length;depth++)
            {
                var body=System.Planets[cursor];int parent=body.ParentIndex;
                sum+=Orbit.Evaluate(body,parent>=0?System.Planets[parent].Mass:System.StarMass,SimulationTime);
                if(parent<0)return sum;cursor=parent;
            }
            throw new InvalidDataException("Cyclic body hierarchy");
        }
        public UniversalPosition PlanetPosition(int index) => System.Position.Add(BodyOffset(index));
        Vector3 DirectionToSun(int index)=>(-BodyOffset(index)).Normalized.ToVector3();
        float SolarExposure(int index)
        {
            double distance=BodyOffset(index).Length;
            double relativeFlux=System.StarLuminosity/(4*Math.PI*Math.Max(1,distance*distance)*1361);
            return (float)Math.Clamp(Math.Pow(Math.Max(0,relativeFlux),.25),0,4);
        }
        double FlightClearance => Planet.IsLandable?Settings.MinimumClearance:Settings.GasCloudSafetyClearance;

        public void FramePlanet(int index,double altitude)
        {
            SelectedPlanet=Math.Clamp(index,0,System.Planets.Length-1);
            GalaxyOverview=false;NucleusView=false;StellarView=false;
            if(!Planet.IsLandable)altitude=Math.Max(altitude,Planet.Radius*3.5);
            Double3 sun=(-BodyOffset(SelectedPlanet)).Normalized;
            Double3 side=Double3.Cross(sun,new Double3(0,1,0)).Normalized;
            if(side.Length<.1)side=new Double3(1,0,0);
            Double3 direction=(sun*.78+side*.46+new Double3(0,.40,0)).Normalized;
            // Prefer a visible, dry landing site while retaining the deterministic generated planet.
            for(int i=0;i<60;i++)
            {
                var candidate=(direction+new Double3(Math.Sin(i*2.39)*.35,Math.Cos(i*1.17)*.35,Math.Sin(i*1.71)*.35)).Normalized;
                if(TerrainNoise.Height(candidate,Planet)>Planet.TerrainHeight*.08) {direction=candidate;break;}
            }
            landingDirection=direction.ToVector3();
            Observer=PlanetPosition(SelectedPlanet).Add(direction*(Planet.Radius+Math.Max(altitude,Settings.MinimumClearance+SurfaceHeight(direction))));
            orientation=Quaternion.LookRotation((-direction).ToVector3(),Vector3.up);
            ViewCamera.transform.rotation=orientation;
            radialNavigation=false;Speed=0;Navigation="FREE FLIGHT";
            UpdateTelemetry();
        }

        public double SurfaceHeight(Double3 direction)
        {
            if(!Planet.IsLandable)return 0;
            double height=TerrainNoise.Height(direction,Planet);
            return Planet.HasOcean?Math.Max(0,height):height;
        }

        public void SetObserver(UniversalPosition position,Quaternion rotation)
        {
            Observer=position;orientation=rotation;ViewCamera.transform.rotation=rotation;UpdateTelemetry();
        }

        public void SetAltitude(double altitude,bool surfaceView=false)
        {
            Double3 direction=CameraFromPlanet.Normalized;
            if(direction.Length<.1)direction=Double3.FromVector3(landingDirection);
            Observer=PlanetPosition(SelectedPlanet).Add(direction*(Planet.Radius+SurfaceHeight(direction)+Math.Max(FlightClearance,altitude)));
            Vector3 up=direction.ToVector3();
            Vector3 tangent=Vector3.ProjectOnPlane(DirectionToSun(SelectedPlanet),up).normalized;
            if(tangent.sqrMagnitude<.1)tangent=Vector3.Cross(up,Vector3.forward).normalized;
            orientation=surfaceView?Quaternion.LookRotation((tangent-up*.12f).normalized,up):Quaternion.LookRotation(-up,Vector3.up);
            ViewCamera.transform.rotation=orientation;UpdateTelemetry();
        }

        public void BeginDescent(){GalaxyOverview=false;NucleusView=false;StellarView=false;targetAltitude=FlightClearance;radialNavigation=true;Navigation=Planet.IsLandable?"CONTINUOUS DESCENT":"APPROACH CLOUD TOPS / NO SOLID SURFACE";}
        public void BeginAscent(){GalaxyOverview=false;NucleusView=false;StellarView=false;targetAltitude=Planet.Radius*3;radialNavigation=true;Navigation="CONTINUOUS ASCENT";}
        public void StopNavigation(){radialNavigation=false;Speed=0;Navigation="FREE FLIGHT";}

        public void AdvanceRadialFlight(double dt)
        {
            if(!radialNavigation)return;
            Double3 relative=CameraFromPlanet;Double3 direction=relative.Normalized;
            double ground=Planet.Radius+SurfaceHeight(direction);
            double current=relative.Length-ground;
            double remaining=targetAltitude-current;
            double step=Math.Min(Math.Abs(remaining),Math.Max(2,Math.Abs(current)*.65)*dt);
            Speed=dt>0?step/dt:0;
            double next=current+Math.Sign(remaining)*step;
            Observer=PlanetPosition(SelectedPlanet).Add(direction*(ground+Math.Max(FlightClearance,next)));
            if((Planet.IsLandable&&next<2500)||(!Planet.IsLandable&&next<Planet.Radius*.01))
            {
                Vector3 up=direction.ToVector3();
                Vector3 tangent=Vector3.ProjectOnPlane(DirectionToSun(SelectedPlanet),up).normalized;
                if(tangent.sqrMagnitude<.1)tangent=Vector3.Cross(up,Vector3.forward).normalized;
                orientation=Quaternion.Slerp(orientation,Quaternion.LookRotation((tangent-up*.12f).normalized,up),(float)Math.Min(1,dt*1.5));
            }
            if(Math.Abs(remaining)<.05){radialNavigation=false;Speed=0;Navigation=!Planet.IsLandable?"HOLDING ABOVE CLOUDS / NON-LANDABLE":targetAltitude<10?"LANDED / TERRAIN FOLLOW":"FREE FLIGHT";}
        }

        void Update()
        {
            double dt=Math.Min(Time.unscaledDeltaTime,.15f);
            FrameMilliseconds=FrameMilliseconds*.94+Time.unscaledDeltaTime*1000*.06;
            if(!VerificationOwnsFlight)
            {
                SelectNearestBody();
                var before=PlanetPosition(SelectedPlanet);
                SimulationTime+=dt*TimeRate;
                // Follow the moving planet's local inertial frame near it, not a float transform parent.
                if(Observer.RelativeTo(before).Length<Planet.Radius*50)
                    Observer=Observer.Add(PlanetPosition(SelectedPlanet).RelativeTo(before));
                HandleInput(dt);
                AdvanceRadialFlight(dt);
                ConstrainGround();
                // Keep nearby navigation responsive. At extreme cruise speeds the galaxy field
                // remains spatially continuous; throttle resolved-catalogue work to four Hz.
                if(Time.unscaledTimeAsDouble>=nextStreamTime)
                {nextStreamTime=Time.unscaledTimeAsDouble+.25;StreamNearestSystem();}
            }
            ViewCamera.transform.SetPositionAndRotation(Vector3.zero,orientation);
            UpdateTelemetry();
            RenderUniverse();
        }

        void HandleInput(double dt)
        {
            var key=Keyboard.current;var mouse=Mouse.current;
            if(key==null)return;
            if(key.escapeKey.wasPressedThisFrame){StopNavigation();Cursor.lockState=CursorLockMode.None;}
            if(key.f1Key.wasPressedThisFrame)ShowHud=!ShowHud;
            if(key.gKey.wasPressedThisFrame)BeginDescent();
            if(key.rKey.wasPressedThisFrame)BeginAscent();
            if(key.fKey.wasPressedThisFrame)FramePlanet(SelectedPlanet,Settings.InitialAltitude);
            if(key.tabKey.wasPressedThisFrame)FramePlanet((SelectedPlanet+1)%System.Planets.Length,Settings.InitialAltitude);
            if(key.nKey.wasPressedThisFrame)JumpToNeighbour();
            if(key.mKey.wasPressedThisFrame)FrameGalaxy();
            if(key.homeKey.wasPressedThisFrame)ReturnHome();
            if(key.bKey.wasPressedThisFrame)FrameNucleus();
            if(key.tKey.wasPressedThisFrame)FrameStar();
            if(key.f5Key.wasPressedThisFrame)SaveFlight();
            if(key.f9Key.wasPressedThisFrame)LoadFlight();
            if(key.spaceKey.wasPressedThisFrame)TimeRate=TimeRate==0?1:0;
            if(mouse!=null)
            {
                SpeedMultiplier=Math.Clamp(SpeedMultiplier*Math.Pow(1.2,mouse.scroll.ReadValue().y/120),.0001,1e12);
                if(mouse.rightButton.isPressed)
                {
                    Vector2 delta=mouse.delta.ReadValue();
                    orientation=Quaternion.AngleAxis(delta.x*.12f,orientation*Vector3.up)*orientation*Quaternion.AngleAxis(-delta.y*.12f,Vector3.right);
                }
            }
            float x=(key.dKey.isPressed?1:0)-(key.aKey.isPressed?1:0);
            float y=(key.eKey.isPressed?1:0)-(key.qKey.isPressed?1:0);
            float z=(key.wKey.isPressed?1:0)-(key.sKey.isPressed?1:0);
            float roll=(key.cKey.isPressed?1:0)-(key.zKey.isPressed?1:0);
            orientation*=Quaternion.AngleAxis(-roll*(float)dt*45,Vector3.forward);
            if(x!=0||y!=0||z!=0)
            {
                StopNavigation();
                Vector3 direction=orientation*new Vector3(x,y,z).normalized;
                double baseSpeed=Math.Max(3,Math.Max(Altitude,0)*.5);
                Speed=Math.Min(PhysicalConstants.LightYear*Settings.MaximumFlightSpeedLyPerSecond,baseSpeed*SpeedMultiplier*(key.leftShiftKey.isPressed?10:1));
                Observer=Observer.Add(Double3.FromVector3(direction)*Speed*dt);
            }
            else Speed=0;
        }

        public void ConstrainGround()
        {
            Double3 relative=CameraFromPlanet;
            if(relative.Length>Planet.Radius*1.1)return;
            Double3 direction=relative.Normalized;
            double floor=Planet.Radius+SurfaceHeight(direction)+FlightClearance;
            if(relative.Length<floor)Observer=PlanetPosition(SelectedPlanet).Add(direction*floor);
        }

        public void SelectNearestBody()
        {
            double best=CameraFromPlanet.Length;int selected=SelectedPlanet;
            for(int i=0;i<System.Planets.Length;i++)
            {
                double distance=Observer.RelativeTo(PlanetPosition(i)).Length;
                if(distance<System.Planets[i].Radius*50&&distance<best*.9){best=distance;selected=i;}
            }
            if(selected!=SelectedPlanet){SelectedPlanet=selected;StopNavigation();Notice="Entered local frame of "+Planet.Name;}
        }

        public void StreamNearestSystem()
        {
            double current=Observer.RelativeTo(System.Position).Length;
            long cx=(long)Math.Round(Observer.sectorX*(UniversalPosition.SectorSize/UniverseGenerator.CellSize)+Observer.local.x/UniverseGenerator.CellSize);
            long cy=(long)Math.Round(Observer.sectorY*(UniversalPosition.SectorSize/UniverseGenerator.CellSize)+Observer.local.y/UniverseGenerator.CellSize);
            long cz=(long)Math.Round(Observer.sectorZ*(UniversalPosition.SectorSize/UniverseGenerator.CellSize)+Observer.local.z/UniverseGenerator.CellSize);
            if(Stars.CenterX!=cx||Stars.CenterY!=cy||Stars.CenterZ!=cz)
                Stars.Generate(Settings.UniverseSeed,cx,cy,cz,Settings.StarCellRadius,starBillboard,Settings.Galaxy);
            if(current<UniverseGenerator.CellSize*.2)return;
            StarCatalogueRenderer.Entry best=default;double nearest=current;
            foreach(var entry in Stars.Entries)
            {
                var candidate=entry.System;
                double distance=Observer.RelativeTo(candidate.Position).Length;
                if(distance<nearest*.98){nearest=distance;best=entry;}
            }
            if(best.System!=null)
            {
                var retained=Observer;LoadSystem(best.X,best.Y,best.Z);Observer=retained;
                Notice="Streamed "+System.Name+"; observer position retained";
            }
        }

        public void JumpToNeighbour()
        {
            var next=NearestCatalogueSystem();
            if(next.System==null){Notice="No resolved system in local volume. Home returns to the inhabited disk.";return;}
            LoadSystem(next.X,next.Y,next.Z);FramePlanet(0,Settings.InitialAltitude);
            Notice="Explicit navigation jump / new deterministic system";
        }

        public StarCatalogueRenderer.Entry NearestCatalogueSystem()
        {
            StarCatalogueRenderer.Entry best=default;double distance=double.MaxValue;
            foreach(var entry in Stars.Entries)
            {
                if(entry.X==CellX&&entry.Y==CellY&&entry.Z==CellZ)continue;
                double candidate=Observer.RelativeTo(entry.System.Position).Length;
                if(candidate<distance){best=entry;distance=candidate;}
            }
            return best;
        }

        public void ReturnHome(){LoadSystem(0,0,0);FramePlanet(0,Settings.InitialAltitude);Notice="Home system / generation v2";}
        public void FrameStar()
        {
            StopNavigation();GalaxyOverview=false;NucleusView=false;StellarView=true;
            Double3 offset=new Double3(0,.3,1).Normalized*(System.StarRadius*6);
            SetObserver(System.Position.Add(offset),Quaternion.LookRotation((-offset).ToVector3(),Vector3.up));
            Notice="STELLAR REFERENCE / "+System.Kind+" / no relativistic lensing";
        }
        public void FrameGalaxy(bool edgeOn=false)
        {
            StopNavigation();GalaxyOverview=true;NucleusView=false;StellarView=false;
            Double3 viewpoint=edgeOn?new Double3(0,5000,160000):new Double3(0,105000,125000);
            var position=GalaxyModel.FromGalacticLy(viewpoint,Settings.Galaxy);
            SetObserver(position,Quaternion.LookRotation((-viewpoint).ToVector3(),Vector3.up));
            StreamNearestSystem();Notice=edgeOn?"GALACTIC EDGE / spatial disk thickness":"GALAXY OVERVIEW / M to frame / Home to return";
        }
        public double NucleusRadius => 2*PhysicalConstants.GravitationalConstant*Settings.Galaxy.CentralMassSolarMasses*PhysicalConstants.SolarMass/(299792458d*299792458d);
        public void FrameNucleus()
        {
            StopNavigation();GalaxyOverview=false;NucleusView=true;StellarView=false;
            Double3 offset=new Double3(0,5,20)*NucleusRadius;
            SetObserver(GalaxyModel.Center(Settings.Galaxy).Add(offset),Quaternion.LookRotation((-offset).ToVector3(),Vector3.up));
            StreamNearestSystem();Notice="CENTRAL BLACK HOLE / geometric horizon; no relativistic lensing";
        }

        void UpdateTelemetry()
        {
            Double3 relative=CameraFromPlanet;
            Altitude=relative.Length-Planet.Radius-SurfaceHeight(relative.Normalized);
            if(NucleusView)Altitude=Observer.RelativeTo(GalaxyModel.Center(Settings.Galaxy)).Length-NucleusRadius;
            if(StellarView)Altitude=Observer.RelativeTo(System.Position).Length-System.StarRadius;
            RenderScale=1/Math.Max(1,Math.Max(Altitude,0)/30000);
        }

        public void RenderUniverse()
        {
            ViewCamera.fieldOfView=GalaxyOverview?Settings.GalaxyOverviewFieldOfView:Settings.FieldOfView;
            Double3 selectedRelative=CameraFromPlanet;
            Vector3 selectedCenter=(-(selectedRelative*RenderScale)).ToVector3();
            Vector3 sunDirection=DirectionToSun(SelectedPlanet);
            bool detailed=Observer.RelativeTo(System.Position).Length<PhysicalConstants.LightYear*.3;
            for(int i=0;i<Terrains.Length;i++)
            {
                Terrains[i].gameObject.SetActive(detailed&&System.Planets[i].IsLandable);
                Double3 from=Observer.RelativeTo(PlanetPosition(i));
                double scale=RenderScale;
                float light=SolarExposure(i);
                Color stellarTint=Color.Lerp(Color.white,StarCatalogueRenderer.TemperatureColor(System.StarTemperature),.4f);
                Terrains[i].SunIntensity=light*Settings.SunLightingExposure;Terrains[i].SunColor=stellarTint;
                Celestials[i].SunIntensity=light*Settings.SunLightingExposure;Celestials[i].SunColor=stellarTint;
                // Far bodies use an angularly exact render proxy; their SI positions and radii do not change.
                if(i!=SelectedPlanet)scale*=ProxyScale(from.Length*scale);
                if(detailed&&System.Planets[i].IsLandable)Terrains[i].Tick(from,scale,(-(from*scale)).ToVector3(),DirectionToSun(i));
                Celestials[i].ShowRenderer=detailed;
                Celestials[i].Tick(from,scale,DirectionToSun(i),SimulationTime);
                Clouds[i].SunIntensity=light*Settings.SunLightingExposure;Clouds[i].SunColor=stellarTint;Clouds[i].ShowRenderer=detailed;
                Clouds[i].Tick(from,scale,DirectionToSun(i),SimulationTime);
            }
            atmosphere.enabled=Planet.AtmosphereHeight>0&&selectedRelative.Length<Planet.Radius*25;
            if(atmosphere.enabled)
            {
                atmosphere.Settings.SunIntensity=Settings.Atmosphere.SunIntensity*SolarExposure(SelectedPlanet);
                atmosphere.UpdateAtmosphere(selectedCenter,(float)(Planet.Radius*RenderScale),(float)((Planet.Radius+Planet.AtmosphereHeight)*RenderScale),sunDirection,Settings.AtmosphereExposure);
            }
            Double3 starFrom=System.Position.RelativeTo(Observer);
            starObject.SetActive(detailed);
            double sunScale=RenderScale*ProxyScale(starFrom.Length*RenderScale);
            starObject.transform.position=(starFrom*sunScale).ToVector3();
            starObject.transform.localScale=Vector3.one*(float)(System.StarRadius*sunScale);
            starMaterial.SetFloat("_StarTime",(float)(SimulationTime%100000));
            Stars.Tick(Observer,ViewCamera,Settings.StarBrightness,Settings.StarMinimumAngularRadius);
            Galaxy.Exposure=Settings.GalaxyExposure;Galaxy.KernelScale=Settings.GalaxyKernelScale;Galaxy.DustStrength=Settings.GalaxyDustStrength;
            Galaxy.DustScaleHeightLy=Settings.GalaxyDustScaleHeightLy;Galaxy.DustAttenuationLengthLy=Settings.GalaxyDustAttenuationLengthLy;
            Galaxy.LocalFadeLy=Settings.GalaxyLocalFadeLy;Galaxy.Tick(Observer);
            GalacticLight.Exposure=Settings.GalaxyVolumeExposure;GalacticLight.ResolutionScale=Settings.GalaxyResolutionScale;
            GalacticLight.SampleCount=Settings.GalaxyVolumeSamples;GalacticLight.DustStrength=Settings.GalaxyDustStrength;
            GalacticLight.InteriorExposureMultiplier=Settings.GalaxyInteriorExposure;GalacticLight.ExteriorExposureMultiplier=Settings.GalaxyExteriorExposure;
            GalacticLight.Contrast=Settings.GalaxyContrast;
            GalacticLight.DustScaleHeightLy=Settings.GalaxyDustScaleHeightLy;GalacticLight.DustAttenuationLengthLy=Settings.GalaxyDustAttenuationLengthLy;
            GalacticLight.Tick(Observer,ViewCamera);
            Double3 coreFrom=GalaxyModel.Center(Settings.Galaxy).RelativeTo(Observer);
            double coreScale=RenderScale*ProxyScale(coreFrom.Length*RenderScale);
            nucleusObject.transform.position=(coreFrom*coreScale).ToVector3();nucleusObject.transform.localScale=Vector3.one*(float)(NucleusRadius*coreScale);
        }

        static double ProxyScale(double renderDistance)
        {
            // Shared monotone depth compression preserves distant-body ordering and angular radii.
            const double threshold=8000000,span=4000000;
            if(renderDistance<=threshold)return 1;
            double mapped=threshold+span*(1-threshold/renderDistance);
            return mapped/renderDistance;
        }

        [Serializable] sealed class SaveData
        {
            public int Version=2,GenerationVersion=UniverseGenerator.GenerationVersion,Seed,PlanetIndex,ViewMode;public long CellX,CellY,CellZ;public UniversalPosition Position;public Quaternion Rotation;public double Time;public GalaxyDefinition Galaxy;
        }
        public void SaveFlight()
        {
            string path=SavePath;
            var save=new SaveData{Seed=Settings.UniverseSeed,CellX=CellX,CellY=CellY,CellZ=CellZ,PlanetIndex=SelectedPlanet,Position=Observer,Rotation=orientation,Time=SimulationTime,Galaxy=Settings.Galaxy,ViewMode=GalaxyOverview?1:NucleusView?2:StellarView?3:0};
            string temporary=path+".tmp";File.WriteAllText(temporary,JsonUtility.ToJson(save,true));
            if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
            Notice="Flight saved / F9 restore";
        }
        public void LoadFlight()
        {
            string path=SavePath;
            if(!File.Exists(path)){Notice=File.Exists(Path.Combine(Application.persistentDataPath,"flight.json"))?"Generation v1 save retained; open it with the 0.1.0 build.":"No generation v2 saved flight";return;}
            try
            {
                var save=JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if(save==null||save.Version!=2||save.GenerationVersion!=UniverseGenerator.GenerationVersion)throw new InvalidDataException("Unsupported save or world generation version");
                if(save.ViewMode<0||save.ViewMode>3)throw new InvalidDataException("Unsupported reference view");
                if(double.IsNaN(save.Time)||double.IsInfinity(save.Time))throw new InvalidDataException("Invalid simulation time");
                var normalized=new UniversalPosition(save.Position.sectorX,save.Position.sectorY,save.Position.sectorZ,save.Position.local);
                float qLength=save.Rotation.x*save.Rotation.x+save.Rotation.y*save.Rotation.y+save.Rotation.z*save.Rotation.z+save.Rotation.w*save.Rotation.w;
                if(float.IsNaN(qLength)||float.IsInfinity(qLength)||qLength<.1f)throw new InvalidDataException("Invalid orientation");
                if(save.Galaxy==null)throw new InvalidDataException("Missing galactic configuration");
                UniverseGenerator.GenerateSystem(save.Seed,save.CellX,save.CellY,save.CellZ,save.Galaxy);
                GalaxyModel.SampleDensity(Double3.Zero,save.Galaxy);
                Settings.Galaxy=save.Galaxy;
                Settings.UniverseSeed=save.Seed;SimulationTime=save.Time;LoadSystem(save.CellX,save.CellY,save.CellZ);
                SelectedPlanet=Math.Clamp(save.PlanetIndex,0,System.Planets.Length-1);
                GalaxyOverview=save.ViewMode==1;NucleusView=save.ViewMode==2;StellarView=save.ViewMode==3;
                SetObserver(normalized,save.Rotation.normalized);
                Notice="Flight restored";
            }
            catch(Exception ex){Notice="Restore failed: "+ex.Message;Debug.LogException(ex);}
        }

        static Mesh CreateStarMesh()
        {
            const int rings=32,segments=64;
            var vertices=new Vector3[(rings+1)*(segments+1)];var normals=new Vector3[vertices.Length];var indices=new int[rings*segments*6];
            int t=0;
            for(int y=0;y<=rings;y++)for(int x=0;x<=segments;x++)
            {
                float a=(float)y/rings*Mathf.PI,b=(float)x/segments*Mathf.PI*2;
                int v=y*(segments+1)+x;vertices[v]=new Vector3(Mathf.Sin(a)*Mathf.Cos(b),Mathf.Cos(a),Mathf.Sin(a)*Mathf.Sin(b));normals[v]=vertices[v];
                if(y<rings&&x<segments){int n=v+segments+1;indices[t++]=v;indices[t++]=v+1;indices[t++]=n;indices[t++]=v+1;indices[t++]=n+1;indices[t++]=n;}
            }
            var result=new Mesh{name="Procedural stellar surface"};result.vertices=vertices;result.normals=normals;result.triangles=indices;result.RecalculateBounds();return result;
        }
        void OnDestroy()
        {
            if(Terrains!=null)foreach(var terrain in Terrains)if(terrain!=null)Destroy(terrain.gameObject);
            if(Celestials!=null)foreach(var celestial in Celestials)if(celestial!=null)Destroy(celestial.gameObject);
            if(Clouds!=null)foreach(var cloud in Clouds)if(cloud!=null)Destroy(cloud.gameObject);
            if(GalacticLight!=null)Destroy(GalacticLight.gameObject);
            if(Galaxy!=null)Destroy(Galaxy.gameObject);if(nucleusObject!=null)Destroy(nucleusObject);
            if(Stars!=null)Destroy(Stars.gameObject);if(starObject!=null)Destroy(starObject);if(starMesh!=null)Destroy(starMesh);
            if(ViewCamera!=null)Destroy(ViewCamera.gameObject);
            foreach(var material in new[]{terrainMaterial,starMaterial,starBillboard,atmosphereMaterial,galaxyMaterial,gasMaterial,ringMaterial,nucleusMaterial,galaxyVolumeMaterial,galaxyCompositeMaterial,cloudMaterial})if(material!=null)Destroy(material);
            if(Settings!=null)Destroy(Settings);
        }
    }
}
