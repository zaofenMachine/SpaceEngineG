# Procedural weather appearance, revision 0.3

The previous giant shader's clean sinusoidal belts and concentric painted storms
read as rings on a sphere. Its replacement uses fewer, broader irregular belts,
differential jet advection, three seeded coordinate-warp vortices, and separate
synoptic, feathered-cloud and close cloud-cell scales. The polar belt contrast
weakens while cellular structure strengthens. A storm deforms the same cloud
field used by surrounding jets instead of overlaying an unrelated bullseye.
Warm giants use cream/ochre albedo; ice giants keep their distinct cooler colors.

All spatial fields evaluate normalized three-dimensional direction, with body
seed and explicit simulation time. There is no imported image or longitude
texture seam. Each fBm octave is tapered by the projected pixel footprint.
Unresolved octaves converge to their mean; the remaining latitude sinusoids use
the existing analytically filtered sine function. Frequencies remain configurable
on `CelestialBodyRenderer` and its material. These are appearance fields, not a
fluid, chemistry, pressure or planetary evolution simulation.

The near-surface visual review exposed a separate geometric error: a 384 by 192
inscribed sphere has kilometre-scale polygon chord error at a 68,849 km reference
radius, even though its silhouette looks smooth in orbit. At a ten-kilometre
approach that error creates visible horizon steps. Both the color and DepthOnly
passes now intersect the exact physical sphere per fragment and write the
analytic surface depth. A slightly inflated generated mesh only bounds raster
work. Its far bound can remain rasterizable beyond the camera far plane while
the true hit is clipped to the actual near/far range. The observer may enter the
conservative raster bounds, so both faces are rasterized; no physical radius,
collision boundary or safety clearance is changed. A filtered mesoscale cloud
field bridges planetary belts and close cells so the view has structure between
those spatial scales.

The shipped Forward+ renderer's SSAO feature requests a DepthNormalsOnly
prepass. Gas giants therefore also provide an analytic DepthNormalsOnly pass,
with the same hit/depth and URP's official world-normal/octahedral packing plus
rendering-layer output variants. A DepthOnly pass alone does not populate this
pipeline's camera depth; treating it as sufficient left atmosphere sampling a
false far plane even though isolated color rendering looked correct.

## Oceanic cloud integration

`PlanetCloudRenderer.Configure(PlanetDefinition, Material)` creates a separate
weather shell only for an Oceanic body. Use a `SpaceEngine/PlanetCloud` template.
`Tick(cameraFromPlanetMeters, renderScale, directionTowardSun, simulationTime)`
uses the same double-precision subtraction followed by common render-scale
multiplication as adaptive terrain. The shell radius is the physical reference
radius plus configurable cloud height (default 12 km, limited to 2% of body radius).

The renderer adds synoptic fronts, edge erosion and wisps from seeded 3D fields.
Explicit wind advection in radians/day is relative to the current landable
surface frame. It does not add independent rigid rotation to a terrain frame
that is currently static. Coverage, optical depth, weather scale, detail strength,
wind, height, direct irradiance and ambient approximation are configurable.

Defaults use coverage 0.42 and optical-depth control 0.65. Cloud radiance is
normalized by a configurable material scattering factor (default 0.55), with
more transparent feathered edges, so white cores retain display headroom and
clear/thin windows reveal the seeded land and sea below.

Clouds render premultiplied alpha at queue 3020, after background stars/rings and
before the full-screen atmosphere. Opaque terrain depth occludes the far side;
clouds do not write a fake solid surface into camera depth. Direct light uses
the analytic planet horizon at cloud altitude, with a thin-layer slant optical
depth and limb taper. Ambient illumination and the cloud relief shading are
explicit approximations, not atmospheric multiple scattering or a calibrated
cloud phase function.

The observer fade is zero at or below 1.1 cloud heights and reaches one at two
cloud heights. This prevents the orbit-only thin sheet becoming a horizontal
ceiling or clipping inside the camera during descent. No collider, safety plane,
terrain replacement, landability change or ground elevation change is created.
Volumetric cloud entry, terrain cloud shadows, coupled weather evolution,
multiple scattering and local storm simulation remain future work.

## Verification

Five EditMode tests cover landable-body eligibility, lack of colliders, physical
radius/scale, fade-before-entry lifecycle, explicit seed/clock and invalid input.
The separate `Space Engine/Validate Planet Cloud Pixels` menu performs ten
actual floating-point URP pixel differentials, including seeded geography,
repeatability, time advection, daylight versus planet shadow, render-scale
invariance, coverage-zero control, entry fade, default clear-window coverage and
radiance headroom. Its independent blue opaque
sphere is a temporary isolated fixture, not the explorer's planet surface.
The report and PNGs are under `Artifacts/V030/CloudPixels`. GPU readback timings
are not gameplay frame timings. Whole-planet visual review and actual flight
acceptance are separate from these isolated checks.

`Space Engine/Validate Gas Horizon Pixels` adds seven actual GPU near-tangent
regressions at 10 km above a 68,849 km radius: coarse and production silhouette
agreement with independent double ray/sphere intersections, mesh-density
invariance, and the actual DepthOnly pass occluding an independent plane at the
analytically expected pixels. Independent plane-only controls check logical
depth clearing and draw setup before the explicit depth differential; Unity's
logical far-depth clear is 1 even on reverse-Z backends. The final two checks
use a real URP camera render and read its actual depth/normal textures, so a
missing DepthNormalsOnly prepass cannot hide behind a passing manual draw.
The probe records post-submit global texture formats separately because
RenderGraph can reset those globals to fallback textures before the request
returns. It identifies the live renderer mode and invokes URP's actual
device-dependent `DepthNormalOnlyPass.GetGraphicsFormat()` selection; local URP
source uses that exact method for Forward/ForwardPlus attachments. It refuses
to infer this format for a deferred renderer. For the selected
`R8G8B8A8_SNorm` normal target, the report retains both the unquantized error and
the ideal round-to-nearest CPU diagnostic. The pass/fail oracle uses the maximum
per-component error of every covered pixel in integer ULP units. Microsoft
[FLOAT to SNORM rules](https://microsoft.github.io/DirectX-Specs/d3d/archive/D3D11_3_FunctionalSpec.htm#3.2.3.4%20FLOAT%20-%3E%20SNORM)
and the [D3D12 header](https://github.com/microsoft/DirectX-Headers/blob/main/include/directx/d3d12.idl)
permit 0.6 ULP for this conversion, so the independently derived per-component
bound is 0.6/127. A bit-exact ideal-round assertion was inappropriate: the device
legally changes conversion at a slightly different boundary. This separates
specified storage quantization from a wrong sphere normal without relaxing the
independent depth and silhouette checks.
A 24 by 12 coarse mesh deliberately makes the old
polygon solution fail visibly. Reports and control PNGs are under
`Artifacts/V030/GasHorizonPixels`.

## Primary references and adopted scope

- [NASA Jupiter facts](https://science.nasa.gov/jupiter/jupiter-facts/) describes
  cloud bands and cyclonic storms and the lack of a true solid surface. This
  motivates retaining the non-landable cloud reference for giant planets.
- [NASA, Jupiter's Colorful Cloud Belts](https://www.nasa.gov/image-article/jupiters-colorful-cloud-belts/)
  shows that belts contain swirling cloud structure, which motivated replacing
  uniform stripes with warped turbulent cells. This is visual motivation, not
  a claim that our procedural noise reproduces Jupiter's measured circulation.
- [Schneider and Vos, The Real-Time Volumetric Cloudscapes of Horizon Zero Dawn](https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn)
  motivates separating weather coverage, cloud shape and smaller erosion scales.
  This implementation uses a much cheaper thin optical sheet for orbital views;
  it does not implement their volumetric ray march or claim comparable quality.

All added code is original to this project. No third-party image, mesh or cloud
asset is distributed.
