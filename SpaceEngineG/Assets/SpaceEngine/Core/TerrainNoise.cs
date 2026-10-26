using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    /// <summary>Direction-sampled 3D gradient noise: no latitude seam, pole singularity, or cube-face dependency.</summary>
    public static class TerrainNoise
    {
        public static double Height(Double3 unitDirection, PlanetDefinition planet)
        {
            if (planet == null) throw new ArgumentNullException(nameof(planet));
            Double3 p = unitDirection.Normalized;
            if (p.SqrLength == 0) throw new ArgumentOutOfRangeException(nameof(unitDirection));
            double continents = Fractal(p * 2.2, planet.Seed, 5, 0.52);
            double hills = Fractal(p * 17.0, unchecked(planet.Seed + 7919), 4, 0.5);
            double ridge = 1 - Math.Abs(Noise(p * 37.0, unchecked(planet.Seed + 104729)));
            ridge = ridge * ridge * ridge;
            double mountainMask = SmoothStep(-0.06, 0.30, continents);
            double fine = Fractal(p * 151.0, unchecked(planet.Seed + 15485863), 3, 0.5);
            double relief = continents * 1.20 + hills * 0.16 + ridge * mountainMask * 0.55 + fine * 0.045;
            // The ocean is a separate datum at radius, while this function includes the underlying seabed.
            return planet.TerrainHeight * (relief + (planet.HasOcean ? -0.08 : 0.2));
        }

        public static Color SurfaceColor(Double3 unitDirection, double height, PlanetDefinition planet)
        {
            double normalizedHeight = height / Math.Max(1, planet.TerrainHeight);
            float latitude = (float)Math.Abs(unitDirection.Normalized.y);
            if (planet.HasOcean && height < 0) return Color.Lerp(new Color(0.015f, 0.06f, 0.12f), new Color(0.035f, 0.24f, 0.29f), (float)SmoothStep(-0.4, 0, normalizedHeight));
            Color rock = Color.Lerp(planet.SurfaceTint, new Color(0.42f, 0.39f, 0.34f), (float)SmoothStep(0.14, 0.7, normalizedHeight));
            if (planet.HasOcean) rock = Color.Lerp(new Color(0.57f, 0.49f, 0.31f), rock, (float)SmoothStep(0, 0.035, normalizedHeight));
            float snow = (float)SmoothStep(0.63, 0.96, latitude + normalizedHeight * 0.3);
            return Color.Lerp(rock, new Color(0.88f, 0.92f, 0.94f), snow);
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
