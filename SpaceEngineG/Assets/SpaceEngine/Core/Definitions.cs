using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    [Serializable]
    public sealed class StarSystemDefinition
    {
        public string Name;
        public int Seed;
        public UniversalPosition Position;
        public double StarRadius, StarTemperature, StarMass;
        public PlanetDefinition[] Planets;
    }

    /// <summary>SI units; all orbital angles in radians. Orbit epoch is simulation time zero.</summary>
    [Serializable]
    public sealed class PlanetDefinition
    {
        public string Name;
        public int Seed;
        public double Radius, Mass, SemiMajorAxis, Eccentricity, Inclination;
        public double LongitudeAscendingNode, ArgumentPeriapsis, MeanAnomalyAtEpoch;
        public double AtmosphereHeight, TerrainHeight;
        public bool HasOcean;
        public Color SurfaceTint;
    }
}
