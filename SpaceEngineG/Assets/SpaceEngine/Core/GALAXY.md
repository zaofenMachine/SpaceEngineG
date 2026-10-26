# Synthetic three-dimensional galaxy model

`GalaxyModel` is an original deterministic approximation of a barred spiral galaxy. It is not a measured Milky Way catalogue, an N-body evolution solver, or a claim to individually instantiate the Galaxy's roughly hundred billion stars. The local cell population uses its physical density; the distant renderer uses bounded luminous population representatives.

## Coordinates and configurable parameters

The galactic plane is x/z; y is vertical. Every `*Ly` parameter is in light-years and each angle is in radians. The origin of universal space remains the home star. The galaxy centre is `(-SolarRadiusLy * LightYear, 0, 0)` in universal SI coordinates, placing the home star 26,700 ly from the centre with default settings. Defaults include a 52,000 ly disc radius, 8,500 ly exponential scale length, 900/3,000 ly thin/thick scale heights, 6,500 ly bulge radius, 12,000 ly bar half-length, four logarithmic arms with 0.22 rad pitch and 1,100 ly perpendicular Gaussian width, and a 160,000 ly stellar halo radius. The central black hole mass metadata is 4.1 million solar masses; it does not yet affect galactic flight trajectories.

`UniversalPosition` remains the authority for travel and local positions. Converting a galaxy-size position to one `Double3` in light-years necessarily loses small distances (the x component near home has about 34 km quantization). `ToGalacticLy` and `FromGalacticLy` are density/render coordinate transforms, not a storage format for flight or a sub-metre position round trip. Exact home transforms exactly to home, while nearby y/z coordinates retain their local precision.

## Density

The thin and thick discs use `exp(-(R - R_sun)/L) * exp(-abs(y)/H)`, with a smooth outer taper from 88% to 100% of the disc radius. The thick-disc component has 0.1 relative midplane amplitude and a scale length 1.2 times the thin-disc value. Logarithmic arm centre lines use `theta = phase(seed) + barAngle + log(R/R_inner)/tan(pitch) + 2*pi*k/armCount`. Gaussian arm contrast is applied to the approximate local perpendicular distance and a younger vertical scale half the thin-disc height. Arms taper in outside 60% of the bar half-length. A finite ellipsoidal exponential bulge and rotated exponential bar fill the centre. A softened spherical power-law stellar halo is tapered to zero at its configured outer radius. The components are summed and normalized so the density at the home location exactly equals `LocalNumberDensityPerCubicParsec` (default 0.1 stars/pc³).

This is number density, not mass density. No unseen dark matter is drawn as stars. Detailed dust extinction, star formation history, exact population synthesis, a galactic gravitational potential, galactic rotation/arm evolution, observed clusters and satellite galaxies are outside this iteration.

## Aggregate sampling

Each particle receives an independent SplitMix64 stream from `(seed, index)`. Appending more particles preserves the previous prefix. There are no Unity random-state dependencies and no generation of per-particle system names or planet arrays. A single output array is allocated. Representative proportions are 46% thin disc, 10% thick disc, 24% arm tracers, 11% bulge, 7% bar and 2% halo; these are configurable-rendering population tracers rather than a calibrated census. Disc cylindrical radius follows the area-correct `r*exp(-r/L)` law; vertical height is a signed exponential. Young-arm samples follow the same logarithmic phase as the analytic density. Bulge and bar samples fill 3D ellipsoids; halo samples fill spherical directions over a logarithmic radial span. Kernel sizes and luminosity are visual aggregate properties, not stellar physical radii or a mass budget.

## Primary references consulted

- [ESA, Guide to our galaxy](https://www.esa.int/Science_Exploration/Space_Science/Gaia/Guide_to_our_galaxy): structural components and approximate spatial scales, including the disc, bar/bulge and stellar halo. Our larger configurable halo extent and component parameters are synthetic choices, not a fit to that illustration.
- [McMillan (2011), Mass models of the Milky Way](https://arxiv.org/abs/1102.4340): parameterized thin/thick discs and the uncertainty of inferred Galactic parameters. We use the family of exponential profiles rather than reproducing its fitted mass model.
- [Bissantz & Gerhard (2002), Spiral arms, bar shape and bulge microlensing in the Milky Way](https://arxiv.org/abs/astro-ph/0110368): a distinct bar and spiral-arm model, with bar shape and orientation constraints. Our analytic ellipsoids and seeded arm phase are an exploration model, not their COBE deprojection.

The code is original and does not incorporate source code or observational data from these references. `GalaxyTests` checks seed repeatability, prefix stability, arm contrast, vertical/radial decay, finite outer support, component volume distributions and the coordinate precision boundary. Visual rendering is accepted separately from these analytic tests.
