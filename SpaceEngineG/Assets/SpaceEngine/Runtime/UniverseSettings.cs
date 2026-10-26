using UnityEngine;
using SpaceEngine.Rendering;

namespace SpaceEngine.Runtime
{
    [CreateAssetMenu(menuName = "Space Engine/Universe Settings")]
    public sealed class UniverseSettings : ScriptableObject
    {
        public int UniverseSeed = 20261026;
        [Range(2, 9)] public int StarCellRadius = 5;
        [Range(4, 24)] public int TerrainResolution = 12;
        [Range(8, 23)] public int TerrainMaxLevel = 20;
        [Range(48, 512)] public int TerrainPatchBudget = 256;
        [Range(1, 16)] public int MeshBuildsPerFrame = 6;
        [Range(1, 16)] public float MeshBuildBudgetMs = 5;
        [Range(.5f, 8)] public float TerrainRefinement = 2.5f;
        [Range(0,5)] public int OrbitalMinimumLod = 3;
        [Range(1,45)] public float OrbitalDetailMinimumDegrees = 5;
        [Range(.005f,.2f)] public float SkirtDepthFactor = .08f;
        [Range(.25f,100)] public float MinimumSkirtDepth = 4;
        [Range(.01f,2)] public float NormalSampleDistance = .25f;
        [Range(.01f, 1)] public float AmbientLight = .035f;
        [Range(.1f, 8)] public float AtmosphereExposure = 1.6f;
        public AtmosphereSettings Atmosphere = new AtmosphereSettings();
        [Range(.25f, 3)] public float StarBrightness = 1.2f;
        [Range(.0001f,.002f)] public float StarMinimumAngularRadius = .00065f;
        [Range(.05f,.6f)] public float StarCoreRadius = .30f;
        [Range(40, 100)] public float FieldOfView = 60;
        public double InitialAltitude = 12000000;
        public double MinimumClearance = 2;
        public Material TerrainMaterial, AtmosphereMaterial, StarMaterial, StarBillboardMaterial;
    }
}
