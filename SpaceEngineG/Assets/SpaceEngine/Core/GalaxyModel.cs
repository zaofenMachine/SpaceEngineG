using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    /// <summary>
    /// Analytic 3D density and stateless seeded population representatives. y is height above
    /// the galactic plane; x/z span the disc. These light-year coordinates are for sampling,
    /// never a replacement for the integer-sector UniversalPosition used by flight.
    /// </summary>
    public static class GalaxyModel
    {
        public const double LightYearsPerParsec = 3.2615637771674336;
        private const double Tau = Math.PI * 2;

        public static GalaxyDefinition Default(int seed) => new GalaxyDefinition { Seed = seed };

        public static UniversalPosition Center(GalaxyDefinition galaxy)
        {
            if (galaxy == null) throw new ArgumentNullException(nameof(galaxy));
            return UniversalPosition.Origin.Add(new Double3(-galaxy.SolarRadiusLy * PhysicalConstants.LightYear, 0, 0));
        }

        public static Double3 ToGalacticLy(UniversalPosition position, GalaxyDefinition galaxy)
        {
            if (galaxy == null) throw new ArgumentNullException(nameof(galaxy));
            // Subtract the home origin first; nearby y/z coordinates retain their precision.
            Double3 homeLy = position.RelativeTo(UniversalPosition.Origin) / PhysicalConstants.LightYear;
            return homeLy + new Double3(galaxy.SolarRadiusLy, 0, 0);
        }

        public static UniversalPosition FromGalacticLy(Double3 positionLy, GalaxyDefinition galaxy)
        {
            if (galaxy == null) throw new ArgumentNullException(nameof(galaxy));
            return UniversalPosition.Origin.Add((positionLy - new Double3(galaxy.SolarRadiusLy, 0, 0)) * PhysicalConstants.LightYear);
        }

        /// <summary>Physical number density in stars per pc^3, normalized to the configured home density.</summary>
        public static double SampleDensity(Double3 positionLy, GalaxyDefinition galaxy) => SampleDensityComponents(positionLy, galaxy).Total;

        public static GalaxyDensity SampleDensityComponents(Double3 positionLy, GalaxyDefinition galaxy)
        {
            Validate(galaxy);
            GalaxyDensity raw = RawDensity(positionLy, galaxy);
            double normalization = galaxy.LocalNumberDensityPerCubicParsec /
                Math.Max(1e-300, RawDensity(new Double3(galaxy.SolarRadiusLy, 0, 0), galaxy).Total);
            return raw.Scaled(normalization);
        }

        /// <summary>Angle of the centre line of arm zero at a cylindrical radius, in radians.</summary>
        public static double ArmAngle(double radiusLy, GalaxyDefinition galaxy)
        {
            double phase = (DeterministicRandom.Hash(galaxy.Seed, 0, 0, 0) >> 11) * (Tau / 9007199254740992.0);
            return phase + galaxy.BarAngleRadians + Math.Log(Math.Max(1, radiusLy) / ArmInnerRadius(galaxy)) / Math.Tan(galaxy.ArmPitchRadians);
        }

        private static GalaxyDensity RawDensity(Double3 p, GalaxyDefinition g)
        {
            var result = new GalaxyDensity();
            double radius = Math.Sqrt(p.x * p.x + p.z * p.z), height = Math.Abs(p.y), sphere = p.Length;
            if (sphere >= g.StellarHaloRadiusLy) return result;
            if (radius < g.RadiusLy)
            {
                // Smoothly taper the outer 12% of the disc to a finite, empty exterior.
                double taper = 1 - SmoothStep(g.RadiusLy * 0.88, g.RadiusLy, radius);
                double radial = Math.Exp((g.SolarRadiusLy - radius) / g.DiskScaleLengthLy) * taper;
                result.ThinDisk = radial * Math.Exp(-height / g.ThinDiskHeightLy);
                result.ThickDisk = 0.1 * Math.Exp((g.SolarRadiusLy - radius) / (g.DiskScaleLengthLy * 1.2)) *
                    Math.Exp(-height / g.ThickDiskHeightLy) * taper;
                double delta = WrapAngle((Math.Atan2(p.z, p.x) - ArmAngle(radius, g)) * g.ArmCount) / g.ArmCount;
                // Local perpendicular distance to the logarithmic spiral, to first order.
                double armDistance = radius * delta * Math.Sin(g.ArmPitchRadians);
                double arm = Math.Exp(-0.5 * armDistance * armDistance / (g.ArmWidthLy * g.ArmWidthLy));
                result.SpiralArms = 0.65 * radial * arm * Math.Exp(-height / (g.ThinDiskHeightLy * 0.5)) *
                    SmoothStep(ArmInnerRadius(g), ArmInnerRadius(g) * 1.25, radius);
            }
            double bulge = Math.Sqrt((p.x * p.x + p.z * p.z) / (g.BulgeRadiusLy * g.BulgeRadiusLy) +
                p.y * p.y / (0.65 * 0.65 * g.BulgeRadiusLy * g.BulgeRadiusLy));
            if (bulge < 1) result.Bulge = 24 * Math.Exp(-3 * bulge) * (1 - SmoothStep(0.8, 1, bulge));
            double c = Math.Cos(g.BarAngleRadians), s = Math.Sin(g.BarAngleRadians);
            double barX = p.x * c + p.z * s, barZ = -p.x * s + p.z * c;
            double bar = Math.Sqrt(barX * barX / (g.BarLengthLy * g.BarLengthLy) +
                barZ * barZ / (g.BarLengthLy * g.BarLengthLy * 0.12) +
                p.y * p.y / (g.BarLengthLy * g.BarLengthLy * 0.09));
            if (bar < 1) result.Bar = 15 * Math.Exp(-3 * bar) * (1 - SmoothStep(0.8, 1, bar));
            if (sphere < g.StellarHaloRadiusLy)
                result.StellarHalo = 0.002 * Math.Pow((g.SolarRadiusLy + g.BulgeRadiusLy) / (sphere + g.BulgeRadiusLy), 3) *
                    (1 - SmoothStep(g.StellarHaloRadiusLy * 0.8, g.StellarHaloRadiusLy, sphere));
            return result;
        }

        /// <summary>
        /// Fixed-index independent samples; increasing count preserves the entire previous prefix.
        /// Component proportions intentionally favour visible structural tracers; this is a
        /// bounded aggregate rendering distribution, not a complete individual-star catalogue.
        /// </summary>
        public static GalaxyParticle[] GenerateRepresentatives(GalaxyDefinition galaxy, int count)
        {
            Validate(galaxy);
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var particles = new GalaxyParticle[count];
            for (int index = 0; index < count; index++)
            {
                var rng = new DeterministicRandom(DeterministicRandom.Hash(galaxy.Seed, index, 0x47a1, 0));
                // Golden-ratio sequence spreads even small buffers over all structural components.
                double selector = Fraction(0.5 + index * 0.6180339887498948482);
                GalaxyComponent component = selector < 0.46 ? GalaxyComponent.ThinDisk :
                    selector < 0.56 ? GalaxyComponent.ThickDisk : selector < 0.80 ? GalaxyComponent.SpiralArms :
                    selector < 0.91 ? GalaxyComponent.Bulge : selector < 0.98 ? GalaxyComponent.Bar : GalaxyComponent.StellarHalo;
                Double3 p;
                if (component == GalaxyComponent.ThinDisk || component == GalaxyComponent.ThickDisk || component == GalaxyComponent.SpiralArms)
                {
                    double minRadius = component == GalaxyComponent.SpiralArms ? ArmInnerRadius(galaxy) : 0;
                    double scale = galaxy.DiskScaleLengthLy * (component == GalaxyComponent.ThickDisk ? 1.2 : 1);
                    double radius = SampleDiskRadius(ref rng, minRadius, galaxy.RadiusLy, scale);
                    double theta = rng.Range(0, Tau);
                    if (component == GalaxyComponent.SpiralArms)
                    {
                        theta = ArmAngle(radius, galaxy) + rng.Range(0, galaxy.ArmCount) * Tau / galaxy.ArmCount +
                            Gaussian(ref rng) * galaxy.ArmWidthLy / (radius * Math.Sin(galaxy.ArmPitchRadians));
                    }
                    double verticalScale = component == GalaxyComponent.ThickDisk ? galaxy.ThickDiskHeightLy :
                        galaxy.ThinDiskHeightLy * (component == GalaxyComponent.SpiralArms ? 0.5 : 1);
                    double height = Math.Min(Math.Sqrt(galaxy.StellarHaloRadiusLy * galaxy.StellarHaloRadiusLy - radius * radius) * 0.999999,
                        Math.Min(8, -Math.Log(Math.Max(1e-15, rng.NextDouble()))) * verticalScale);
                    if (rng.NextDouble() < 0.5) height = -height;
                    p = new Double3(radius * Math.Cos(theta), height, radius * Math.Sin(theta));
                }
                else if (component == GalaxyComponent.StellarHalo)
                {
                    // Log proposal p(r)~1/(r+core), corrected for spherical area to obtain
                    // p(r)~r^2/(r+core)^3 with the same finite outer taper as the density.
                    double radius, acceptance;
                    do
                    {
                        radius = galaxy.BulgeRadiusLy * (Math.Exp(rng.NextDouble() * Math.Log(1 + galaxy.StellarHaloRadiusLy / galaxy.BulgeRadiusLy)) - 1);
                        double radialFraction = radius / (radius + galaxy.BulgeRadiusLy);
                        acceptance = radialFraction * radialFraction * (1 - SmoothStep(galaxy.StellarHaloRadiusLy * 0.8, galaxy.StellarHaloRadiusLy, radius));
                    } while (rng.NextDouble() > acceptance);
                    p = UnitSphere(ref rng) * radius;
                }
                else
                {
                    // Truncated exponential radial profile in an ellipsoidal volume.
                    double radius;
                    do { radius = Math.Pow(rng.NextDouble(), 1.0 / 3); }
                    while (rng.NextDouble() > Math.Exp(-3 * radius) * (1 - SmoothStep(0.8, 1, radius)));
                    Double3 unit = UnitSphere(ref rng) * radius;
                    if (component == GalaxyComponent.Bulge)
                        p = new Double3(unit.x * galaxy.BulgeRadiusLy, unit.y * galaxy.BulgeRadiusLy * 0.65, unit.z * galaxy.BulgeRadiusLy);
                    else
                    {
                        double x = unit.x * galaxy.BarLengthLy, z = unit.z * galaxy.BarLengthLy * Math.Sqrt(0.12);
                        p = new Double3(x * Math.Cos(galaxy.BarAngleRadians) - z * Math.Sin(galaxy.BarAngleRadians),
                            unit.y * galaxy.BarLengthLy * 0.3, x * Math.Sin(galaxy.BarAngleRadians) + z * Math.Cos(galaxy.BarAngleRadians));
                    }
                }
                Color warm = new Color(1, 0.72f, 0.43f), cool = new Color(0.48f, 0.68f, 1);
                Color tint = component == GalaxyComponent.SpiralArms ? Color.Lerp(new Color(0.65f, 0.76f, 1), cool, (float)rng.NextDouble()) :
                    Color.Lerp(warm, new Color(1, 0.94f, 0.81f), (float)rng.NextDouble());
                particles[index] = new GalaxyParticle
                {
                    PositionLy = p, Component = component, Color = tint,
                    Luminosity = (float)rng.Range(0.35, component == GalaxyComponent.SpiralArms ? 2.1 : 1.25),
                    SizeLy = (float)rng.Range(component == GalaxyComponent.StellarHalo ? 30 : 45, component == GalaxyComponent.SpiralArms ? 130 : 95)
                };
            }
            return particles;
        }

        private static double SampleDiskRadius(ref DeterministicRandom rng, double min, double max, double scale)
        {
            // Area-correct p(r)~r*exp(-r/L). Choosing proposals by scale keeps configurable
            // very broad discs and arms outside a tiny central scale from a pathological retry loop.
            double r;
            if (scale >= max)
            {
                do { r = Math.Sqrt(min * min + rng.NextDouble() * (max * max - min * min)); }
                while (rng.NextDouble() > Math.Exp(-(r - min) / scale) * (1 - SmoothStep(max * 0.88, max, r)));
            }
            else
            {
                // The gamma(k=2) radial law conditional on r>=min is the mixture
                // min + Gamma(1,L) / Gamma(2,L), weighted by min/(min+L).
                do
                {
                    double u = Math.Max(1e-30, rng.NextDouble());
                    if (rng.NextDouble() >= min / (min + scale)) u *= Math.Max(1e-30, rng.NextDouble());
                    r = min - scale * Math.Log(u);
                } while (r >= max || rng.NextDouble() > 1 - SmoothStep(max * 0.88, max, r));
            }
            return r;
        }

        private static Double3 UnitSphere(ref DeterministicRandom rng)
        {
            double y = rng.Range(-1.0, 1.0), theta = rng.Range(0, Tau), radius = Math.Sqrt(Math.Max(0, 1 - y * y));
            return new Double3(radius * Math.Cos(theta), y, radius * Math.Sin(theta));
        }

        private static double Gaussian(ref DeterministicRandom rng) => Math.Sqrt(-2 * Math.Log(Math.Max(1e-15, rng.NextDouble()))) * Math.Cos(Tau * rng.NextDouble());
        private static double ArmInnerRadius(GalaxyDefinition g) => Math.Max(1, g.BarLengthLy * 0.6);
        private static double Fraction(double value) => value - Math.Floor(value);
        private static double WrapAngle(double value) => value - Tau * Math.Floor((value + Math.PI) / Tau);
        private static double SmoothStep(double min, double max, double value)
        {
            double t = Math.Max(0, Math.Min(1, (value - min) / (max - min)));
            return t * t * (3 - 2 * t);
        }

        private static void Validate(GalaxyDefinition g)
        {
            if (g == null) throw new ArgumentNullException(nameof(g));
            if (!(g.RadiusLy > 0) || !(g.DiskScaleLengthLy > 0) || !(g.ThinDiskHeightLy > 0) || !(g.ThickDiskHeightLy > 0) ||
                !(g.BulgeRadiusLy > 0 && g.BulgeRadiusLy < g.RadiusLy) || !(g.BarLengthLy > 0 && g.BarLengthLy < g.RadiusLy) ||
                !(g.ArmWidthLy > 0) || !(g.StellarHaloRadiusLy > g.RadiusLy) ||
                !(g.SolarRadiusLy > 0 && g.SolarRadiusLy < g.RadiusLy * 0.88) ||
                !(g.LocalNumberDensityPerCubicParsec >= 0) || !(g.CentralMassSolarMasses > 0) || g.ArmCount < 1 || g.ArmCount > 16 ||
                !(g.ArmPitchRadians > 0.02 && g.ArmPitchRadians < 1.2) || !(g.BarLengthLy * 0.75 < g.RadiusLy))
                throw new ArgumentOutOfRangeException(nameof(g), "Galaxy scales must be positive and finite, home must be inside the disc, and arms must fit its radius.");
            if (!Finite(g.RadiusLy) || !Finite(g.DiskScaleLengthLy) || !Finite(g.ThinDiskHeightLy) || !Finite(g.ThickDiskHeightLy) ||
                !Finite(g.BulgeRadiusLy) || !Finite(g.BarLengthLy) || !Finite(g.ArmWidthLy) || !Finite(g.StellarHaloRadiusLy) ||
                !Finite(g.LocalNumberDensityPerCubicParsec) || !Finite(g.BarAngleRadians) || !Finite(g.CentralMassSolarMasses))
                throw new ArgumentOutOfRangeException(nameof(g), "Galaxy parameters must be finite.");
        }

        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
