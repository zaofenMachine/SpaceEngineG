using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    /// <summary>
    /// Seeded candidate systems in jittered four-light-year cells. Galaxy density controls admission;
    /// GenerateSystem remains available to inspect a candidate regardless of admission.
    /// This is a population approximation, not an observed catalogue or formation simulation.
    /// </summary>
    public static class UniverseGenerator
    {
        public const double CellSize = 4 * PhysicalConstants.LightYear;
        public const int GenerationVersion = 3;
        public const int LegacyGenerationVersion = 2;
        private const double SolarLuminosity = 3.828e26;
        private const double StefanBoltzmann = 5.670374419e-8;
        private const double ParsecInLightYears = 3.2615637771674336;
        private const ulong OccupancyStream = 0x70f74f89f61a2b75UL;
        [ThreadStatic] private static GalaxyDefinition cachedGalaxy;
        [ThreadStatic] private static int cachedGalaxySeed;

        private static GalaxyDefinition DefaultGalaxy(int seed)
        {
            if (cachedGalaxy == null || cachedGalaxySeed != seed)
            {
                cachedGalaxy = GalaxyModel.Default(seed);
                cachedGalaxySeed = seed;
            }
            return cachedGalaxy;
        }

        public static bool IsCellOccupied(int universeSeed, long cellX, long cellY, long cellZ)
            => IsCellOccupied(universeSeed, cellX, cellY, cellZ, DefaultGalaxy(universeSeed));

        /// <summary>Allocation-free admission query after the caller creates its galaxy settings.</summary>
        public static bool IsCellOccupied(int universeSeed, long cellX, long cellY, long cellZ, GalaxyDefinition galaxy)
        {
            if (galaxy == null) throw new ArgumentNullException(nameof(galaxy));
            if (cellX == 0 && cellY == 0 && cellZ == 0) return true;
            ulong hash = DeterministicRandom.Hash(universeSeed, cellX, cellY, cellZ);
            var random = new DeterministicRandom(hash);
            int seed;
            UniversalPosition candidate = CandidatePosition(ref random, cellX, cellY, cellZ, out seed);
            double density = GalaxyModel.SampleDensity(GalaxyModel.ToGalacticLy(candidate, galaxy), galaxy);
            return Admit(hash, density);
        }

        public static bool TryGenerateSystem(int universeSeed, long cellX, long cellY, long cellZ, out StarSystemDefinition system)
            => TryGenerateSystem(universeSeed, cellX, cellY, cellZ, DefaultGalaxy(universeSeed), out system);

        public static bool TryGenerateSystem(int universeSeed, long cellX, long cellY, long cellZ, GalaxyDefinition galaxy, out StarSystemDefinition system)
        {
            if (!IsCellOccupied(universeSeed, cellX, cellY, cellZ, galaxy)) { system = null; return false; }
            system = GenerateSystem(universeSeed, cellX, cellY, cellZ, galaxy);
            return true;
        }

        private static bool Admit(ulong hash, double density)
        {
            // Poisson cells may contain unresolved stars; this streamer resolves at most one.
            double volumePc3 = Math.Pow(4.0 / ParsecInLightYears, 3);
            double probability = 1 - Math.Exp(-Math.Max(0, density) * volumePc3);
            double draw = (DeterministicRandom.Mix(hash ^ OccupancyStream) >> 11) * (1.0 / 9007199254740992.0);
            return draw < probability;
        }

        public static StarSystemDefinition GenerateSystem(int universeSeed, long cellX, long cellY, long cellZ)
            => GenerateSystem(universeSeed, cellX, cellY, cellZ, DefaultGalaxy(universeSeed));

        /// <summary>Candidate generation and admission metadata use the same authoritative galaxy settings.</summary>
        public static StarSystemDefinition GenerateSystem(int universeSeed, long cellX, long cellY, long cellZ, GalaxyDefinition galaxy, int generationVersion = GenerationVersion)
        {
            if (galaxy == null) throw new ArgumentNullException(nameof(galaxy));
            if (generationVersion != GenerationVersion && generationVersion != LegacyGenerationVersion) throw new ArgumentOutOfRangeException(nameof(generationVersion));
            ulong hash = DeterministicRandom.Hash(universeSeed, cellX, cellY, cellZ);
            var random = new DeterministicRandom(hash);
            int seed;
            UniversalPosition position = CandidatePosition(ref random, cellX, cellY, cellZ, out seed);
            bool home = cellX == 0 && cellY == 0 && cellZ == 0;
            double density = GalaxyModel.SampleDensity(GalaxyModel.ToGalacticLy(position, galaxy), galaxy);
            var system = new StarSystemDefinition
            {
                Name = $"SEG-{hash & 0xffffff:X6}", Seed = seed, Position = position, GenerationVersion = generationVersion,
                GalacticDensity = density, IsOccupied = home || Admit(hash, density)
            };
            GenerateStar(system, ref random, home);

            int moonCount = random.Range(1, 4);
            const int primaryCount = 5;
            system.Planets = new PlanetDefinition[primaryCount + moonCount];
            // Move the reference orbit with luminosity, and keep giant periapsides clear of the star.
            double orbit = Math.Max(0.06 * PhysicalConstants.AstronomicalUnit,
                Math.Max(Math.Sqrt(system.StarLuminosity / SolarLuminosity) * random.Range(0.86, 1.12) * PhysicalConstants.AstronomicalUnit,
                    system.StarRadius * 12));
            PlanetKind[] kinds = { PlanetKind.Oceanic,
                random.NextDouble() < 0.5 ? PlanetKind.Desert : PlanetKind.Rocky,
                PlanetKind.Ice, PlanetKind.GasGiant, PlanetKind.IceGiant };
            for (int i = 0; i < primaryCount; i++)
            {
                var body = GenerateBody(kinds[i], ref random, false);
                body.Name = $"{system.Name} {(char)('b' + i)}";
                if (!body.IsLandable)
                    orbit = Math.Max(orbit, body.Radius * 70 / Math.Pow(body.Mass / (3 * system.StarMass), 1.0 / 3));
                body.SemiMajorAxis = orbit;
                system.Planets[i] = body;
                orbit *= random.Range(1.8, 2.25);
            }

            double previousGasApoapsis = 0;
            for (int i = 0; i < moonCount; i++)
            {
                int parentIndex = i == 1 ? 4 : 3;
                PlanetDefinition parent = system.Planets[parentIndex];
                PlanetKind kind = i == 0 ? PlanetKind.Lava : (i == 1 ? PlanetKind.Ice : PlanetKind.Rocky);
                var moon = GenerateBody(kind, ref random, true);
                moon.ParentIndex = parentIndex;
                moon.Name = $"{parent.Name}-{(i == 2 ? 2 : 1)}";
                double hillRadius = parent.SemiMajorAxis * (1 - parent.Eccentricity)
                    * Math.Pow(parent.Mass / (3 * system.StarMass), 1.0 / 3);
                double safeInner = Math.Max(parent.Radius * 5, parent.RingOuterRadius * 1.7);
                if (parentIndex == 3 && previousGasApoapsis > 0) safeInner = Math.Max(safeInner, previousGasApoapsis * 1.8);
                moon.SemiMajorAxis = safeInner * random.Range(1.05, 1.2);
                if (moon.SemiMajorAxis * (1 + moon.Eccentricity) > hillRadius * 0.35)
                    throw new InvalidOperationException("Generated moon exceeds its parent's stable Hill envelope.");
                moon.RotationPeriod = Orbit.Period(moon, parent.Mass);
                system.Planets[primaryCount + i] = moon;
                if (parentIndex == 3) previousGasApoapsis = moon.SemiMajorAxis * (1 + moon.Eccentricity);
            }
            if (generationVersion == GenerationVersion) PlanetGenerationRules.Apply(system);
            return system;
        }

        private static void GenerateStar(StarSystemDefinition system, ref DeterministicRandom random, bool home)
        {
            double draw = random.NextDouble();
            system.Kind = home ? StellarKind.MainSequence : draw < 0.63 ? StellarKind.RedDwarf
                : draw < 0.92 ? StellarKind.MainSequence : draw < 0.925 ? StellarKind.BlueMainSequence
                : draw < 0.945 ? StellarKind.RedGiant : draw < 0.995 ? StellarKind.WhiteDwarf
                : draw < 0.999 ? StellarKind.NeutronStar : StellarKind.BlackHole;
            double mass, radius, temperature;
            switch (system.Kind)
            {
                case StellarKind.RedDwarf:
                    mass = random.Range(0.09, 0.55); radius = Math.Pow(mass, 0.85);
                    temperature = 2400 + 2400 * mass; break;
                case StellarKind.BlueMainSequence:
                    mass = random.Range(2, 12); radius = Math.Pow(mass, 0.7);
                    temperature = 5772 * Math.Pow(mass, 0.58); break;
                case StellarKind.RedGiant:
                    mass = random.Range(0.8, 4); radius = random.Range(12, 70);
                    temperature = random.Range(3200, 4800); break;
                case StellarKind.WhiteDwarf:
                    mass = random.Range(0.45, 1.15); radius = 0.012 * Math.Pow(0.6 / mass, 1.0 / 3);
                    temperature = random.Range(6500, 25000); break;
                case StellarKind.NeutronStar:
                    mass = random.Range(1.25, 2.1); radius = random.Range(10000, 14000) / PhysicalConstants.SolarRadius;
                    temperature = random.Range(300000, 1000000); break;
                case StellarKind.BlackHole:
                    mass = random.Range(5, 18);
                    radius = 2 * PhysicalConstants.GravitationalConstant * mass * PhysicalConstants.SolarMass
                        / (PhysicalConstants.SpeedOfLight * PhysicalConstants.SpeedOfLight * PhysicalConstants.SolarRadius);
                    temperature = 0; break;
                default:
                    mass = home ? random.Range(0.94, 1.08) : random.Range(0.58, 1.5);
                    radius = Math.Pow(mass, 0.8); temperature = 5772 * Math.Sqrt(mass); break;
            }
            system.StarMass = mass * PhysicalConstants.SolarMass;
            system.StarRadius = radius * PhysicalConstants.SolarRadius;
            system.StarTemperature = temperature;
            system.StarLuminosity = 4 * Math.PI * system.StarRadius * system.StarRadius * StefanBoltzmann * Math.Pow(temperature, 4);
        }

        private static PlanetDefinition GenerateBody(PlanetKind kind, ref DeterministicRandom random, bool moon)
        {
            var body = new PlanetDefinition
            {
                Seed = unchecked((int)random.NextUInt64()), Kind = kind,
                Eccentricity = random.Range(0.002, moon ? 0.025 : 0.09), Inclination = random.Range(-0.07, 0.07),
                LongitudeAscendingNode = random.Range(0, 2 * Math.PI), ArgumentPeriapsis = random.Range(0, 2 * Math.PI),
                MeanAnomalyAtEpoch = random.Range(0, 2 * Math.PI),
                RotationPeriod = random.Range(0.6, 2.5) * PhysicalConstants.SecondsPerDay,
                AxialTilt = random.Range(0, 0.6)
            };
            double radiusEarth, density;
            switch (kind)
            {
                case PlanetKind.Oceanic:
                    radiusEarth = random.Range(0.85, 1.12); density = random.Range(4600, 5700);
                    body.HasOcean = true; body.AtmosphereHeight = random.Range(85000, 125000);
                    body.SurfaceTint = new Color(0.22f, 0.39f, 0.18f); break;
                case PlanetKind.Desert:
                    radiusEarth = random.Range(0.65, 1.25); density = random.Range(3900, 5600);
                    body.AtmosphereHeight = random.Range(18000, 65000);
                    body.SurfaceTint = new Color(0.66f, 0.39f, 0.20f);
                    body.AtmosphereTint = new Color(0.95f, 0.58f, 0.30f); break;
                case PlanetKind.Ice:
                    radiusEarth = moon ? random.Range(0.2, 0.43) : random.Range(0.45, 1.1);
                    density = random.Range(1600, 2800); body.AtmosphereHeight = moon ? 0 : random.Range(0, 18000);
                    body.SurfaceTint = new Color(0.66f, 0.81f, 0.89f);
                    body.AtmosphereTint = new Color(0.5f, 0.76f, 0.95f); break;
                case PlanetKind.Lava:
                    radiusEarth = moon ? random.Range(0.2, 0.42) : random.Range(0.6, 1.2);
                    density = random.Range(2900, 4500); body.AtmosphereHeight = random.Range(0, 8000);
                    body.SurfaceTint = new Color(0.27f, 0.13f, 0.075f);
                    body.AtmosphereTint = new Color(1f, 0.24f, 0.035f); break;
                case PlanetKind.GasGiant:
                    radiusEarth = random.Range(8.5, 11.5); density = random.Range(650, 1500);
                    body.AtmosphereHeight = random.Range(800000, 1800000);
                    body.SurfaceTint = new Color(0.74f, 0.59f, 0.39f);
                    body.AtmosphereTint = new Color(0.85f, 0.69f, 0.46f);
                    body.RotationPeriod = random.Range(8, 13) * 3600; break;
                case PlanetKind.IceGiant:
                    radiusEarth = random.Range(3.2, 4.6); density = random.Range(1250, 1900);
                    body.AtmosphereHeight = random.Range(400000, 1000000);
                    body.SurfaceTint = new Color(0.16f, 0.52f, 0.74f);
                    body.AtmosphereTint = new Color(0.25f, 0.67f, 0.96f);
                    body.RotationPeriod = random.Range(14, 19) * 3600; break;
                default:
                    radiusEarth = moon ? random.Range(0.2, 0.45) : random.Range(0.5, 1.4);
                    density = random.Range(3500, 5500); body.AtmosphereHeight = moon ? 0 : random.Range(0, 22000);
                    body.SurfaceTint = new Color(0.43f, 0.37f, 0.32f); break;
            }
            body.Radius = radiusEarth * PhysicalConstants.EarthRadius;
            body.Mass = 4.0 / 3 * Math.PI * Math.Pow(body.Radius, 3) * density;
            body.TerrainHeight = body.IsLandable ? Math.Min(body.Radius * 0.004, random.Range(3500, 12000)) : 0;
            if (!body.IsLandable)
            {
                body.RingInnerRadius = body.Radius * random.Range(1.35, 1.55);
                body.RingOuterRadius = body.Radius * random.Range(2.05, 2.45);
            }
            return body;
        }

        private static UniversalPosition CandidatePosition(ref DeterministicRandom random, long cellX, long cellY, long cellZ, out int seed)
        {
            seed = unchecked((int)random.NextUInt64());
            Double3 jitter = new Double3(random.Range(-0.2, 0.2), random.Range(-0.2, 0.2), random.Range(-0.2, 0.2)) * CellSize;
            if (cellX == 0 && cellY == 0 && cellZ == 0) return UniversalPosition.Origin;
            long sx, sy, sz;
            double lx, ly, lz;
            CellAxis(cellX, out sx, out lx); CellAxis(cellY, out sy, out ly); CellAxis(cellZ, out sz, out lz);
            return new UniversalPosition(sx, sy, sz, new Double3(lx, ly, lz)).Add(jitter);
        }

        private static void CellAxis(long cell, out long sector, out double local)
        {
            // Preserve the exact sector/remainder split for distant-cell precision.
            decimal remainder = checked((decimal)cell * 921890323200m);
            decimal carry = decimal.Truncate(remainder / 1000000000000m);
            sector = checked((long)((decimal)cell * 37842m + carry));
            local = (double)(remainder - carry * 1000000000000m);
        }
    }
}
