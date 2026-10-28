using System;
using System.Collections.Generic;
using System.IO;
using SpaceEngine.Core;
using SpaceEngine.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceEngine.Runtime
{
    /// <summary>Selection, catalogue browsing and view tools never substitute a jump for continuous flight.</summary>
    public sealed class ExplorerTools:MonoBehaviour
    {
        public bool SearchOpen {get;private set;}
        public bool BookmarksOpen {get;private set;}
        public bool ShowOrbits,ShowLabels=true,ShowDetails,TrackingTarget;
        public bool IsFlyingToTarget {get;private set;}
        public CelestialTarget Target {get;private set;}
        public readonly CatalogueFilter Filter=new CatalogueFilter();
        public readonly List<CelestialTarget> Results=new List<CelestialTarget>();
        public int MatchedCount {get;private set;}
        public int CatalogueCount=>catalogue.Count;
        public string BookmarkPathOverride;
        public string BookmarkPath=>string.IsNullOrEmpty(BookmarkPathOverride)?Path.Combine(Application.persistentDataPath,"bookmarks-v2.json"):BookmarkPathOverride;
        public IReadOnlyList<ViewBookmark> Bookmarks=>bookmarks;
        public bool CapturesInput=>app!=null&&app.ShowHud&&(SearchOpen||BookmarksOpen||ShowDetails);
        readonly CelestialDetailsPage detailsPage=new CelestialDetailsPage();
        public CelestialDetailsPage DetailsPage=>detailsPage;
        public bool PointerOverUI
        {
            get
            {
                if(app==null||!app.ShowHud||Mouse.current==null)return false;
                Vector2 p=Mouse.current.position.ReadValue();p=new Vector2(p.x*1600/Screen.width,(Screen.height-p.y)*900/Screen.height);
                return (ShowTargetControls&&new Rect(330,96,900,90).Contains(p))||((SearchOpen||BookmarksOpen||ShowDetails)&&new Rect(330,198,900,600).Contains(p));
            }
        }
        [Serializable] public sealed class ViewBookmark {public string Name,State,Utc;}
        [Serializable] sealed class BookmarkFile {public int Version=1;public List<ViewBookmark> Items=new List<ViewBookmark>();}
        UniverseApp app;OrbitOverlay orbits;Material orbitTemplate;
        readonly List<CelestialTarget> catalogue=new List<CelestialTarget>();readonly List<ViewBookmark> bookmarks=new List<ViewBookmark>();
        int catalogueSeed;long catalogueX,catalogueY,catalogueZ;StarSystemDefinition orbitSystem;
        GUIStyle label,small,button,field,toggle;Vector2 searchScroll,bookmarkScroll;string addressText="",bookmarkName="";
        public OrbitOverlay Orbits=>orbits;
        bool ShowTargetControls=>Target!=null&&!app.GalaxyOverview&&!app.NucleusView;
        readonly List<Rect> occupiedLabels=new List<Rect>();
        void Awake()
        {
            app=GetComponent<UniverseApp>();
            orbits=new GameObject("Physical orbital paths").AddComponent<OrbitOverlay>();
            orbitTemplate=app.Settings.OrbitMaterial;
            catalogueSeed=app.Settings.UniverseSeed;
            if(app.System!=null)SelectCurrentBody(app.SelectedPlanet);
            LoadBookmarks();
        }
        public void SelectCurrentBody(int index)
        {
            if(app.System==null||index<0||index>=app.System.Planets.Length)return;
            Select(new CelestialTarget{X=app.CellX,Y=app.CellY,Z=app.CellZ,System=app.System,BodyIndex=index});
        }
        public void Select(CelestialTarget target)
        {
            if(target==null||target.System==null)return;
            IsFlyingToTarget=false;Target=target;
            app.Notice="已选中 "+Target.Name+"；C 居中，G 连续飞往，F 跳转观察";
        }
        public void ToggleSearch(){SearchOpen=!SearchOpen;BookmarksOpen=false;ShowDetails=false;if(SearchOpen){app.ShowHud=true;GetComponent<ExplorerHud>()?.CloseTransientPanels();RefreshCatalogue();}}
        public void ToggleBookmarks(){BookmarksOpen=!BookmarksOpen;SearchOpen=false;ShowDetails=false;app.ShowHud=true;GetComponent<ExplorerHud>()?.CloseTransientPanels();}
        public void ToggleDetails(){ShowDetails=!ShowDetails;SearchOpen=BookmarksOpen=false;if(ShowDetails){app.ShowHud=true;GetComponent<ExplorerHud>()?.CloseTransientPanels();}}
        public void ToggleOrbits(){ShowOrbits=!ShowOrbits;app.Notice=ShowOrbits?"已显示真实轨道；V 可查看恒星系总览":"轨道已隐藏";}
        public void ToggleLabels(){ShowLabels=!ShowLabels;}
        public void ToggleTracking(){TrackingTarget=!TrackingTarget;if(TrackingTarget)CenterOnTarget();}
        public void ClosePanels(){SearchOpen=false;BookmarksOpen=false;ShowDetails=false;GUI.FocusControl(null);}
        public void StopTargetFlight(){IsFlyingToTarget=false;}
        public void CenterOnTarget()
        {
            if(Target==null)return;Double3 delta=Target.Position(app.SimulationTime).RelativeTo(app.Observer);
            if(delta.Length>1)app.SetObserver(app.Observer,Quaternion.LookRotation(delta.Normalized.ToVector3(),Vector3.up));
        }
        public void FrameTarget()
        {
            if(Target==null)return;var selected=Target;
            app.LoadSystem(selected.X,selected.Y,selected.Z);
            if(selected.IsStar)app.FrameStar();else app.FramePlanet(selected.BodyIndex,Math.Max(app.Settings.InitialAltitude,selected.Radius*2));
            Target=selected;app.Notice="已跳转观察 / "+selected.Name;
        }
        public void FrameSystem(){app.FrameSystem();ShowOrbits=true;}
        public void StartTargetFlight()
        {
            if(Target==null){SelectCurrentBody(app.SelectedPlanet);}
            app.StopNavigation();IsFlyingToTarget=true;TrackingTarget=true;ClosePanels();
            app.Notice="连续飞往 "+Target.Name+"；Esc 取消";
        }
        public void TickMotion(double dt)
        {
            if(Target==null)return;
            if(IsFlyingToTarget)
            {
                UniversalPosition destination=Target.Position(app.SimulationTime);
                Double3 delta=destination.RelativeTo(app.Observer);double distance=delta.Length;
                if(!Target.IsStar&&distance<Target.Radius*50)
                {
                    var retained=app.Observer;var rotation=app.ViewCamera.transform.rotation;
                    if(app.CellX!=Target.X||app.CellY!=Target.Y||app.CellZ!=Target.Z)
                    {app.LoadSystem(Target.X,Target.Y,Target.Z);app.SetObserver(retained,rotation);}
                    app.SelectReferenceBody(Target.BodyIndex);IsFlyingToTarget=false;TrackingTarget=false;app.BeginDescent();
                }
                else
                {
                    double stop=Target.Radius*(Target.IsStar?6:25),remaining=Math.Max(0,distance-stop);
                    double step=Math.Min(remaining,Math.Min(PhysicalConstants.LightYear*app.Settings.MaximumFlightSpeedLyPerSecond,Math.Max(1000,remaining*.7))*dt);
                    Quaternion rotation=distance>1?Quaternion.LookRotation(delta.Normalized.ToVector3(),Vector3.up):app.ViewCamera.transform.rotation;
                    app.MoveContinuously(app.Observer.Add(delta.Normalized*step),rotation,dt>0?step/dt:0,"连续飞往 / "+Target.Name);
                    if(remaining<Math.Max(1,stop*1e-8)){IsFlyingToTarget=false;app.StopNavigation();app.Notice="已抵达恒星观察位置";}
                }
            }
            if(TrackingTarget&&!app.RadialNavigationActive&&!IsFlyingToTarget)CenterOnTarget();
        }
        public bool OrbitTarget(Vector2 mouseDelta)
        {
            if(Target==null)return false;
            var centre=Target.Position(app.SimulationTime);Double3 offset=app.Observer.RelativeTo(centre);
            if(offset.Length<Target.Radius*1.05)return false;
            Quaternion turn=Quaternion.AngleAxis(mouseDelta.x*.15f,Vector3.up)*Quaternion.AngleAxis(-mouseDelta.y*.15f,app.ViewCamera.transform.right);
            Double3 rotated=Double3.FromVector3(turn*offset.Normalized.ToVector3()).Normalized*offset.Length;
            app.StopNavigation();IsFlyingToTarget=false;TrackingTarget=false;
            app.SetObserver(centre.Add(rotated),Quaternion.LookRotation((-rotated).Normalized.ToVector3(),Vector3.up));return true;
        }
        public bool PickAtScreen(Vector2 pixel)
        {
            Ray ray=app.ViewCamera.ScreenPointToRay(pixel);Double3 direction=Double3.FromVector3(ray.direction).Normalized;
            CelestialTarget best=null;double nearest=double.MaxValue;
            for(int body=-1;body<app.System.Planets.Length;body++)
            {
                var target=new CelestialTarget{X=app.CellX,Y=app.CellY,Z=app.CellZ,System=app.System,BodyIndex=body};
                Double3 delta=target.Position(app.SimulationTime).RelativeTo(app.Observer);double projected=Double3.Dot(delta,direction);
                double discriminant=target.Radius*target.Radius-(delta.SqrLength-projected*projected);
                if(discriminant<0||projected<=0)continue;
                double hit=projected-Math.Sqrt(discriminant);if(hit>=0&&hit<nearest){nearest=hit;best=target;}
            }
            if(best==null)
            {
                double maxAngle=Math.Cos(.012);
                foreach(var entry in app.Stars.Entries)
                {
                    double angle=Double3.Dot(entry.System.Position.RelativeTo(app.Observer).Normalized,direction);
                    if(angle>maxAngle){maxAngle=angle;best=new CelestialTarget{X=entry.X,Y=entry.Y,Z=entry.Z,System=entry.System};}
                }
            }
            if(best==null)return false;Select(best);return true;
        }
        public void RefreshCatalogue()
        {
            catalogue.Clear();var seen=new HashSet<string>();
            Action<long,long,long,StarSystemDefinition> add=(x,y,z,system)=>
            {
                if(!system.IsOccupied||!seen.Add(x+"/"+y+"/"+z))return;
                for(int body=-1;body<system.Planets.Length;body++)catalogue.Add(new CelestialTarget{X=x,Y=y,Z=z,System=system,BodyIndex=body});
            };
            add(app.CellX,app.CellY,app.CellZ,app.System);
            foreach(var cell in app.Stars.Entries)add(cell.X,cell.Y,cell.Z,cell.System);
            catalogueSeed=app.Settings.UniverseSeed;catalogueX=app.Stars.CenterX;catalogueY=app.Stars.CenterY;catalogueZ=app.Stars.CenterZ;RefreshResults();
        }
        public void RefreshResults()
        {
            Results.Clear();foreach(var target in catalogue)if(Filter.Matches(target,app.Observer,app.SimulationTime))Results.Add(target);
            Results.Sort((a,b)=>a.Position(app.SimulationTime).RelativeTo(app.Observer).SqrLength.CompareTo(b.Position(app.SimulationTime).RelativeTo(app.Observer).SqrLength));
            MatchedCount=Results.Count;if(Results.Count>256)Results.RemoveRange(256,Results.Count-256);searchScroll=Vector2.zero;
        }
        public bool AddAddress(long x,long y,long z)
        {
            try
            {
            if(!UniverseGenerator.IsCellOccupied(app.Settings.UniverseSeed,x,y,z,app.Settings.Galaxy)){app.Notice="该单元没有已解析恒星系";return false;}
            var system=UniverseGenerator.GenerateSystem(app.Settings.UniverseSeed,x,y,z,app.Settings.Galaxy,app.Settings.WorldGenerationVersion);
            string key=x+"/"+y+"/"+z+":";
            catalogue.RemoveAll(t=>t.Id.StartsWith(key,StringComparison.Ordinal));
            for(int body=-1;body<system.Planets.Length;body++)catalogue.Add(new CelestialTarget{X=x,Y=y,Z=z,System=system,BodyIndex=body});
            RefreshResults();return true;
            }
            catch(OverflowException){app.Notice="单元坐标超出高精度坐标支持范围";return false;}
            catch(ArgumentOutOfRangeException){app.Notice="无效的单元坐标或银河参数";return false;}
        }
        public void AddBookmark(string name=null)
        {
            if(bookmarks.Count>=64){app.Notice="书签已达 64 个，请先删除不需要的位置";return;}
            bookmarks.Add(new ViewBookmark{Name=string.IsNullOrWhiteSpace(name)?app.System.Name+" / "+app.Planet.Name:name.Trim(),State=app.CaptureFlightState(),Utc=DateTime.UtcNow.ToString("O")});
            SaveBookmarks();app.Notice="已保存当前位置书签";
        }
        public bool RestoreBookmark(int index)
        {
            if(index<0||index>=bookmarks.Count)return false;
            IsFlyingToTarget=false;TrackingTarget=false;
            bool restored=app.RestoreFlightState(bookmarks[index].State);
            if(restored){SelectCurrentBody(app.SelectedPlanet);ClosePanels();app.Notice="已恢复书签位置（快捷跳转）";}
            return restored;
        }
        public IDisposable UseTemporaryBookmarkStore(string path)
        {
            string previous=BookmarkPathOverride;var retained=new List<ViewBookmark>(bookmarks);
            bookmarks.Clear();BookmarkPathOverride=path;LoadBookmarks();
            return new RestoreScope(()=>{bookmarks.Clear();bookmarks.AddRange(retained);BookmarkPathOverride=previous;});
        }
        sealed class RestoreScope:IDisposable
        {Action restore;public RestoreScope(Action action){restore=action;}public void Dispose(){restore?.Invoke();restore=null;}}
        public void RemoveBookmark(int index){if(index>=0&&index<bookmarks.Count){bookmarks.RemoveAt(index);SaveBookmarks();}}
        void SaveBookmarks()
        {
            string path=BookmarkPath;Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path+".tmp",JsonUtility.ToJson(new BookmarkFile{Items=bookmarks},true));
            if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);
        }
        void LoadBookmarks()
        {
            if(!File.Exists(BookmarkPath))return;
            try
            {
                var file=JsonUtility.FromJson<BookmarkFile>(File.ReadAllText(BookmarkPath));
                if(file==null||file.Version!=1||file.Items==null||file.Items.Count>64)throw new InvalidDataException();
                foreach(var item in file.Items)if(item!=null&&!string.IsNullOrEmpty(item.State)&&item.State.Length<65536)bookmarks.Add(item);
            }
            catch(Exception){app.Notice="书签文件无法读取，原文件已保留";}
        }
        public string CaptureScreenshot()
        {
            string folder=Path.Combine(Application.persistentDataPath,"Screenshots");Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"宇宙-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".png");ScreenCapture.CaptureScreenshot(path);
            app.Notice="截图已保存："+path;return path;
        }
        void LateUpdate()
        {
            if(app.System==null)return;
            if(catalogueSeed!=app.Settings.UniverseSeed)
            {catalogueSeed=app.Settings.UniverseSeed;IsFlyingToTarget=false;TrackingTarget=false;SelectCurrentBody(app.SelectedPlanet);if(SearchOpen)RefreshCatalogue();}
            if(orbitSystem!=app.System){orbitSystem=app.System;orbits.Configure(app.System,orbitTemplate);}
            orbits.ShowOrbits=ShowOrbits;orbits.SelectedBody=Target!=null&&Target.X==app.CellX&&Target.Y==app.CellY&&Target.Z==app.CellZ?Target.BodyIndex:-1;
            orbits.Tick(app.Observer,app.SimulationTime,app.RenderScale,app.ViewCamera);
            if(SearchOpen&&(catalogueX!=app.Stars.CenterX||catalogueY!=app.Stars.CenterY||catalogueZ!=app.Stars.CenterZ))RefreshCatalogue();
        }
        void Styles()
        {
            if(label!=null)return;label=new GUIStyle(GUI.skin.label){font=ChineseText.Font,fontSize=15};label.normal.textColor=new Color(.76f,.86f,.9f);
            small=new GUIStyle(label){fontSize=12};button=new GUIStyle(GUI.skin.button){font=ChineseText.Font,fontSize=14,alignment=TextAnchor.MiddleCenter,padding=new RectOffset(8,8,3,3)};
            button.normal.background=button.hover.background=button.active.background=Texture2D.whiteTexture;
            button.normal.textColor=label.normal.textColor;button.hover.textColor=Color.cyan;button.active.textColor=Color.white;
            field=new GUIStyle(GUI.skin.textField){font=ChineseText.Font,fontSize=15,padding=new RectOffset(8,8,3,3)};
            toggle=new GUIStyle(GUI.skin.toggle){font=ChineseText.Font,fontSize=15};toggle.normal.textColor=label.normal.textColor;toggle.onNormal.textColor=Color.cyan;
        }
        static void Panel(Rect rect){Color c=GUI.color;GUI.color=new Color(.025f,.055f,.075f,.96f);GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=c;}
        bool Button(float x,float y,float w,string text)
        {Color previous=GUI.backgroundColor;GUI.backgroundColor=new Color(.07f,.14f,.19f,.98f);bool clicked=GUI.Button(new Rect(x,y,w,29),text,button);GUI.backgroundColor=previous;return clicked;}
        void Text(float x,float y,float w,string text,bool tiny=false)=>GUI.Label(new Rect(x,y,w,29),text,tiny?small:label);
        void OnGUI()
        {
            if(app==null||!app.ShowHud)return;Styles();GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
            if(ShowLabels)DrawLabels();
            if(ShowTargetControls)
            {
                Panel(new Rect(330,96,900,90));Text(345,104,850,"目标："+Target.Name+"  /  "+(Target.IsStar?CatalogueFilter.StellarName(Target.System.Kind):CatalogueFilter.PlanetName(Target.Body.Kind))+"  /  距离 "+ExplorerHud.Distance(Target.Position(app.SimulationTime).RelativeTo(app.Observer).Length));
                if(Button(345,143,105,"居中 C"))CenterOnTarget();if(Button(460,143,140,"连续飞往 G"))StartTargetFlight();if(Button(610,143,130,"跳转观察 F"))FrameTarget();
                if(Button(750,143,130,TrackingTarget?"停止跟踪 Y":"跟踪目标 Y"))ToggleTracking();if(Button(890,143,125,ShowDetails?"关闭详情 F2":"天体详情 F2"))ToggleDetails();if(Button(1025,143,185,"恒星系总览 V"))FrameSystem();
            }
            if(SearchOpen)DrawSearch();else if(BookmarksOpen)DrawBookmarks();else if(ShowDetails&&Target!=null)DrawDetails();
        }
        void DrawLabels()
        {
            if(app.GalaxyOverview||app.NucleusView||SearchOpen||BookmarksOpen||ShowDetails||app.System.Position.RelativeTo(app.Observer).Length>PhysicalConstants.LightYear*.3)return;
            occupiedLabels.Clear();
            int selected=Target!=null&&Target.X==app.CellX&&Target.Y==app.CellY&&Target.Z==app.CellZ?Target.BodyIndex:-2;
            if(selected>=-1)DrawBodyLabel(selected);
            for(int i=-1;i<app.System.Planets.Length;i++)if(i!=selected)DrawBodyLabel(i);
        }
        void DrawBodyLabel(int i)
        {
            UniversalPosition pos=i<0?app.System.Position:app.PlanetPosition(i);
            if(IsLabelOccluded(i,pos))return;
            Vector3 point=app.ViewCamera.WorldToScreenPoint(OrbitOverlay.Project(pos,app.Observer,app.RenderScale));
            if(point.z<=0)return;float x=point.x*1600/Screen.width,y=(Screen.height-point.y)*900/Screen.height;
            if(x<330||x>1220||y<205||y>786)return;
            string text=(i<0?app.System.Name:app.System.Planets[i].Name)+(i>=0?" · "+CatalogueFilter.PlanetName(app.System.Planets[i].Kind):"");
            float width=Math.Min(300,small.CalcSize(new GUIContent(text)).x+14);
            for(int attempt=0;attempt<13;attempt++)
            {
                float offset=attempt==0?0:((attempt+1)/2)*24*(attempt%2==1?1:-1);
                var rect=new Rect(Math.Min(x+13,1228-width),y-10+offset,width,22);
                if(rect.y<200||rect.yMax>796)continue;
                bool overlaps=false;foreach(var used in occupiedLabels)if(used.Overlaps(rect)){overlaps=true;break;}if(overlaps)continue;
                occupiedLabels.Add(new Rect(rect.x-3,rect.y-2,rect.width+6,rect.height+4));
                Color color=GUI.color;GUI.color=new Color(.32f,.65f,.75f,.65f);
                var from=new Vector2(x,y);var end=new Vector2(rect.x+3,rect.center.y);var delta=end-from;var matrix=GUI.matrix;
                GUI.matrix=matrix*Matrix4x4.TRS(new Vector3(x,y,0),Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg),Vector3.one);
                GUI.DrawTexture(new Rect(0,0,delta.magnitude,1),Texture2D.whiteTexture);GUI.matrix=matrix;
                GUI.color=color;Panel(rect);GUI.Label(new Rect(rect.x+6,rect.y+2,rect.width-10,20),text,small);return;
            }
        }
        bool IsLabelOccluded(int body,UniversalPosition position)
        {
            Double3 delta=position.RelativeTo(app.Observer);double distance=delta.Length;if(distance<=0)return true;
            Double3 direction=delta/distance;double targetRadius=body<0?app.System.StarRadius:app.System.Planets[body].Radius;
            for(int other=-1;other<app.System.Planets.Length;other++)
            {
                if(other==body)continue;
                Double3 centre=(other<0?app.System.Position:app.PlanetPosition(other)).RelativeTo(app.Observer);
                double along=Double3.Dot(centre,direction);if(along<=0||along>=distance)continue;
                double radius=other<0?app.System.StarRadius:app.System.Planets[other].Radius;
                double discriminant=radius*radius-(centre.SqrLength-along*along);
                if(discriminant>0&&along-Math.Sqrt(discriminant)<distance-targetRadius)return true;
            }
            return false;
        }
        void DrawSearch()
        {
            Panel(new Rect(330,198,900,600));Text(348,209,850,"天体搜索 / 本恒星系与已解析邻域（按距离排序，最多显示 256 项）");
            GUI.SetNextControlName("celestial-query");string query=GUI.TextField(new Rect(348,248,665,30),Filter.Query,field);
            if(query!=Filter.Query){Filter.Query=query;RefreshResults();}if(Button(1025,248,185,"刷新邻域目录"))RefreshCatalogue();
            string[] kinds={"全部","岩石","海洋","沙漠","冰冻","熔岩","气巨星","冰巨星"};
            for(int i=0;i<kinds.Length;i++)if(Button(348+i*108,289,100,(Filter.PlanetKind==i-1?"● ":"")+kinds[i])){Filter.PlanetKind=i-1;RefreshResults();}
            bool land=GUI.Toggle(new Rect(348,332,170,26),Filter.LandableOnly,"只看可降落",toggle);
            bool ocean=GUI.Toggle(new Rect(530,332,150,26),Filter.OceanOnly,"具有海洋",toggle);
            bool atmosphere=GUI.Toggle(new Rect(698,332,150,26),Filter.AtmosphereOnly,"具有大气",toggle);
            bool moons=GUI.Toggle(new Rect(866,332,150,26),Filter.MoonsOnly,"只看卫星",toggle);
            if(land!=Filter.LandableOnly||ocean!=Filter.OceanOnly||atmosphere!=Filter.AtmosphereOnly||moons!=Filter.MoonsOnly)
            {Filter.LandableOnly=land;Filter.OceanOnly=ocean;Filter.AtmosphereOnly=atmosphere;Filter.MoonsOnly=moons;RefreshResults();}
            Text(348,365,640,"目录 "+CatalogueCount+" 个天体 · 符合 "+MatchedCount+" 项 · 地址仅代表已生成目录",true);
            if(Button(1018,328,192,double.IsInfinity(Filter.MaximumDistanceLy)?"距离：不限":"距离："+Filter.MaximumDistanceLy.ToString("F0")+" 光年"))
            {Filter.MaximumDistanceLy=Filter.MaximumDistanceLy==10?50:Filter.MaximumDistanceLy==50?100:Filter.MaximumDistanceLy==100?double.PositiveInfinity:10;RefreshResults();}
            searchScroll=GUI.BeginScrollView(new Rect(348,398,864,302),searchScroll,new Rect(0,0,838,Math.Max(300,Results.Count*34)));
            int first=Math.Max(0,(int)(searchScroll.y/34)),last=Math.Min(Results.Count,first+11);
            for(int i=first;i<last;i++)
            {
                var target=Results[i];float y=i*34;
                if(Button(2,y,520,target.Name+" / "+(target.IsStar?CatalogueFilter.StellarName(target.System.Kind):CatalogueFilter.PlanetName(target.Body.Kind)))){Select(target);SearchOpen=false;GUI.FocusControl(null);}
                Text(535,y+3,295,ExplorerHud.Distance(target.Position(app.SimulationTime).RelativeTo(app.Observer).Length),true);
            }
            GUI.EndScrollView();Text(348,710,500,"按单元地址添加（x,y,z）：",true);
            addressText=GUI.TextField(new Rect(348,744,500,29),addressText,field);
            if(Button(860,744,175,"生成并加入目录"))
            {
                string[] xyz=addressText.Split(',','，');
                if(xyz.Length==3&&long.TryParse(xyz[0],out long x)&&long.TryParse(xyz[1],out long y)&&long.TryParse(xyz[2],out long z))AddAddress(x,y,z);else app.Notice="请输入三个整数单元坐标，例如 0,0,0";
            }
            if(Button(1045,744,165,"关闭 Esc"))ClosePanels();
        }
        void DrawBookmarks()
        {
            Panel(new Rect(330,198,900,600));Text(348,215,850,"位置书签 / 保存完整高精度位置、朝向、时间与宇宙参数");
            Text(348,250,170,"书签名称（可选）",true);bookmarkName=GUI.TextField(new Rect(520,250,448,30),bookmarkName,field);if(Button(982,250,230,"保存当前位置")){AddBookmark(bookmarkName);bookmarkName="";}
            bookmarkScroll=GUI.BeginScrollView(new Rect(348,306,864,425),bookmarkScroll,new Rect(0,0,838,Math.Max(420,bookmarks.Count*40)));
            for(int i=0;i<bookmarks.Count;i++){Text(4,i*40+4,530,bookmarks[i].Name);if(Button(550,i*40,155,"恢复位置"))RestoreBookmark(i);if(Button(715,i*40,100,"删除")){RemoveBookmark(i);break;}}
            GUI.EndScrollView();Text(348,751,665,"恢复书签属于显式位置跳转，不是连续飞行。",true);if(Button(1035,744,175,"关闭 Esc"))ClosePanels();
        }
        void DrawDetails()
        {
            detailsPage.Draw(new Rect(330,198,900,600),Target,app.SimulationTime,
                Select,ClosePanels,()=>{ShowDetails=false;ToggleSearch();});
        }
        void OnDestroy(){if(orbits!=null)Destroy(orbits.gameObject);}
    }
}
