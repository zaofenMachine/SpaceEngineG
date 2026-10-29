using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpaceEngine.Editor
{
    public static class BatchValidation
    {
        [Serializable] sealed class Header {public bool passed;}
        public static void RunGraphics()
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Artifacts/V050"));Directory.CreateDirectory(root);
            string[,] checks={
                {"Validate Shaders","shaders.json"}, {"Validate Atmosphere Pixels","atmosphere-pixels.json"},
                {"Validate Celestial Pixels","CelestialPixels/celestial-pixels.json"}, {"Validate Terrain Pixels","TerrainPixels/terrain-pixels.json"},
                {"Validate Ground Terrain Pixels","GroundPixels/ground-pixels.json"}, {"Validate Ground Atmosphere Pixels","GroundAtmospherePixels/ground-pixels.json"},
                {"Validate Planet Cloud Pixels","CloudPixels/cloud-pixels.json"}, {"Validate Gas Horizon Pixels","GasHorizonPixels/gas-horizon-pixels.json"},
                {"Validate Galaxy Pixels","GalaxyPixels/galaxy-pixels.json"}, {"Validate Galaxy Clarity","GalaxyClarity/galaxy-clarity.json"},
                {"Validate Terrain LOD Pixels","TerrainLodPixels/terrain-lod-pixels.json"}, {"Validate Chinese Glyph Pixels","ChineseGlyphPixels/glyph-pixels.json"},
                {"Validate Terrain Loading Pixels","LoadingPixels/loading-pixels.json"}
            };
            for(int i=0;i<checks.GetLength(0);i++)
            {
                string path=Path.Combine(root,checks[i,1]);
                if(!EditorApplication.ExecuteMenuItem("Space Engine/"+checks[i,0]))throw new InvalidOperationException("Missing graphics check "+checks[i,0]);
                if(!File.Exists(path)||!JsonUtility.FromJson<Header>(File.ReadAllText(path)).passed)throw new InvalidOperationException("Graphics check failed: "+checks[i,1]);
                Debug.Log("V050_GRAPHICS_PASS "+checks[i,1]);
            }
        }
        /// <summary>Runs every graphics check without stopping at the first failure, for local iteration.</summary>
        public static void SurveyGraphics()
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Artifacts/V050"));Directory.CreateDirectory(root);
            string[] menus={"Validate Shaders","Validate Atmosphere Pixels","Validate Celestial Pixels","Validate Terrain Pixels","Validate Ground Terrain Pixels",
                "Validate Ground Atmosphere Pixels","Validate Planet Cloud Pixels","Validate Gas Horizon Pixels","Validate Galaxy Pixels","Validate Galaxy Clarity",
                "Validate Terrain LOD Pixels","Validate Chinese Glyph Pixels","Validate Terrain Loading Pixels"};
            foreach(string menu in menus)
            {
                try{Debug.Log("GRAPHICS_SURVEY "+menu+" "+(EditorApplication.ExecuteMenuItem("Space Engine/"+menu)?"ran":"missing"));}
                catch(Exception ex){Debug.Log("GRAPHICS_SURVEY "+menu+" threw "+ex.Message);}
            }
        }
    }
}
