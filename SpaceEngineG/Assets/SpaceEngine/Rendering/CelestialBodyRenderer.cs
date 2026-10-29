using System;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Seeded rotating cloud-top reference sphere for non-landable giants, and particle-ring optical sheet.
    /// SI doubles are reduced relative to the observer before conversion to Unity coordinates.
    /// This is an appearance model, not a fluid solver or a solid ground surface for gas giants.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class CelestialBodyRenderer : MonoBehaviour
    {
        [Range(24, 768)] public int LongitudeSegments = 384;
        [Range(12, 384)] public int LatitudeSegments = 192;
        [Range(32, 1024)] public int RingAngularSegments = 512;
        [Range(1, 64)] public int RingRadialSegments = 8;
        // Seeded band counts (9-17) read as a few thick, cartoon belts; real giants show many thin ones.
        [Range(.25f, 3f)] public float BandFrequencyMultiplier = 1.4f;
        [Range(0, 1)] public float BandContrast = .72f;
        [Range(0, 1)] public float CloudTurbulence = .65f;
        [Range(0, 1)] public float StormStrength = .85f;
        [Range(.01f, .5f)] public float StormSize = .15f;
        [Range(0, 2)] public float DifferentialWindRate = .12f;
        [Range(0, 3)] public float RingOpticalDepth = .8f;
        [Range(0, 8)] public float SunIntensity = 1.5f;
        [Range(0, .3f)] public float AmbientFloor = .005f;
        public Color SunColor = new Color(1, .96f, .88f);
        public bool ShowRenderer = true;

        public PlanetDefinition Definition { get; private set; }
        public bool IsGasBody => Definition != null && !Definition.IsLandable;
        public int TriangleCount => visible ? triangleCount : 0;
        public bool HasRings => ringRenderer != null;
        public float RotationDegrees { get; private set; }

        Mesh cloudMesh, ringMesh;
        Material cloudMaterial, ringMaterial;
        MeshRenderer cloudRenderer, ringRenderer;
        GameObject cloudObject, ringObject;
        float bandCount, shaderSeed;
        Quaternion axialRotation;
        int triangleCount;
        bool visible;

        public void Configure(PlanetDefinition definition, Material gasMaterial, Material ringTemplate)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!(definition.Radius > 0)) throw new ArgumentOutOfRangeException(nameof(definition.Radius));
            ReleaseResources();
            Definition = definition;
            var random = new DeterministicRandom(unchecked((ulong)(long)definition.Seed));
            bandCount = (float)random.Range(9, 17);
            shaderSeed = (float)random.Range(0, 1000);
            axialRotation = Quaternion.AngleAxis((float)(definition.AxialTilt * Mathf.Rad2Deg), Vector3.forward);
            if (!definition.IsLandable)
            {
                if (gasMaterial == null) throw new ArgumentNullException(nameof(gasMaterial));
                cloudMaterial = new Material(gasMaterial) { name = definition.Name + " seeded clouds" };
                cloudMesh = CelestialMesh.Sphere(Mathf.Clamp(LongitudeSegments, 24, 768), Mathf.Clamp(LatitudeSegments, 12, 384));
                cloudRenderer = CreateRenderer("Cloud reference (non-landable)", cloudMesh, cloudMaterial, out cloudObject);
                triangleCount += cloudMesh.triangles.Length / 3;
            }
            if (definition.RingInnerRadius > definition.Radius && definition.RingOuterRadius > definition.RingInnerRadius)
            {
                if (ringTemplate == null) throw new ArgumentNullException(nameof(ringTemplate));
                ringMaterial = new Material(ringTemplate) { name = definition.Name + " seeded particle rings" };
                ringMesh = CelestialMesh.Ring((float)(definition.RingInnerRadius / definition.Radius),
                    (float)(definition.RingOuterRadius / definition.Radius), Mathf.Clamp(RingAngularSegments, 32, 1024),
                    Mathf.Clamp(RingRadialSegments, 1, 64));
                ringRenderer = CreateRenderer("Equatorial ring optical sheet", ringMesh, ringMaterial, out ringObject);
                triangleCount += ringMesh.triangles.Length / 3;
            }
            SetVisible(ShowRenderer && isActiveAndEnabled);
        }

        /// <param name="renderScale">Unity render units per physical metre, shared with PlanetTerrain.</param>
        public void Tick(Double3 cameraFromPlanetMeters, double renderScale, Vector3 sunDirection, double simulationTime)
        {
            if (Definition == null) return;
            bool valid = renderScale > 0 && !double.IsNaN(renderScale) && !double.IsInfinity(renderScale);
            SetVisible(valid && ShowRenderer && isActiveAndEnabled);
            if (!visible) return;
            Vector3 center = (-(cameraFromPlanetMeters * renderScale)).ToVector3();
            float radius = (float)(Definition.Radius * renderScale);
            if (!float.IsFinite(radius) || !float.IsFinite(center.x) || !float.IsFinite(center.y) || !float.IsFinite(center.z))
            {
                SetVisible(false);
                return;
            }
            double turns = Definition.RotationPeriod > 0 && double.IsFinite(simulationTime)
                ? simulationTime / Definition.RotationPeriod : 0;
            RotationDegrees = (float)((turns - Math.Floor(turns)) * 360);
            Quaternion spin = axialRotation * Quaternion.AngleAxis(RotationDegrees, Vector3.up);
            Vector3 toSun = sunDirection.sqrMagnitude > 0 ? sunDirection.normalized : Vector3.right;
            if (cloudObject != null)
            {
                cloudObject.transform.SetPositionAndRotation(center, spin);
                cloudObject.transform.localScale = Vector3.one * radius;
                UploadCommon(cloudMaterial, toSun);
                bool iceGiant = Definition.Kind == PlanetKind.IceGiant;
                cloudMaterial.SetColor("_LightCloudColor", iceGiant
                    ? Color.Lerp(Definition.SurfaceTint, new Color(.79f, .91f, .98f), .44f)
                    : Color.Lerp(Definition.SurfaceTint, new Color(.97f, .94f, .85f), .75f));
                cloudMaterial.SetColor("_DarkCloudColor", iceGiant
                    ? Definition.SurfaceTint * .69f
                    : Color.Lerp(Definition.SurfaceTint, new Color(.52f, .35f, .23f), .55f));
                cloudMaterial.SetFloat("_IceFraction", Definition.Kind == PlanetKind.IceGiant ? 1 : 0);
                cloudMaterial.SetFloat("_BandCount", bandCount * BandFrequencyMultiplier);
                cloudMaterial.SetFloat("_BandContrast", BandContrast);
                cloudMaterial.SetFloat("_CloudTurbulence", CloudTurbulence);
                cloudMaterial.SetFloat("_StormStrength", StormStrength);
                cloudMaterial.SetFloat("_StormSize", StormSize);
                cloudMaterial.SetFloat("_WindPhase", (float)((turns * DifferentialWindRate % 1) * Math.PI * 2));
                cloudMaterial.SetVector("_PlanetCenter", center);
                cloudMaterial.SetFloat("_PlanetRadius", radius);
                // The mesh bounds raster coverage only. The shader intersects
                // the physical sphere and writes its analytic depth in both passes.
                double halfLongitude = Math.PI / Mathf.Clamp(LongitudeSegments, 24, 768);
                double halfLatitude = Math.PI / (2 * Mathf.Clamp(LatitudeSegments, 12, 384));
                double diagonal = Math.Sqrt(halfLongitude * halfLongitude + halfLatitude * halfLatitude);
                cloudMaterial.SetFloat("_RasterInflation", (float)(1 / Math.Cos(diagonal) + .00001));
                cloudMaterial.SetVector("_RingNormal", axialRotation * Vector3.up);
                cloudMaterial.SetFloat("_RingInnerRatio", HasRings ? (float)(Definition.RingInnerRadius / Definition.Radius) : 0);
                cloudMaterial.SetFloat("_RingOuterRatio", HasRings ? (float)(Definition.RingOuterRadius / Definition.Radius) : 0);
                cloudMaterial.SetFloat("_RingOpticalDepth", RingOpticalDepth);
            }
            if (ringObject != null)
            {
                ringObject.transform.SetPositionAndRotation(center, axialRotation);
                ringObject.transform.localScale = Vector3.one * radius;
                UploadCommon(ringMaterial, toSun);
                ringMaterial.SetColor("_RingColor", Color.Lerp(Definition.SurfaceTint, new Color(.85f, .79f, .64f), .72f));
                ringMaterial.SetFloat("_RingInnerRatio", (float)(Definition.RingInnerRadius / Definition.Radius));
                ringMaterial.SetFloat("_RingOuterRatio", (float)(Definition.RingOuterRadius / Definition.Radius));
                ringMaterial.SetFloat("_OpticalDepth", RingOpticalDepth);
            }
        }

        void UploadCommon(Material material, Vector3 sunDirection)
        {
            material.SetVector("_SunDirection", sunDirection);
            material.SetColor("_SunColor", SunColor);
            material.SetFloat("_SunIntensity", SunIntensity);
            material.SetFloat("_AmbientFloor", AmbientFloor);
            material.SetFloat("_Seed", shaderSeed);
        }

        MeshRenderer CreateRenderer(string label, Mesh mesh, Material material, out GameObject child)
        {
            child = new GameObject(label);
            child.transform.SetParent(transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        void SetVisible(bool value)
        {
            visible = value;
            if (cloudRenderer != null) cloudRenderer.enabled = value;
            if (ringRenderer != null) ringRenderer.enabled = value;
        }

        void OnDisable() => SetVisible(false);
        void OnDestroy() => ReleaseResources();

        void ReleaseResources()
        {
            SetVisible(false);
            DestroyOwned(cloudObject); DestroyOwned(ringObject);
            DestroyOwned(cloudMesh); DestroyOwned(ringMesh);
            DestroyOwned(cloudMaterial); DestroyOwned(ringMaterial);
            cloudObject = null; ringObject = null;
            cloudMesh = null; ringMesh = null;
            cloudMaterial = null; ringMaterial = null;
            cloudRenderer = null; ringRenderer = null;
            Definition = null;
            triangleCount = 0;
        }

        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
