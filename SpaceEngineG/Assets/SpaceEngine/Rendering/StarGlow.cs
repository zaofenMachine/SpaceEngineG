using System;
using UnityEngine;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Corona and optical glare of the local star. A resolved photosphere gets a falloff ring
    /// scaled by its angular radius; an unresolved one keeps a pixel-sized core whose amplitude
    /// follows its angular area with a perceptual compression, so a distant sun stays visible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StarGlow : MonoBehaviour
    {
        [Range(0, 4)] public float Strength = 1;
        Material material;
        Mesh mesh;
        MeshRenderer rendererComponent;

        public void Configure(Material source)
        {
            if (material != null) return;
            Shader shader = source != null ? source.shader : Shader.Find("SpaceEngine/StarGlow");
            if (shader == null) throw new InvalidOperationException("Star glow shader is missing.");
            material = source != null ? new Material(source) : new Material(shader);
            material.name = "Star glow instance";
            rendererComponent = FarPlaneQuad.Attach(gameObject, material, out mesh);
            rendererComponent.enabled = false;
        }

        /// <param name="centre">Camera-relative render position of the star.</param>
        /// <param name="radius">Photosphere radius in render units.</param>
        /// <param name="radiance">Linear photosphere radiance (colour times intensity).</param>
        public void Tick(bool visible, Vector3 centre, float radius, Vector4 radiance, Camera camera, float seed)
        {
            if (material == null) return;
            float distance = centre.magnitude;
            rendererComponent.enabled = visible && isActiveAndEnabled && distance > radius && float.IsFinite(distance);
            if (!rendererComponent.enabled) return;
            float pixel = 2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) / Mathf.Max(1, camera.pixelHeight);
            float rho = Mathf.Asin(Mathf.Clamp01(radius / distance));
            float area = Mathf.Min(1, rho / (pixel * 1.4f));
            float boost = Strength * Mathf.Pow(area * area, .35f);
            material.SetVector("_GlowDirection", centre / distance);
            material.SetVector("_GlowColor", radiance);
            material.SetVector("_GlowShape", new Vector4(rho, pixel, boost, seed % 97f));
        }

        void OnDisable() { if (rendererComponent != null) rendererComponent.enabled = false; }
        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
