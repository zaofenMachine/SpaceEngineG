#ifndef SPACE_ENGINE_CELESTIAL_PROCEDURAL_INCLUDED
#define SPACE_ENGINE_CELESTIAL_PROCEDURAL_INCLUDED

float CelestialHash(float3 p)
{
    p = frac(p * .1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float CelestialNoise(float3 p)
{
    float3 cell = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(CelestialHash(cell), CelestialHash(cell + float3(1,0,0)), f.x),
                     lerp(CelestialHash(cell + float3(0,1,0)), CelestialHash(cell + float3(1,1,0)), f.x), f.y),
                lerp(lerp(CelestialHash(cell + float3(0,0,1)), CelestialHash(cell + float3(1,0,1)), f.x),
                     lerp(CelestialHash(cell + float3(0,1,1)), CelestialHash(cell + float3(1,1,1)), f.x), f.y), f.z);
}

// A dimensionless, deterministic radial optical-depth profile shared by rings and ring shadows.
// Gaps and band density are procedural appearance parameters; no individual particles are simulated.
float CelestialSinc(float x)
{
    float denominator = abs(x) < .001 ? .001 : x;
    return abs(x) < .001 ? 1 - x * x / 6 : sin(x) / denominator;
}

// Exact first-order pixel-box integration of a sinusoidal field, with a conservative
// Nyquist taper. Unresolved bands converge to their mean instead of producing moire.
float CelestialFilteredSin(float phase)
{
    float dx = ddx(phase), dy = ddy(phase);
    float box = CelestialSinc(dx * .5) * CelestialSinc(dy * .5);
    float limit = 1 - smoothstep(2.0, 3.4, abs(dx) + abs(dy));
    return sin(phase) * box * limit;
}

float CelestialRingDensity(float radius, float innerRatio, float outerRatio, float seed)
{
    float u = (radius - innerRatio) / max(.001, outerRatio - innerRatio);
    float footprint = max(.00001, fwidth(u));
    float edge = smoothstep(0, max(.018, footprint), u) * (1 - smoothstep(1 - max(.02, footprint), 1, u));
    float bands = .45 + .12 * CelestialFilteredSin(u * 31 + seed * .31);
    bands += .22 * CelestialFilteredSin(u * 173 + seed) + .13 * CelestialFilteredSin(u * 519 + seed * 2.31);
    float fine = .72 + .28 * CelestialFilteredSin(u * 1247 + seed * .71);
    float gapCenter = .53 + .09 * sin(seed);
    float gap = smoothstep(max(0, .012 - footprint * .5), .027 + footprint * .5, abs(u - gapCenter));
    return saturate(bands * fine) * gap * edge * step(0, u) * step(u, 1);
}

#endif
