# Adaptive planetary terrain

`PlanetTerrain` streams six cube-sphere quadtrees using a deterministic 3D height field from `SpaceEngine.Core.TerrainNoise`. This is the rendered surface at every altitude; the application does not replace the planet with a static close-up mesh.

## Integration

Create a `PlanetTerrain` on an identity-scale transform, call `Configure(PlanetDefinition, Material)`, and then call `Tick(cameraFromPlanetMeters, renderScale, planetCenterRender, sunDirection)` once per frame. The supplied material can use shader `SpaceEngine/Terrain`, vertex colors and vertex normals. The sun direction points from the planet toward the star.

`cameraFromPlanetMeters` must be the double precision, planet-relative camera position. The Unity camera must be at the render origin. `planetCenterRender` is accepted for compatibility with the other render systems but is deliberately not used for terrain placement: each patch subtracts the camera from its double precision anchor, scales that double difference, then converts to float. Mesh vertices similarly subtract the patch anchor before conversion. Therefore an Earth-size planet can retain centimeter camera movement at ground level without float cancellation between million-metre values.

## LOD and memory

The selector refines tiles in descending width / closest surface distance order. A conservative analytic horizon test excludes invisible tiles. Maximum level, mesh resolution, leaf count, resident cache count, build count, build time budget and skirt depth are inspector parameters. Defaults are 12 grid cells per patch, level 20, 192 visible leaves, 384 resident records, at most 6 builds / 6 milliseconds per frame. At Earth radius, the smallest patches are about 12 metres wide with roughly one-metre grid spacing. A synchronous build may exceed the time budget by one patch; the count limit is strict.

Parents remain visible until all required direct children are built, avoiding holes during refinement. Cached inactive patches are released by least recent use when the resident budget is exceeded. The cache target can temporarily be exceeded if configured below the number of required ancestors plus leaves; required coverage is retained. A tiny distant planet uses the same hierarchy at coarse LOD.

Each patch has an inward skirt on every edge. This masks T junctions between different LODs and chart seams. Normals come from a shared, fixed physical finite-difference interval in the continuous height field, so an edge has the same normal from both chart parameterizations. Ocean geometry is the zero height datum where the seeded terrain lies below sea level; its color varies with the underlying depth. This implementation has no ocean waves, erosion simulation, caves, biome textures, or geomorphing yet. Mesh changes can visibly pop during fast travel; skirts solve cracks, not temporal blending.

## Verification

`Tests/TerrainGeometryTests.cs` includes Unity EditMode checks for chart edge agreement, outward triangle winding on all six faces, complete skirt indices, unit normals, seeded repeatability, centimeter camera precision at Earth radius, continuous boundary normals, bounded LOD streaming and coarse/fine convergence.

On 2026-10-26, an additional isolated .NET Release harness compiled the terrain and core sources against the installed Unity 6000.5.7f1 managed assemblies. Geometry checks passed; a level 20 patch had an 8.604 m corner offset from its double origin. Sixty resolution-12 patch payload builds averaged 1.161 ms each (217 vertices, 384 triangles). This measures CPU payload generation, not native mesh upload or GPU frame time.

The same selector was exercised outside Unity with its native shader-property initializer bypassed only in a temporary copy: for 192 leaves, representative altitudes of 100,000 m, 1,000 m and 2 m selected 45 / level 6, 115 / level 13 and 190 / level 20 tiles respectively. Native renderer correctness still requires actual Unity screenshots and player frame measurements.

## Research basis

- Filip Strugar, [CDLOD reference implementation and paper](https://github.com/fstrugar/CDLOD): regular-grid quadtree patches and three-dimensional distance-based refinement. This implementation uses CPU generation and skirts rather than claiming full CDLOD geomorphing.
- Asirvatham and Hoppe, [GPU Gems 2, Terrain Rendering Using GPU-Based Geometry Clipmaps](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry): bounded regular grid blocks, active detail according to altitude, and streaming tradeoffs.
- [Ellipsoidal Cube Maps for Accurate Rendering of Planetary-Scale Terrain Data](https://doi.org/10.2312/PE/PG/PG2012short/005-010): planetary coordinate precision and cube chart representation. The current procedural body is spherical rather than an ellipsoidal geodetic dataset.
