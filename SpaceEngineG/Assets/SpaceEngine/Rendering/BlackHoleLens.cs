using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Screen-space Schwarzschild lens for one black hole. The deflection of a null geodesic
    /// with impact parameter b is tabulated once by integrating u'' + u = 3/2 Rs u^2 from
    /// infinity to infinity; the shader bends each view ray by that angle and re-evaluates
    /// the background sky. The observer is treated as distant from the hole.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlackHoleLens : MonoBehaviour
    {
        public const double CriticalImpact = 2.598076211353316;
        const int TableSize = 512;
        const float TableMinLog = -9.21034f;
        static readonly float TableSpan = Mathf.Log(400f - (float)CriticalImpact) - TableMinLog;
        static Texture2D table;

        Material material;
        Mesh mesh;
        MeshRenderer rendererComponent;

        public void Configure(Material source)
        {
            if (material != null) return;
            Shader shader = source != null ? source.shader : Shader.Find("SpaceEngine/BlackHoleLens");
            if (shader == null) throw new InvalidOperationException("Black hole lens shader is missing.");
            material = source != null ? new Material(source) : new Material(shader);
            material.name = "Black hole lens instance";
            material.SetTexture("_DeflectionTable", DeflectionTable);
            rendererComponent = FarPlaneQuad.Attach(gameObject, material, out mesh);
            rendererComponent.enabled = false;
        }

        /// <param name="centre">Camera-relative render-space position of the hole.</param>
        /// <param name="schwarzschildRadius">Horizon radius in the same render units.</param>
        public bool Visible => rendererComponent != null && rendererComponent.enabled;

        /// <summary>
        /// Optional thin accretion disk, traced through the lens. Radii are in Schwarzschild radii;
        /// the normal also sets the prograde rotation sense. This is an appearance model of a
        /// Shakura-Sunyaev disk (temperature profile, Doppler and gravitational shifts), not MHD.
        /// </summary>
        public void SetDisk(bool enabled, Vector3 normal, float innerRadius, float outerRadius, float radiance, float time)
        {
            if (material == null) return;
            Vector3 n = normal.sqrMagnitude > 0 ? normal.normalized : Vector3.up;
            material.SetVector("_DiskNormal", new Vector4(n.x, n.y, n.z, enabled ? 1 : 0));
            material.SetVector("_DiskShape", new Vector4(Mathf.Max(1.5f, innerRadius), Mathf.Max(innerRadius + .5f, outerRadius), Mathf.Max(0, radiance), time));
        }

        public void Tick(bool visible, Vector3 centre, float schwarzschildRadius, Camera camera)
        {
            if (material == null) return;
            bool finite = float.IsFinite(centre.x) && float.IsFinite(centre.y) && float.IsFinite(centre.z) && schwarzschildRadius > 0;
            rendererComponent.enabled = visible && finite && isActiveAndEnabled;
            if (!rendererComponent.enabled) return;
            float pixel = 2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) / Mathf.Max(1, camera.pixelHeight);
            material.SetVector("_LensCentre", centre);
            material.SetVector("_LensShape", new Vector4(schwarzschildRadius, pixel, TableMinLog, TableSpan));
        }

        public static Texture2D DeflectionTable
        {
            get
            {
                if (table != null) return table;
                var values = new float[TableSize];
                for (int i = 0; i < TableSize; i++)
                    values[i] = (float)Deflection(CriticalImpact + Math.Exp(TableMinLog + TableSpan * i / (TableSize - 1.0)));
                table = new Texture2D(TableSize, 1, TextureFormat.RFloat, false, true)
                    { name = "Schwarzschild deflection table", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                table.SetPixelData(values, 0);
                table.Apply(false, true);
                return table;
            }
        }

        /// <summary>Total deflection (radians) of a ray with impact parameter b in Schwarzschild radii.</summary>
        public static double Deflection(double impact)
        {
            if (impact <= CriticalImpact) return double.PositiveInfinity;
            double u = 0, w = 1 / impact, phi = 0;
            for (int step = 0; step < 200000; step++)
            {
                // Finer steps while the ray lingers near the photon sphere (u -> 2/3).
                double h = .0004 + .006 * Math.Abs(w) / (Math.Abs(w) + .05);
                double k1u = w, k1w = -u + 1.5 * u * u;
                double u2 = u + .5 * h * k1u, w2 = w + .5 * h * k1w;
                double k2u = w2, k2w = -u2 + 1.5 * u2 * u2;
                double u3 = u + .5 * h * k2u, w3 = w + .5 * h * k2w;
                double k3u = w3, k3w = -u3 + 1.5 * u3 * u3;
                double u4 = u + h * k3u, w4 = w + h * k3w;
                double k4u = w4, k4w = -u4 + 1.5 * u4 * u4;
                double nextU = u + h / 6 * (k1u + 2 * k2u + 2 * k3u + k4u);
                double nextW = w + h / 6 * (k1w + 2 * k2w + 2 * k3w + k4w);
                if (nextU > 1) return double.PositiveInfinity;
                if (step > 0 && nextU < 0) return phi + h * u / (u - nextU) - Math.PI;
                u = nextU; w = nextW; phi += h;
            }
            return double.PositiveInfinity;
        }

        void OnDisable() { if (rendererComponent != null) rendererComponent.enabled = false; }
        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
