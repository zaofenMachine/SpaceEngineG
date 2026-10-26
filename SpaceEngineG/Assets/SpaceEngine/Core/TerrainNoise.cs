using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    /// <summary>Direction-sampled continental noise plus metre-scaled relief; no latitude seam or cube-face dependency.</summary>
    public static class TerrainNoise
    {
        private struct Crater
        {
            public Double3 Centre;
            public double InverseChordRadiusSquared, Strength;
        }
        [ThreadStatic] private static Crater[] cachedCraters;
        [ThreadStatic] private static int cachedCraterSeed;

        public static double Height(Double3 unitDirection, PlanetDefinition planet)
        {
            if (planet == null) throw new ArgumentNullException(nameof(planet));
            Double3 p = unitDirection.Normalized;
            if (p.SqrLength == 0) throw new ArgumentOutOfRangeException(nameof(unitDirection));
            if (!planet.IsLandable) return 0;
            double continents = Fractal(p * 2.2, planet.Seed, 5, 0.52);
            double hills = Fractal(p * 17.0, unchecked(planet.Seed + 7919), 4, 0.5);
            double ridge = 1 - Math.Abs(Noise(p * 37.0, unchecked(planet.Seed + 104729)));
            ridge = ridge * ridge * ridge;
            double mountainMask = SmoothStep(-0.06, 0.30, continents);
            double fine = Fractal(p * 151.0, unchecked(planet.Seed + 15485863), 3, 0.5);
            double relief = continents * 1.20 + hills * 0.16 + ridge * mountainMask * 0.55 + fine * 0.045;
            // Continental direction-space noise alone becomes almost a plane at human scale.
            // These five physical bands keep hills, gullies and rocks in the actual geometry:
            // their spacing stays in metres when planet radius or mesh LOD changes.
            Double3 metres = p * planet.Radius;
            double smallRelief = Noise(metres / 10000.0, unchecked(planet.Seed + 32452843)) * 350.0
                + Noise(metres / 1600.0, unchecked(planet.Seed + 49979687)) * 220.0
                + Noise(metres / 200.0, unchecked(planet.Seed + 67867967)) * 40.0
                + Noise(metres / 25.0, unchecked(planet.Seed + 86028121)) * 5.0
                + Noise(metres / 6.0, unchecked(planet.Seed + 104395301)) * 0.75;
            double detailStrength = Math.Min(1, Math.Max(0, planet.TerrainHeight) / 8000.0);
            relief += smallRelief * detailStrength / Math.Max(1, planet.TerrainHeight);
            if (!planet.HasOcean)
            {
                if (planet.Kind == PlanetKind.Rocky || planet.Kind == PlanetKind.Ice)
                    relief += CraterField(p, planet.Seed) * (planet.Kind == PlanetKind.Ice ? .7 : 1);
                else if (planet.Kind == PlanetKind.Lava)
                    relief -= LavaFissureMask(p, planet.Seed) * .16;
            }
            // The ocean is a separate datum at radius, while this function includes the underlying seabed.
            // Smooth bounding gives culling/LOD a strict +/-TerrainHeight envelope without clipped plateaus.
            return planet.TerrainHeight * Math.Tanh(relief + (planet.HasOcean ? -0.08 : 0.2));
        }

        public static Color SurfaceColor(Double3 unitDirection, double height, PlanetDefinition planet)
        {
            if (planet == null) throw new ArgumentNullException(nameof(planet));
            Double3 direction = unitDirection.Normalized;
            double normalizedHeight = height / Math.Max(1, planet.TerrainHeight);
            if (!planet.HasOcean)
            {
                Color typed;
                switch (planet.Kind)
                {
                    case PlanetKind.Desert:
                        double strata = .5 + .5 * Math.Sin(height / Math.Max(80, planet.TerrainHeight * .035)
                            + Noise(direction * 11, unchecked(planet.Seed + 271828)) * 1.2);
                        typed = Color.Lerp(new Color(.50f, .27f, .12f), new Color(.84f, .61f, .34f), (float)strata);
                        break;
                    case PlanetKind.Ice:
                        typed = Color.Lerp(new Color(.38f, .63f, .79f), new Color(.91f, .97f, 1f),
                            (float)SmoothStep(-.45, .35, normalizedHeight));
                        break;
                    case PlanetKind.Lava:
                        float emission = (float)LavaFissureMask(direction, planet.Seed);
                        typed = Color.Lerp(new Color(.055f, .035f, .030f), new Color(1f, .17f, .012f), emission);
                        typed.a = emission;
                        return typed;
                    case PlanetKind.GasGiant:
                    case PlanetKind.IceGiant:
                        typed = planet.SurfaceTint;
                        break;
                    default:
                        typed = Color.Lerp(planet.SurfaceTint * .62f, new Color(.53f, .50f, .47f),
                            (float)SmoothStep(-.45, .5, normalizedHeight));
                        break;
                }
                typed.a = 0;
                return typed;
            }
            float latitude = (float)Math.Abs(direction.y);
            if (height < 0)
            {
                Color sea = Color.Lerp(new Color(0.015f, 0.06f, 0.12f), new Color(0.035f, 0.24f, 0.29f), (float)SmoothStep(-0.4, 0, normalizedHeight));
                sea.a = 0;
                return sea;
            }
            Color rock = Color.Lerp(planet.SurfaceTint, new Color(0.42f, 0.39f, 0.34f), (float)SmoothStep(0.14, 0.7, normalizedHeight));
            if (planet.HasOcean) rock = Color.Lerp(new Color(0.57f, 0.49f, 0.31f), rock, (float)SmoothStep(0, 0.035, normalizedHeight));
            float snow = (float)SmoothStep(0.63, 0.96, latitude + normalizedHeight * 0.3);
            Color result = Color.Lerp(rock, new Color(0.88f, 0.92f, 0.94f), snow);
            result.a = 0;
            return result;
        }

        /// <summary>Direction-space hot fissure mask, also written to vertex COLOR.a by the mesh builder.</summary>
        public static double LavaFissureMask(Double3 direction, int seed)
        {
            Double3 p = direction.Normalized;
            double field = Noise(p * 24, unchecked(seed + 1301081))
                + .16 * Noise(p * 55, unchecked(seed + 15485863));
            return 1 - SmoothStep(.018, .075, Math.Abs(field));
        }

        private static double CraterField(Double3 direction, int seed)
        {
            // Construct these ten analytic features once per seed/thread, never once per vertex.
            // Sampling then uses ten dot products; expensive rim evaluation runs only inside a crater.
            if (cachedCraters == null || cachedCraterSeed != seed)
            {
                cachedCraterSeed = seed;
                cachedCraters = new Crater[10];
                var random = new DeterministicRandom(DeterministicRandom.Hash(seed, 719, 3121, 9973));
                for (int i = 0; i < cachedCraters.Length; i++)
                {
                    double y = random.Range(-1.0, 1.0), phase = random.Range(0, Math.PI * 2);
                    double horizontal = Math.Sqrt(1 - y * y), angularRadius = random.Range(.045, .16);
                    cachedCraters[i] = new Crater
                    {
                        Centre = new Double3(horizontal * Math.Cos(phase), y, horizontal * Math.Sin(phase)),
                        InverseChordRadiusSquared = 1 / (2 * (1 - Math.Cos(angularRadius))),
                        Strength = random.Range(.55, 1.0)
                    };
                }
            }
            double result = 0;
            for (int i = 0; i < cachedCraters.Length; i++)
            {
                Crater crater = cachedCraters[i];
                double q = Math.Max(0, 2 * (1 - Double3.Dot(direction, crater.Centre))) * crater.InverseChordRadiusSquared;
                if (q >= 1.5) continue;
                double bowl = q < 1 ? -.75 * (1 - q) * (1 - q) : 0;
                double rimDistance = (q - .92) / .21;
                double rim = .21 * Math.Exp(-rimDistance * rimDistance) * (1 - SmoothStep(1.2, 1.5, q));
                result += (bowl + rim) * crater.Strength;
            }
            return result;
        }

        public static double Noise(Double3 p, int seed)
        {
            long ix = (long)Math.Floor(p.x), iy = (long)Math.Floor(p.y), iz = (long)Math.Floor(p.z);
            double x = p.x - ix, y = p.y - iy, z = p.z - iz;
            double u = Fade(x), v = Fade(y), w = Fade(z);
            double z0 = Lerp(Lerp(Gradient(seed, ix, iy, iz, x, y, z), Gradient(seed, ix + 1, iy, iz, x - 1, y, z), u),
                Lerp(Gradient(seed, ix, iy + 1, iz, x, y - 1, z), Gradient(seed, ix + 1, iy + 1, iz, x - 1, y - 1, z), u), v);
            double z1 = Lerp(Lerp(Gradient(seed, ix, iy, iz + 1, x, y, z - 1), Gradient(seed, ix + 1, iy, iz + 1, x - 1, y, z - 1), u),
                Lerp(Gradient(seed, ix, iy + 1, iz + 1, x, y - 1, z - 1), Gradient(seed, ix + 1, iy + 1, iz + 1, x - 1, y - 1, z - 1), u), v);
            return Lerp(z0, z1, w);
        }

        private static double Fractal(Double3 p, int seed, int octaves, double persistence)
        {
            double sum = 0, amplitude = 1, weight = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(p, unchecked(seed + i * 1013)) * amplitude; weight += amplitude;
                p = p * 2.07; amplitude *= persistence;
            }
            return sum / weight;
        }

        private static double Gradient(int seed, long x, long y, long z, double dx, double dy, double dz)
        {
            int h = (int)(DeterministicRandom.Hash(seed, x, y, z) % 12);
            switch (h)
            {
                case 0: return dx + dy; case 1: return -dx + dy; case 2: return dx - dy; case 3: return -dx - dy;
                case 4: return dx + dz; case 5: return -dx + dz; case 6: return dx - dz; case 7: return -dx - dz;
                case 8: return dy + dz; case 9: return -dy + dz; case 10: return dy - dz; default: return -dy - dz;
            }
        }
        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double SmoothStep(double a, double b, double value)
        {
            double t = Math.Max(0, Math.Min(1, (value - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }
    }
}
