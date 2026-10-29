using System;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Rendering
{
    /// <summary>
    /// Unresolved background stars behind the resolved catalogue. Star directions are seeded per
    /// galactic region and cross-fade at region borders, so long journeys change the field. Counts
    /// follow the local stellar density and the integrated galactic radiance in each direction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StarfieldRenderer : MonoBehaviour
    {
        [Range(0, 4)] public float Brightness = 1;
        [Range(0, 12)] public float BandBoost = 6.5f;
        [Range(500, 20000)] public float RegionSizeLy = 3000;
        [Range(0, 1)] public float Visibility = 1;
        public bool ShowField = true;
        public float GlobalDensity { get; private set; }

        static readonly int Seeds = Shader.PropertyToID("_StarfieldSeeds");
        static readonly int Optics = Shader.PropertyToID("_StarfieldOptics");
        static readonly int Radiance = Shader.PropertyToID("_GalaxyRadiance");
        Material material;
        Mesh mesh;
        MeshRenderer rendererComponent;
        GalaxyDefinition definition;
        double homeDensity;

        public void Configure(GalaxyDefinition galaxy, Material source)
        {
            if (galaxy == null) throw new ArgumentNullException(nameof(galaxy));
            definition = galaxy;
            homeDensity = Math.Max(1e-12, GalaxyModel.SampleDensity(new Double3(galaxy.SolarRadiusLy, 0, 0), galaxy));
            if (material != null) return;
            Shader shader = source != null ? source.shader : Shader.Find("SpaceEngine/Starfield");
            if (shader == null) throw new InvalidOperationException("Starfield shader is missing.");
            material = source != null ? new Material(source) : new Material(shader);
            material.name = "Unresolved starfield instance";
            rendererComponent = FarPlaneQuad.Attach(gameObject, material, out mesh);
        }

        public void Tick(UniversalPosition observer, Camera camera, Texture galaxyRadiance)
        {
            if (material == null || definition == null || camera == null) return;
            Double3 galactic = GalaxyModel.ToGalacticLy(observer, definition);
            double ratio = GalaxyModel.SampleDensity(galactic, definition) / homeDensity;
            GlobalDensity = (float)Math.Min(1.6, Math.Pow(Math.Max(0, ratio), .4));
            rendererComponent.enabled = ShowField && isActiveAndEnabled && GlobalDensity * Visibility > .002f;

            // Region seeds: the nearest region border fades into its neighbour over 10% of a region.
            Double3 cell = galactic / RegionSizeLy;
            long rx = (long)Math.Floor(cell.x), ry = (long)Math.Floor(cell.y), rz = (long)Math.Floor(cell.z);
            double fx = cell.x - rx, fy = cell.y - ry, fz = cell.z - rz;
            double ex = Math.Min(fx, 1 - fx), ey = Math.Min(fy, 1 - fy), ez = Math.Min(fz, 1 - fz);
            long nx = rx, ny = ry, nz = rz; double edge;
            if (ex <= ey && ex <= ez) { edge = ex; nx += fx < .5 ? -1 : 1; }
            else if (ey <= ez) { edge = ey; ny += fy < .5 ? -1 : 1; }
            else { edge = ez; nz += fz < .5 ? -1 : 1; }
            float blend = (float)(.5 * Math.Max(0, 1 - edge / .1));
            Shader.SetGlobalVector(Seeds, new Vector4(RegionSeed(rx, ry, rz), RegionSeed(nx, ny, nz), blend, GlobalDensity));
            float pixel = 2 * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) / Mathf.Max(1, camera.pixelHeight);
            Shader.SetGlobalVector(Optics, new Vector4(pixel, Brightness * Visibility, BandBoost, 0));
            Shader.SetGlobalTexture(Radiance, galaxyRadiance != null ? galaxyRadiance : Texture2D.blackTexture);
        }

        float RegionSeed(long x, long y, long z)
        {
            ulong hash = DeterministicRandom.Hash(definition.Seed, x, y, z);
            return (float)(hash % 1048573UL);
        }

        void OnDisable() { if (rendererComponent != null) rendererComponent.enabled = false; }
        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
