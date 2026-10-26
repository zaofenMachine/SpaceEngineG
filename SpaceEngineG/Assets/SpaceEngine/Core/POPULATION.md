# Deterministic population, generation version 2

All positions use the existing integer-sector plus bounded-double coordinate. `GenerateSystem(seed,x,y,z)` retains candidate-generation compatibility, even for an empty cell. Runtime streamers should call `IsCellOccupied` or `TryGenerateSystem`. The admission query samples the galaxy density at exactly the same jittered candidate position, hashes a separate admission stream, and applies `1-exp(-density*cellVolume)` with density in stars/pc³ and a 4 ly cell. The origin is an intentional occupied starting system. Each cell resolves at most one system; dense regions contain additional unresolved population represented by the galaxy renderer.

`IsCellOccupied` avoids constructing planets. Its overload accepts a `GalaxyDefinition`; the default overload caches the latest seeded settings per thread. This cache is internal and independent of caller-editable settings.

## Stellar classes

The seeded mixture includes red dwarfs, other main-sequence stars, blue main-sequence stars, red giants, white dwarfs, neutron stars and isolated stellar black holes. Main-sequence stars dominate; compact remnants are rare. The home system is deliberately a solar-like main-sequence star so the landing regression remains accessible. The weights are a development population preset, not a measured initial-mass function or a stellar-evolution calculation.

Mass/radius/temperature ranges differ by class. The emitted bolometric luminosity is `4*pi*radius²*sigma*temperature⁴` in watts. Black-hole radius is the Schwarzschild event-horizon radius `2GM/c²`; its emitted luminosity is zero because this model has no accretion. No relativistic trajectories, lensing or accretion simulation is implied by the classification.

The first reference orbit scales with `sqrt(luminosity/solarLuminosity)`, with a minimum 0.06 AU and 12 stellar radii. Expanded red giants therefore do not receive a planet inside their photosphere. Primary giant orbits have an additional Hill-radius guard so their moon systems fit safely even around a massive compact object.

## Body classes and hierarchy

Each system contains five primaries plus one to three moons: an oceanic landing body, a rocky or desert world, an icy world, a gas giant and an ice giant. The first giant moon is volcanic; further icy/rocky moons vary by seed. This deliberately supplies explorable examples in each streamed system. It is not an occurrence-rate prediction or a formation history.

`Planets` is a flattened hierarchy. The first five entries orbit the star with `ParentIndex=-1`; moons appear later and reference an earlier parent index. Orbital elements for a moon are relative to its parent, so renderers must sum the parent's system-relative position and evaluate the moon with the parent's mass. Moon rotation is synchronized to the analytic orbital period. Moon apoapsides remain below 0.35 of the parent's pericentre Hill radius and outside the ring edges. This is a conservative two-body stability check, not an N-body proof.

All masses are calculated from radius and a composition-dependent bulk density. Atmospheric heights, color, terrain relief, rotation, obliquity and ring radii are seeded parameters. Gas and ice giants have no landable solid surface and zero terrain relief. Ring edge radii are absolute metres from the planet centre. The landable body kinds cover rocky, oceanic, desert, ice and lava surfaces; the existence of an oceanic preset does not assert liquid-water habitability, climate equilibrium, biosphere or survival through a progenitor's giant phase. Climate and stellar evolution remain separate future systems.

## Typed solid terrain

Oceanic terrain retains the original metre-scaled height field unchanged; three recorded numeric samples protect its landing geometry. Rocky and icy terrain add ten deterministic analytic crater bowls with raised rims to that height field, before its final bounded `tanh` transform. Crater directions and angular radii are cached per seed/thread, so each height query performs ten dot products and evaluates an exponential rim only inside a crater. This keeps the features part of the actual CPU mesh rather than a surface-color illusion. Gas and ice giants return exactly zero terrain height.

Desert material uses height-dependent strata; ice uses bright blue-white material; lava combines dark crust with a deterministic hot-fissure field. Lava fissures also lower the actual geometry. Vertex `COLOR.a` retains the CPU fissure mask as diagnostic data only for lava; every other terrain kind, including ocean and skirt vertices, has exactly zero alpha. Visible lava albedo and emission are evaluated per fragment from bounded planet-relative coordinates, so the fissure network is independent of the CPU color sample grid and terrain mesh LOD. Each patch provides its double anchor divided by radius plus inverse radius, avoiding large world-float subtraction near the ground. The GPU field uses seeded 32-bit integer hashing; it is a deterministic visual approximation of the CPU geology rather than a bit-identical copy. Emission strength, plate frequency and fissure width are configurable material properties, with derivative filtering at small screen scales. The existing 0.25 m physical normal sampling and two-sided skirts are preserved.

`TypedTerrainTests` verifies height bounds, deterministic sampling, zero non-lava emission, genuine crater bowls/rims, typed palettes and lava troughs. `TypedTerrainGeometryTests` verifies actual vertex radii, fixed-interval normals, emission masks/skirt data and cube-chart continuity. These numeric checks do not replace screenshots of the rendered worlds.

## Verification

`PopulationTests` checks class coverage, new-field determinism, valid earlier-parent references, moon/ring clearance, Hill envelopes, orbit-period closure, densities, landing rules, Stefan-Boltzmann luminosity, compact radii, cell admission consistency and empty intergalactic space. The updated legacy generation test checks non-crossing primary orbits and separately validates parent-relative moon orbits. Runtime images and tests are reported by the main validation pipeline; data checks alone are not visual acceptance.

## Primary sources consulted, 2026-10-26

- [NASA: Types of Stars](https://science.nasa.gov/universe/stars/types/) supports separating main-sequence stars, evolved giants and compact remnants rather than applying a solar scaling law to all objects.
- [Fortney & Nettelmann, 2010: The Interior Structure, Composition, and Evolution of Giant Planets](https://arxiv.org/abs/0912.0533) motivates separate gas-giant and ice-giant composition/radius presets. This implementation does not solve the paper's interior equations.
- [NASA: Saturn Facts](https://science.nasa.gov/saturn/facts/) motivates treating a gas giant as non-landable and modeling rings and moons as distinct structures.
- [NASA NSSDCA: Saturnian Rings Fact Sheet](https://nssdc.gsfc.nasa.gov/planetary/factsheet/satringfact.html) supplies a scale reference for ring-edge ratios; generated rings use their own deterministic parameters rather than copying Saturn.
