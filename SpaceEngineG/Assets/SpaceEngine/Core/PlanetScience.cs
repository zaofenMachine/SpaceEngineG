using System;
using System.Collections.Generic;

namespace SpaceEngine.Core
{
    public enum BodyReferenceKind { SolidSurface, AtmosphereReferenceRadius, StellarPhotosphere, GeometricHorizon }
    public enum GasSpecies { Hydrogen, Helium, Nitrogen, CarbonDioxide, SulfurDioxide, WaterVapor, Argon }
    [Flags]
    public enum ScienceCaveat
    {
        None = 0, LegacyClimateUncalibrated = 1, NoStellarHeating = 2,
        GreyAtmosphereAssumed = 4, NoClimateDynamics = 8, LocalLavaTemperatureUnknown = 16,
        AtmospherePhaseNotModelled = 32, SolarDayRequiresMultipleOrbits = 64,
        TidalApproximationUnavailable = 128, VisualClassClimateMismatch = 256,
        StellarEclipsesAndPlanetshineOmitted = 512, StellarParametersInconsistent = 1024
    }

    public readonly struct GasFraction
    {
        public GasSpecies Species { get; }
        public double MoleFraction { get; }
        public GasFraction(GasSpecies species, double moleFraction) { Species = species; MoleFraction = moleFraction; }
    }

    public sealed class GeometryDerived
    {
        public double RadiusMetres { get; internal set; }
        public double DiameterMetres { get; internal set; }
        public double MassKilograms { get; internal set; }
        public double VolumeCubicMetres { get; internal set; }
        public double MeanDensityKgM3 { get; internal set; }
        public double GravityAtReferenceRadiusMps2 { get; internal set; }
        public double EscapeSpeedAtReferenceRadiusMps { get; internal set; }
        public bool HasSolidSurface { get; internal set; }
        public BodyReferenceKind ReferenceKind { get; internal set; }
    }
    public sealed class OrbitDerived
    {
        public int ParentIndex { get; internal set; }
        public int HierarchyDepth { get; internal set; }
        public double ParentMassKilograms { get; internal set; }
        public double PeriodSeconds { get; internal set; }
        public double SemiMajorAxisMetres { get; internal set; }
        public double Eccentricity { get; internal set; }
        public double InclinationRadians { get; internal set; }
        public double LongitudeAscendingNodeRadians { get; internal set; }
        public double ArgumentPeriapsisRadians { get; internal set; }
        public double PeriapsisMetres { get; internal set; }
        public double ApoapsisMetres { get; internal set; }
        public double InstantaneousDistanceMetres { get; internal set; }
        public double InstantaneousSpeedMps { get; internal set; }
        public double StellarDistanceMetres { get; internal set; }
        /// <summary>Conservative triangle-inequality bounds, not simultaneous multi-orbit extrema.</summary>
        public double StellarDistanceMinimumBoundMetres { get; internal set; }
        public double StellarDistanceMaximumBoundMetres { get; internal set; }
        public Double3 PositionFromParentMetres { get; internal set; }
        public Double3 PositionFromStarMetres { get; internal set; }
    }
    public sealed class RotationDerived
    {
        public double SiderealPeriodSeconds { get; internal set; }
        public double AxialTiltRadians { get; internal set; }
        public double EquatorialSpeedMps { get; internal set; }
        public bool SynchronousWithParent { get; internal set; }
        public double? SolarDaySeconds { get; internal set; }
        public bool SolarDayInfinite { get; internal set; }
    }
    public sealed class StellarDerived
    {
        public double MassSolarUnits { get; internal set; }
        public double RadiusSolarUnits { get; internal set; }
        public double LuminositySolarUnits { get; internal set; }
        public double BolometricLuminosityW { get; internal set; }
        /// <summary>Derived from the authoritative luminosity and radius, not a new stellar evolution model.</summary>
        public double EffectiveTemperatureK { get; internal set; }
        public bool IsNonAccretingBlackHole { get; internal set; }
    }
    public sealed class ClimateDerived
    {
        public double IncidentStellarFluxWm2 { get; internal set; }
        public double StellarFluxEarthUnits { get; internal set; }
        public double BondAlbedo { get; internal set; }
        public double EquilibriumTemperatureK { get; internal set; }
        public double EffectiveTemperatureK { get; internal set; }
        public double IntrinsicHeatFluxWm2 { get; internal set; }
        public double TidalHeatFluxWm2 { get; internal set; }
        public double? TidalDissipationK2OverQ { get; internal set; }
        public double InfraredOpticalDepth { get; internal set; }
        public double InfraredEmissivity { get; internal set; }
        public double InfraredMassAbsorptionM2Kg { get; internal set; }
        public double ReferencePressurePascals { get; internal set; }
        public double ReferenceTemperatureK { get; internal set; }
        public double MeanMolarMassKgPerMol { get; internal set; }
        public double SpecificGasConstantJkgK { get; internal set; }
        public double? SurfacePressurePascals { get; internal set; }
        public double? SurfaceTemperatureK { get; internal set; }
        public double? SurfaceTemperatureMinimumK { get; internal set; }
        public double? SurfaceTemperatureMaximumK { get; internal set; }
        public double? AtmosphericDensityKgM3 { get; internal set; }
        public double? ScaleHeightMetres { get; internal set; }
        public bool HasAtmosphere { get; internal set; }
        public bool IsOneBarReference { get; internal set; }
        public bool HasSurfaceTemperatureEstimate => SurfaceTemperatureK.HasValue;
        public bool IsSyntheticAtmosphereModel => true;
        public bool IsLegacyGeneration { get; internal set; }
        /// <summary>Only a necessary thermodynamic screen for assumed water, not a habitability claim.</summary>
        public bool LiquidWaterModelCompatible { get; internal set; }
        public IReadOnlyList<GasFraction> GasMoleFractions { get; internal set; }
    }
    public sealed class PlanetScienceReport
    {
        public bool IsStar { get; internal set; }
        public int BodyIndex { get; internal set; }
        public int GenerationVersion { get; internal set; }
        public GeometryDerived Geometry { get; internal set; }
        public OrbitDerived Orbit { get; internal set; }
        public RotationDerived Rotation { get; internal set; }
        public ClimateDerived Climate { get; internal set; }
        public StellarDerived Star { get; internal set; }
        public ScienceCaveat Caveats { get; internal set; }
    }

    /// <summary>
    /// Pure SI calculations. Geometry and Kepler results are derived from the stored world;
    /// atmosphere/albedo/geothermal/grey opacity are explicitly synthetic, independent-seed presets.
    /// No age, life, magnetic field, real spectrum, weather or interior-evolution data are inferred.
    /// References: JPL https://ssd.jpl.nasa.gov/astro_par.html ; IAU B3
    /// https://arxiv.org/abs/1510.07674 ; temperature distinction, Del Genio et al. 2019
    /// https://www.giss.nasa.gov/pubs/abs/de06700y.html ; synchronous small-e tides
    /// https://ntrs.nasa.gov/api/citations/20230014603/downloads/Enceladus%20ISSI.pdf .
    /// </summary>
    public static class PlanetScience
    {
        public const double StefanBoltzmann = 5.670374419e-8;
        public const double MolarGasConstant = 8.31446261815324;
        public const double SolarLuminosityW = 3.828e26;
        public const double HeatRedistributionDivisor = 4;
        public static readonly double EarthReferenceFluxWm2 = SolarLuminosityW /
            (4 * Math.PI * PhysicalConstants.AstronomicalUnit * PhysicalConstants.AstronomicalUnit);
        const ulong ClimateStream = 0x3a48d9f7e712c065UL;

        sealed class Profile
        {
            internal double Albedo, Pressure, Opacity, Intrinsic, Humidity, Dissipation;
            internal GasFraction[] Dry;
        }

        public static PlanetScienceReport Evaluate(StarSystemDefinition system, int index, double simulationTime)
        {
            ValidateSystem(system);
            Finite(simulationTime, nameof(simulationTime));
            if (index < -1 || (index >= 0 && (system.Planets == null || index >= system.Planets.Length)))
                throw new ArgumentOutOfRangeException(nameof(index));
            var star = new StellarDerived
            {
                MassSolarUnits = system.StarMass / PhysicalConstants.SolarMass,
                RadiusSolarUnits = system.StarRadius / PhysicalConstants.SolarRadius,
                LuminositySolarUnits = system.StarLuminosity / SolarLuminosityW,
                BolometricLuminosityW = system.StarLuminosity,
                EffectiveTemperatureK = Math.Pow(system.StarLuminosity /
                    (4 * Math.PI * system.StarRadius * system.StarRadius * StefanBoltzmann), .25),
                IsNonAccretingBlackHole = system.Kind == StellarKind.BlackHole && system.StarLuminosity == 0
            };
            var report = new PlanetScienceReport { IsStar = index == -1, BodyIndex = index,
                GenerationVersion = system.GenerationVersion, Star = star };
            if (system.GenerationVersion < 3) report.Caveats |= ScienceCaveat.LegacyClimateUncalibrated;
            if (system.StarLuminosity == 0) report.Caveats |= ScienceCaveat.NoStellarHeating;
            double statedL = 4 * Math.PI * system.StarRadius * system.StarRadius * StefanBoltzmann * Math.Pow(system.StarTemperature, 4);
            if (Math.Abs(statedL - system.StarLuminosity) > Math.Max(1, system.StarLuminosity) * 1e-6)
                report.Caveats |= ScienceCaveat.StellarParametersInconsistent;
            if (index == -1)
            {
                report.Geometry = Geometry(system.StarRadius, system.StarMass, false,
                    system.Kind == StellarKind.BlackHole ? BodyReferenceKind.GeometricHorizon : BodyReferenceKind.StellarPhotosphere);
                return report;
            }
            PlanetDefinition body = system.Planets[index];
            ValidateBody(body);
            int parent = body.ParentIndex;
            double parentMass = ParentMass(system, parent);
            Double3 position = Double3.Zero, local = Double3.Zero;
            double childApoSum = 0, rootPeri = 0, rootApo = 0;
            int cursor = index, depth = 0;
            while (cursor >= 0)
            {
                if (++depth > system.Planets.Length) throw new ArgumentException("Cyclic planetary parent hierarchy.", nameof(system));
                PlanetDefinition member = system.Planets[cursor];
                ValidateBody(member);
                Double3 offset = Orbit.Evaluate(member, ParentMass(system, member.ParentIndex), simulationTime);
                if (cursor == index) local = offset;
                position += offset;
                if (member.ParentIndex < 0)
                {
                    rootPeri = member.SemiMajorAxis * (1 - member.Eccentricity);
                    rootApo = member.SemiMajorAxis * (1 + member.Eccentricity);
                }
                else childApoSum += member.SemiMajorAxis * (1 + member.Eccentricity);
                cursor = member.ParentIndex;
            }
            double distance = position.Length;
            Positive(distance, "stellar distance");
            double period = Orbit.Period(body, parentMass), r = local.Length;
            report.Geometry = Geometry(body.Radius, body.Mass, body.IsLandable,
                body.IsLandable ? BodyReferenceKind.SolidSurface : BodyReferenceKind.AtmosphereReferenceRadius);
            report.Orbit = new OrbitDerived { ParentIndex = parent, HierarchyDepth = depth,
                ParentMassKilograms = parentMass, PeriodSeconds = period,
                SemiMajorAxisMetres = body.SemiMajorAxis, Eccentricity = body.Eccentricity,
                InclinationRadians = body.Inclination, LongitudeAscendingNodeRadians = body.LongitudeAscendingNode,
                ArgumentPeriapsisRadians = body.ArgumentPeriapsis,
                PeriapsisMetres = body.SemiMajorAxis * (1 - body.Eccentricity),
                ApoapsisMetres = body.SemiMajorAxis * (1 + body.Eccentricity),
                InstantaneousDistanceMetres = r,
                InstantaneousSpeedMps = Math.Sqrt(PhysicalConstants.GravitationalConstant * (parentMass + body.Mass) * (2 / r - 1 / body.SemiMajorAxis)),
                StellarDistanceMetres = distance, StellarDistanceMinimumBoundMetres = Math.Max(0, rootPeri - childApoSum),
                StellarDistanceMaximumBoundMetres = rootApo + childApoSum,
                PositionFromParentMetres = local, PositionFromStarMetres = position };
            report.Rotation = Rotation(body, period);
            if (body.IsMoon) report.Caveats |= ScienceCaveat.SolarDayRequiresMultipleOrbits | ScienceCaveat.StellarEclipsesAndPlanetshineOmitted;
            double tidal = 0;
            double? dissipation = null;
            if (body.IsMoon)
            {
                if (report.Rotation.SynchronousWithParent && body.Eccentricity <= .1)
                {
                    dissipation = CreateProfile(body).Dissipation;
                    tidal = SynchronousTidalHeatFlux(body, parentMass, dissipation.Value);
                }
                else report.Caveats |= ScienceCaveat.TidalApproximationUnavailable;
            }
            report.Climate = EvaluateClimate(body, StellarFlux(system.StarLuminosity, distance), tidal);
            report.Climate.TidalDissipationK2OverQ = dissipation;
            report.Climate.IsLegacyGeneration = system.GenerationVersion < 3;
            if (body.IsLandable)
            {
                double minFlux = StellarFlux(system.StarLuminosity, report.Orbit.StellarDistanceMaximumBoundMetres);
                report.Climate.SurfaceTemperatureMinimumK = EvaluateClimate(body, minFlux, tidal).SurfaceTemperatureK;
                if (report.Orbit.StellarDistanceMinimumBoundMetres > 0)
                    report.Climate.SurfaceTemperatureMaximumK = EvaluateClimate(body,
                        StellarFlux(system.StarLuminosity, report.Orbit.StellarDistanceMinimumBoundMetres), tidal).SurfaceTemperatureK;
                report.Climate.LiquidWaterModelCompatible = WaterCompatible(report.Climate.SurfacePressurePascals ?? 0,
                    report.Climate.SurfaceTemperatureMinimumK, report.Climate.SurfaceTemperatureMaximumK);
            }
            report.Caveats |= ScienceCaveat.GreyAtmosphereAssumed | ScienceCaveat.NoClimateDynamics | ScienceCaveat.AtmospherePhaseNotModelled;
            if (body.Kind == PlanetKind.Lava) report.Caveats |= ScienceCaveat.LocalLavaTemperatureUnknown;
            if ((body.Kind == PlanetKind.Oceanic && !report.Climate.LiquidWaterModelCompatible) ||
                (body.Kind == PlanetKind.Ice && report.Climate.SurfaceTemperatureMaximumK > 273.16))
                report.Caveats |= ScienceCaveat.VisualClassClimateMismatch;
            return report;
        }

        /// <summary>
        /// Instantaneous global-mean grey estimate. All atmospheres use an assumed mass opacity,
        /// tau=kappa*P/g and epsilonIR=1-exp(-tau), not a spectral greenhouse calculation.
        /// Radiative equilibrium: sigma*Teq^4=S*(1-A)/4; sigma*Teff^4=absorbed+intrinsic+tidal.
        /// The transparent-shortwave single layer emits up and down, giving Ts^4=Teff^4/(1-epsilonIR/2).
        /// No day/night, convection, latent heat, atmosphere loss or composition equilibrium is solved.
        /// Giant temperatures/pressures describe an assumed 1bar reference layer, never solid ground.
        /// </summary>
        public static ClimateDerived EvaluateClimate(PlanetDefinition body, double stellarFluxWm2, double additionalInternalFluxWm2 = 0)
        {
            ValidateBodyGeometry(body);
            Nonnegative(stellarFluxWm2, nameof(stellarFluxWm2));
            Nonnegative(additionalInternalFluxWm2, nameof(additionalInternalFluxWm2));
            Profile profile = CreateProfile(body);
            double gravity = PhysicalConstants.GravitationalConstant * body.Mass / (body.Radius * body.Radius);
            double tau = profile.Opacity * profile.Pressure / gravity;
            double epsilon = 1 - Math.Exp(-tau);
            double absorbed = stellarFluxWm2 * (1 - profile.Albedo) / HeatRedistributionDivisor;
            double effective = Math.Pow((absorbed + profile.Intrinsic + additionalInternalFluxWm2) / StefanBoltzmann, .25);
            double referenceT = effective / Math.Pow(1 - epsilon / 2, .25);
            GasFraction[] gases = profile.Pressure > 0 ? HumidMixture(profile, referenceT) : Array.Empty<GasFraction>();
            double molar = 0;
            foreach (GasFraction gas in gases) molar += gas.MoleFraction * MolarMass(gas.Species);
            double specific = molar > 0 ? MolarGasConstant / molar : 0;
            var result = new ClimateDerived { IncidentStellarFluxWm2 = stellarFluxWm2,
                StellarFluxEarthUnits = stellarFluxWm2 / EarthReferenceFluxWm2, BondAlbedo = profile.Albedo,
                EquilibriumTemperatureK = Math.Pow(absorbed / StefanBoltzmann, .25), EffectiveTemperatureK = effective,
                IntrinsicHeatFluxWm2 = profile.Intrinsic, TidalHeatFluxWm2 = additionalInternalFluxWm2,
                InfraredOpticalDepth = tau, InfraredEmissivity = epsilon, InfraredMassAbsorptionM2Kg = profile.Opacity,
                ReferencePressurePascals = profile.Pressure, ReferenceTemperatureK = referenceT,
                MeanMolarMassKgPerMol = molar, SpecificGasConstantJkgK = specific,
                SurfacePressurePascals = body.IsLandable ? (double?)profile.Pressure : null,
                SurfaceTemperatureK = body.IsLandable ? (double?)referenceT : null,
                AtmosphericDensityKgM3 = profile.Pressure > 0 ? (double?)(profile.Pressure / (specific * referenceT)) : null,
                ScaleHeightMetres = profile.Pressure > 0 ? (double?)(specific * referenceT / gravity) : null,
                HasAtmosphere = profile.Pressure > 0, IsOneBarReference = !body.IsLandable,
                GasMoleFractions = Array.AsReadOnly(gases) };
            result.LiquidWaterModelCompatible = WaterCompatible(profile.Pressure, result.SurfaceTemperatureK, result.SurfaceTemperatureK);
            return result;
        }

        /// <summary>Inverse of exactly the same grey model. Useful for Gen3 orbital placement, without modifying the body.</summary>
        public static double RequiredStellarFluxForSurfaceTemperature(PlanetDefinition body, double temperatureK, double additionalInternalFluxWm2 = 0)
        {
            ValidateBodyGeometry(body); Positive(temperatureK, nameof(temperatureK));
            Nonnegative(additionalInternalFluxWm2, nameof(additionalInternalFluxWm2));
            if (!body.IsLandable) throw new ArgumentException("A gas reference radius is not a solid surface.", nameof(body));
            Profile profile = CreateProfile(body);
            double gravity = PhysicalConstants.GravitationalConstant * body.Mass / (body.Radius * body.Radius);
            double epsilon = 1 - Math.Exp(-profile.Opacity * profile.Pressure / gravity);
            double absorbed = StefanBoltzmann * Math.Pow(temperatureK, 4) * (1 - epsilon / 2) - profile.Intrinsic - additionalInternalFluxWm2;
            return Math.Max(0, absorbed) * HeatRedistributionDivisor / (1 - profile.Albedo);
        }

        public static double StellarFlux(double luminosityW, double distanceMetres)
        {
            Nonnegative(luminosityW, nameof(luminosityW)); Positive(distanceMetres, nameof(distanceMetres));
            return luminosityW / (4 * Math.PI * distanceMetres * distanceMetres);
        }
        public static double EquilibriumTemperature(double fluxWm2, double bondAlbedo)
        {
            Nonnegative(fluxWm2, nameof(fluxWm2));
            if (!IsFinite(bondAlbedo) || bondAlbedo < 0 || bondAlbedo > 1) throw new ArgumentOutOfRangeException(nameof(bondAlbedo));
            return Math.Pow(fluxWm2 * (1 - bondAlbedo) / (HeatRedistributionDivisor * StefanBoltzmann), .25);
        }
        /// <summary>21/2 (k2/Q) G Mparent^2 R^5 n e^2/a^6 divided by 4piR^2. Small-e synchronous eccentricity tide only.</summary>
        public static double SynchronousTidalHeatFlux(PlanetDefinition moon, double parentMassKg, double k2OverQ)
        {
            ValidateBody(moon); Positive(parentMassKg, nameof(parentMassKg)); Nonnegative(k2OverQ, nameof(k2OverQ));
            if (!moon.IsMoon || moon.Eccentricity > .1) throw new ArgumentException("Small-e satellite approximation only.", nameof(moon));
            double period = Orbit.Period(moon, parentMassKg);
            if (Math.Abs(moon.RotationPeriod - period) > period * 1e-6) throw new ArgumentException("Satellite must be synchronously rotating.", nameof(moon));
            double n = 2 * Math.PI / period;
            double power = 10.5 * k2OverQ * PhysicalConstants.GravitationalConstant * parentMassKg * parentMassKg *
                Math.Pow(moon.Radius, 5) * n * moon.Eccentricity * moon.Eccentricity / Math.Pow(moon.SemiMajorAxis, 6);
            double flux = power / (4 * Math.PI * moon.Radius * moon.Radius);
            Nonnegative(flux, "tidal heat flux"); return flux;
        }

        static GeometryDerived Geometry(double radius, double mass, bool solid, BodyReferenceKind kind)
        {
            double volume = 4.0 / 3 * Math.PI * radius * radius * radius;
            double mu = PhysicalConstants.GravitationalConstant * mass;
            return new GeometryDerived { RadiusMetres = radius, DiameterMetres = radius * 2,
                MassKilograms = mass, VolumeCubicMetres = volume, MeanDensityKgM3 = mass / volume,
                GravityAtReferenceRadiusMps2 = mu / (radius * radius), EscapeSpeedAtReferenceRadiusMps = Math.Sqrt(2 * mu / radius),
                HasSolidSurface = solid, ReferenceKind = kind };
        }
        static RotationDerived Rotation(PlanetDefinition body, double orbitalPeriod)
        {
            var result = new RotationDerived { SiderealPeriodSeconds = body.RotationPeriod, AxialTiltRadians = body.AxialTilt,
                EquatorialSpeedMps = 2 * Math.PI * body.Radius / body.RotationPeriod,
                SynchronousWithParent = body.AxialTilt < Math.PI / 2 && Math.Abs(body.RotationPeriod - orbitalPeriod) <= orbitalPeriod * 1e-6 };
            if (body.IsMoon) return result;
            double spin = (body.AxialTilt > Math.PI / 2 ? -1 : 1) / body.RotationPeriod, orbit = 1 / orbitalPeriod;
            double relative = Math.Abs(spin - orbit);
            result.SolarDayInfinite = relative <= Math.Max(Math.Abs(spin), orbit) * 1e-12;
            result.SolarDaySeconds = result.SolarDayInfinite ? null : (double?)(1 / relative);
            return result;
        }

        static Profile CreateProfile(PlanetDefinition body)
        {
            // Separate stream: does not consume or alter catalogue/terrain randomness.
            var random = new DeterministicRandom(DeterministicRandom.Mix(unchecked((ulong)(uint)body.Seed) ^ ClimateStream));
            var p = new Profile(); double co2, argon, hydrogen;
            switch (body.Kind)
            {
                case PlanetKind.Oceanic:
                    p.Albedo = random.Range(.23, .36); p.Pressure = random.Range(65000, 170000);
                    p.Opacity = random.Range(.00007, .00014); p.Intrinsic = random.Range(.04, .12); p.Humidity = random.Range(.4, .7);
                    co2 = random.Range(.0002, .0012); argon = random.Range(.005, .02);
                    p.Dry = Mixture(new GasFraction(GasSpecies.Nitrogen, 1 - co2 - argon), new GasFraction(GasSpecies.CarbonDioxide, co2), new GasFraction(GasSpecies.Argon, argon)); break;
                case PlanetKind.Desert:
                    p.Albedo = random.Range(.22, .4); p.Pressure = random.Range(5000, 70000);
                    p.Opacity = random.Range(.000005, .00004); p.Intrinsic = random.Range(.02, .09);
                    co2 = random.Range(.8, .97); argon = random.Range(.01, .02);
                    p.Dry = Mixture(new GasFraction(GasSpecies.CarbonDioxide, co2), new GasFraction(GasSpecies.Nitrogen, 1 - co2 - argon), new GasFraction(GasSpecies.Argon, argon)); break;
                case PlanetKind.Ice:
                    p.Albedo = random.Range(.45, .75); p.Pressure = random.Range(5, 2000);
                    p.Opacity = random.Range(.000001, .00001); p.Intrinsic = random.Range(.001, .02);
                    co2 = random.Range(.001, .02); argon = random.Range(.005, .02);
                    p.Dry = Mixture(new GasFraction(GasSpecies.Nitrogen, 1 - co2 - argon), new GasFraction(GasSpecies.CarbonDioxide, co2), new GasFraction(GasSpecies.Argon, argon)); break;
                case PlanetKind.Lava:
                    p.Albedo = random.Range(.06, .18); p.Pressure = random.Range(.005, 2);
                    p.Opacity = random.Range(.00001, .0001); p.Intrinsic = random.Range(.04, .12);
                    co2 = random.Range(.05, .25);
                    p.Dry = Mixture(new GasFraction(GasSpecies.SulfurDioxide, 1 - co2), new GasFraction(GasSpecies.CarbonDioxide, co2)); break;
                case PlanetKind.GasGiant:
                case PlanetKind.IceGiant:
                    p.Albedo = random.Range(.25, .5); p.Pressure = 100000;
                    p.Opacity = random.Range(.000005, .00002);
                    p.Intrinsic = body.Kind == PlanetKind.GasGiant ? random.Range(2, 8) : random.Range(.01, .6);
                    hydrogen = body.Kind == PlanetKind.GasGiant ? random.Range(.82, .9) : random.Range(.75, .85);
                    p.Dry = Mixture(new GasFraction(GasSpecies.Hydrogen, hydrogen), new GasFraction(GasSpecies.Helium, 1 - hydrogen)); break;
                default:
                    p.Albedo = random.Range(.08, .25); p.Pressure = random.Range(20, 15000);
                    p.Opacity = random.Range(.000004, .00002); p.Intrinsic = random.Range(.01, .08);
                    co2 = random.Range(.05, .3); argon = random.Range(.001, .01);
                    p.Dry = Mixture(new GasFraction(GasSpecies.Nitrogen, 1 - co2 - argon), new GasFraction(GasSpecies.CarbonDioxide, co2), new GasFraction(GasSpecies.Argon, argon)); break;
            }
            p.Dissipation = body.Kind == PlanetKind.Lava ? random.Range(.003, .02) : random.Range(.0001, .002);
            if (body.IsLandable && body.AtmosphereHeight <= 0) p.Pressure = 0;
            return p;
        }
        static GasFraction[] Mixture(params GasFraction[] values) => values;
        static GasFraction[] HumidMixture(Profile profile, double temperatureK)
        {
            if (profile.Humidity <= 0) return profile.Dry;
            // Approximate Clausius-Clapeyron vapour pressure near temperate water, not a full phase equation.
            // Cap the trace vapour and stay below saturation; no volatile abundance is inferred.
            double water = temperatureK > 180 && temperatureK < 647.096
                ? Math.Min(.04, profile.Humidity * SaturationPressureWater(temperatureK) / profile.Pressure) : 0;
            if (water <= 0) return profile.Dry;
            var result = new GasFraction[profile.Dry.Length + 1];
            for (int i = 0; i < profile.Dry.Length; i++) result[i] = new GasFraction(profile.Dry[i].Species, profile.Dry[i].MoleFraction * (1 - water));
            result[result.Length - 1] = new GasFraction(GasSpecies.WaterVapor, water); return result;
        }
        static double SaturationPressureWater(double temperatureK)
            => 611.657 * Math.Exp(2.5e6 / 461.5 * (1 / 273.16 - 1 / temperatureK));
        static bool WaterCompatible(double pressure, double? minimum, double? maximum)
            => pressure >= 611.657 && minimum.HasValue && maximum.HasValue && minimum.Value >= 273.16 &&
               maximum.Value < 647.096 && pressure >= SaturationPressureWater(maximum.Value);
        public static double MolarMass(GasSpecies species)
        {
            switch (species)
            {
                case GasSpecies.Hydrogen: return .00201588;
                case GasSpecies.Helium: return .004002602;
                case GasSpecies.Nitrogen: return .0280134;
                case GasSpecies.CarbonDioxide: return .0440095;
                case GasSpecies.SulfurDioxide: return .064066;
                case GasSpecies.WaterVapor: return .01801528;
                case GasSpecies.Argon: return .039948;
                default: throw new ArgumentOutOfRangeException(nameof(species));
            }
        }
        static double ParentMass(StarSystemDefinition system, int parent)
        {
            if (parent == -1) return system.StarMass;
            if (parent < 0 || parent >= system.Planets.Length || system.Planets[parent] == null)
                throw new ArgumentException("Invalid planetary parent index.", nameof(system));
            Positive(system.Planets[parent].Mass, "parent mass"); return system.Planets[parent].Mass;
        }
        static void ValidateSystem(StarSystemDefinition system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            Positive(system.StarMass, nameof(system.StarMass)); Positive(system.StarRadius, nameof(system.StarRadius));
            Nonnegative(system.StarLuminosity, nameof(system.StarLuminosity)); Nonnegative(system.StarTemperature, nameof(system.StarTemperature));
        }
        static void ValidateBodyGeometry(PlanetDefinition body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            Positive(body.Radius, nameof(body.Radius)); Positive(body.Mass, nameof(body.Mass));
            Nonnegative(body.AtmosphereHeight, nameof(body.AtmosphereHeight));
        }
        static void ValidateBody(PlanetDefinition body)
        {
            ValidateBodyGeometry(body); Positive(body.SemiMajorAxis, nameof(body.SemiMajorAxis));
            if (!IsFinite(body.Eccentricity) || body.Eccentricity < 0 || body.Eccentricity >= 1) throw new ArgumentOutOfRangeException(nameof(body.Eccentricity));
            Finite(body.Inclination, nameof(body.Inclination)); Finite(body.LongitudeAscendingNode, nameof(body.LongitudeAscendingNode));
            Finite(body.ArgumentPeriapsis, nameof(body.ArgumentPeriapsis)); Finite(body.MeanAnomalyAtEpoch, nameof(body.MeanAnomalyAtEpoch));
            Positive(body.RotationPeriod, nameof(body.RotationPeriod)); Finite(body.AxialTilt, nameof(body.AxialTilt));
        }
        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static void Finite(double value, string name) { if (!IsFinite(value)) throw new ArgumentOutOfRangeException(name); }
        static void Positive(double value, string name) { if (!IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name); }
        static void Nonnegative(double value, string name) { if (!IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
    }
}
