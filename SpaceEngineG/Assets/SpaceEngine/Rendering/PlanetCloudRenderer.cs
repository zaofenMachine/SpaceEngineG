using System;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Seeded thin optical weather layer over the independently generated Oceanic
    /// terrain. It fades before the observer enters the layer; no solid surface,
    /// collision or navigation data is created here.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class PlanetCloudRenderer : MonoBehaviour
    {
        [Range(48, 512)] public int LongitudeSegments = 256;
        [Range(24, 256)] public int LatitudeSegments = 128;
        [Range(0, 1)] public float Coverage = .42f;
        [Range(0, 2)] public float Opacity = .65f;
        [Range(1000, 50000)] public float CloudHeightMeters = 12000;
        [Range(1, 20)] public float WeatherScale = 5;
        [Range(0, 1)] public float DetailStrength = .45f;
        [Range(-1, 1)] public float WindRate = .08f;
        [Range(0, 8)] public float SunIntensity = 1.5f;
        [Range(0, .2f)] public float AmbientFloor = .004f;
        public Color SunColor = new Color(1, .96f, .88f);
        public bool ShowRenderer = true;

        public PlanetDefinition Definition { get; private set; }
        public bool HasCloudLayer => cloudRenderer != null;
        public bool Visible => cloudRenderer != null && cloudRenderer.enabled;
        public float ObserverFade { get; private set; }
        public float EffectiveHeightMeters { get; private set; }
        public int TriangleCount => Visible ? triangleCount : 0;

        Mesh mesh;
        Material material;
        MeshRenderer cloudRenderer;
        GameObject cloudObject;
        float shaderSeed;
        int triangleCount;

        public void Configure(PlanetDefinition definition, Material cloudTemplate)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!(definition.Radius > 0)) throw new ArgumentOutOfRangeException(nameof(definition.Radius));
            ReleaseResources();
            Definition = definition;
            if (definition.Kind != PlanetKind.Oceanic) return;
            if (cloudTemplate == null) throw new ArgumentNullException(nameof(cloudTemplate));
            var random = new DeterministicRandom(unchecked((ulong)(long)definition.Seed) ^ 0xC10D5EEDUL);
            shaderSeed = (float)random.Range(0, 1000);
            mesh = CelestialMesh.Sphere(Mathf.Clamp(LongitudeSegments, 48, 512), Mathf.Clamp(LatitudeSegments, 24, 256));
            mesh.name = definition.Name + " thin weather optical layer";
            triangleCount = mesh.triangles.Length / 3;
            material = new Material(cloudTemplate) { name = definition.Name + " seeded weather" };
            cloudObject = new GameObject("Seeded oceanic weather shell");
            cloudObject.transform.SetParent(transform, false);
            cloudObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            cloudRenderer = cloudObject.AddComponent<MeshRenderer>();
            cloudRenderer.sharedMaterial = material;
            cloudRenderer.shadowCastingMode = ShadowCastingMode.Off;
            cloudRenderer.receiveShadows = false;
            cloudRenderer.lightProbeUsage = LightProbeUsage.Off;
            cloudRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            SetVisible(false);
        }

        /// <param name="renderScale">Shared render units per physical metre, after double precision origin subtraction.</param>
        public void Tick(Double3 cameraFromPlanetMeters, double renderScale, Vector3 sunDirection, double simulationTime)
        {
            if (Definition == null || cloudRenderer == null) return;
            EffectiveHeightMeters = Mathf.Clamp(CloudHeightMeters, 1000,
                (float)Math.Max(1000, Math.Min(50000, Definition.Radius * .02)));
            double clearance = cameraFromPlanetMeters.Length - Definition.Radius;
            ObserverFade = Mathf.SmoothStep(0, 1, (float)((clearance - EffectiveHeightMeters * 1.10) / (EffectiveHeightMeters * .90)));
            bool valid = renderScale > 0 && double.IsFinite(renderScale) && double.IsFinite(clearance);
            Vector3 center = (-(cameraFromPlanetMeters * renderScale)).ToVector3();
            float radius = (float)((Definition.Radius + EffectiveHeightMeters) * renderScale);
            valid &= float.IsFinite(radius) && float.IsFinite(center.x) && float.IsFinite(center.y) && float.IsFinite(center.z);
            SetVisible(valid && ShowRenderer && isActiveAndEnabled && ObserverFade > .001f && Coverage > 0 && Opacity > 0);
            if (!Visible) return;
            cloudObject.transform.SetPositionAndRotation(center, Quaternion.identity);
            cloudObject.transform.localScale = Vector3.one * radius;
            double phase = double.IsFinite(simulationTime) ? simulationTime / 86400 * WindRate : 0;
            phase -= Math.Floor(phase / (Math.PI * 2)) * Math.PI * 2;
            material.SetFloat("_Seed", shaderSeed);
            material.SetFloat("_WindPhase", (float)phase);
            material.SetFloat("_Coverage", Coverage);
            material.SetFloat("_Opacity", Opacity);
            material.SetFloat("_WeatherScale", WeatherScale);
            material.SetFloat("_DetailStrength", DetailStrength);
            material.SetFloat("_ObserverFade", ObserverFade);
            material.SetFloat("_SurfaceRadiusRatio", (float)(Definition.Radius / (Definition.Radius + EffectiveHeightMeters)));
            material.SetFloat("_SunIntensity", SunIntensity);
            material.SetFloat("_AmbientFloor", AmbientFloor);
            material.SetColor("_SunColor", SunColor);
            material.SetVector("_SunDirection", sunDirection.sqrMagnitude > 0 ? sunDirection.normalized : Vector3.right);
        }

        void SetVisible(bool value) { if (cloudRenderer != null) cloudRenderer.enabled = value; }
        void OnDisable() => SetVisible(false);
        void OnDestroy() => ReleaseResources();
        void ReleaseResources()
        {
            SetVisible(false);
            DestroyOwned(cloudObject); DestroyOwned(mesh); DestroyOwned(material);
            cloudObject = null; mesh = null; material = null; cloudRenderer = null;
            Definition = null; ObserverFade = 0; triangleCount = 0;
        }
        static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
