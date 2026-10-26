using UnityEngine;
using SpaceEngine.Rendering;
using SpaceEngine.Core;

namespace SpaceEngine.Runtime
{
    [CreateAssetMenu(menuName = "Space Engine/Universe Settings")]
    public sealed class UniverseSettings : ScriptableObject
    {
        public int UniverseSeed = 20261026;
        public GalaxyDefinition Galaxy = new GalaxyDefinition();
        [Range(10000,150000)] public int GalaxyParticleCount = 60000;
        [Range(0,8)] public float GalaxyExposure = .15f;
        [Range(.2f,5)] public float GalaxyKernelScale = .5f;
        [Range(0,8)] public float GalaxyVolumeExposure = 1;
        [Range(.2f,1)] public float GalaxyResolutionScale = .5f;
        [Range(32,192)] public int GalaxyVolumeSamples = 96;
        [Range(.01f,2)] public float GalaxyInteriorExposure = .22f;
        [Range(.1f,4)] public float GalaxyExteriorExposure = 1.6f;
        [Range(1,3)] public float GalaxyContrast = 2;
        [Range(0,5)] public float GalaxyDustStrength = 1;
        [Range(20,2000)] public float GalaxyDustScaleHeightLy = 220;
        [Range(100,10000)] public float GalaxyDustAttenuationLengthLy = 1200;
        [Range(20,500)] public float GalaxyLocalFadeLy = 100;
        public double MaximumFlightSpeedLyPerSecond = 50000;
        [Range(.1f,2)] public float GasBandContrast = .72f;
        [Range(0,1)] public float GasStormStrength = .85f;
        [Range(24,768)] public int GasLongitudeSegments = 384;
        [Range(12,384)] public int GasLatitudeSegments = 192;
        [Range(32,1024)] public int RingAngularSegments = 512;
        [Range(0,3)] public float RingOpticalDepth = .8f;
        public double GasCloudSafetyClearance = 10000;
        [Range(2, 9)] public int StarCellRadius = 5;
        [Range(4, 24)] public int TerrainResolution = 12;
        [Range(128,2048)] public int TerrainMaterialResolution = 1024;
        [Range(0,2)] public float TerrainMaterialDetail = .65f;
        [Range(.03f,.4f)] public float OceanRoughness = .12f;
        [Range(0,2)] public float OceanSpecularStrength = .8f;
        [Range(0,2)] public float SurfaceNormalStrength = .7f;
        [Range(0,1)] public float CloudCoverage = .42f;
        [Range(0,2)] public float CloudOpacity = .65f;
        [Range(1000,50000)] public float CloudHeightMeters = 12000;
        [Range(1,20)] public float CloudWeatherScale = 5;
        [Range(0,1)] public float CloudDetailStrength = .45f;
        [Range(-1,1)] public float CloudWindRate = .08f;
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
        [Range(0,8)] public float SunLightingExposure = 1.5f;
        [Range(.1f, 8)] public float AtmosphereExposure = 1.6f;
        public AtmosphereSettings Atmosphere = new AtmosphereSettings();
        [Range(.25f, 3)] public float StarBrightness = 1.2f;
        [Range(.0001f,.002f)] public float StarMinimumAngularRadius = .00065f;
        [Range(.05f,.6f)] public float StarCoreRadius = .30f;
        [Range(40, 100)] public float FieldOfView = 60;
        [Range(30, 90)] public float GalaxyOverviewFieldOfView = 45;
        public double InitialAltitude = 12000000;
        public double MinimumClearance = 2;
        public Material TerrainMaterial, AtmosphereMaterial, StarMaterial, StarBillboardMaterial;
        public Material GalaxyMaterial,GasGiantMaterial,RingMaterial;
        public Material GalaxyVolumeMaterial,GalaxyVolumeCompositeMaterial,PlanetCloudMaterial;
    }
}
