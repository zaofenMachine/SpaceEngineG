#ifndef SPACE_ENGINE_STARFIELD_INCLUDED
#define SPACE_ENGINE_STARFIELD_INCLUDED

// Unresolved background stars as a function of galactic-frame direction.
// Each layer hashes equal-angle cube-face cells; a cell holds at most one star.
// Presence follows the integrated galactic radiance in that direction, so dust
// lanes thin the field and the disc band crowds it. Resolved neighbours remain
// the catalogue billboards; this layer only supplies what they cannot.

float4 _StarfieldSeeds;      // seed A, seed B, weight of B, global density
float4 _StarfieldOptics;     // angular pixel size (rad), brightness, band boost, minimum density
TEXTURE2D(_GalaxyRadiance); SAMPLER(sampler_GalaxyRadiance);

uint StarfieldHash(uint3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
    return v.x ^ v.y ^ v.z;
}

float StarfieldUnit(uint h) { return (h & 0x00ffffffu) * (1.0 / 16777216.0); }

// Photospheric colour from temperature, perceptually desaturated for point sources.
float3 StarfieldTint(float kelvin)
{
    float t = kelvin / 100.0;
    float3 c;
    c.r = t <= 66 ? 1.0 : saturate(1.29293618 * pow(t - 60, -0.1332047592));
    c.g = t <= 66 ? saturate(0.39008157 * log(t) - 0.63184144) : saturate(1.12989086 * pow(t - 60, -0.0755148492));
    c.b = t >= 66 ? 1.0 : (t <= 19 ? 0.0 : saturate(0.54320679 * log(t - 10) - 1.19625409));
    return lerp(dot(c, float3(.2126, .7152, .0722)).xxx, c, .78);
}

void StarfieldFace(float3 d, out uint face, out float2 uv)
{
    float3 a = abs(d);
    if (a.x >= a.y && a.x >= a.z) { face = d.x > 0 ? 0u : 1u; uv = d.yz / a.x; }
    else if (a.y >= a.z) { face = d.y > 0 ? 2u : 3u; uv = d.xz / a.y; }
    else { face = d.z > 0 ? 4u : 5u; uv = d.xy / a.z; }
}

float3 StarfieldDirection(uint face, float2 uv)
{
    float s = (face & 1u) == 0u ? 1.0 : -1.0;
    float3 d = face < 2u ? float3(s, uv.x, uv.y) : face < 4u ? float3(uv.x, s, uv.y) : float3(uv.x, uv.y, s);
    return normalize(d);
}

// One magnitude layer. cells: grid cells per face edge; probability: per-cell occupancy;
// flux range in display units; radius: neighbour search (0 = own cell, 1 = 3x3).
float3 StarfieldLayer(float3 d, float cells, uint seed, float probability, float2 flux, int radius, float pixel)
{
    uint face; float2 uv;
    StarfieldFace(d, face, uv);
    float2 warped = atan(uv) * (4.0 / PI);
    float2 grid = (warped * .5 + .5) * cells;
    int2 base = (int2)floor(grid);
    float3 sum = 0;
    [loop] for (int y = -radius; y <= radius; y++)
    [loop] for (int x = -radius; x <= radius; x++)
    {
        int2 cell = base + int2(x, y);
        if (any(cell < 0) || any(cell >= (int)cells)) continue;
        uint h = StarfieldHash(uint3(cell, face * 7919u + seed));
        if (StarfieldUnit(h) >= probability) continue;
        uint h2 = StarfieldHash(uint3(h, cell.y, seed + 17u));
        uint h3 = StarfieldHash(uint3(h2, cell.x, seed + 31u));
        float2 jitter = float2(StarfieldUnit(h2), StarfieldUnit(h3)) * .76 + .12;
        float2 starWarped = (cell + jitter) / cells * 2 - 1;
        float3 star = StarfieldDirection(face, tan(starWarped * (PI / 4.0)));
        float angle = length(cross(d, star));
        // Number counts rise steeply toward faint stars: most of a layer is near its floor.
        float u = StarfieldUnit(h3 ^ 0x9e3779b9u);
        float brightness = lerp(flux.x, flux.y, u * u * u);
        float sigma = pixel * (.55 + .28 * saturate(brightness));
        float r = angle / sigma;
        float psf = exp(-.5 * r * r) / (6.2831853 * (sigma / pixel) * (sigma / pixel));
        float kelvin = 2600 + 9000 * pow(StarfieldUnit(h2 ^ 0x51ed27u), 2.2);
        kelvin = StarfieldUnit(h ^ 0x2c1b3c6du) > .985 ? 16000 + 14000 * StarfieldUnit(h3) : kelvin;
        sum += StarfieldTint(kelvin) * brightness * psf;
    }
    return sum;
}

// Galactic radiance at a screen position modulates local star counts.
float StarfieldDensity(float bandLuminance)
{
    float band = saturate(bandLuminance * _StarfieldOptics.z);
    return max(_StarfieldOptics.w, .28 + 1.9 * band * band + .9 * band) * _StarfieldSeeds.w;
}

float3 StarfieldSeeded(float3 d, uint seed, float density)
{
    float pixel = _StarfieldOptics.x;
    float3 c = 0;
    c += StarfieldLayer(d, 1024, seed + 11u, saturate(.10 * density), float2(.025, .11), 0, pixel);
    c += StarfieldLayer(d, 360, seed + 23u, saturate(.075 * density), float2(.08, .55), 1, pixel);
    c += StarfieldLayer(d, 110, seed + 37u, saturate(.09 * min(density, 2.2)), float2(.35, 2.2), 1, pixel);
    c += StarfieldLayer(d, 32, seed + 53u, saturate(.12 * min(density, 1.6)), float2(1.6, 9.0), 1, pixel);
    return c;
}

float3 Starfield(float3 direction, float bandLuminance)
{
    float density = StarfieldDensity(bandLuminance);
    if (density <= .0001) return 0;
    float3 c = StarfieldSeeded(direction, (uint)_StarfieldSeeds.x, density) * (1 - _StarfieldSeeds.z);
    [branch] if (_StarfieldSeeds.z > .001)
        c += StarfieldSeeded(direction, (uint)_StarfieldSeeds.y, density) * _StarfieldSeeds.z;
    return c * _StarfieldOptics.y;
}

#endif
