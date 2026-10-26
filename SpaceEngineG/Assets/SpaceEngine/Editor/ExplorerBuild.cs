using System;
using System.IO;
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
            EditorUtility.SetDirty(settings);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Universe simulation").AddComponent<UniverseApp>().Settings=settings;
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.companyName="SpaceEngineG";PlayerSettings.productName="SpaceEngineG Explorer";
            PlayerSettings.bundleVersion="0.1.0";
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
        [MenuItem("Space Engine/Build Windows Explorer")]
        public static void BuildWindows()
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            string output=Path.Combine(root,"Builds/Windows/SpaceEngineG.exe");Directory.CreateDirectory(Path.GetDirectoryName(output));
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            Directory.CreateDirectory(Path.Combine(root,"Artifacts/Latest"));
            File.WriteAllText(Path.Combine(root,"Artifacts/Latest/build.txt"),$"Result: {result.summary.result}\nBytes: {result.summary.totalSize}\nSeconds: {result.summary.totalTime.TotalSeconds:F2}\nErrors: {result.summary.totalErrors}\nWarnings: {result.summary.totalWarnings}\nOutput: Builds/Windows/SpaceEngineG.exe\n");
            if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Player build failed");
        }
    }
}
