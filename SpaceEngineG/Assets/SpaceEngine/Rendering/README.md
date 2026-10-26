# Rendering implementation

This folder provides URP 17 shaders for parameter-driven terrain, stars and a
camera-local atmospheric scattering integral. No textures or fixed skybox are
used by these systems.

## Runtime interfaces

`AtmosphereRenderer.Configure(Camera, Material)` creates and owns two cloned
materials and two camera-child fullscreen meshes. Pass a material using
`SpaceEngine/Atmosphere`. The component enables the URP camera depth texture.
It renders only for the configured camera. The caller owns the original material.

Call `UpdateAtmosphere(center, planetRadius, atmosphereRadius, directionToSun,
exposure)` after coordinate-origin or render-scale changes. Center and radii are
in the **same render coordinate system as the camera and terrain**. Optical
integration divides all positions by planet radius; changing render scale does
not change the atmosphere's optical thickness. The atmosphere must follow the
active planet when travelling between planetary systems.

`Settings` exposes RGB Rayleigh/Mie coefficients per reference planet radius,
separate exponential density falloffs across the shell, Mie anisotropy, solar
radiance and integration counts. Defaults approximate clear terrestrial air with
a reference radius of 6,371 km and a 100 km shell. Atmosphere thickness and the
density falloffs must be configured together for other worlds. Exposure is a
radiance multiplier; HDR tone mapping belongs to the camera pipeline. The default
solar irradiance is 2 in the renderer's relative display-radiance convention,
chosen consistently with terrain's direct illumination multiplier. It is not an
SI measurement of solar luminance. Optical coefficients and RGB transmittance
retain their physical scale independent of this display normalization.

The first fullscreen draw, queue 3100, multiplies the scene by **RGB
transmittance**. The second, queue 3101, adds single-scattered radiance. This
preserves wavelength-dependent extinction over opaque surfaces and transparent
stellar billboards. Stars must render earlier than queue 3100. Both atmosphere
draws terminate at the opaque camera depth. If no opaque depth exists they stop
at the analytic reference ground sphere; a visible terrain valley below sea
level is therefore not clipped by that fallback sphere. Transparent local objects
do not contribute depth.

`SpaceEngine/Terrain` reads biome RGB from vertex `COLOR`, real mesh normals,
`_SunDirection` (world-space direction **toward** the sun), `_PlanetTint`,
`_SunColor`, `_SunIntensity`, `_AmbientFloor`, `_Roughness` and
`_SpecularStrength`. Its optional artistic fog defaults off; keep it off when
using the physical fullscreen atmosphere. Ambient illumination is an explicit
approximation; it does not simulate global illumination or night sky irradiance.

`SpaceEngine/StarBillboard` reads mesh UV and vertex RGB/alpha. UV (0.5,0.5)
is the stellar core; alpha is the catalog brightness multiplier. The caller
constructs camera-facing angular-size quads from deterministic catalog entries;
the shader supplies radial emission and uses opaque depth to hide stars behind
planets. It has no star positions or baked star texture.

`SpaceEngine/Star` provides a seeded, object-direction-continuous photosphere
granulation field with configurable scale, contrast, intensity and limb
darkening. `_StarTime` is explicit simulation time, avoiding a hidden dependency
on wall-clock shader time. A sphere is the emitting stellar boundary, not an
implementation of the rest of the universe or of stellar interior physics.

## Physical model and limits

The atmosphere explicitly ray-marches the spherical shell for both ground and
space viewpoints. Each view sample integrates attenuation toward the sun and
toward the camera, with a Rayleigh phase function and Henyey–Greenstein Mie
phase function. The analytic planet sphere casts the night-side shadow. The
outermost ten percent smoothly truncates the exponential to avoid a discontinuity
when crossing the atmosphere boundary. This is a numerical single-scattering
model, **not** the full Bruneton or Hillaire multiple-scattering solution.

There is no multiple scattering, ozone absorption, spectral solar integration,
terrain shadowing within the atmosphere, clouds, refraction, or atmosphere
reflected light. Terrain can occlude the view ray through camera depth; sun rays
use the reference sphere. This can miss local mountain shadows and can clip fog
inside terrain below the reference radius. The current full-resolution nested
integration prioritizes a measurable baseline; LUTs and lower-resolution
reconstruction are later performance routes. A perspective camera and the URP
opaque depth texture are required. XR and orthographic cameras are unvalidated.

## Primary technical references

- Sean O'Neil, [GPU Gems 2, Chapter 16: Accurate Atmospheric Scattering](https://developer.nvidia.com/gpugems/gpugems2/part-ii-shading-lighting-and-shadows/chapter-16-accurate-atmospheric-scattering).
  Spherical atmosphere entry/exit geometry and nested optical-depth integration.
- Eric Bruneton, [Precomputed Atmospheric Scattering: a New Implementation](https://ebruneton.github.io/precomputed_atmospheric_scattering/).
  Scale-aware physical parameters and numerical verification as implementation
  requirements; its LUTs and full spectral validation are not included here.
- Sébastien Hillaire, [A Scalable and Production Ready Sky and Atmosphere Rendering Technique](https://diglib.eg.org/bitstream/handle/10.1111/cgf14050/v39i4pp013-022.pdf).
  Ground-to-space rendering and the future LUT/multiple-scattering route.

Shader compilation alone is not visual acceptance. Required render checks are
ground zenith/horizon, low orbit/atmosphere limb, night-side shadow, visible
terrain depth termination, stellar occlusion, and equal views under a coordinate
origin or render-scale change. The root project validation records the actual
screenshots and measured frame timings.

The first rendered review exposed clipped white horizons with the initial solar
irradiance 18 and application exposure 1.6. A separate high-sample double-precision
integration for the 100 km / 6,371 km reference atmosphere gives a daytime
horizon radiance of approximately (1.137, 1.323, 1.223) in that setup: all three
channels exceed the display range before terrain is added. At irradiance 2 the
same integral yields (0.126, 0.147, 0.136), with identical optical transmittance.
The fix corrects radiance normalization rather than removing the physical
extinction. A two-metre ground path transmits more than 99.99% of light under
these reference parameters; missing nearby relief must also be checked in the
terrain/landing position rather than attributed to an atmosphere shader alone.

The Editor menu `Space Engine/Validate Shaders` synchronously compiles all passes
for all four shaders, including both atmosphere material variants, and records
active graphics API support plus all shader compiler messages in
`Artifacts/Latest/shaders.json` at the repository root. The optional batch entry
point is `SpaceEngine.Rendering.Editor.ShaderValidation.RunBatch`. Run it with a
real graphics API; `-nographics` cannot establish GPU support. This report is
explicitly separate from rendered-frame inspection.

`Space Engine/Validate Atmosphere Pixels` performs actual 64 by 64 floating-point
GPU renders using URP's `RenderPipeline.SubmitRenderRequest`, then reads pixels
to check vacuum, blue terrestrial sky, disabling a configured atmosphere,
render-scale invariance, and opaque depth termination at a ten-metre fixture
using the independent URP Unlit shader. It writes `atmosphere-pixels.json` beside the
shader report. The fixture uses an isolated temporary layer and is destroyed
after the test; this geometry is not part of the explorer. The check refuses to
run if that layer is in use or no real graphics device is available.

The independent unlit fixture's expected value uses the material Color property's
sRGB-to-linear conversion when the project uses linear color space. The test
records the first draw after registering the new mesh as a warmup, then checks
the subsequent steady reference against that calculated value with a 0.001
tolerance. The report retains the initial pixel and whether registration changed
it. The ten-metre atmosphere check requires both a small upper error and a
measurable nonzero effect, so an atmosphere that stayed disabled cannot pass.

The atmosphere component executes its visibility lifecycle in the Editor as
well as play mode. This matters for GPU menu checks: disabling a regular
play-mode-only MonoBehaviour in the Editor does not invoke its `OnDisable`, so
its generated renderers can stay visible. The vacuum control is configured with
real atmosphere parameters first, and the disable control follows a proven sky
render, so default sphere geometry cannot produce a misleading black control.
