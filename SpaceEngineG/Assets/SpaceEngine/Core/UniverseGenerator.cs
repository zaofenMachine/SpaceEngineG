using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    /// <summary>
    /// A reproducible sparse catalogue: one star system per jittered four-light-year cell.
    /// This is a procedural population model, not a real star catalogue or cosmological simulation.
    /// </summary>
    public static class UniverseGenerator
    {
        public const double CellSize = 4 * PhysicalConstants.LightYear;
        public const int GenerationVersion = 1;

        public static StarSystemDefinition GenerateSystem(int universeSeed, long cellX, long cellY, long cellZ)
        {
            ulong hash = DeterministicRandom.Hash(universeSeed, cellX, cellY, cellZ);
            var random = new DeterministicRandom(hash);
            int seed = unchecked((int)random.NextUInt64());
            Double3 jitter = new Double3(random.Range(-0.2, 0.2), random.Range(-0.2, 0.2), random.Range(-0.2, 0.2)) * CellSize;
            UniversalPosition position;
            if (cellX == 0 && cellY == 0 && cellZ == 0) position = UniversalPosition.Origin;
            else
            {
                long sx, sy, sz;
                double lx, ly, lz;
                CellAxis(cellX, out sx, out lx); CellAxis(cellY, out sy, out ly); CellAxis(cellZ, out sz, out lz);
                position = new UniversalPosition(sx, sy, sz, new Double3(lx, ly, lz)).Add(jitter);
            }
            double massRatio = random.Range(0.72, 1.25);
            var system = new StarSystemDefinition
            {
                Name = $"SEG-{hash & 0xffffff:X6}", Seed = seed, Position = position,
                StarMass = massRatio * PhysicalConstants.SolarMass,
                StarRadius = Math.Pow(massRatio, 0.8) * PhysicalConstants.SolarRadius,
                StarTemperature = 5772 * Math.Pow(massRatio, 0.5),
                Planets = new PlanetDefinition[random.Range(4, 8)]
            };
            double orbitAu = random.Range(0.72, 1.1) * Math.Pow(massRatio, 1.75);
            for (int i = 0; i < system.Planets.Length; i++)
            {
                int planetSeed = unchecked((int)random.NextUInt64());
                double radiusRatio = i == 0 ? random.Range(0.85, 1.12) : random.Range(0.42, 1.9);
                bool ocean = i == 0 || (i < 3 && random.NextDouble() > 0.55);
                var tint = ocean ? new Color((float)random.Range(0.17, 0.3), (float)random.Range(0.29, 0.46), (float)random.Range(0.12, 0.22))
                    : new Color((float)random.Range(0.35, 0.62), (float)random.Range(0.22, 0.45), (float)random.Range(0.12, 0.3));
                system.Planets[i] = new PlanetDefinition
                {
                    Name = $"{system.Name} {(char)('b' + i)}", Seed = planetSeed,
                    Radius = radiusRatio * PhysicalConstants.EarthRadius,
                    Mass = Math.Pow(radiusRatio, 3) * random.Range(0.72, 1.2) * PhysicalConstants.EarthMass,
                    SemiMajorAxis = orbitAu * PhysicalConstants.AstronomicalUnit,
                    Eccentricity = random.Range(0.002, 0.13), Inclination = random.Range(-0.07, 0.07),
                    LongitudeAscendingNode = random.Range(0, 2 * Math.PI), ArgumentPeriapsis = random.Range(0, 2 * Math.PI),
                    MeanAnomalyAtEpoch = random.Range(0, 2 * Math.PI),
                    AtmosphereHeight = (i == 0 ? random.Range(85000, 125000) : random.Range(0, 150000)) * radiusRatio,
                    TerrainHeight = random.Range(5000, 14000), HasOcean = ocean, SurfaceTint = tint
                };
                orbitAu *= random.Range(1.65, 2.05);
            }
            return system;
        }

        private static void CellAxis(long cell, out long sector, out double local)
        {
            // CellSize is exactly 37842 sectors + 921890323200 m. Decimal keeps this integer remainder
            // exact at distant cell indices without ever flattening the whole position into a double.
            decimal remainder = checked((decimal)cell * 921890323200m);
            decimal carry = decimal.Truncate(remainder / 1000000000000m);
            sector = checked((long)((decimal)cell * 37842m + carry));
            local = (double)(remainder - carry * 1000000000000m);
        }
    }
}
