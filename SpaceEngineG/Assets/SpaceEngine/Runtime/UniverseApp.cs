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
        public string Navigation { get; private set; } = "FREE FLIGHT";
        public double FrameMilliseconds { get; private set; }
        public bool VerificationOwnsFlight;
        public bool ShowHud=true;
        public string Notice="RMB look  /  WASD fly  /  scroll speed";
        public PlanetDefinition Planet => System.Planets[SelectedPlanet];
        public Double3 CameraFromPlanet => Observer.RelativeTo(PlanetPosition(SelectedPlanet));
        public int ActiveTriangles { get {int total=0; if(Terrains!=null)foreach(var t in Terrains)total+=t.TriangleCount;return total;} }
        public int ActivePatches { get {int total=0; if(Terrains!=null)foreach(var t in Terrains)total+=t.ActivePatchCount;return total;} }
        public int MaxLod => Terrains==null?0:Terrains[SelectedPlanet].MaxActiveLevel;
        public double BuildMilliseconds { get {double total=0;if(Terrains!=null)foreach(var t in Terrains)total+=t.LastBuildMilliseconds;return total;} }

        AtmosphereRenderer atmosphere;
        Material terrainMaterial,starMaterial,starBillboard,atmosphereMaterial;
        GameObject starObject;
        Mesh starMesh;
        Quaternion orientation;
        double targetAltitude;
        bool radialNavigation;
        int frame;
        Vector3 landingDirection;

        void Awake()
        {
            Application.runInBackground=true;
            QualitySettings.vSyncCount=0;
            Application.targetFrameRate=120;
            Settings=Settings!=null?Settings:Resources.Load<UniverseSettings>("UniverseSettings");
            if(Settings==null) Settings=ScriptableObject.CreateInstance<UniverseSettings>();
            else Settings=Instantiate(Settings);
            terrainMaterial=Resolve(Settings.TerrainMaterial,"SpaceEngine/Terrain");
            starMaterial=Resolve(Settings.StarMaterial,"SpaceEngine/Star");
            starBillboard=Resolve(Settings.StarBillboardMaterial,"SpaceEngine/StarBillboard");
            starBillboard.SetFloat("_CoreSize",Settings.StarCoreRadius);
            atmosphereMaterial=Resolve(Settings.AtmosphereMaterial,"SpaceEngine/Atmosphere");
            terrainMaterial.SetFloat("_AmbientFloor",Settings.AmbientLight);
            RenderSettings.skybox=null; RenderSettings.fog=false; RenderSettings.ambientLight=Color.black;
            var cameraObject=new GameObject("Observer camera - always local origin");
            cameraObject.tag="MainCamera";
            ViewCamera=cameraObject.AddComponent<Camera>(); ViewCamera.clearFlags=CameraClearFlags.SolidColor;
            ViewCamera.backgroundColor=new Color(.0002f,.0003f,.0008f); ViewCamera.nearClipPlane=.1f; ViewCamera.farClipPlane=16000000;
            ViewCamera.fieldOfView=Settings.FieldOfView; ViewCamera.allowHDR=true;
            atmosphere=cameraObject.AddComponent<AtmosphereRenderer>(); atmosphere.Settings=Settings.Atmosphere;atmosphere.Configure(ViewCamera,atmosphereMaterial);
            Stars=new GameObject("Streamed star catalogue").AddComponent<StarCatalogueRenderer>();
            starObject=new GameObject("Procedural stellar photosphere");
            starMesh=CreateStarMesh(); starObject.AddComponent<MeshFilter>().sharedMesh=starMesh;
            starObject.AddComponent<MeshRenderer>().sharedMaterial=starMaterial;
            LoadSystem(0,0,0);
            FramePlanet(0,Settings.InitialAltitude);
            gameObject.AddComponent<ExplorerHud>();
            gameObject.AddComponent<FlightVerification>();
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
            CellX=x;CellY=y;CellZ=z;
            System=UniverseGenerator.GenerateSystem(Settings.UniverseSeed,x,y,z);
            SelectedPlanet=0; radialNavigation=false;
            Terrains=new PlanetTerrain[System.Planets.Length];
            for(int i=0;i<Terrains.Length;i++)
            {
                var terrain=new GameObject(System.Planets[i].Name).AddComponent<PlanetTerrain>();
                terrain.PatchResolution=Settings.TerrainResolution;terrain.MaximumLevel=Settings.TerrainMaxLevel;
                terrain.MaximumVisiblePatches=Settings.TerrainPatchBudget;terrain.MaximumResidentPatches=Settings.TerrainPatchBudget*2;
                terrain.BuildsPerFrame=Settings.MeshBuildsPerFrame;terrain.BuildBudgetMilliseconds=Settings.MeshBuildBudgetMs;
                terrain.RefinementThreshold=Settings.TerrainRefinement;
                terrain.OrbitalMinimumLevel=Settings.OrbitalMinimumLod;terrain.OrbitalDetailMinimumDegrees=Settings.OrbitalDetailMinimumDegrees;
                terrain.SkirtDepthFactor=Settings.SkirtDepthFactor;terrain.MinimumSkirtDepth=Settings.MinimumSkirtDepth;terrain.NormalSampleDistance=Settings.NormalSampleDistance;
                terrain.Configure(System.Planets[i],terrainMaterial); Terrains[i]=terrain;
            }
            Stars.Generate(Settings.UniverseSeed,x,y,z,Settings.StarCellRadius,starBillboard);
            starMaterial.SetColor("_BaseColor",StarCatalogueRenderer.TemperatureColor(System.StarTemperature));
            starMaterial.SetFloat("_Seed",System.Seed);
        }

        public UniversalPosition PlanetPosition(int index) => System.Position.Add(Orbit.Evaluate(System.Planets[index],System.StarMass,SimulationTime));

        public void FramePlanet(int index,double altitude)
        {
            SelectedPlanet=Math.Clamp(index,0,System.Planets.Length-1);
            Double3 sun=(-Orbit.Evaluate(Planet,System.StarMass,SimulationTime)).Normalized;
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
            radialNavigation=false;Navigation="FREE FLIGHT";
            UpdateTelemetry();
        }

        public double SurfaceHeight(Double3 direction)
        {
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
            Observer=PlanetPosition(SelectedPlanet).Add(direction*(Planet.Radius+SurfaceHeight(direction)+Math.Max(Settings.MinimumClearance,altitude)));
            Vector3 up=direction.ToVector3();
            Vector3 tangent=Vector3.ProjectOnPlane((-Orbit.Evaluate(Planet,System.StarMass,SimulationTime)).ToVector3(),up).normalized;
            if(tangent.sqrMagnitude<.1)tangent=Vector3.Cross(up,Vector3.forward).normalized;
            orientation=surfaceView?Quaternion.LookRotation((tangent-up*.12f).normalized,up):Quaternion.LookRotation(-up,Vector3.up);
            ViewCamera.transform.rotation=orientation;UpdateTelemetry();
        }

        public void BeginDescent(){targetAltitude=Settings.MinimumClearance;radialNavigation=true;Navigation="CONTINUOUS DESCENT";}
        public void BeginAscent(){targetAltitude=Planet.Radius*3;radialNavigation=true;Navigation="CONTINUOUS ASCENT";}
        public void StopNavigation(){radialNavigation=false;Navigation="FREE FLIGHT";}

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
            Observer=PlanetPosition(SelectedPlanet).Add(direction*(ground+Math.Max(Settings.MinimumClearance,next)));
            if(next<2500)
            {
                Vector3 up=direction.ToVector3();
                Vector3 tangent=Vector3.ProjectOnPlane((-Orbit.Evaluate(Planet,System.StarMass,SimulationTime)).ToVector3(),up).normalized;
                if(tangent.sqrMagnitude<.1)tangent=Vector3.Cross(up,Vector3.forward).normalized;
                orientation=Quaternion.Slerp(orientation,Quaternion.LookRotation((tangent-up*.12f).normalized,up),(float)Math.Min(1,dt*1.5));
            }
            if(Math.Abs(remaining)<.05){radialNavigation=false;Navigation=targetAltitude<10?"LANDED / TERRAIN FOLLOW":"FREE FLIGHT";}
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
                if(++frame%60==0)StreamNearestSystem();
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
                Speed=Math.Min(PhysicalConstants.LightYear*2,baseSpeed*SpeedMultiplier*(key.leftShiftKey.isPressed?10:1));
                Observer=Observer.Add(Double3.FromVector3(direction)*Speed*dt);
            }
            else Speed=0;
        }

        public void ConstrainGround()
        {
            Double3 relative=CameraFromPlanet;
            if(relative.Length>Planet.Radius*1.1)return;
            Double3 direction=relative.Normalized;
            double floor=Planet.Radius+SurfaceHeight(direction)+Settings.MinimumClearance;
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
            if(current<UniverseGenerator.CellSize*.2)return;
            long cx=(long)Math.Round(Observer.sectorX*(UniversalPosition.SectorSize/UniverseGenerator.CellSize)+Observer.local.x/UniverseGenerator.CellSize);
            long cy=(long)Math.Round(Observer.sectorY*(UniversalPosition.SectorSize/UniverseGenerator.CellSize)+Observer.local.y/UniverseGenerator.CellSize);
            long cz=(long)Math.Round(Observer.sectorZ*(UniversalPosition.SectorSize/UniverseGenerator.CellSize)+Observer.local.z/UniverseGenerator.CellSize);
            StarCatalogueRenderer.Entry best=default;double nearest=current;
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
            {
                var candidate=UniverseGenerator.GenerateSystem(Settings.UniverseSeed,cx+x,cy+y,cz+z);
                double distance=Observer.RelativeTo(candidate.Position).Length;
                if(distance<nearest*.98){nearest=distance;best=new StarCatalogueRenderer.Entry{X=cx+x,Y=cy+y,Z=cz+z,System=candidate};}
            }
            if(best.System!=null)
            {
                var retained=Observer;LoadSystem(best.X,best.Y,best.Z);Observer=retained;
                Notice="Streamed "+System.Name+"; observer position retained";
            }
        }

        public void JumpToNeighbour()
        {
            LoadSystem(CellX+1,CellY,CellZ);FramePlanet(0,Settings.InitialAltitude);
            Notice="Explicit navigation jump / new deterministic system";
        }

        void UpdateTelemetry()
        {
            Double3 relative=CameraFromPlanet;
            Altitude=relative.Length-Planet.Radius-SurfaceHeight(relative.Normalized);
            RenderScale=1/Math.Max(1,Math.Max(Altitude,0)/30000);
        }

        public void RenderUniverse()
        {
            Double3 selectedRelative=CameraFromPlanet;
            Vector3 selectedCenter=(-(selectedRelative*RenderScale)).ToVector3();
            Vector3 sunDirection=(-Orbit.Evaluate(Planet,System.StarMass,SimulationTime)).Normalized.ToVector3();
            for(int i=0;i<Terrains.Length;i++)
            {
                Double3 from=Observer.RelativeTo(PlanetPosition(i));
                double scale=RenderScale;
                // Far bodies use an angularly exact render proxy; their SI positions and radii do not change.
                if(i!=SelectedPlanet)scale*=ProxyScale(from.Length*scale);
                Terrains[i].Tick(from,scale,(-(from*scale)).ToVector3(),(-Orbit.Evaluate(System.Planets[i],System.StarMass,SimulationTime)).Normalized.ToVector3());
            }
            atmosphere.enabled=Planet.AtmosphereHeight>0&&selectedRelative.Length<Planet.Radius*25;
            if(atmosphere.enabled)
                atmosphere.UpdateAtmosphere(selectedCenter,(float)(Planet.Radius*RenderScale),(float)((Planet.Radius+Planet.AtmosphereHeight)*RenderScale),sunDirection,Settings.AtmosphereExposure);
            Double3 starFrom=System.Position.RelativeTo(Observer);
            double sunScale=RenderScale*ProxyScale(starFrom.Length*RenderScale);
            starObject.transform.position=(starFrom*sunScale).ToVector3();
            starObject.transform.localScale=Vector3.one*(float)(System.StarRadius*sunScale);
            starMaterial.SetFloat("_StarTime",(float)(SimulationTime%100000));
            Stars.Tick(Observer,ViewCamera,Settings.StarBrightness,Settings.StarMinimumAngularRadius);
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
            public int Version=1,Seed,PlanetIndex;public long CellX,CellY,CellZ;public UniversalPosition Position;public Quaternion Rotation;public double Time;
        }
        public void SaveFlight()
        {
            string path=Path.Combine(Application.persistentDataPath,"flight.json");
            var save=new SaveData{Seed=Settings.UniverseSeed,CellX=CellX,CellY=CellY,CellZ=CellZ,PlanetIndex=SelectedPlanet,Position=Observer,Rotation=orientation,Time=SimulationTime};
            string temporary=path+".tmp";File.WriteAllText(temporary,JsonUtility.ToJson(save,true));
            if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
            Notice="Flight saved / F9 restore";
        }
        public void LoadFlight()
        {
            string path=Path.Combine(Application.persistentDataPath,"flight.json");
            if(!File.Exists(path)){Notice="No saved flight";return;}
            try
            {
                var save=JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if(save==null||save.Version!=1)throw new InvalidDataException("Unsupported save version");
                if(double.IsNaN(save.Time)||double.IsInfinity(save.Time))throw new InvalidDataException("Invalid simulation time");
                var normalized=new UniversalPosition(save.Position.sectorX,save.Position.sectorY,save.Position.sectorZ,save.Position.local);
                float qLength=save.Rotation.x*save.Rotation.x+save.Rotation.y*save.Rotation.y+save.Rotation.z*save.Rotation.z+save.Rotation.w*save.Rotation.w;
                if(float.IsNaN(qLength)||float.IsInfinity(qLength)||qLength<.1f)throw new InvalidDataException("Invalid orientation");
                UniverseGenerator.GenerateSystem(save.Seed,save.CellX,save.CellY,save.CellZ);
                Settings.UniverseSeed=save.Seed;SimulationTime=save.Time;LoadSystem(save.CellX,save.CellY,save.CellZ);
                SelectedPlanet=Math.Clamp(save.PlanetIndex,0,System.Planets.Length-1);SetObserver(normalized,save.Rotation.normalized);
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
            if(Stars!=null)Destroy(Stars.gameObject);if(starObject!=null)Destroy(starObject);if(starMesh!=null)Destroy(starMesh);
            if(ViewCamera!=null)Destroy(ViewCamera.gameObject);
            foreach(var material in new[]{terrainMaterial,starMaterial,starBillboard,atmosphereMaterial})if(material!=null)Destroy(material);
            if(Settings!=null)Destroy(Settings);
        }
    }
}
