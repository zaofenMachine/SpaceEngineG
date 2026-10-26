#ifndef SPACE_ENGINE_TERRAIN_MATERIAL_NOISE_INCLUDED
#define SPACE_ENGINE_TERRAIN_MATERIAL_NOISE_INCLUDED

// Portable 64-bit SplitMix arithmetic expressed as (low, high) uint words.
// Exact integer compatibility with Core/DeterministicRandom is essential:
// a different hash would produce a sharp but unrelated coastline.
uint2 TerrainAdd64(uint2 a, uint2 b)
{
    // Derive carry from bounded 16-bit sums. In the tested shader compiler, the
    // otherwise valid comparison `(a.x + b.x) < a.x` was folded incorrectly
    // when a.x came from a negative lattice coordinate plus a corner offset.
    // This explicit carry keeps neighbouring noise corners CPU compatible.
    uint lower = (a.x & 65535u) + (b.x & 65535u);
    uint upper = (a.x >> 16) + (b.x >> 16) + (lower >> 16);
    uint low = (upper << 16) | (lower & 65535u);
    return uint2(low, a.y + b.y + (upper >> 16));
}
uint2 TerrainShift64(uint2 value, uint bits)
{
    return uint2((value.x >> bits) | (value.y << (32u - bits)), value.y >> bits);
}
uint2 TerrainMultiply64(uint2 a, uint2 b)
{
    // Four 16-bit products expose the high word on HLSL SM5 without
    // non-portable GLSL intrinsics or optional native 64-bit arithmetic.
    uint a0 = a.x & 65535u, a1 = a.x >> 16;
    uint b0 = b.x & 65535u, b1 = b.x >> 16;
    uint w0 = a0 * b0;
    uint t = a1 * b0 + (w0 >> 16);
    uint w1 = a0 * b1 + (t & 65535u);
    uint low = (w1 << 16) + (w0 & 65535u);
    uint high = a1 * b1 + (t >> 16) + (w1 >> 16);
    return uint2(low, high + a.x * b.y + a.y * b.x);
}
uint2 TerrainMix64(uint2 value)
{
    value = TerrainMultiply64(value ^ TerrainShift64(value, 30u), uint2(0x1ce4e5b9u, 0xbf58476du));
    value = TerrainMultiply64(value ^ TerrainShift64(value, 27u), uint2(0x133111ebu, 0x94d049bbu));
    return value ^ TerrainShift64(value, 31u);
}
uint2 TerrainSign64(int value) { return uint2(asuint(value), value < 0 ? 0xffffffffu : 0u); }
uint2 TerrainSeedKey(uint seed)
{
    return TerrainMix64(TerrainAdd64(TerrainSign64(asint(seed)), uint2(0x7f4a7c15u, 0x9e3779b9u)));
}
uint2 TerrainHash64(int3 cell, uint2 seedKey)
{
    uint2 h = TerrainMix64(seedKey ^ TerrainSign64(cell.x));
    h = TerrainMix64(h ^ TerrainAdd64(TerrainSign64(cell.y), uint2(0xd9b4e019u, 0x632be59bu)));
    h = TerrainMix64(h ^ TerrainAdd64(TerrainSign64(cell.z), uint2(0x2f3d8dd7u, 0x8cb92ba7u)));
    return h;
}
uint TerrainGradientIndex(int3 cell, uint2 seedKey)
{
    uint2 h = TerrainHash64(cell, seedKey);
    return ((h.x % 12u) + 4u * (h.y % 12u)) % 12u;
}
float TerrainGradient(int3 cell, uint2 seedKey, float3 d)
{
    uint h = TerrainGradientIndex(cell, seedKey);
    if (h < 4u) return (h == 1u || h == 3u ? -d.x : d.x) + (h >= 2u ? -d.y : d.y);
    if (h < 8u) return (h == 5u || h == 7u ? -d.x : d.x) + (h >= 6u ? -d.z : d.z);
    return (h == 9u || h == 11u ? -d.y : d.y) + (h >= 10u ? -d.z : d.z);
}
float TerrainExactNoise(float3 p, uint seed)
{
    int3 cell = (int3)floor(p);
    float3 d = p - floor(p);
    float3 w = d * d * d * (d * (d * 6 - 15) + 10);
    uint2 key = TerrainSeedKey(seed);
    float z0 = lerp(lerp(TerrainGradient(cell, key, d), TerrainGradient(cell + int3(1,0,0), key, d - float3(1,0,0)), w.x),
        lerp(TerrainGradient(cell + int3(0,1,0), key, d - float3(0,1,0)), TerrainGradient(cell + int3(1,1,0), key, d - float3(1,1,0)), w.x), w.y);
    float z1 = lerp(lerp(TerrainGradient(cell + int3(0,0,1), key, d - float3(0,0,1)), TerrainGradient(cell + int3(1,0,1), key, d - float3(1,0,1)), w.x),
        lerp(TerrainGradient(cell + int3(0,1,1), key, d - float3(0,1,1)), TerrainGradient(cell + int3(1,1,1), key, d - float3(1,1,1)), w.x), w.y);
    return lerp(z0, z1, w.z);
}
float TerrainExactFractal(float3 p, uint seed, int octaves, float persistence)
{
    float sum = 0, amplitude = 1, weight = 0;
    [loop] for (int octave = 0; octave < octaves; octave++)
    {
        sum += TerrainExactNoise(p, seed + (uint)octave * 1013u) * amplitude;
        weight += amplitude; p *= 2.07; amplitude *= persistence;
    }
    return sum / weight;
}
float3 TerrainFaceDirection(uint face, float2 uv)
{
    precise float3 p;
    if (face == 0u) p = float3(1, uv.y, -uv.x);
    else if (face == 1u) p = float3(-1, uv.y, uv.x);
    else if (face == 2u) p = float3(uv.x, 1, -uv.y);
    else if (face == 3u) p = float3(uv.x, -1, uv.y);
    else if (face == 4u) p = float3(uv.x, uv.y, 1);
    else p = float3(-uv.x, uv.y, -1);
    precise float inverseLength = rsqrt(p.x*p.x + p.y*p.y + p.z*p.z);
    return p * inverseLength;
}
#endif
