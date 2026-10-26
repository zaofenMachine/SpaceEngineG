using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    /// <summary>
    /// A synthetic barred spiral galaxy, not a measured star catalogue. Distances have explicit
    /// light-year suffixes, angles are radians, density is stars per cubic parsec.
    /// </summary>
    [Serializable]
    public sealed class GalaxyDefinition
    {
        public int Seed;
        public double RadiusLy = 52000;
        public double DiskScaleLengthLy = 8500;
        public double ThinDiskHeightLy = 900;
        public double ThickDiskHeightLy = 3000;
        public double BulgeRadiusLy = 6500;
        /// <summary>Bar half-length along its major axis.</summary>
        public double BarLengthLy = 12000;
        public double BarAngleRadians = 0.47;
        public int ArmCount = 4;
        public double ArmPitchRadians = 0.22;
        public double ArmWidthLy = 1100;
        public double StellarHaloRadiusLy = 160000;
        public double SolarRadiusLy = 26700;
        public double LocalNumberDensityPerCubicParsec = 0.1;
        /// <summary>Central object metadata; this density model does not integrate its gravity.</summary>
        public double CentralMassSolarMasses = 4.1e6;
    }

    public enum GalaxyComponent { ThinDisk, ThickDisk, SpiralArms, Bulge, Bar, StellarHalo }

    /// <summary>One luminous spatial representative of a population, not one catalogued star.</summary>
    public struct GalaxyParticle
    {
        public Double3 PositionLy;
        public Color Color;
        public float Luminosity;
        /// <summary>Rendering kernel radius in light-years, unrelated to physical stellar radius.</summary>
        public float SizeLy;
        public GalaxyComponent Component;
    }

    public struct GalaxyDensity
    {
        public double ThinDisk, ThickDisk, SpiralArms, Bulge, Bar, StellarHalo;
        public double Total => ThinDisk + ThickDisk + SpiralArms + Bulge + Bar + StellarHalo;

        internal GalaxyDensity Scaled(double factor) => new GalaxyDensity
        {
            ThinDisk = ThinDisk * factor, ThickDisk = ThickDisk * factor,
            SpiralArms = SpiralArms * factor, Bulge = Bulge * factor,
            Bar = Bar * factor, StellarHalo = StellarHalo * factor
        };
    }
}
