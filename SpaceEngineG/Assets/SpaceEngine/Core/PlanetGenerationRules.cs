using System;
using UnityEngine;

namespace SpaceEngine.Core
{
    /// <summary>Version 3 energy-aware orbital placement. V2's original random stream, masses, seeds and terrain stay intact.</summary>
    public static class PlanetGenerationRules
    {
        public static void Apply(StarSystemDefinition system)
        {
            if (system == null || system.Planets == null || system.Planets.Length == 0) throw new ArgumentException("A generated system is required.");
            var first = system.Planets[0];
            double originalReference = first.SemiMajorAxis;
            double reference = originalReference;
            if (system.StarLuminosity > 0)
            {
                // Entire eccentric orbit is checked in the population tests. This
                // is a generation design target, not a claim of observed habitability.
                double flux = PlanetScience.RequiredStellarFluxForSurfaceTemperature(first, 292);
                reference = Math.Sqrt(system.StarLuminosity / (4 * Math.PI * flux));
            }
            double previousApoapsis = 0;
            foreach (var planet in system.Planets)
            {
                if (planet.IsMoon) continue;
                double candidate = planet.SemiMajorAxis * reference / originalReference;
                double starDensity = system.StarMass / (4.0 / 3 * Math.PI * Math.Pow(system.StarRadius, 3));
                double bodyDensity = planet.Mass / (4.0 / 3 * Math.PI * Math.Pow(planet.Radius, 3));
                double roche = 2.44 * system.StarRadius * Math.Pow(starDensity / bodyDensity, 1.0 / 3);
                double minimumPeriapsis = Math.Max(system.StarRadius * 3, roche * 1.25);
                candidate = Math.Max(candidate, minimumPeriapsis / (1 - planet.Eccentricity));
                if (previousApoapsis > 0) candidate = Math.Max(candidate, previousApoapsis * 1.35 / (1 - planet.Eccentricity));
                // Preserve room for the already generated ring and satellite cohort.
                if (!planet.IsLandable) candidate = Math.Max(candidate,
                    planet.Radius * 70 / Math.Pow(planet.Mass / (3 * system.StarMass), 1.0 / 3));
                planet.SemiMajorAxis = candidate;
                previousApoapsis = candidate * (1 + planet.Eccentricity);
            }
            if (system.StarLuminosity <= 0 && first.HasOcean)
            {
                // A non-accreting black hole supplies no stellar heat. Keep the
                // material/terrain seed but explicitly freeze the reservoir.
                first.Kind = PlanetKind.Ice;
                first.HasOcean = false;
                first.SurfaceTint = new Color(.66f,.81f,.89f);
                first.AtmosphereTint = new Color(.5f,.76f,.95f);
            }
            if(system.StarLuminosity<=0)
                foreach(var planet in system.Planets)if(planet.IsLandable)planet.AtmosphereHeight=0;
        }
    }
}
