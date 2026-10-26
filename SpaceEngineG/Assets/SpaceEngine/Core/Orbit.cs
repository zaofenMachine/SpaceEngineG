using System;

namespace SpaceEngine.Core
{
    /// <summary>Analytic elliptic two-body propagation. Result is system-relative metres, with Unity Y up.</summary>
    public static class Orbit
    {
        public static double Period(PlanetDefinition planet, double starMass)
        {
            Validate(planet, starMass);
            double a = planet.SemiMajorAxis;
            return 2 * Math.PI * Math.Sqrt(a * a * a / (PhysicalConstants.GravitationalConstant * (starMass + planet.Mass)));
        }

        public static Double3 Evaluate(PlanetDefinition planet, double starMass, double timeSeconds)
        {
            double period = Period(planet, starMass);
            if (double.IsNaN(timeSeconds) || double.IsInfinity(timeSeconds)) throw new ArgumentOutOfRangeException(nameof(timeSeconds));
            // Reduce time first; multiplying an arbitrarily large epoch by angular velocity loses phase precision.
            double mean = planet.MeanAnomalyAtEpoch + 2 * Math.PI * ((timeSeconds % period) / period);
            double eccentric = SolveEccentricAnomaly(mean, planet.Eccentricity);
            double x = planet.SemiMajorAxis * (Math.Cos(eccentric) - planet.Eccentricity);
            double y = planet.SemiMajorAxis * Math.Sqrt(1 - planet.Eccentricity * planet.Eccentricity) * Math.Sin(eccentric);
            double cw = Math.Cos(planet.ArgumentPeriapsis), sw = Math.Sin(planet.ArgumentPeriapsis);
            double co = Math.Cos(planet.LongitudeAscendingNode), so = Math.Sin(planet.LongitudeAscendingNode);
            double ci = Math.Cos(planet.Inclination), si = Math.Sin(planet.Inclination);
            // JPL ecliptic XYZ becomes Unity XZY. This preserves the orbital pole as +Y.
            return new Double3(
                (cw * co - sw * so * ci) * x + (-sw * co - cw * so * ci) * y,
                sw * si * x + cw * si * y,
                (cw * so + sw * co * ci) * x + (-sw * so + cw * co * ci) * y);
        }

        public static double SolveEccentricAnomaly(double meanAnomaly, double eccentricity)
        {
            if (double.IsNaN(meanAnomaly) || double.IsInfinity(meanAnomaly)) throw new ArgumentOutOfRangeException(nameof(meanAnomaly));
            if (!(eccentricity >= 0 && eccentricity < 1)) throw new ArgumentOutOfRangeException(nameof(eccentricity), "Only elliptic orbits are supported.");
            double mean = Math.IEEERemainder(meanAnomaly, 2 * Math.PI);
            if (mean == 0 || eccentricity == 0) return mean;
            double lower = -Math.PI, upper = Math.PI;
            double value = eccentricity < 0.8 ? mean : (mean > 0 ? Math.PI : -Math.PI);
            for (int iteration = 0; iteration < 80; iteration++)
            {
                double residual = value - eccentricity * Math.Sin(value) - mean;
                if (Math.Abs(residual) < 1e-14) return value;
                if (residual > 0) upper = value; else lower = value;
                double candidate = value - residual / (1 - eccentricity * Math.Cos(value));
                value = candidate > lower && candidate < upper ? candidate : (lower + upper) * 0.5;
            }
            throw new InvalidOperationException("Kepler solver did not converge within its safeguarded iteration limit.");
        }

        private static void Validate(PlanetDefinition planet, double starMass)
        {
            if (planet == null) throw new ArgumentNullException(nameof(planet));
            if (!(planet.SemiMajorAxis > 0) || double.IsInfinity(planet.SemiMajorAxis)) throw new ArgumentOutOfRangeException(nameof(planet.SemiMajorAxis));
            if (!(starMass > 0) || double.IsInfinity(starMass)) throw new ArgumentOutOfRangeException(nameof(starMass));
            if (!(planet.Mass >= 0) || double.IsInfinity(planet.Mass)) throw new ArgumentOutOfRangeException(nameof(planet.Mass));
            if (!(planet.Eccentricity >= 0 && planet.Eccentricity < 1)) throw new ArgumentOutOfRangeException(nameof(planet.Eccentricity));
        }
    }
}
