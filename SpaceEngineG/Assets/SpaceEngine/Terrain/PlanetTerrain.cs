using System;
using System.Collections.Generic;
using System.Diagnostics;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceEngine.Terrain
{
    public readonly struct TerrainPatchState
    {
        public readonly TerrainPatchKey Key;
        public readonly float Morph;
        public TerrainPatchState(TerrainPatchKey key, float morph) { Key = key; Morph = morph; }
    }

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
        [Tooltip("A split tile merges only below this fractional distance margin. Preserves the selected tree near thresholds and leaf-budget ties.")]
        [Range(0, .45f)] public float LodHysteresis = .2f;
        [Tooltip("Seconds for a reversible parent-triangle to child-detail transition. Zero is an explicit diagnostic control.")]
        [Range(0, 2)] public float LodTransitionSeconds = .25f;
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
        [Header("Surface material independent of geometry LOD")]
        [Tooltip("Pixels per cube face for the GPU baked, seed-exact geological height map. 1024 uses approximately 32 MiB including mips.")]
        [Range(64, 2048)] public int MaterialMapResolution = 1024;
        [Range(0, 2)] public float MaterialDetailStrength = .65f;
        [Range(.03f, .4f)] public float OceanRoughness = .12f;
        [Range(0, 2)] public float OceanSpecularStrength = .8f;
        [Range(0, 2)] public float SurfaceNormalStrength = .7f;
        public bool MaterialMapReady => surfaceMap != null && surfaceMap.Texture != null;
        public long MaterialMapBytes => surfaceMap == null ? 0 : surfaceMap.EstimatedBytes;
        public double LastMaterialBakeCpuMilliseconds => surfaceMap == null ? 0 : surfaceMap.DispatchCpuMilliseconds;

        public int ActivePatchCount { get; private set; }
        public int TriangleCount { get; private set; }
        public int MaxActiveLevel { get; private set; }
        public int PendingBuildCount { get; private set; }
        public int ResidentPatchCount => patches.Count;
        public int BuiltThisFrame { get; private set; }
        public double LastBuildMilliseconds { get; private set; }
        public double LastSelectionMilliseconds { get; private set; }
        public int DesiredPatchCount { get; private set; }
        public int TransitioningPatchCount { get; private set; }
        public int LodSwitchesThisFrame { get; private set; }
        public bool IsSettled => PendingBuildCount == 0 && TransitioningPatchCount == 0;
        public PlanetDefinition Definition => definition;

        sealed class Patch
        {
            public TerrainPatchKey Key;
            public Double3 Direction, Anchor;
            public double AngularRadius, Priority;
            public GameObject Object;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public TerrainPatchMesh Payload;
            public int Triangles;
            public bool Desired, Split, WasSplit, ChildrenVisible;
            public float ChildrenMorph, RenderMorph;
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
        TerrainMaterialMap surfaceMap;
        bool materialBakeAttempted;
        float transitionDeltaTime;

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
            properties.SetColor("_SurfaceTint", definition.SurfaceTint);
            properties.SetFloat("_TerrainRadius", (float)definition.Radius);
            properties.SetFloat("_TerrainAmplitude", (float)definition.TerrainHeight);
            properties.SetFloat("_PlanetKind", (int)definition.Kind);
            properties.SetFloat("_HasOcean", definition.HasOcean ? 1 : 0);
            properties.SetFloat("_SurfaceMapReady", 0);
            configured = true;
            gameObject.name = $"Terrain - {definition.Name}";
        }

        public void Tick(Double3 cameraFromPlanetMeters, double renderScale, Vector3 planetCenterRender, Vector3 sunDirection,
            float elapsedSeconds = -1)
        {
            if (!configured || material == null || renderScale <= 0 || double.IsNaN(renderScale)) return;
            tick++;
            transitionDeltaTime = elapsedSeconds >= 0 ? elapsedSeconds : Application.isPlaying ? Time.unscaledDeltaTime : 1f / 60;
            transitionDeltaTime = Mathf.Clamp(transitionDeltaTime, 0, .1f);
            cameraPosition = cameraFromPlanetMeters;
            // Lazy allocation: distant/unloaded bodies never reserve high-resolution
            // material memory. Dispatch precedes drawing on the same graphics queue.
            double angularRadius = definition.Radius / Math.Max(1, cameraPosition.Length);
            if (!materialBakeAttempted && definition.IsLandable && angularRadius > .015)
            {
                materialBakeAttempted = true;
                surfaceMap = new TerrainMaterialMap();
                if (surfaceMap.Bake(definition, MaterialMapResolution))
                {
                    properties.SetTexture("_SurfaceHeightMap", surfaceMap.Texture);
                    properties.SetFloat("_SurfaceMapReady", 1);
                    properties.SetFloat("_MaterialMapResolution", surfaceMap.Resolution);
                }
            }
            stopwatch.Restart();
            foreach (Patch patch in patches.Values)
            {
                patch.Desired = false; patch.WasSplit = patch.Split; patch.Split = false;
                if (patch.Renderer != null) patch.Renderer.enabled = false;
            }
            SelectDesired();
            LastSelectionMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            BuildPending();
            LodSwitchesThisFrame = 0;
            for (int face = 0; face < 6; face++)
            {
                if (!patches.TryGetValue(new TerrainPatchKey(face, 0, 0, 0), out Patch root)) continue;
                if (root.Desired) UpdateTransitions(root, true);
                else ResetTransitions(root);
            }
            TransitioningPatchCount = 0;
            foreach (Patch patch in patches.Values)
                if ((patch.Desired && patch.Split && (!patch.ChildrenVisible || patch.ChildrenMorph < 1))
                    || (patch.ChildrenVisible && !patch.Split)) TransitioningPatchCount++;
            properties.SetVector(sunDirectionId, sunDirection.normalized);
            properties.SetFloat("_SunIntensity",SunIntensity);
            properties.SetColor("_SunColor",SunColor);
            properties.SetFloat("_MaterialDetailStrength", MaterialDetailStrength);
            properties.SetFloat("_OceanRoughness", OceanRoughness);
            properties.SetFloat("_OceanSpecularStrength", OceanSpecularStrength);
            properties.SetFloat("_SurfaceNormalStrength", SurfaceNormalStrength);
            properties.SetFloat("_RenderUnitsPerMetre",(float)renderScale);
            double groundBelowCamera = cameraPosition.SqrLength > 0
                ? TerrainPatchMesh.SurfaceHeight(cameraPosition.Normalized, definition) : 0;
            properties.SetFloat("_CameraAltitudeMetres", (float)Math.Max(0, cameraPosition.Length - definition.Radius - groundBelowCamera));
            ActivePatchCount = TriangleCount = MaxActiveLevel = 0;
            for (int face = 0; face < 6; face++)
            {
                var key = new TerrainPatchKey(face, 0, 0, 0);
                if (patches.TryGetValue(key, out Patch root) && root.Desired)
                    RenderReady(root, renderScale, 1);
            }
            EvictUnused();
        }

        void SelectDesired()
        {
            double cameraDistance = cameraPosition.Length;
            // The orbital floor is also useful during approach. The former
            // altitude>0.1R hard gate collapsed an entire cohort at that height.
            double angularDegrees = 2 * Math.Asin(Math.Min(1, definition.Radius / Math.Max(1, cameraDistance))) * 180 / Math.PI;
            orbitalDetail = angularDegrees >= OrbitalDetailMinimumDegrees * (orbitalDetail ? 1 - LodHysteresis : 1);
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
                    double retainedPriority = candidate.Priority / (candidate.WasSplit ? 1 - Mathf.Clamp(LodHysteresis, 0, .45f) : 1);
                    if (candidate.Key.Level >= MaximumLevel || retainedPriority <= RefinementThreshold) continue;
                    double bestPriority = best == null ? 0 : best.Priority / (best.WasSplit ? 1 - Mathf.Clamp(LodHysteresis, 0, .45f) : 1);
                    if (best == null || retainedPriority > bestPriority)
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
                    // Build a complete sibling cohort, including the culled
                    // siblings, so a reversible transition always has coverage.
                    Require(child);
                    if (Visible(child)) frontier.Add(child);
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
                if (patch.Key.Level > 0 && patches.TryGetValue(new TerrainPatchKey(patch.Key.Face, patch.Key.Level - 1,
                    patch.Key.X / 2, patch.Key.Y / 2), out Patch parent)) payload.SetParent(patch.Key, parent.Payload);
                patch.Payload = payload;
                patch.Anchor = payload.Anchor;
                patch.Mesh = new Mesh { name = $"{definition.Name} {patch.Key}", indexFormat = IndexFormat.UInt16 };
                patch.Mesh.vertices = payload.Vertices;
                patch.Mesh.normals = payload.Normals;
                patch.Mesh.colors = payload.Colors;
                patch.Mesh.uv = payload.UV;
                patch.Mesh.uv2 = payload.SurfaceData;
                patch.Mesh.SetUVs(2, new List<Vector4>(payload.ParentData));
                patch.Mesh.SetUVs(3, new List<Vector3>(payload.ParentNormals));
                patch.Mesh.SetUVs(4, new List<Vector4>(payload.ParentColors));
                patch.Mesh.triangles = payload.Triangles;
                patch.Triangles = payload.Triangles.Length / 3;
                patch.Mesh.bounds = payload.MorphBounds;
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

        void UpdateTransitions(Patch patch, bool allowRefinement)
        {
            bool wantChildren = patch.Desired && patch.Split && allowRefinement;
            bool started = false;
            bool ready = true;
            for (int i = 0; i < 4; i++)
                if (!patches.TryGetValue(patch.Key.Child(i), out Patch child) || child.Mesh == null) { ready = false; break; }
            if (!patch.ChildrenVisible)
            {
                if (wantChildren && ready)
                {
                    patch.ChildrenVisible = true;
                    patch.ChildrenMorph = LodTransitionSeconds <= 0 ? 1 : 0;
                    started = LodTransitionSeconds > 0;
                    LodSwitchesThisFrame++;
                }
                else return;
            }
            // Refine top-down; merge bottom-up. No child starts from a parent
            // whose own geometry is still moving toward an ancestor.
            bool descendantsCollapsed = true;
            for (int i = 0; i < 4; i++)
            {
                if (!patches.TryGetValue(patch.Key.Child(i), out Patch child)) continue;
                child.LastNeeded = tick;
                UpdateTransitions(child, wantChildren && patch.ChildrenMorph >= 1);
                if (child.ChildrenVisible) descendantsCollapsed = false;
            }
            if (!started && (wantChildren || descendantsCollapsed))
            {
                float step = LodTransitionSeconds <= 0 ? 1 : transitionDeltaTime / LodTransitionSeconds;
                patch.ChildrenMorph = Mathf.MoveTowards(patch.ChildrenMorph, wantChildren ? 1 : 0, step);
            }
            if (!wantChildren && patch.ChildrenMorph <= 0)
            {
                patch.ChildrenVisible = false;
                LodSwitchesThisFrame++;
            }
        }

        void ResetTransitions(Patch patch)
        {
            if (!patch.ChildrenVisible) return;
            patch.ChildrenVisible = false; patch.ChildrenMorph = 0;
            for (int i = 0; i < 4; i++)
                if (patches.TryGetValue(patch.Key.Child(i), out Patch child)) ResetTransitions(child);
        }

        void RenderReady(Patch patch, double scale, float morph)
        {
            patch.LastNeeded = tick;
            if (patch.ChildrenVisible)
            {
                float visibleMorph = Mathf.SmoothStep(0, 1, patch.ChildrenMorph);
                for (int i = 0; i < 4; i++)
                    if (patches.TryGetValue(patch.Key.Child(i), out Patch child) && Visible(child))
                        RenderReady(child, scale, visibleMorph);
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
            properties.SetVector("_PatchDetailAnchor", new Vector4((float)(patch.Anchor.x % 16384),
                (float)(patch.Anchor.y % 16384), (float)(patch.Anchor.z % 16384), 0));
            patch.RenderMorph = morph;
            properties.SetFloat("_LodMorph", morph);
            CubeSphere.Bounds(patch.Key, out _, out _, out double span);
            float spacing = (float)(definition.Radius * span / patch.Payload.Resolution);
            properties.SetFloat("_TerrainSampleMetres", spacing * (2 - morph));
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
            foreach (Patch patch in patches.Values)
                if (!patch.Desired && patch.LastNeeded != tick) eviction.Add(patch);
            eviction.Sort((a, b) => a.LastNeeded.CompareTo(b.LastNeeded));
            int remove = Math.Min(eviction.Count, patches.Count - MaximumResidentPatches);
            for (int i = 0; i < remove; i++)
            {
                Patch patch = eviction[i];
                Release(patch);
                patches.Remove(patch.Key);
            }
        }

        /// <summary>Stable address order, independent of dictionary/cache insertion order.</summary>
        public void CopyActivePatchStates(List<TerrainPatchState> destination)
        {
            destination.Clear();
            foreach (Patch patch in patches.Values)
                if (patch.Renderer != null && patch.Renderer.enabled) destination.Add(new TerrainPatchState(patch.Key, patch.RenderMorph));
            destination.Sort((a,b) => a.Key.Face != b.Key.Face ? a.Key.Face.CompareTo(b.Key.Face)
                : a.Key.Level != b.Key.Level ? a.Key.Level.CompareTo(b.Key.Level)
                : a.Key.X != b.Key.X ? a.Key.X.CompareTo(b.Key.X) : a.Key.Y.CompareTo(b.Key.Y));
        }

        /// <summary>
        /// Samples the actual visible triangle representation, including its
        /// parent endpoint. Used to compare stop/return paths with the fixed
        /// authoritative Height field; it is not substituted for collision.
        /// </summary>
        public bool TrySampleRenderedSurface(Double3 direction, out Double3 position, out Vector3 normal, out float rawHeight)
        {
            position = Double3.Zero; normal = Vector3.zero; rawHeight = 0;
            double ax = Math.Abs(direction.x), ay = Math.Abs(direction.y), az = Math.Abs(direction.z);
            int face = ax >= ay && ax >= az ? direction.x >= 0 ? 0 : 1 : ay >= az ? direction.y >= 0 ? 2 : 3 : direction.z >= 0 ? 4 : 5;
            foreach (Patch patch in patches.Values)
            {
                if (patch.Key.Face != face || patch.Renderer == null || !patch.Renderer.enabled) continue;
                CubeSphere.ProjectToFace(patch.Key.Face, direction, out double u, out double v);
                CubeSphere.Bounds(patch.Key, out double u0, out double v0, out double span);
                if (u < u0 - 1e-12 || v < v0 - 1e-12 || u > u0 + span + 1e-12 || v > v0 + span + 1e-12) continue;
                patch.Payload.Sample((u-u0)/span*patch.Payload.Resolution, (v-v0)/span*patch.Payload.Resolution,
                    patch.RenderMorph, out position, out normal, out _, out rawHeight);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            surfaceMap?.Dispose(); surfaceMap = null; materialBakeAttempted = false;
            properties?.SetFloat("_SurfaceMapReady", 0);
            foreach (Patch patch in patches.Values) Release(patch);
            patches.Clear(); pending.Clear(); frontier.Clear();
            ActivePatchCount = TriangleCount = MaxActiveLevel = PendingBuildCount = 0;
            TransitioningPatchCount = LodSwitchesThisFrame = 0;
            orbitalDetail = false;
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
