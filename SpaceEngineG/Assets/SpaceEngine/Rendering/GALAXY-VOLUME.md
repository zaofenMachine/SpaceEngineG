# Galactic volume rendering

`GalaxyVolume` integrates a bounded three-dimensional stellar emission and dust
field for the actual galactic observer and camera orientation. It replaces the
large overlapping population kernels as the primary visible structure. The
existing `GalaxyField` mesh remains a seeded spatial stellar accent and retains
its GPU position buffer interface; its default maximum angular radius is now
0.00075 radians rather than 0.005 radians. No image background is bundled.

## Integration and controls

Call `Configure(definition, integrationTemplate, compositeTemplate)`, supplying
materials for `SpaceEngine/GalaxyVolume` and `SpaceEngine/GalaxyVolumeComposite`.
Supplying serialized material references also preserves both shaders in player
builds. Call `Tick(observer, camera)` after updating the observer and orientation.
The component owns cloned materials, one HDR render target and a fullscreen
composite mesh. Caller-owned templates are not mutated. Disable/destroy hides
the renderer and destruction releases its GPU resources.

The analytic thin disk, thick disk, logarithmic spiral arms, ellipsoidal bulge,
bar and finite spherical halo use the geometric parameters of `GalaxyDefinition`
and the same seeded arm phase as `GalaxyModel`. Relative light per population
is intentionally display weighted: young arm tracers are more luminous and blue
than the older disk. A seeded continuous three-dimensional noise field changes
stellar cloud structure and the much thinner molecular dust layer. Dust lanes
follow an offset of the same logarithmic arms. Blue light attenuates more strongly
than red light. This is a synthetic population appearance model, not calibrated
photometry or an observational Milky Way reconstruction.

The GPU integrates emission and wavelength-dependent Beer--Lambert absorption
front to back. Each interval uses an analytic constant-coefficient emission/
absorption solution rather than treating a long segment as an unattenuated
point emitter. A finite disk/bulge box avoids marching enormous empty regions.
For a ray crossing the thin dust slab, 60 percent of the configurable quadrature
budget is assigned to the slab and the rest to the near/far stellar intervals.
This prevents an exterior view from stepping over a 220 ly dust scale height
inside a 100,000 ly path. Rays wholly within the slab use the complete budget.
A separate 16-step optically thin outer-halo integral keeps the halo finite
without diluting the disk budget. Outer-halo light is an approximation that
does not receive the full foreground dust integration.

`ResolutionScale` defaults to 0.5 in each dimension. `SampleCount` defaults to
96 (range 32--192); 192 is the validation reference. `Exposure`, `DustStrength`,
`DustScaleHeightLy` and `DustAttenuationLengthLy` are explicit controls. The target
uses half-float linear radiance and bilinear reconstruction. The far-depth draw
tests the real opaque depth buffer, so it cannot cover a rendered planet.
The subsequent atmospheric extinction pass attenuates this galactic background.
Half-resolution sampling is suitable for broad volume structure; the retained
local catalogue and small stellar accents provide fine points separately.

`InteriorExposureMultiplier` defaults to 0.22, `ExteriorExposureMultiplier` to
1.6, and `Contrast` to 2. These are explicit display controls. Exposure varies
continuously with whether the observer is embedded within the stellar disk;
the contrast toe preserves dark off-plane night sky while the dense galactic
band remains luminous. This display adaptation follows the integrated radiance
and observer position, not a screen-space painted band. Seeded coherent arm
displacement and stronger luminosity/dust knots prevent the analytic logarithmic
skeleton from appearing as identical concentric rings.

Changing the observer, orientation, FOV, aspect, target dimensions, optical
controls, quality controls or structural definition invalidates the texture
cache. An unchanged viewpoint performs no new volume integration. The displayed
`LastSubmissionMilliseconds` measures CPU submission only; actual performance
claims require frame profiling. The renderer has no time-based random noise,
so capture repeatability does not require temporal accumulation.
Rotation cache comparison uses exact quaternion components through `Equals`.
Unity's approximate quaternion `==` accepts roughly 0.16 degrees of difference,
which previously caused small/slow camera turns to reuse an old sky texture.
A 0.02 degree real-Tick regression verifies invalidation followed by stationary
cache reuse.

## Validation

`Space Engine/Validate Galaxy Pixels` renders/readbacks 256-square HDR targets
and saves controls under `Artifacts/V030/GalaxyPixels`. It checks visible
continuous exterior structure, deterministic caching, convergence from 96 to
192 steps, thin edge-on projection, interior dust attenuation over a spatial
area, seed changes, observer translation/perspective, finite empty exterior,
zero exposure and an independent real URP opaque-depth fixture. Separate
checks require the off-plane interior sky to remain dark while retaining the
luminous galactic band. The D3D composite explicitly converts the Blit texture
Y convention to the scene-mesh convention; an unoccluded image comparison
catches a vertically flipped sky independently of the opaque-depth check. Separate
EditMode tests check owned resource lifecycle, geometric parameter upload and
invalid configuration. Compiler support alone is not acceptance. The root
validation also inspects full game and standalone screenshots for exterior,
edge-on, inside-disk and planetary views.

## Primary references and limits

- [Pharr, Jakob and Humphreys, *Physically Based Rendering*, 4th ed.,
  Transmittance](https://www.pbr-book.org/4ed/Volume_Scattering/Transmittance):
  wavelength-dependent Beer--Lambert transmittance and interval composition.
- [The Equation of Transfer](https://www.pbr-book.org/4ed/Light_Transport_II_Volume_Rendering/The_Equation_of_Transfer):
  emission and extinction along a viewing ray. This implementation omits
  multiple scattering and is not an unbiased spectral path tracer.
- [NVIDIA GPU Gems 3, Chapter 30, Real-Time Simulation and Rendering of 3D
  Fluids](https://developer.nvidia.com/gpugems/gpugems3/part-v-physics-simulation/chapter-30-real-time-simulation-and-rendering-3d-fluids):
  reduced-resolution full-screen volume integration and front-to-back sampling.

There are no measured dust maps, individual-star billions-scale catalogue,
multi-galaxy rendering, absolute photometry, HDR eye adaptation, dust scattering
or XR/orthographic-camera validation. Stellar halo integration is deliberately
coarse. The galaxy retains finite seeded spatial structure and observer-dependent
perspective throughout; these limitations do not turn it into a static skybox.
