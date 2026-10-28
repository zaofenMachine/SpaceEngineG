using System;
using System.IO;
using System.Reflection;
using SpaceEngine.Runtime;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceEngine.Editor
{
    public static class ExplorerBuild
    {
        public const string ScenePath="Assets/Scenes/Universe.unity";
        [MenuItem("Space Engine/Setup Explorer Scene")]
        public static void Setup()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode before setup.");
            Directory.CreateDirectory("Assets/SpaceEngine/Resources");
            var settings=AssetDatabase.LoadAssetAtPath<UniverseSettings>("Assets/SpaceEngine/Resources/UniverseSettings.asset");
            if(settings==null){settings=ScriptableObject.CreateInstance<UniverseSettings>();AssetDatabase.CreateAsset(settings,"Assets/SpaceEngine/Resources/UniverseSettings.asset");}
            settings.TerrainMaterial=CreateMaterial("Terrain","SpaceEngine/Terrain");
            settings.StarMaterial=CreateMaterial("Star","SpaceEngine/Star");
            settings.StarBillboardMaterial=CreateMaterial("StarBillboard","SpaceEngine/StarBillboard");
            settings.AtmosphereMaterial=CreateMaterial("Atmosphere","SpaceEngine/Atmosphere");
            settings.GalaxyMaterial=CreateMaterial("GalaxyField","SpaceEngine/GalaxyField");
            settings.GasGiantMaterial=CreateMaterial("GasGiant","SpaceEngine/GasGiant");
            settings.RingMaterial=CreateMaterial("PlanetRing","SpaceEngine/PlanetRing");
            settings.GalaxyVolumeMaterial=CreateMaterial("GalaxyVolume","SpaceEngine/GalaxyVolume");
            settings.GalaxyVolumeCompositeMaterial=CreateMaterial("GalaxyVolumeComposite","SpaceEngine/GalaxyVolumeComposite");
            settings.PlanetCloudMaterial=CreateMaterial("PlanetCloud","SpaceEngine/PlanetCloud");
            settings.OrbitMaterial=CreateMaterial("OrbitOverlay","SpaceEngine/OrbitOverlay");
            EditorUtility.SetDirty(settings);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Universe simulation").AddComponent<UniverseApp>().Settings=settings;
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.companyName="SpaceEngineG";PlayerSettings.productName="SpaceEngineG Explorer";
            PlayerSettings.bundleVersion="0.5.0";
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.runInBackground=true;
            AssetDatabase.SaveAssets();
            Debug.Log("Explorer scene and resources created at "+ScenePath);
        }
        static Material CreateMaterial(string name,string shaderName)
        {
            string path="Assets/SpaceEngine/Resources/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){var shader=Shader.Find(shaderName);if(shader==null)throw new InvalidOperationException("Missing shader "+shaderName);material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
            return material;
        }
        [MenuItem("Space Engine/Run Rendered Flight Acceptance")]
        public static void Verify()
        {
            if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            SessionState.SetBool("SpaceEngine.VerifyFlight",true);
            EditorApplication.isPlaying=true;
        }
        [MenuItem("Space Engine/Use Reliable PlayMode Reload")]
        public static void EnableDomainReload()
        {
            EditorSettings.enterPlayModeOptionsEnabled=false;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.None;
        }
        [MenuItem("Space Engine/Run Galaxy And Body Acceptance")]
        public static void VerifyGalaxy()
        {
            if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
            EditorSceneManager.OpenScene(ScenePath);EditorApplication.ExecuteMenuItem("Window/General/Game");
            SessionState.SetBool("SpaceEngine.VerifyGalaxy",true);EditorApplication.isPlaying=true;
        }
        [MenuItem("Space Engine/Run Explorer Feature Acceptance")]
        public static void VerifyExplorer()
        {
            if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
            EditorSceneManager.OpenScene(ScenePath);EditorApplication.ExecuteMenuItem("Window/General/Game");
            SessionState.SetBool("SpaceEngine.VerifyExplorer",true);EditorApplication.isPlaying=true;
        }
        [MenuItem("Space Engine/Build Windows Explorer")]
        public static void BuildWindows()
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            string output=Path.Combine(root,"Builds/Windows-v0.5.0/SpaceEngineG.exe");Directory.CreateDirectory(Path.GetDirectoryName(output));
            // Unity's engine diagnostics is separate from the legacy cloud diagnostics toggle.
            // Preserve this project's explicit disabled services in the actual Player configuration.
            UnityEditor.EngineDiagnostics.EngineDiagnosticsSettings.enabled=false;
            UnityEditor.CrashReporting.CrashReportingSettings.enabled=false;
            var connect=Type.GetType("UnityEngine.Connect.UnityConnectSettings, UnityEngine.UnityConnectModule",true)
                .GetProperty("enabled",BindingFlags.Public|BindingFlags.Static);
            if(connect==null||!connect.CanWrite)throw new InvalidOperationException("Cannot enforce the project's Unity Connect build setting.");
            connect.SetValue(null,false);
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            Directory.CreateDirectory(Path.Combine(root,"Artifacts/V050"));
            File.WriteAllText(Path.Combine(root,"Artifacts/V050/build.txt"),$"Result: {result.summary.result}\nBytes: {result.summary.totalSize}\nSeconds: {result.summary.totalTime.TotalSeconds:F2}\nErrors: {result.summary.totalErrors}\nWarnings: {result.summary.totalWarnings}\nOutput: Builds/Windows-v0.5.0/SpaceEngineG.exe\n");
            if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Player build failed");
            if((bool)connect.GetValue(null)||UnityEditor.EngineDiagnostics.EngineDiagnosticsSettings.enabled||UnityEditor.CrashReporting.CrashReportingSettings.enabled)
                throw new InvalidOperationException("Unity unexpectedly enabled project services during the Player build; inspect the actual Bee Services configuration.");
            string notices=Path.Combine(Path.GetDirectoryName(output),"ThirdPartyNotices");Directory.CreateDirectory(notices);
            foreach(string name in new[]{"OFL-NotoSansCJK.txt","NOTICE-NotoSansCJK.txt"})
                File.Copy(Path.Combine(Application.dataPath,"SpaceEngine/Resources/Fonts",name),Path.Combine(notices,name),true);
        }
    }
}
