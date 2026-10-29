using System;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Observer-dependent integration of a finite galactic volume. The low-resolution target
    /// contains emitted radiance, never a painted sky or a projection tied to one observer.
    /// Camera rotation, position and optical controls invalidate its deterministic cache.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class GalaxyVolume : MonoBehaviour
    {
        public GalaxyDefinition Definition { get; private set; }
        [Range(0, 8)] public float Exposure = 1;
        [Range(.01f, 2)] public float InteriorExposureMultiplier = .8f;
        [Range(.1f, 4)] public float ExteriorExposureMultiplier = 1.6f;
        [Range(1, 3)] public float Contrast = 2;
        [Range(0, 5)] public float DustStrength = 1.35f;
        [Range(.2f, 1)] public float ResolutionScale = 1;
        [Range(32, 384)] public int SampleCount = 128;
        [Range(32, 512)] public int InteriorSampleCount = 224;
        [Range(0, 2)] public float DetailStrength = 1;
        [Range(80, 1500)] public float DetailScaleLy = 360;
        [Range(1000, 20000)] public float NearDetailDistanceLy = 8000;
        [Min(10)] public float DustScaleHeightLy = 220;
        [Min(1)] public float DustAttenuationLengthLy = 1200;
        public bool ShowVolume = true;
        public int RenderedWidth => radiance == null ? 0 : radiance.width;
        public int RenderedHeight => radiance == null ? 0 : radiance.height;
        public int RenderRevision { get; private set; }
        public RenderTexture RadianceTexture => radiance;
        /// <summary>When set, also integrate the whole sky (equirectangular, longitude in x) for off-screen lookups.</summary>
        public bool RenderSkyMap;
        public RenderTexture SkyMap => skyMap;
        public float LastSubmissionMilliseconds { get; private set; }
        public int EffectiveSampleCount { get; private set; }

        Material integration, composite;
        RenderTexture radiance, skyMap;
        Vector3 previousSkyObserver;
        int previousSkyParameters;
        Mesh mesh;
        MeshRenderer rendererComponent;
        Vector3 previousObserver;
        Quaternion previousRotation;
        float previousFov, previousAspect, previousExposure, previousDust, previousDustHeight, previousDustLength;
        float previousInteriorExposure, previousExteriorExposure, previousContrast;
        float previousDetailStrength, previousDetailScale, previousNearDistance;
        int previousSamples;
        int previousDefinitionHash;
        bool dirty;

        /// <summary>Explicitly refresh after caller-owned material edits, or for a non-cached profiling sample.</summary>
        public void Invalidate() { dirty = true; }

        public void Configure(GalaxyDefinition definition, Material source = null, Material compositeSource = null)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Release();
            Definition = definition;
            Shader shader = source == null ? Shader.Find("SpaceEngine/GalaxyVolume") : source.shader;
            if (shader == null) throw new InvalidOperationException("Galaxy volume shader is missing.");
            integration = source == null ? new Material(shader) : new Material(source);
            integration.name = "Galaxy volume integration instance";
            integration.SetTexture("_Turbulence", TurbulenceTexture);
            Shader compositeShader = compositeSource == null ? Shader.Find("SpaceEngine/GalaxyVolumeComposite") : compositeSource.shader;
            if (compositeShader == null) throw new InvalidOperationException("Galaxy volume composite shader is missing.");
            composite = compositeSource == null ? new Material(compositeShader) : new Material(compositeSource);
            composite.name = "Galaxy volume composite instance";
            mesh = new Mesh { name = "Galactic volume far-depth composite" };
            mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 32000000);
            var filter = GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            rendererComponent = GetComponent<MeshRenderer>();
            if (rendererComponent == null) rendererComponent = gameObject.AddComponent<MeshRenderer>();
            rendererComponent.sharedMaterial = composite;
            rendererComponent.shadowCastingMode = ShadowCastingMode.Off;
            rendererComponent.receiveShadows = false;
            rendererComponent.enabled = ShowVolume && isActiveAndEnabled;
            dirty = true;
        }

        public void Tick(UniversalPosition observer, Camera camera)
        {
            if (integration == null || Definition == null || camera == null) return;
            // A shader reimport (editor hot reload) drops textures that are not material properties.
            if (integration.GetTexture(TurbulenceId) == null) { integration.SetTexture(TurbulenceId, TurbulenceTexture); dirty = true; }
            rendererComponent.enabled = ShowVolume && isActiveAndEnabled;
            if (!rendererComponent.enabled) return;
            int width = Mathf.Max(64, Mathf.CeilToInt(camera.pixelWidth * Mathf.Clamp(ResolutionScale, .2f, 1)));
            int height = Mathf.Max(64, Mathf.CeilToInt(camera.pixelHeight * Mathf.Clamp(ResolutionScale, .2f, 1)));
            if (radiance == null || radiance.width != width || radiance.height != height)
            {
                ReleaseTexture();
                radiance = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Galactic emitted radiance", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, useMipMap = false
                };
                radiance.Create();
                composite.SetTexture("_MainTex", radiance);
                dirty = true;
            }
            Vector3 galacticObserver = GalaxyModel.ToGalacticLy(observer, Definition).ToVector3();
            Quaternion rotation = camera.transform.rotation;
            bool embedded = Math.Abs(galacticObserver.y) < Definition.ThickDiskHeightLy * 2 &&
                Math.Sqrt((double)galacticObserver.x * galacticObserver.x + (double)galacticObserver.z * galacticObserver.z) < Definition.RadiusLy * 1.1;
            int samples = Mathf.Clamp(embedded ? Math.Max(SampleCount, InteriorSampleCount) : SampleCount, 32, 512);
            EffectiveSampleCount = samples;
            int definitionHash = DefinitionFingerprint(Definition);
            if (RenderSkyMap) TickSkyMap(galacticObserver, samples, definitionHash);
            // Quaternion.operator== deliberately treats rotations within about 0.16 degrees
            // as equal. That is several display pixels and stalls a slowly moving sky.
            // Cache only an identical quaternion; true stationary views still reuse it.
            if (!dirty && galacticObserver == previousObserver && rotation.Equals(previousRotation) &&
                camera.fieldOfView == previousFov && camera.aspect == previousAspect &&
                Exposure == previousExposure && DustStrength == previousDust && samples == previousSamples &&
                InteriorExposureMultiplier == previousInteriorExposure && ExteriorExposureMultiplier == previousExteriorExposure &&
                Contrast == previousContrast &&
                DetailStrength == previousDetailStrength && DetailScaleLy == previousDetailScale && NearDetailDistanceLy == previousNearDistance &&
                DustScaleHeightLy == previousDustHeight && DustAttenuationLengthLy == previousDustLength &&
                definitionHash == previousDefinitionHash) return;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            SetDefinition(integration, Definition);
            integration.SetVector("_ObserverLy", galacticObserver);
            integration.SetVector("_ViewRight", rotation * Vector3.right);
            integration.SetVector("_ViewUp", rotation * Vector3.up);
            integration.SetVector("_ViewForward", rotation * Vector3.forward);
            integration.SetVector("_ViewShape", new Vector4(Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f), camera.aspect, width, height));
            integration.SetFloat("_Exposure", Mathf.Max(0, Exposure));
            integration.SetFloat("_InteriorExposure", Mathf.Clamp(InteriorExposureMultiplier, .01f, 2));
            integration.SetFloat("_ExteriorExposure", Mathf.Clamp(ExteriorExposureMultiplier, .1f, 4));
            integration.SetFloat("_Contrast", Mathf.Clamp(Contrast, 1, 3));
            integration.SetFloat("_DustStrength", Mathf.Max(0, DustStrength));
            integration.SetFloat("_DustScaleHeightLy", Mathf.Max(10, DustScaleHeightLy));
            integration.SetFloat("_DustAttenuationLengthLy", Mathf.Max(1, DustAttenuationLengthLy));
            integration.SetInt("_SampleCount", samples);
            integration.SetFloat("_DetailStrength", Mathf.Clamp(DetailStrength, 0, 2));
            integration.SetFloat("_DetailScaleLy", Mathf.Max(80, DetailScaleLy));
            integration.SetFloat("_NearDetailDistanceLy", Mathf.Max(1000, NearDetailDistanceLy));
            RenderTexture previousTarget = RenderTexture.active;
            Graphics.Blit(Texture2D.blackTexture, radiance, integration, 0);
            RenderTexture.active = previousTarget;
            LastSubmissionMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            RenderRevision++;
            previousObserver = galacticObserver; previousRotation = rotation;
            previousFov = camera.fieldOfView; previousAspect = camera.aspect;
            previousExposure = Exposure; previousDust = DustStrength; previousSamples = samples;
            previousInteriorExposure = InteriorExposureMultiplier; previousExteriorExposure = ExteriorExposureMultiplier;
            previousContrast = Contrast;
            previousDetailStrength = DetailStrength; previousDetailScale = DetailScaleLy; previousNearDistance = NearDetailDistanceLy;
            previousDustHeight = DustScaleHeightLy; previousDustLength = DustAttenuationLengthLy;
            previousDefinitionHash = definitionHash;
            dirty = false;
        }

        // The sky map depends on position only, so turning the camera never invalidates it.
        void TickSkyMap(Vector3 galacticObserver, int samples, int definitionHash)
        {
            int parameters = HashCode.Combine(definitionHash, samples, Exposure, DustStrength, Contrast,
                HashCode.Combine(InteriorExposureMultiplier, ExteriorExposureMultiplier, DetailStrength, DetailScaleLy, DustScaleHeightLy, DustAttenuationLengthLy));
            if (skyMap != null && galacticObserver == previousSkyObserver && parameters == previousSkyParameters) return;
            if (skyMap == null)
            {
                skyMap = new RenderTexture(512, 256, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                    { name = "Galactic emitted radiance (whole sky)", filterMode = FilterMode.Bilinear, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, useMipMap = false };
                skyMap.Create();
            }
            SetDefinition(integration, Definition);
            integration.SetVector("_ObserverLy", galacticObserver);
            integration.SetVector("_ViewShape", new Vector4(Mathf.PI * .5f, 2, skyMap.width, skyMap.height));
            integration.SetFloat("_Equirect", 1);
            integration.SetFloat("_Exposure", Mathf.Max(0, Exposure));
            integration.SetFloat("_InteriorExposure", Mathf.Clamp(InteriorExposureMultiplier, .01f, 2));
            integration.SetFloat("_ExteriorExposure", Mathf.Clamp(ExteriorExposureMultiplier, .1f, 4));
            integration.SetFloat("_Contrast", Mathf.Clamp(Contrast, 1, 3));
            integration.SetFloat("_DustStrength", Mathf.Max(0, DustStrength));
            integration.SetFloat("_DustScaleHeightLy", Mathf.Max(10, DustScaleHeightLy));
            integration.SetFloat("_DustAttenuationLengthLy", Mathf.Max(1, DustAttenuationLengthLy));
            integration.SetInt("_SampleCount", samples);
            integration.SetFloat("_DetailStrength", Mathf.Clamp(DetailStrength, 0, 2));
            integration.SetFloat("_DetailScaleLy", Mathf.Max(80, DetailScaleLy));
            integration.SetFloat("_NearDetailDistanceLy", Mathf.Max(1000, NearDetailDistanceLy));
            RenderTexture previousTarget = RenderTexture.active;
            Graphics.Blit(Texture2D.blackTexture, skyMap, integration, 0);
            RenderTexture.active = previousTarget;
            integration.SetFloat("_Equirect", 0);
            previousSkyObserver = galacticObserver; previousSkyParameters = parameters;
            // The perspective pass shares this material; force it to restore its own view next.
            dirty = true;
        }

        static Texture3D turbulence;
        static readonly int TurbulenceId = Shader.PropertyToID("_Turbulence");
        /// <summary>
        /// Two independent channels of tileable 3D gradient noise (8 lattice cells per 64-voxel tile).
        /// The shader builds multi-octave interstellar-medium turbulence from it with hardware
        /// filtering; the seed enters through the sampling offset, so one texture serves all galaxies.
        /// </summary>
        public static Texture3D TurbulenceTexture
        {
            get
            {
                if (turbulence != null) return turbulence;
                const int size = 64, period = 8, cell = size / period;
                var random = new DeterministicRandom(0x47616c4475737431UL);
                var gradients = new Vector3[2, period, period, period];
                for (int c = 0; c < 2; c++)
                for (int z = 0; z < period; z++)
                for (int y = 0; y < period; y++)
                for (int x = 0; x < period; x++)
                {
                    Vector3 g;
                    do g = new Vector3((float)random.Range(-1.0, 1.0), (float)random.Range(-1.0, 1.0), (float)random.Range(-1.0, 1.0));
                    while (g.sqrMagnitude > 1 || g.sqrMagnitude < .01f);
                    gradients[c, x, y, z] = g.normalized;
                }
                var data = new byte[size * size * size * 4];
                for (int z = 0; z < size; z++)
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int index = ((z * size + y) * size + x) * 4;
                    for (int c = 0; c < 2; c++)
                    {
                        float value = Gradient(gradients, c, (x + .5f) / cell, (y + .5f) / cell, (z + .5f) / cell, period);
                        data[index + c] = (byte)Mathf.Clamp(Mathf.RoundToInt((value * .75f + .5f) * 255), 0, 255);
                    }
                    data[index + 3] = 255;
                }
                turbulence = new Texture3D(size, size, size, TextureFormat.RGBA32, false)
                    { name = "Interstellar turbulence", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
                turbulence.SetPixelData(data, 0);
                turbulence.Apply(false, true);
                return turbulence;
            }
        }

        static float Gradient(Vector3[,,,] gradients, int channel, float x, float y, float z, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
            float fx = x - x0, fy = y - y0, fz = z - z0;
            float u = Fade(fx), v = Fade(fy), w = Fade(fz);
            float result = 0;
            for (int k = 0; k < 2; k++)
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                Vector3 g = gradients[channel, (x0 + i) % period, (y0 + j) % period, (z0 + k) % period];
                float dot = g.x * (fx - i) + g.y * (fy - j) + g.z * (fz - k);
                result += dot * (i == 0 ? 1 - u : u) * (j == 0 ? 1 - v : v) * (k == 0 ? 1 - w : w);
            }
            return result;
        }
        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        static int DefinitionFingerprint(GalaxyDefinition galaxy)
        {
            var hash = new HashCode();
            hash.Add(galaxy.Seed); hash.Add(galaxy.RadiusLy); hash.Add(galaxy.DiskScaleLengthLy);
            hash.Add(galaxy.ThinDiskHeightLy); hash.Add(galaxy.ThickDiskHeightLy); hash.Add(galaxy.BulgeRadiusLy);
            hash.Add(galaxy.BarLengthLy); hash.Add(galaxy.BarAngleRadians); hash.Add(galaxy.ArmCount);
            hash.Add(galaxy.ArmPitchRadians); hash.Add(galaxy.ArmWidthLy); hash.Add(galaxy.StellarHaloRadiusLy);
            hash.Add(galaxy.SolarRadiusLy);
            hash.Add(galaxy.LocalNumberDensityPerCubicParsec);
            return hash.ToHashCode();
        }

        public static void SetDefinition(Material material, GalaxyDefinition definition)
        {
            material.SetVector("_DiskScales", new Vector4((float)definition.RadiusLy, (float)definition.DiskScaleLengthLy,
                (float)definition.ThinDiskHeightLy, (float)definition.ThickDiskHeightLy));
            material.SetVector("_CentralScales", new Vector4((float)definition.BulgeRadiusLy, (float)definition.BarLengthLy,
                (float)definition.BarAngleRadians, (float)definition.SolarRadiusLy));
            material.SetVector("_ArmScales", new Vector4(definition.ArmCount, (float)definition.ArmPitchRadians,
                (float)definition.ArmWidthLy, (float)GalaxyModel.ArmAngle(10000, definition)));
            material.SetFloat("_HaloRadiusLy", (float)definition.StellarHaloRadiusLy);
            material.SetFloat("_PopulationScale", (float)Math.Max(0, definition.LocalNumberDensityPerCubicParsec / .1));
            var random = new DeterministicRandom(DeterministicRandom.Hash(definition.Seed, 0x564f4c, 0, 0));
            material.SetVector("_SeedOffset", new Vector4((float)random.Range(0.0, 100.0),
                (float)random.Range(0.0, 100.0), (float)random.Range(0.0, 100.0), 0));
        }

        void ReleaseTexture()
        {
            if (radiance == null) return;
            radiance.Release(); DestroyOwned(radiance); radiance = null;
            if (skyMap != null) { skyMap.Release(); DestroyOwned(skyMap); skyMap = null; }
        }
        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        void Release()
        {
            if (rendererComponent != null) rendererComponent.enabled = false;
            ReleaseTexture(); DestroyOwned(mesh); DestroyOwned(integration); DestroyOwned(composite);
            mesh = null; integration = null; composite = null;
        }
        void OnDisable() { if (rendererComponent != null) rendererComponent.enabled = false; }
        void OnDestroy() { Release(); }
    }
}
