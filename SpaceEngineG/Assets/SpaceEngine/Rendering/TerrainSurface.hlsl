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
float3 SurfaceHeightNormal(float3 direction, float height, float3 fallback)
{
    float3 dx = ddx(direction) * _TerrainRadius;
    float3 dy = ddy(direction) * _TerrainRadius;
    float3 n = cross(dx + direction * ddx(height), dy + direction * ddy(height));
    n *= dot(n, direction) >= 0 ? 1 : -1;
    return dot(n,n) > 1e-12 ? normalize(n) : fallback;
}
void EvaluateSurface(float3 direction, float3 positionWS, float3 detailPosition, float rawVertexHeight, float3 meshNormal,
    out float3 albedo, out float3 surfaceNormal, out float water)
{
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
    float latitude = abs(direction.y);
    uint seed = (uint)_LavaSeed.x | ((uint)_LavaSeed.y << 16);
    float regional = SurfaceNoise(direction * 41, seed ^ 0x1d872b41u, 0xffffffffu);
    float humidity = SurfaceNoise(direction * 9, seed ^ 0xb5297a4du, 0xffffffffu);
    float4 materialField = SurfaceFilteredNoiseGradient(detailPosition,64,footprint,seed ^ 0x68e31da4u)*.50
        + SurfaceFilteredNoiseGradient(detailPosition,8,footprint,seed ^ 0xb5297a4du)*.30
        + SurfaceFilteredNoiseGradient(detailPosition,.5,footprint,seed ^ 0x1b56c4e9u)*.20;
    float material=materialField.x;
    float3 mapNormal=SurfaceHeightNormal(direction,max(_HasOcean>.5?0:-1e20,mapHeight),direction);
    float3 coarseNormal = normalize(lerp(direction, mapNormal, _SurfaceNormalStrength));
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
        float3 beach = float3(.56,.46,.28);
        float3 land = lerp(beach, vegetation, smoothstep(5, max(80, _TerrainAmplitude * .025), height));
        // Climate/large biome regions should not trace every ridge as a thin
        // white contour from orbit. Filter the same geological field for those
        // masks, while keeping the full-resolution zero contour for the coast.
        float climateLod = max(mapLod, log2(max(1, _MaterialMapResolution / 256)));
        float climateHeight = SAMPLE_TEXTURE2D_ARRAY_LOD(_SurfaceHeightMap, sampler_SurfaceHeightMap,
            coordinates.xy, coordinates.z, climateLod).r;
        float biomeHeight = lerp(climateHeight, rawVertexHeight, closeGeometry) / max(1, _TerrainAmplitude);
        float rock = smoothstep(.24, .65, biomeHeight + regional * .06);
        land = lerp(land, float3(.30,.285,.26), rock);
        float snow = smoothstep(.78, .95, latitude + biomeHeight * .30 + regional * .035);
        land = lerp(land, float3(.83,.90,.94), snow);
        albedo = lerp(land * (1 + material * _MaterialDetailStrength * .22), ocean, water);
        surfaceNormal = normalize(lerp(surfaceNormal, direction, water));
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
    }
    else if (_PlanetKind > 2.5 && _PlanetKind < 3.5)
    {
        float ice = smoothstep(-.45,.35,normalizedHeight + regional * .055);
        albedo = lerp(float3(.23,.43,.57), float3(.80,.90,.96), ice);
        albedo *= 1 + material * _MaterialDetailStrength * .09;
    }
    else
    {
        albedo = lerp(_SurfaceTint.rgb * .62, float3(.43,.41,.38), smoothstep(-.45,.5,normalizedHeight));
        albedo *= 1 + regional * .12 + material * _MaterialDetailStrength * .25;
    }
}
#endif
