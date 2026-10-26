using System;
using System.Collections.Generic;
using System.Diagnostics;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Terrain
{
    /// <summary>
    /// Budgeted cube-sphere quadtree. A complete parent remains visible until
    /// every visible child has a mesh. All positions are camera relative in
    /// double precision before the Unity transform is populated.
    /// </summary>
    public sealed class PlanetTerrain : MonoBehaviour
    {
        [Header("Geometry / streaming budgets")]
        [Range(4, 32)] public int PatchResolution = 12;
        [Range(1, 24)] public int MaximumLevel = 20;
        [Range(24, 512)] public int MaximumVisiblePatches = 192;
        [Range(64, 1024)] public int MaximumResidentPatches = 384;
        [Range(1, 32)] public int BuildsPerFrame = 6;
        [Range(.2f, 30)] public float BuildBudgetMilliseconds = 6;
        [Tooltip("Refine when patch width divided by closest surface distance exceeds this value.")]
        [Range(.5f, 12)] public float RefinementThreshold = 2.5f;
        [Range(.005f, .2f)] public float SkirtDepthFactor = .08f;
        [Range(.25f, 100)] public float MinimumSkirtDepth = 4f;
        [Tooltip("Normal finite-difference probe distance in physical metres, independent of LOD and planet radius.")]
        [Range(.01f, 2)] public float NormalSampleDistance = .25f;
        public bool HorizonCulling = true;
        public float SunIntensity = 1.5f;
        public Color SunColor = new Color(1,.96f,.88f);
        [Header("Visible orbital detail")]
        [Range(0, 5)] public int OrbitalMinimumLevel = 3;
        [Tooltip("Apply orbital minimum LOD when the planet's angular diameter exceeds this many degrees.")]
        [Range(1, 45)] public float OrbitalDetailMinimumDegrees = 5;

        public int ActivePatchCount { get; private set; }
        public int TriangleCount { get; private set; }
        public int MaxActiveLevel { get; private set; }
        public int PendingBuildCount { get; private set; }
        public int ResidentPatchCount => patches.Count;
        public int BuiltThisFrame { get; private set; }
        public double LastBuildMilliseconds { get; private set; }
        public double LastSelectionMilliseconds { get; private set; }
        public int DesiredPatchCount { get; private set; }
        public PlanetDefinition Definition => definition;

        sealed class Patch
        {
            public TerrainPatchKey Key;
            public Double3 Direction, Anchor;
            public double AngularRadius, Priority;
            public GameObject Object;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public int Triangles;
            public bool Desired, Split;
            public int LastNeeded;
        }

        PlanetDefinition definition;
        Material material;
        readonly Dictionary<TerrainPatchKey, Patch> patches = new Dictionary<TerrainPatchKey, Patch>();
        readonly List<Patch> frontier = new List<Patch>(512);
        readonly List<Patch> pending = new List<Patch>(512);
        readonly List<Patch> eviction = new List<Patch>(512);
        readonly Stopwatch stopwatch = new Stopwatch();
        // Unity native resources must be created on the main thread, never in a
        // MonoBehaviour field initializer (Unity also constructs these while loading).
        MaterialPropertyBlock properties;
        int sunDirectionId;
        bool configured;
        int tick;
        Double3 cameraPosition;
        bool orbitalDetail;

        public void Configure(PlanetDefinition planetDefinition, Material terrainMaterial)
        {
            if (planetDefinition == null) throw new ArgumentNullException(nameof(planetDefinition));
            Clear();
            if (properties == null) properties = new MaterialPropertyBlock();
            sunDirectionId = Shader.PropertyToID("_SunDirection");
            definition = planetDefinition;
            material = terrainMaterial;
            properties.SetFloat("_IsLava", definition.Kind == PlanetKind.Lava && !definition.HasOcean ? 1 : 0);
            uint lavaSeed = unchecked((uint)definition.Seed);
            properties.SetVector("_LavaSeed", new Vector4(lavaSeed & 0xffffu, lavaSeed >> 16, 0, 0));
            configured = true;
            gameObject.name = $"Terrain - {definition.Name}";
        }

        public void Tick(Double3 cameraFromPlanetMeters, double renderScale, Vector3 planetCenterRender, Vector3 sunDirection)
        {
            if (!configured || material == null || renderScale <= 0 || double.IsNaN(renderScale)) return;
            tick++;
            cameraPosition = cameraFromPlanetMeters;
            stopwatch.Restart();
            foreach (Patch patch in patches.Values)
            {
                patch.Desired = false; patch.Split = false;
                if (patch.Renderer != null) patch.Renderer.enabled = false;
            }
            SelectDesired();
            LastSelectionMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            BuildPending();
            properties.SetVector(sunDirectionId, sunDirection.normalized);
            properties.SetFloat("_SunIntensity",SunIntensity);
            properties.SetColor("_SunColor",SunColor);
            ActivePatchCount = TriangleCount = MaxActiveLevel = 0;
            for (int face = 0; face < 6; face++)
            {
                var key = new TerrainPatchKey(face, 0, 0, 0);
                if (patches.TryGetValue(key, out Patch root) && root.Desired)
                    RenderReady(root, renderScale);
            }
            EvictUnused();
        }

        void SelectDesired()
        {
            double cameraDistance = cameraPosition.Length;
            orbitalDetail = cameraDistance > definition.Radius * 1.1 &&
                2 * Math.Asin(Math.Min(1, definition.Radius / cameraDistance)) >= OrbitalDetailMinimumDegrees * Math.PI / 180;
            frontier.Clear();
            for (int face = 0; face < 6; face++)
            {
                Patch root = GetPatch(new TerrainPatchKey(face, 0, 0, 0));
                if (Visible(root))
                {
                    Require(root);
                    frontier.Add(root);
                }
            }
            // Best-first subdivision spends detail where actual 3D distance
            // makes it useful. The closest direction is clamped to each tile,
            // unlike center-distance metrics that stop refining near the ground.
            while (frontier.Count + 3 <= MaximumVisiblePatches)
            {
                Patch best = null;
                int bestIndex = -1;
                for (int i = 0; i < frontier.Count; i++)
                {
                    Patch candidate = frontier[i];
                    if (candidate.Key.Level >= MaximumLevel || candidate.Priority <= RefinementThreshold) continue;
                    if (best == null || candidate.Priority > best.Priority)
                    {
                        best = candidate; bestIndex = i;
                    }
                }
                if (best == null) break;
                frontier.RemoveAt(bestIndex);
                best.Split = true;
                for (int childIndex = 0; childIndex < 4; childIndex++)
                {
                    Patch child = GetPatch(best.Key.Child(childIndex));
                    if (!Visible(child)) continue;
                    Require(child);
                    frontier.Add(child);
                }
            }
            DesiredPatchCount = frontier.Count;
            pending.Clear();
            foreach (Patch patch in patches.Values)
                if (patch.Desired && patch.Mesh == null) pending.Add(patch);
            // Coarse coverage must exist before any of its finer replacements.
            pending.Sort((a, b) => a.Key.Level != b.Key.Level ? a.Key.Level.CompareTo(b.Key.Level) : b.Priority.CompareTo(a.Priority));
            PendingBuildCount = pending.Count;
        }

        void Require(Patch patch)
        {
            patch.Desired = true;
            patch.LastNeeded = tick;
            CubeSphere.Bounds(patch.Key, out double u, out double v, out double span);
            Double3 direction = cameraPosition.Normalized;
            CubeSphere.ProjectToFace(patch.Key.Face, direction, out double cameraU, out double cameraV);
            cameraU = Math.Max(u, Math.Min(u + span, cameraU));
            cameraV = Math.Max(v, Math.Min(v + span, cameraV));
            Double3 nearestDirection = CubeSphere.Direction(patch.Key.Face, cameraU, cameraV);
            Double3 surface = TerrainPatchMesh.SurfacePoint(nearestDirection, definition);
            double distance = Math.Max(.5, (cameraPosition - surface).Length);
            patch.Priority = definition.Radius * span / distance;
            // A planet that fills a useful part of the view needs enough color
            // and coastline samples even before proximity-driven terrain LOD.
            // Breadth priority reaches the orbital floor across the visible disk;
            // the same strict leaf budget still governs refinement.
            if (orbitalDetail && patch.Key.Level < OrbitalMinimumLevel)
                patch.Priority = 1000000.0 / (patch.Key.Level + 1) + Math.Min(10000, patch.Priority);
        }

        Patch GetPatch(TerrainPatchKey key)
        {
            if (patches.TryGetValue(key, out Patch patch)) return patch;
            patch = new Patch { Key = key, Direction = CubeSphere.CenterDirection(key), LastNeeded = tick };
            CubeSphere.Bounds(key, out double u, out double v, out double span);
            double minDot = 1;
            for (int corner = 0; corner < 4; corner++)
                minDot = Math.Min(minDot, CubeSphere.Dot(patch.Direction, CubeSphere.Direction(key.Face, u + (corner & 1) * span, v + (corner >> 1) * span)));
            patch.AngularRadius = Math.Acos(Math.Max(-1, Math.Min(1, minDot)));
            patches.Add(key, patch);
            return patch;
        }

        bool Visible(Patch patch)
        {
            if (!HorizonCulling) return true;
            double distance = cameraPosition.Length;
            // The inscribed sphere is wholly inside the terrain. Culling with
            // it plus the outer elevation envelope cannot remove visible peaks.
            double innerRadius = Math.Max(1, definition.Radius - Math.Abs(definition.TerrainHeight));
            if (distance <= innerRadius) return true;
            double horizon = Math.Acos(Math.Min(1, innerRadius / distance));
            double mountainHorizon = Math.Acos(Math.Min(1, innerRadius / (definition.Radius + Math.Abs(definition.TerrainHeight))));
            double limit = Math.Min(Math.PI, horizon + mountainHorizon + patch.AngularRadius + .005);
            return CubeSphere.Dot(patch.Direction, cameraPosition / distance) >= Math.Cos(limit);
        }

        void BuildPending()
        {
            stopwatch.Restart();
            BuiltThisFrame = 0;
            for (int i = 0; i < pending.Count && BuiltThisFrame < BuildsPerFrame; i++)
            {
                if (BuiltThisFrame > 0 && stopwatch.Elapsed.TotalMilliseconds >= BuildBudgetMilliseconds) break;
                Patch patch = pending[i];
                TerrainPatchMesh payload = TerrainPatchMesh.Build(patch.Key, definition, PatchResolution, SkirtDepthFactor, NormalSampleDistance, MinimumSkirtDepth);
                patch.Anchor = payload.Anchor;
                patch.Mesh = new Mesh { name = $"{definition.Name} {patch.Key}", indexFormat = IndexFormat.UInt16 };
                patch.Mesh.vertices = payload.Vertices;
                patch.Mesh.normals = payload.Normals;
                patch.Mesh.colors = payload.Colors;
                patch.Mesh.uv = payload.UV;
                patch.Mesh.triangles = payload.Triangles;
                patch.Triangles = payload.Triangles.Length / 3;
                patch.Mesh.RecalculateBounds();
                patch.Object = new GameObject(patch.Mesh.name);
                patch.Object.transform.SetParent(transform, false);
                var filter = patch.Object.AddComponent<MeshFilter>();
                filter.sharedMesh = patch.Mesh;
                patch.Renderer = patch.Object.AddComponent<MeshRenderer>();
                patch.Renderer.sharedMaterial = material;
                patch.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                patch.Renderer.receiveShadows = false;
                patch.Renderer.enabled = false;
                BuiltThisFrame++;
            }
            PendingBuildCount = Math.Max(0, PendingBuildCount - BuiltThisFrame);
            LastBuildMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        }

        void RenderReady(Patch patch, double scale)
        {
            bool childrenReady = patch.Split;
            if (childrenReady)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (!patches.TryGetValue(patch.Key.Child(i), out Patch child) || !child.Desired) continue;
                    if (child.Mesh == null) { childrenReady = false; break; }
                }
            }
            if (childrenReady)
            {
                for (int i = 0; i < 4; i++)
                    if (patches.TryGetValue(patch.Key.Child(i), out Patch child) && child.Desired) RenderReady(child, scale);
                return;
            }
            if (patch.Mesh == null) return;
            // Never add planetCenterRender (a potentially imprecise large float)
            // to a large float vertex. Cancellation must happen in double here.
            patch.Object.transform.position = ((patch.Anchor - cameraPosition) * scale).ToVector3();
            patch.Object.transform.rotation = Quaternion.identity;
            patch.Object.transform.localScale = Vector3.one * (float)scale;
            Double3 anchorUnits = patch.Anchor / definition.Radius;
            properties.SetVector("_PatchAnchor", new Vector4((float)anchorUnits.x, (float)anchorUnits.y, (float)anchorUnits.z, (float)(1 / definition.Radius)));
            patch.Renderer.SetPropertyBlock(properties);
            patch.Renderer.enabled = true;
            ActivePatchCount++;
            TriangleCount += patch.Triangles;
            MaxActiveLevel = Math.Max(MaxActiveLevel, patch.Key.Level);
        }

        void EvictUnused()
        {
            if (patches.Count <= MaximumResidentPatches) return;
            eviction.Clear();
            foreach (Patch patch in patches.Values) if (!patch.Desired) eviction.Add(patch);
            eviction.Sort((a, b) => a.LastNeeded.CompareTo(b.LastNeeded));
            int remove = Math.Min(eviction.Count, patches.Count - MaximumResidentPatches);
            for (int i = 0; i < remove; i++)
            {
                Patch patch = eviction[i];
                Release(patch);
                patches.Remove(patch.Key);
            }
        }

        public void Clear()
        {
            foreach (Patch patch in patches.Values) Release(patch);
            patches.Clear(); pending.Clear(); frontier.Clear();
            ActivePatchCount = TriangleCount = MaxActiveLevel = PendingBuildCount = 0;
        }

        static void Release(Patch patch)
        {
            if (Application.isPlaying)
            {
                if (patch.Object != null) Destroy(patch.Object);
                if (patch.Mesh != null) Destroy(patch.Mesh);
            }
            else
            {
                if (patch.Object != null) DestroyImmediate(patch.Object);
                if (patch.Mesh != null) DestroyImmediate(patch.Mesh);
            }
        }

        void OnDestroy() => Clear();
    }
}
