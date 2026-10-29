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
// Close-range ground layers: x grass, y dirt, z rock (layersA); x sand, y snow, z regolith (layersB).
void EvaluateSurface(float3 direction, float3 positionWS, float3 detailPosition, float rawVertexHeight, float3 meshNormal,
    out float3 albedo, out float3 surfaceNormal, out float water, out float3 layersA, out float3 layersB)
{
    layersA = float3(0, 0, 1); layersB = 0;
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
    float3 mapNormal=SurfaceHeightNormal(direction,max(_HasOcean>.5?0:-1e20,mapHeight),direction,dxMetres,dyMetres);
    // Far below one map texel the map slope is unresolved and its screen derivatives only
    // carry sampling quantization; geometry shading takes over there (closeGeometry).
    float mapResolved = saturate(footprint / texelMetres * 8);
    float3 coarseNormal = normalize(lerp(direction, mapNormal, _SurfaceNormalStrength * mapResolved));
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
        float outcrop = smoothstep(.62, .85, broadGeology + filteredStrata * .25);
        layersA = float3(0, .25 * (1 - outcrop), outcrop); layersB = float3(.75 * (1 - outcrop), 0, 0);
    }
    else if (_PlanetKind > 2.5 && _PlanetKind < 3.5)
    {
        float ice = smoothstep(-.45,.35,normalizedHeight + regional * .055);
        albedo = lerp(float3(.23,.43,.57), float3(.80,.90,.96), ice);
        albedo *= 1 + material * _MaterialDetailStrength * .09;
        layersA = float3(0, 0, 1 - ice); layersB = float3(0, ice, 0);
    }
    else
    {
        float highland = smoothstep(-.45,.5,normalizedHeight);
        albedo = lerp(_SurfaceTint.rgb * .62, float3(.43,.41,.38), highland);
        albedo *= 1 + regional * .12 + material * _MaterialDetailStrength * .25;
        layersA = float3(0, 0, highland * .45); layersB = float3(0, 0, 1 - highland * .45);
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
