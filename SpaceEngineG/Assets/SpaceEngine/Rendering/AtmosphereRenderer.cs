using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Camera-local full-screen atmosphere, independent of planet/render scale.
    /// Draws RGB transmittance then single-scattered radiance after transparent stars.
    /// Opaque camera depth terminates the integration at visible terrain.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class AtmosphereRenderer : MonoBehaviour
    {
        public AtmosphereSettings Settings = new AtmosphereSettings();
        /// <summary>Radius (render units) below which no surface can be visible; 0 trusts scene depth alone.</summary>
        public float SurfaceFloorRadius;

        private Camera targetCamera;
        private Material extinctionMaterial;
        private Material scatteringMaterial;
        private Mesh quad;
        private GameObject quadRoot;
        private MeshRenderer extinctionRenderer;
        private MeshRenderer scatteringRenderer;
        private Vector3 planetCenter;
        private float planetRadius = 1f;
        private float outerRadius = 1.0157f;
        private Vector3 sunDirection = Vector3.right;
        private float exposure = 1f;
        private bool configured;

        public Camera TargetCamera => targetCamera;

        public void Configure(Camera camera, Material atmosphereMaterial)
        {
            if (camera == null) throw new System.ArgumentNullException(nameof(camera));
            if (atmosphereMaterial == null) throw new System.ArgumentNullException(nameof(atmosphereMaterial));
            ReleaseResources();
            targetCamera = camera;
            targetCamera.depthTextureMode |= DepthTextureMode.Depth;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.requiresDepthTexture = true;

            quad = new Mesh { name = "Atmosphere fullscreen quad" };
            quad.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
            };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            // The shader emits clip coordinates. Bounds ensure normal frustum culling
            // cannot remove this camera-local helper at extreme render scales.
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1000000f);
            quad.UploadMeshData(true);
            quadRoot = new GameObject("Atmosphere compositing");
            quadRoot.transform.SetParent(camera.transform, false);

            extinctionMaterial = new Material(atmosphereMaterial)
            {
                name = "Atmosphere RGB transmittance", renderQueue = 3100
            };
            extinctionMaterial.EnableKeyword("_ATMOSPHERE_EXTINCTION");
            extinctionMaterial.SetInt("_SrcBlend", (int)BlendMode.Zero);
            extinctionMaterial.SetInt("_DstBlend", (int)BlendMode.SrcColor);
            scatteringMaterial = new Material(atmosphereMaterial)
            {
                name = "Atmosphere single scattering", renderQueue = 3101
            };
            scatteringMaterial.DisableKeyword("_ATMOSPHERE_EXTINCTION");
            scatteringMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
            scatteringMaterial.SetInt("_DstBlend", (int)BlendMode.One);

            extinctionRenderer = CreateQuad("Extinction", extinctionMaterial);
            scatteringRenderer = CreateQuad("In-scattering", scatteringMaterial);
            configured = true;
            UploadParameters();
        }

        public void UpdateAtmosphere(Vector3 center, float radius, float atmosphereRadius,
            Vector3 directionToSun, float radianceExposure)
        {
            planetCenter = center;
            planetRadius = Mathf.Max(radius, 0.000001f);
            outerRadius = Mathf.Max(atmosphereRadius, planetRadius * 1.0001f);
            sunDirection = directionToSun.sqrMagnitude > 0f ? directionToSun.normalized : Vector3.right;
            exposure = Mathf.Max(0f, radianceExposure);
            UploadParameters();
        }

        private MeshRenderer CreateQuad(string label, Material material)
        {
            var child = new GameObject(label);
            child.transform.SetParent(quadRoot.transform, false);
            child.layer = targetCamera.gameObject.layer;
            child.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeginCamera;
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            SetVisible(false);
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            bool visible = configured && camera == targetCamera && isActiveAndEnabled;
            SetVisible(visible);
            if (!visible) return;
            quadRoot.transform.localPosition = Vector3.forward * (camera.nearClipPlane + 0.01f);
            UploadParameters();
        }

        private void SetVisible(bool visible)
        {
            if (extinctionRenderer != null) extinctionRenderer.enabled = visible;
            if (scatteringRenderer != null) scatteringRenderer.enabled = visible;
        }

        private void UploadParameters()
        {
            if (!configured) return;
            UploadMaterial(extinctionMaterial);
            UploadMaterial(scatteringMaterial);
        }

        private void UploadMaterial(Material material)
        {
            material.SetVector("_PlanetCenter", planetCenter);
            material.SetFloat("_PlanetRadius", planetRadius);
            material.SetFloat("_AtmosphereRadius", outerRadius);
            material.SetVector("_SunDirection", sunDirection);
            material.SetVector("_Rayleigh", Settings.Rayleigh);
            material.SetVector("_Mie", Settings.Mie);
            material.SetFloat("_MieG", Mathf.Clamp(Settings.MieG, 0f, 0.95f));
            material.SetFloat("_DensityFalloff", Mathf.Max(0.1f, Settings.DensityFalloff));
            material.SetFloat("_MieDensityFalloff", Mathf.Max(0.1f, Settings.MieDensityFalloff));
            material.SetFloat("_SunIntensity", Mathf.Max(0f, Settings.SunIntensity));
            material.SetFloat("_Exposure", exposure);
            material.SetFloat("_SurfaceFloor", SurfaceFloorRadius > 0 ? SurfaceFloorRadius / planetRadius : 0);
            material.SetInt("_ViewSamples", Mathf.Clamp(Settings.ViewSamples, 4, 48));
            material.SetInt("_LightSamples", Mathf.Clamp(Settings.LightSamples, 2, 16));
        }

        private void OnDestroy() => ReleaseResources();

        private void ReleaseResources()
        {
            configured = false;
            SetVisible(false);
            DestroyOwned(quadRoot);
            DestroyOwned(quad);
            DestroyOwned(extinctionMaterial);
            DestroyOwned(scatteringMaterial);
            quadRoot = null;
            quad = null;
            extinctionMaterial = null;
            scatteringMaterial = null;
            extinctionRenderer = null;
            scatteringRenderer = null;
        }

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
