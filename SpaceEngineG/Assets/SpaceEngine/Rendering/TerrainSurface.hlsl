#ifndef SPACE_ENGINE_TERRAIN_SURFACE_INCLUDED
#define SPACE_ENGINE_TERRAIN_SURFACE_INCLUDED

TEXTURE2D_ARRAY(_SurfaceHeightMap);
SAMPLER(sampler_SurfaceHeightMap);

float3 TerrainMapCoordinates(float3 direction)
{
    float3 a = abs(direction);
    float2 uv; float face;
    if (a.x >= a.y && a.x >= a.z)
    {
        if (direction.x >= 0) { face = 0; uv = float2(-direction.z, direction.y) / a.x; }
        else { face = 1; uv = float2(direction.z, direction.y) / a.x; }
    }
    else if (a.y >= a.z)
    {
        if (direction.y >= 0) { face = 2; uv = float2(direction.x, -direction.z) / a.y; }
        else { face = 3; uv = float2(direction.x, direction.z) / a.y; }
    }
    else
    {
        if (direction.z >= 0) { face = 4; uv = direction.xy / a.z; }
        else { face = 5; uv = float2(-direction.x, direction.y) / a.z; }
    }
    // Matching edge texels refer to identical 3D directions on neighbouring faces.
    uv = (uv * .5 + .5) * ((_MaterialMapResolution - 1) / _MaterialMapResolution) + .5 / _MaterialMapResolution;
    return float3(uv, face);
}

uint SurfaceHash(uint3 c, uint seed)
{
    uint h = c.x * 0x8da6b343u ^ c.y * 0xd8163841u ^ c.z * 0xcb1ab31fu ^ seed;
    h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
    return h;
}
float SurfaceLattice(int3 cell, uint seed, uint mask)
{
    return (SurfaceHash(asuint(cell) & mask, seed) >> 8) * (1.0 / 16777216.0);
}
float4 SurfaceNoiseGradient(float3 p, uint seed, uint mask)
{
    int3 c = (int3)floor(p);
    float3 f = frac(p), w = f * f * (3 - 2 * f);
    float3 dw = 6 * f * (1 - f);
    float c000=SurfaceLattice(c,seed,mask), c100=SurfaceLattice(c+int3(1,0,0),seed,mask);
    float c010=SurfaceLattice(c+int3(0,1,0),seed,mask), c110=SurfaceLattice(c+int3(1,1,0),seed,mask);
    float c001=SurfaceLattice(c+int3(0,0,1),seed,mask), c101=SurfaceLattice(c+int3(1,0,1),seed,mask);
    float c011=SurfaceLattice(c+int3(0,1,1),seed,mask), c111=SurfaceLattice(c+int3(1,1,1),seed,mask);
    float z0=lerp(lerp(c000,c100,w.x),lerp(c010,c110,w.x),w.y);
    float z1=lerp(lerp(c001,c101,w.x),lerp(c011,c111,w.x),w.y);
    float dx=lerp(lerp(c100-c000,c110-c010,w.y),lerp(c101-c001,c111-c011,w.y),w.z)*dw.x;
    float dy=lerp(lerp(c010-c000,c110-c100,w.x),lerp(c011-c001,c111-c101,w.x),w.z)*dw.y;
    float dz=(z1-z0)*dw.z;
    return float4(lerp(z0,z1,w.z)*2-1,float3(dx,dy,dz)*2);
}
float SurfaceNoise(float3 p, uint seed, uint mask) { return SurfaceNoiseGradient(p,seed,mask).x; }
float4 SurfaceFilteredNoiseGradient(float3 metres, float scale, float footprint, uint seed)
{
    float amplitude = 1 - smoothstep(.25, 1.0, footprint / scale);
    // A 16,384 metre chart period permits precise local positions at the ground.
    // Its integer wrap makes material boundaries agree across double CPU anchors.
    uint mask = (uint)(16384 / scale) - 1u;
    if (amplitude <= .0001) return 0;
    float4 noiseSample = SurfaceNoiseGradient(metres / scale,seed,mask);
    return noiseSample * float4(amplitude,amplitude/scale,amplitude/scale,amplitude/scale);
}
// Cubic B-spline reconstruction of a map level from four bilinear taps. Thresholds of a bilinear
// level kink at every texel edge; B-spline iso-contours stay smooth curves. Within two texels of
// a face edge it hands back to bilinear, whose shared edge texels keep neighbouring faces continuous.
float SurfaceHeightSmooth(float3 coordinates, float lod)
{
    lod = ceil(lod);
    float size = max(2, _MaterialMapResolution / exp2(lod));
    float2 t = coordinates.xy * size - .5;
    float2 i = floor(t), f = t - i;
    float2 f2 = f * f, f3 = f2 * f;
    float2 w0 = (1 - 3 * f + 3 * f2 - f3) / 6, w1 = (4 - 6 * f2 + 3 * f3) / 6;
    float2 w2 = (1 + 3 * f + 3 * f2 - 3 * f3) / 6, w3 = f3 / 6;
    float2 g0 = w0 + w1, g1 = w2 + w3;
    float2 p0 = (i - .5 + w1 / g0) / size, p1 = (i + 1.5 + w3 / g1) / size;
    float cubic = g0.y * (g0.x * SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, p0, coordinates.z, lod).r
                        + g1.x * SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, float2(p1.x, p0.y), coordinates.z, lod).r)
                + g1.y * (g0.x * SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, float2(p0.x, p1.y), coordinates.z, lod).r
                        + g1.x * SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, p1, coordinates.z, lod).r);
    float bilinear = SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, coordinates.xy, coordinates.z, lod).r;
    float2 edge = min(coordinates.xy, 1 - coordinates.xy) * size;
    return lerp(bilinear, cubic, smoothstep(1, 2.5, min(edge.x, edge.y)));
}

// Map relief normal from central differences of the filtered map along the planet tangents.
// Screen derivatives are constant across each 2x2 pixel quad and differentiate a near-Nyquist
// map at orbit; under grazing light (terminator, limb) that prints as speckle. Differences one
// filtered texel apart are smooth per pixel and do not depend on the view.
float3 SurfaceMapNormal(float3 direction, float lod, float texelMetres, float floorHeight)
{
    float3 up = abs(direction.y) < .99 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 east = normalize(cross(up, direction)), north = cross(direction, east);
    float stepMetres = texelMetres * exp2(lod);
    float angle = stepMetres / max(1, _TerrainRadius);
    float3 c;
    c = TerrainMapCoordinates(normalize(direction + east * angle));
    float hE = SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, c.xy, c.z, lod).r;
    c = TerrainMapCoordinates(normalize(direction - east * angle));
    float hW = SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, c.xy, c.z, lod).r;
    c = TerrainMapCoordinates(normalize(direction + north * angle));
    float hN = SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, c.xy, c.z, lod).r;
    c = TerrainMapCoordinates(normalize(direction - north * angle));
    float hS = SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, c.xy, c.z, lod).r;
    float gradientEast = (max(floorHeight, hE) - max(floorHeight, hW)) / (2 * stepMetres);
    float gradientNorth = (max(floorHeight, hN) - max(floorHeight, hS)) / (2 * stepMetres);
    return normalize(direction - east * gradientEast - north * gradientNorth);
}

float3 SurfaceHeightNormal(float3 direction, float height, float3 fallback, float3 dxMetres, float3 dyMetres)
{
    // Tangent steps come from the precise camera-relative metre gradients. Differentiating
    // the float unit direction quantizes to ~0.5 m near the ground and turns into pixel noise.
    float3 dx = dxMetres - direction * dot(direction, dxMetres);
    float3 dy = dyMetres - direction * dot(direction, dyMetres);
    float3 n = cross(dx + direction * ddx(height), dy + direction * ddy(height));
    n *= dot(n, direction) >= 0 ? 1 : -1;
    return dot(n,n) > 1e-12 ? normalize(n) : fallback;
}

// ---- Orbital-scale geology: shading only. Geometry and the seed-exact height map are untouched,
// so collision, landing heights and the CPU/GPU height comparison are unaffected. ----
float GeologyUnit(uint h) { return (h >> 8) * (1.0 / 16777216.0); }

// One crater population from a 3D cellular field sampled on the unit sphere. Returns the slope
// (d height / d horizontal distance, both in cell units, so dimensionless); accumulates fresh
// ejecta brightness and the dark fill of old, flooded floors.
float3 CraterScale(float3 direction, float cells, uint seed, float density, float depthRatio, float footprintDirection,
    float3 wind, inout float ejecta, inout float floorFill, inout float streak)
{
    float pixel = footprintDirection * cells;
    float visible = 1 - smoothstep(.05, .2, pixel);
    if (visible <= 0 || density <= 0) return 0;
    float3 p = direction * cells;
    int3 base = (int3)floor(p);
    float3 slope = 0;
    [loop] for (int z = -1; z <= 1; z++)
    [loop] for (int y = -1; y <= 1; y++)
    [loop] for (int x = -1; x <= 1; x++)
    {
        int3 cell = base + int3(x, y, z);
        uint h = SurfaceHash(asuint(cell), seed);
        if (GeologyUnit(h) > density) continue;
        uint h2 = SurfaceHash(asuint(cell) + 7u, seed ^ 0x9e3779b9u);
        uint h3 = SurfaceHash(asuint(cell) + 13u, seed ^ 0x85ebca6bu);
        float3 centre = cell + .15 + .7 * float3(h2 & 1023u, (h2 >> 10) & 1023u, (h2 >> 20) & 1023u) / 1023.0;
        float sizeDraw = (h3 & 0xffffu) / 65535.0;
        // Size-frequency: many small craters, few large ones.
        float radius = .1 + .48 * sizeDraw * sizeDraw * sizeDraw;
        float3 offset = p - centre;
        float distance = length(offset);
        float d = distance / radius;
        float age = ((h3 >> 16) & 0xffffu) / 65535.0;
        // Wind-swept tails: the rim shelters a lee of differently coloured sand. A tail fades out
        // before the 1.15-cell reach of the neighbour search, so it never ends at a cell wall.
        [branch] if (dot(wind, wind) > 0)
        {
            float along = dot(offset, wind);
            float across = length(offset - wind * along) / (radius * (.85 + .3 * along / radius));
            streak += (1 - smoothstep(.55, 1.1, along)) * smoothstep(radius * .7, radius * 1.3, along)
                * exp(-across * across * 1.6) * exp(-along / (radius * 4)) * visible;
        }
        if (d > 2.4) continue;
        float depth = depthRatio * radius * lerp(1, .3, age);
        // Parabolic bowl and a raised rim; slopes are analytic so no derivatives are differenced.
        float bowlSlope = d < 1 ? 2 * depth * d : 0;
        float rd = (d - 1) / .24;
        float rimSlope = depth * .38 * exp(-rd * rd) * (-2 * rd / .24);
        slope += offset / max(1e-5, distance) * (bowlSlope + rimSlope) / radius * visible;
        float fresh = (1 - age) * (1 - age);
        ejecta += fresh * exp(-max(0, d - .95) / .55) * smoothstep(.75, .95, d) * visible;
        floorFill += age * (1 - smoothstep(.8, 1, d)) * visible;
    }
    return slope;
}

// Crater populations from ~6% of the radius down to ~0.01%, each fading once its craters are
// smaller than a few pixels. density scales every population (0 disables cratering).
float3 CraterField(float3 direction, uint seed, float density, float depthRatio, float footprintDirection,
    float3 wind, out float ejecta, out float floorFill, out float streak)
{
    ejecta = 0; floorFill = 0; streak = 0;
    float3 slope = 0;
    slope += CraterScale(direction, 6, seed ^ 0x2f1a3c5du, .45 * density, depthRatio, footprintDirection, wind, ejecta, floorFill, streak);
    slope += CraterScale(direction, 17, seed ^ 0x7b3e9a11u, .6 * density, depthRatio, footprintDirection, wind, ejecta, floorFill, streak);
    slope += CraterScale(direction, 47, seed ^ 0x4c8d2e73u, .7 * density, depthRatio, footprintDirection, wind, ejecta, floorFill, streak);
    slope += CraterScale(direction, 131, seed ^ 0x19f0b6a5u, .78 * density, depthRatio, footprintDirection, wind, ejecta, floorFill, streak);
    slope += CraterScale(direction, 367, seed ^ 0x6a2d4f97u, .82 * density, depthRatio, footprintDirection, wind, ejecta, floorFill, streak);
    // Two finer populations for low orbit and approach; each returns before its loop until resolvable.
    slope += CraterScale(direction, 1031, seed ^ 0x3c6ef372u, .85 * density, depthRatio, footprintDirection, wind, ejecta, floorFill, streak);
    slope += CraterScale(direction, 2903, seed ^ 0x1b873593u, .88 * density, depthRatio, footprintDirection, wind, ejecta, floorFill, streak);
    return slope;
}

// Low plains (maria, basaltic sand seas) flood the actual lowlands: a warped threshold of a
// B-spline-smoothed coarse height. Outline octaves stop at ~1/60 of the radius and fade before
// they reach a pixel, so margins read as lobate flow fronts rather than a pixel-scale fringe.
// depth: 0 at the shore to 1 well inside, for the darker, thicker interior fill.
float LowPlains(float3 direction, float smoothHeight, uint seed, float level, float margin, float footprintDirection, out float depth)
{
    float warp = SurfaceNoise(direction * 2.7, seed ^ 0x3c6ef372u, 0xffffffffu);
    float3 warped = direction + float3(warp, SurfaceNoise(direction * 3.9, seed ^ 0x1f83d9abu, 0xffffffffu), -warp) * .05;
    float fine = 1 - smoothstep(.08, .3, footprintDirection * 61);
    float field = -smoothHeight + .3 * SurfaceNoise(warped * 5.3, seed ^ 0xa54ff53au, 0xffffffffu)
        + .13 * SurfaceNoise(warped * 19, seed ^ 0x510e527fu, 0xffffffffu)
        + .055 * fine * SurfaceNoise(warped * 61, seed ^ 0x9b05688cu, 0xffffffffu);
    float aa = max(.002, fwidth(field));
    depth = saturate((field - level) / .22);
    return smoothstep(level - aa, level + aa + margin, field);
}

// Europa-like lineae: segmented, slightly wandering arcs along random great circles.
float IceLineae(float3 direction, uint seed, float footprintDirection)
{
    float lines = 0;
    float3 wobble = float3(SurfaceNoise(direction * 7, seed ^ 0x243f6a88u, 0xffffffffu),
        SurfaceNoise(direction * 7, seed ^ 0x85a308d3u, 0xffffffffu), SurfaceNoise(direction * 7, seed ^ 0x13198a2eu, 0xffffffffu));
    float3 bent = normalize(direction + wobble * .025);
    [loop] for (int i = 0; i < 16; i++)
    {
        uint h = SurfaceHash(uint3(i, 31u, 7u), seed ^ 0x03707344u);
        uint h2 = SurfaceHash(uint3(i, 59u, 3u), seed ^ 0xa4093822u);
        float z = GeologyUnit(h) * 2 - 1, phase = GeologyUnit(h2) * 6.2831853;
        float3 normal = float3(sqrt(1 - z * z) * cos(phase), z, sqrt(1 - z * z) * sin(phase));
        float width = .0025 + .004 * GeologyUnit(h ^ 0x9e3779b9u);
        float distance = abs(dot(bent, normal));
        float aa = max(footprintDirection, 1e-5);
        float stripe = 1 - smoothstep(width, width + aa * 1.5, distance);
        // Each arc runs only part of the way around the moon.
        float segment = smoothstep(-.05, .25, SurfaceNoise(direction * 1.6 + i * 3.7, seed ^ 0x299f31d0u, 0xffffffffu));
        lines = max(lines, stripe * segment);
    }
    return lines;
}

// Relief finer than the height map, in shading only: ridged fBm slopes from about two map texels
// down to the pixel footprint (octaves fade as their cells approach a pixel). Without it, terrain
// between craters is featureless "clay" from the moment the map resolves until mesh geometry takes
// over near the ground. Returns a slope vector (dimensionless) in planet space.
float3 SubMapRelief(float3 direction, float texelAngle, float footprintAngle, uint seed)
{
    // Rough provinces (mountain belts, broken terrain) alternate with smooth plains.
    float province = .35 + .65 * smoothstep(-.15, .45, SurfaceNoise(direction * 5.3, seed ^ 0x5851f42du, 0xffffffffu));
    float3 slope = 0;
    float frequency = .5 / max(1e-7, texelAngle), strength = .075;
    [loop] for (int octave = 0; octave < 6; octave++)
    {
        float visible = 1 - smoothstep(.3, .9, footprintAngle * frequency);
        if (visible <= 0) break;
        float4 n = SurfaceNoiseGradient(direction * frequency + octave * 17.31, seed + (uint)octave * 0x9e3779b9u, 0xffffffffu);
        // The two broad octaves are ridged into crests and valleys; finer ones roughen them smoothly.
        slope += (octave < 2 ? -sign(n.x) : 1) * n.yzw * strength * visible;
        frequency *= 2.03; strength *= .82;
    }
    return slope * province;
}

// Close-range ground layers: x grass, y dirt, z rock (layersA); x sand, y snow, z regolith (layersB).
void EvaluateSurface(float3 direction, float3 positionWS, float3 detailPosition, float rawVertexHeight, float3 meshNormal,
    out float3 albedo, out float3 surfaceNormal, out float water, out float3 layersA, out float3 layersB)
{
    layersA = float3(0, 0, 1); layersB = 0;
    float geologyCraters = 0, geologyDepth = 0, geologyWind = 0, reliefRoughness = 0;
    float3 coordinates = TerrainMapCoordinates(direction);
    float texelMetres = 2 * _TerrainRadius / max(1, _MaterialMapResolution);
    // Camera-relative positions retain centimetres at the ground. A unit planet
    // direction has roughly0.5m float spacing on an Earth-sized body, making
    // its screen derivatives unsuitable for local material/normal footprints.
    float3 dxMetres=ddx(positionWS)/max(1e-18,_RenderUnitsPerMetre);
    float3 dyMetres=ddy(positionWS)/max(1e-18,_RenderUnitsPerMetre);
    float footprint=max(length(dxMetres),length(dyMetres));
    // Derivatives of face UV jump at a chart transition. Derive the mip level
    // from the continuous 3D direction instead, avoiding a bright/blurred seam.
    float mapLod = max(0, log2(max(.0001, footprint / texelMetres)));
    float mapHeight = SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap, coordinates.xy, coordinates.z, mapLod).r;
    // Material ownership follows the actual (morphed) mesh sample spacing.
    // Camera altitude used to repaint the same stationary triangle while the
    // observer descended. Coarse representations keep the seed-exact map;
    // resolved geometry smoothly takes ownership together with its morph.
    float closeGeometry = (1 - smoothstep(.08, .4, footprint / texelMetres))
        * (1 - smoothstep(texelMetres * .125, texelMetres * .75, _TerrainSampleMetres));
    float height = lerp(mapHeight, rawVertexHeight, closeGeometry);
    float normalizedHeight = height / max(1, _TerrainAmplitude);
    float plainsHeight = SurfaceHeightSmooth(coordinates, max(mapLod, log2(max(1, _MaterialMapResolution / 128)))) / max(1, _TerrainAmplitude);
    float latitude = abs(direction.y);
    uint seed = (uint)_LavaSeed.x | ((uint)_LavaSeed.y << 16);
    float regional = SurfaceNoise(direction * 41, seed ^ 0x1d872b41u, 0xffffffffu);
    float humidity = SurfaceNoise(direction * 9, seed ^ 0xb5297a4du, 0xffffffffu);
    float4 materialField = SurfaceFilteredNoiseGradient(detailPosition,64,footprint,seed ^ 0x68e31da4u)*.50
        + SurfaceFilteredNoiseGradient(detailPosition,8,footprint,seed ^ 0xb5297a4du)*.30
        + SurfaceFilteredNoiseGradient(detailPosition,.5,footprint,seed ^ 0x1b56c4e9u)*.20;
    float material=materialField.x;
    float3 mapNormal=SurfaceMapNormal(direction,mapLod,texelMetres,_HasOcean>.5?0:-1e20);
    // Far below one map texel the map slope is unresolved and its screen derivatives only
    // carry sampling quantization; geometry shading takes over there (closeGeometry).
    float mapResolved = saturate(footprint / texelMetres * 8);
    float3 coarseNormal = normalize(lerp(direction, mapNormal, _SurfaceNormalStrength * mapResolved));
    // Optional orbital relief exaggeration (1 keeps the physical slope).
    // Young ice shells are smooth apart from their lineae; full exaggeration turned their gentle
    // ridged relief into a field of "worms", so ice worlds take half of it.
    float exaggeration = _PlanetKind > 2.5 && _PlanetKind < 3.5 ? 1 + (_ReliefExaggeration - 1) * .5 : _ReliefExaggeration;
    if (exaggeration > 1.001)
        coarseNormal = normalize(direction + (mapNormal - direction * dot(direction, mapNormal))
            * _SurfaceNormalStrength * exaggeration * mapResolved);
    surfaceNormal = normalize(lerp(coarseNormal, meshNormal, closeGeometry));
    // Analytic material gradients avoid differencing quantized unit directions
    // or large float chart values. Solid terrain transforms preserve planet axes.
    float3 materialGradient=materialField.yzw;
    materialGradient-=surfaceNormal*dot(surfaceNormal,materialGradient);
    surfaceNormal=normalize(surfaceNormal-materialGradient*(.06*_MaterialDetailStrength*closeGeometry));
    water = 0;
    if (_HasOcean > .5)
    {
        // The coast is the zero contour of exactly the geometry's seeded height.
        // fwidth gives a one-pixel shoreline instead of vertex-color triangles.
        float shoreAA = max(.1, fwidth(height));
        water = 1 - smoothstep(-shoreAA, shoreAA, height);
        float3 deep = float3(.006, .028, .065);
        float3 shallow = float3(.018, .16, .20);
        float3 ocean = lerp(deep, shallow, exp(min(0, height) / max(80, _TerrainAmplitude * .055)));
        float aridity = saturate((latitude - .1) * .8 + .45 - humidity * .55 + regional * .2);
        float3 vegetation = lerp(float3(.07,.19,.055), float3(.32,.27,.11), aridity);
        vegetation *= .90 + regional * .18;
        // Forest and meadow patches on a ~0.5 km scale. The footprint filter takes them
        // to their mean from orbit, so they only appear on approach.
        float stands = SurfaceFilteredNoiseGradient(detailPosition, 512, footprint, seed ^ 0x2545f491u).x
            + .5 * SurfaceFilteredNoiseGradient(detailPosition, 64, footprint, seed ^ 0x6c8e9cf5u).x;
        vegetation *= 1 + clamp(stands, -1, 1) * float3(.34, .2, .26) * (1 - aridity * .6);
        float3 beach = float3(.56,.46,.28);
        float vegetated = smoothstep(1.5, max(18, _TerrainAmplitude * .004), height);
        float3 land = lerp(beach, vegetation, vegetated);
        layersA = float3(vegetated * (1 - aridity), vegetated * aridity, 0); layersB = float3(1 - vegetated, 0, 0);
        // Climate/large biome regions should not trace every ridge as a thin
        // white contour from orbit. Filter the same geological field for those
        // masks, while keeping the full-resolution zero contour for the coast.
        float climateLod = max(mapLod, log2(max(1, _MaterialMapResolution / 256)));
        float climateHeight = SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap,
            coordinates.xy, coordinates.z, climateLod).r;
        float biomeHeight = lerp(climateHeight, rawVertexHeight, closeGeometry) / max(1, _TerrainAmplitude);
        float rock = smoothstep(.24, .65, biomeHeight + regional * .06);
        land = lerp(land, float3(.30,.285,.26), rock);
        layersA *= 1 - rock; layersB *= 1 - rock; layersA.z += rock;
        float snow = smoothstep(.78, .95, latitude + biomeHeight * .30 + regional * .035);
        land = lerp(land, float3(.83,.90,.94), snow);
        layersA *= 1 - snow; layersB *= 1 - snow; layersB.y += snow;
        // Steep relief exposes bedrock. The slope comes from the seed-exact height map, so
        // it does not change with mesh density or LOD.
        float cliff = smoothstep(.22, .5, 1 - dot(coarseNormal, direction)) * (1 - snow * .7);
        float exposed = cliff * smoothstep(2, 30, height);
        land = lerp(land, float3(.36,.33,.3) * (.85 + regional * .2), exposed);
        layersA *= 1 - exposed; layersB *= 1 - exposed; layersA.z += exposed;
        albedo = lerp(land * (1 + material * _MaterialDetailStrength * .22), ocean, water);
        surfaceNormal = normalize(lerp(surfaceNormal, direction, water));
        reliefRoughness = (1 - water) * lerp(.35, 1, saturate(rock + exposed)) * smoothstep(1.5, 40, height);
    }
    else if (_PlanetKind > 1.5 && _PlanetKind < 2.5)
    {
        float phase = height / max(80, _TerrainAmplitude * .035) + regional * 1.2;
        float filteredStrata = sin(phase) * (1 - smoothstep(.5, 3, fwidth(phase)));
        // Strong elevation bands are useful at a canyon wall, but from orbit
        // they read as a drawn contour map. Large-scale mineral regions govern
        // the orbital palette; closeGeometry restores the fine rock layers.
        float strataContrast = lerp(.025, .275, closeGeometry);
        float broadGeology = saturate(.52 + humidity * .36 + regional * .12 + normalizedHeight * .16);
        albedo = lerp(float3(.38,.20,.09), float3(.73,.49,.25),
            saturate(broadGeology + filteredStrata * strataContrast));
        albedo *= 1 + regional * .12 + material * _MaterialDetailStrength * .26;
        // Dark basaltic sand seas in the lowlands, thinning out downwind into streaky margins.
        float footprintDirection = footprint / max(1, _TerrainRadius);
        float seaDepth;
        // Zonal wind streaks: noise stretched ~5x along latitude circles (east-west).
        float streakFade = 1 - smoothstep(.1, .35, footprintDirection * 160);
        float zonal = SurfaceNoise(direction * float3(32, 160, 32), seed ^ 0x2b7e1516u, 0xffffffffu) * .65
            + SurfaceNoise(direction * float3(70, 330, 70), seed ^ 0x28aed2a6u, 0xffffffffu) * .35 * streakFade;
        // Sand seas thin out over a wide, wind-combed margin instead of ending at a shoreline.
        float sea = LowPlains(direction, plainsHeight + zonal * .025, seed, .16, .06, footprintDirection, seaDepth) * _PlanetDetailStrength;
        float3 darkSand = float3(.17, .135, .1) * (.85 + regional * .25) * lerp(1.15, .85, seaDepth);
        albedo = lerp(albedo, darkSand, sea * (.62 + .25 * seaDepth) * (1 + zonal * .25));
        albedo *= 1 + zonal * .07 * _PlanetDetailStrength;
        // Bright dust settles on the flats; slopes shed it and show darker rock.
        float shed = smoothstep(.015, .09, 1 - dot(coarseNormal, direction)) * (1 - closeGeometry);
        albedo *= 1 - shed * .3 * _PlanetDetailStrength;
        geologyCraters = .35 * (1 - sea * .7); geologyDepth = .09;
        geologyWind = .6;
        reliefRoughness = (1 - sea * .8) * lerp(.55, 1, smoothstep(-.2, .5, normalizedHeight));
        float outcrop = smoothstep(.62, .85, broadGeology + filteredStrata * .25);
        layersA = float3(0, .25 * (1 - outcrop), outcrop); layersB = float3(.75 * (1 - outcrop), 0, 0);
    }
    else if (_PlanetKind > 2.5 && _PlanetKind < 3.5)
    {
        float ice = smoothstep(-.45,.35,normalizedHeight + regional * .055);
        albedo = lerp(float3(.23,.43,.57), float3(.80,.90,.96), ice);
        albedo *= 1 + material * _MaterialDetailStrength * .09;
        // Tectonic lineae: long reddish-brown arcs across a young, lightly cratered ice shell.
        float lineae = IceLineae(direction, seed, footprint / max(1, _TerrainRadius)) * _PlanetDetailStrength;
        albedo *= 1 - lineae * float3(.38, .5, .6);
        geologyCraters = .18; geologyDepth = .07;
        reliefRoughness = .3;
        layersA = float3(0, 0, 1 - ice); layersB = float3(0, ice, 0);
    }
    else
    {
        float highland = smoothstep(-.45,.5,normalizedHeight);
        albedo = lerp(_SurfaceTint.rgb * .62, float3(.43,.41,.38), highland);
        albedo *= 1 + regional * .12 + material * _MaterialDetailStrength * .25;
        // Bright, heavily cratered highlands and dark, younger flooded maria.
        float footprintDirection = footprint / max(1, _TerrainRadius), mareDepth = 0;
        float mare = (_PlanetKind > 3.5 ? 0 : LowPlains(direction, plainsHeight, seed, .05, .014, footprintDirection, mareDepth)) * _PlanetDetailStrength;
        // Successive flows differ in composition: bluish titanium-rich and browner units, darkest in the basin centre.
        float flows = SurfaceNoise(direction * 13, seed ^ 0x6d2b79f5u, 0xffffffffu) * .65
            + SurfaceNoise(direction * 41, seed ^ 0x3243f6a8u, 0xffffffffu) * .35 * (1 - smoothstep(.1, .35, footprintDirection * 41));
        float3 mareTone = lerp(float3(.45, .44, .45), float3(.53, .48, .42), saturate(.5 + flows)) * lerp(1.08, .8, mareDepth);
        albedo = lerp(albedo * 1.12, albedo * mareTone, mare);
        geologyCraters = _PlanetKind > 3.5 ? 0 : 1 - mare * .65; geologyDepth = .16;
        reliefRoughness = (1 - mare * .75) * lerp(.5, 1, highland);
        layersA = float3(0, 0, highland * .45); layersB = float3(0, 0, 1 - highland * .45);
    }
    [branch] if (_PlanetDetailStrength > .001 && reliefRoughness > .001 && closeGeometry < .999)
    {
        float3 relief = SubMapRelief(direction, texelMetres / max(1, _TerrainRadius), footprint / max(1, _TerrainRadius), seed ^ 0x7f4a7c15u);
        relief -= direction * dot(direction, relief);
        float reliefWeight = reliefRoughness * _PlanetDetailStrength * (1 - closeGeometry);
        surfaceNormal = normalize(surfaceNormal - relief * reliefWeight);
        // Dust drains off steep crests and gathers in hollows.
        albedo *= 1 - saturate(length(relief) * 2.2) * reliefWeight * geologyWind * .35;
    }
    // Craters below the map resolution, in shading only; real geometry takes over near the ground.
    [branch] if (_PlanetDetailStrength > .001 && geologyCraters > .001)
    {
        float ejecta, floorFill, streak;
        // Prevailing winds blow along latitude circles, turned a little by regional weather.
        float3 east = normalize(float3(direction.z, 0, -direction.x) + float3(1e-5, 0, 0));
        float3 wind = geologyWind > 0 ? normalize(east + cross(direction, east) * regional * .5) : 0;
        float3 slope = CraterField(direction, seed, geologyCraters, geologyDepth, footprint / max(1, _TerrainRadius), wind, ejecta, floorFill, streak);
        slope -= direction * dot(direction, slope);
        surfaceNormal = normalize(surfaceNormal - slope * _PlanetDetailStrength * (1 - closeGeometry));
        albedo *= 1 + (saturate(ejecta) * .55 - saturate(floorFill) * .22 - saturate(streak) * geologyWind * .5) * _PlanetDetailStrength;
    }
}

// ---- Close-range ground detail (texture arrays built by Scripts/build_ground_textures.py) ----
TEXTURE2D_ARRAY(_GroundDetail); SAMPLER(sampler_GroundDetail);
TEXTURE2D_ARRAY(_GroundNormal); SAMPLER(sampler_GroundNormal);
// Mean of each stored layer (ratio * 0.4 after clipping), so detail averages to exactly 1.
static const float GroundLayerMean[6] = { .363, .389, .399, .399, .400, .398 };

float3 GroundAxisWeights(float3 n)
{
    float3 w = pow(abs(n), 8);
    return w / max(1e-5, w.x + w.y + w.z);
}

// Planet-fixed metre coordinates; texture periods (2^k m) divide the 16384 m chart period.
float3 GroundAlbedo(float3 p, float3 axes, float layer, float scale)
{
    float3 c = 0;
    [branch] if (axes.x > .01) c += axes.x * SAMPLE_TEXTURE2D_ARRAY(_GroundDetail, sampler_GroundDetail, p.zy / scale, layer).rgb;
    [branch] if (axes.y > .01) c += axes.y * SAMPLE_TEXTURE2D_ARRAY(_GroundDetail, sampler_GroundDetail, p.xz / scale, layer).rgb;
    [branch] if (axes.z > .01) c += axes.z * SAMPLE_TEXTURE2D_ARRAY(_GroundDetail, sampler_GroundDetail, p.xy / scale, layer).rgb;
    return c / GroundLayerMean[(int)layer];
}

// Whiteout-blended triplanar normal (tangent-space normals stored in RG).
float3 GroundNormalBlend(float3 p, float3 n, float3 axes, float layer, float scale)
{
    float3 result = 0;
    [branch] if (axes.x > .01)
    {
        float2 t = SAMPLE_TEXTURE2D_ARRAY(_GroundNormal, sampler_GroundNormal, p.zy / scale, layer).rg * 2 - 1;
        float3 tn = float3(t + n.zy, sqrt(saturate(1 - dot(t, t))) * n.x);
        result += axes.x * tn.zyx;
    }
    [branch] if (axes.y > .01)
    {
        float2 t = SAMPLE_TEXTURE2D_ARRAY(_GroundNormal, sampler_GroundNormal, p.xz / scale, layer).rg * 2 - 1;
        float3 tn = float3(t + n.xz, sqrt(saturate(1 - dot(t, t))) * n.y);
        result += axes.y * tn.xzy;
    }
    [branch] if (axes.z > .01)
    {
        float2 t = SAMPLE_TEXTURE2D_ARRAY(_GroundNormal, sampler_GroundNormal, p.xy / scale, layer).rg * 2 - 1;
        float3 tn = float3(t + n.xy, sqrt(saturate(1 - dot(t, t))) * n.z);
        result += axes.z * tn.xyz;
    }
    return normalize(result);
}

// Adds metre-scale structure to the two dominant layers. It fades to nothing well before
// the pixel footprint reaches a metre, so orbital appearance and coarse colours are unchanged.
void ApplyGroundDetail(float3 detailPosition, float3 positionWS, float3 layersA, float3 layersB, float strength,
    inout float3 albedo, inout float3 normal)
{
    float3 dxMetres = ddx(positionWS) / max(1e-18, _RenderUnitsPerMetre);
    float3 dyMetres = ddy(positionWS) / max(1e-18, _RenderUnitsPerMetre);
    float footprint = max(length(dxMetres), length(dyMetres));
    const float near = 2, far = 16, macro = 128;
    float nearVisible = (1 - smoothstep(.06, .5, footprint)) * strength;
    float farVisible = (1 - smoothstep(.4, 3.2, footprint)) * strength;
    // The macro scale keeps mid-distance terrain from turning abruptly smooth.
    float macroVisible = (1 - smoothstep(3, 24, footprint)) * strength * .55;
    [branch] if (macroVisible <= .001) return;
    float weights[6] = { layersA.x, layersA.y, layersA.z, layersB.x, layersB.y, layersB.z };
    int first = 0, second = 1;
    [unroll] for (int i = 1; i < 6; i++) if (weights[i] > weights[first]) first = i;
    second = first == 0 ? 1 : 0;
    [unroll] for (int j = 0; j < 6; j++) if (j != first && weights[j] > weights[second]) second = j;
    float total = max(1e-4, weights[first] + weights[second]);
    float w0 = weights[first] / total, w1 = weights[second] / total;
    float3 axes = GroundAxisWeights(normal);
    float3 detail = lerp(1, w0 * GroundAlbedo(detailPosition, axes, first, macro)
        + (w1 > .02 ? w1 * GroundAlbedo(detailPosition, axes, second, macro) : w1), macroVisible);
    [branch] if (farVisible > .001)
    {
        float3 middle = w0 * GroundAlbedo(detailPosition, axes, first, far);
        middle += w1 > .02 ? w1 * GroundAlbedo(detailPosition, axes, second, far) : w1;
        detail *= lerp(1, middle, farVisible);
    }
    [branch] if (nearVisible > .001)
    {
        float3 fine = w0 * GroundAlbedo(detailPosition, axes, first, near);
        if (w1 > .02) fine += w1 * GroundAlbedo(detailPosition, axes, second, near);
        else fine += w1;
        // Coarse and fine scales multiply: the large one breaks up the repetition of the small one.
        detail *= lerp(1, fine, nearVisible);
        float3 bumped = GroundNormalBlend(detailPosition, normal, axes, first, near);
        normal = normalize(lerp(normal, bumped, nearVisible * w0 + (w1 > .02 ? 0 : nearVisible * w1)));
    }
    albedo *= detail;
}
#endif
