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

// Fixed-cost octave field with a screen-space low-pass at each spatial frequency.
// Filtered octaves tend to their mean; rotating the 3D domain avoids axis-aligned
// streaks without introducing a latitude/longitude texture seam.
float CelestialFbm(float3 p, float footprint, int layers)
{
    float value = 0, amplitude = .5, normalization = 0;
    [loop] for (int octave = 0; octave < 6; octave++)
    {
        if (octave >= layers) break;
        float visibility = 1 - smoothstep(.18, .8, footprint);
        value += lerp(.5, CelestialNoise(p), visibility) * amplitude;
        normalization += amplitude;
        p = float3(.00 * p.x + .80 * p.y + .60 * p.z,
                   -.80 * p.x + .36 * p.y - .48 * p.z,
                   -.60 * p.x - .48 * p.y + .64 * p.z) * 2.03 + 17.17;
        footprint *= 2.03;
        amplitude *= .51;
    }
    return value / max(.001, normalization);
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

float CelestialRingBand(float u, float start, float finish, float footprint)
{
    float soft = max(.004, footprint);
    return smoothstep(start - soft, start + soft, u) * (1 - smoothstep(finish - soft, finish + soft, u));
}

// Saturn-like system: a faint inner C ring, a dense B ring, a Cassini-like division, an A ring
// with a narrow gap, and a thin outer ringlet. Boundaries and densities shift with the seed;
// fine ringlets are band-limited so they average out instead of aliasing.
float CelestialRingDensity(float radius, float innerRatio, float outerRatio, float seed)
{
    float u = (radius - innerRatio) / max(.001, outerRatio - innerRatio);
    float footprint = max(.00001, fwidth(u));
    float jitter = sin(seed * 1.37) * .035, jitter2 = sin(seed * 2.91) * .025;
    float cEnd = .2 + jitter, bEnd = .56 + jitter2, gapEnd = bEnd + .05 + .015 * sin(seed * .7);
    float aEnd = .9 + .02 * sin(seed * 3.3);
    float gapA = aEnd - .07 + .02 * sin(seed * 5.1);
    float density = .22 * CelestialRingBand(u, 0, cEnd, footprint);
    // B ring: densest, brightening outward with broad internal structure.
    density += (.7 + .22 * smoothstep(cEnd, bEnd, u) + .08 * CelestialFilteredSin(u * 41 + seed)) * CelestialRingBand(u, cEnd, bEnd, footprint);
    density += .045 * CelestialRingBand(u, bEnd, gapEnd, footprint);
    density += (.55 - .12 * smoothstep(gapEnd, aEnd, u)) * CelestialRingBand(u, gapEnd, aEnd, footprint)
        * (1 - .92 * CelestialRingBand(u, gapA, gapA + .012, footprint));
    density += .5 * CelestialRingBand(u, .955, .968, footprint);
    // Unresolved ringlets: several octaves of filtered radial structure.
    float ringlets = .5 + .22 * CelestialFilteredSin(u * 173 + seed) + .14 * CelestialFilteredSin(u * 519 + seed * 2.31)
        + .09 * CelestialFilteredSin(u * 1247 + seed * .71) + .05 * CelestialFilteredSin(u * 3011 + seed * 1.9);
    density *= .72 + .56 * ringlets;
    return saturate(density) * step(0, u) * step(u, 1);
}

#endif
