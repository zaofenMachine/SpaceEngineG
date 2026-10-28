using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    public enum StellarKind { RedDwarf, MainSequence, BlueMainSequence, RedGiant, WhiteDwarf, NeutronStar, BlackHole }
    public enum PlanetKind { Rocky, Oceanic, Desert, Ice, Lava, GasGiant, IceGiant }

    [Serializable]
    public sealed class StarSystemDefinition
    {
        public int GenerationVersion = 2;
        public string Name;
        public int Seed;
        public UniversalPosition Position;
        public StellarKind Kind;
        public double StarRadius, StarTemperature, StarMass;
        /// <summary>Bolometric luminosity in watts. Zero for an isolated, non-accreting black hole.</summary>
        public double StarLuminosity;
        public double GalacticDensity;
        public bool IsOccupied;
        public PlanetDefinition[] Planets;
    }

    /// <summary>SI units; all orbital angles in radians. Orbit epoch is simulation time zero.</summary>
    [Serializable]
    public sealed class PlanetDefinition
    {
        public string Name;
        public int Seed;
        public PlanetKind Kind;
        /// <summary>Index of the parent in Planets, or -1 when orbiting the star. Parents precede moons.</summary>
        public int ParentIndex = -1;
        public bool IsMoon => ParentIndex >= 0;
        public bool IsLandable => Kind != PlanetKind.GasGiant && Kind != PlanetKind.IceGiant;
        public double Radius, Mass, SemiMajorAxis, Eccentricity, Inclination;
        public double LongitudeAscendingNode, ArgumentPeriapsis, MeanAnomalyAtEpoch;
        /// <summary>Sidereal rotation seconds, axial tilt radians, and ring edge radii in metres (zero if absent).</summary>
        public double RotationPeriod, AxialTilt, RingInnerRadius, RingOuterRadius;
        public double AtmosphereHeight, TerrainHeight;
        public bool HasOcean;
        public Color SurfaceTint;
        public Color AtmosphereTint = new Color(0.32f, 0.53f, 1f);
    }
}
