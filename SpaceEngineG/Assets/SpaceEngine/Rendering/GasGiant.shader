Shader "SpaceEngine/GasGiant"
{
    Properties
    {
        _LightCloudColor ("High cloud albedo", Color) = (.91,.78,.58,1)
        _DarkCloudColor ("Deep cloud albedo", Color) = (.39,.22,.10,1)
        _SunDirection ("Direction toward sun", Vector) = (1,0,0,0)
        _SunColor ("Stellar light color", Color) = (1,.96,.88,1)
        _SunIntensity ("Stellar irradiance multiplier", Range(0,8)) = 1.5
        _AmbientFloor ("Ambient illumination", Range(0,.3)) = .025
        _BandCount ("Latitude band count", Range(2,64)) = 16
        _BandContrast ("Latitude contrast", Range(0,1)) = .72
        _CloudTurbulence ("Band turbulence", Range(0,1)) = .65
        _StormStrength ("Vortex contrast", Range(0,1)) = .85
        _StormSize ("Vortex angular size", Range(.01,.5)) = .15
        _IceFraction ("Ice giant appearance", Range(0,1)) = 0
        _Roughness ("Cloud roughness", Range(.05,1)) = .92
        _DetailStrength ("Close cloud-cell contrast", Range(0,1)) = .7
        _DetailFrequency ("Close cloud-cell frequency / radius", Range(512,65536)) = 4096
        _DetailLayers ("Close cloud-cell layers", Range(1,8)) = 5
        _WindPhase ("Differential wind phase", Float) = 0
        _Seed ("Seed offset", Float) = 0
        _PlanetCenter ("Planet center in render units", Vector) = (0,0,0,0)
        _PlanetRadius ("Cloud reference radius in render units", Float) = 1
        _RasterInflation ("Conservative raster bounds / physical radius", Float) = 1.001
        _RingNormal ("Equatorial plane normal", Vector) = (0,1,0,0)
        _RingInnerRatio ("Ring inner radius / reference radius", Float) = 0
        _RingOuterRatio ("Ring outer radius / reference radius", Float) = 0
        _RingOpticalDepth ("Ring optical depth", Range(0,3)) = .8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "CelestialProcedural.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _LightCloudColor, _DarkCloudColor, _SunDirection, _SunColor, _PlanetCenter, _RingNormal;
            float _SunIntensity, _AmbientFloor, _BandCount, _BandContrast, _CloudTurbulence;
            float _StormStrength, _StormSize, _IceFraction, _Roughness, _WindPhase, _Seed;
            float _PlanetRadius, _RingInnerRatio, _RingOuterRatio, _RingOpticalDepth;
            float _DetailStrength, _DetailFrequency, _DetailLayers, _RasterInflation;
        CBUFFER_END

        // A generated, inflated closed mesh bounds fragment work. The physical
        // cloud reference is an analytic sphere; polygon chords must never define
        // its silhouette or opaque depth during a ten-kilometre approach.
        float4 CloudRasterPosition(float3 positionWS)
        {
            float4 clipPosition = TransformWorldToHClip(positionWS);
            // The observer may be inside the conservative mesh but remains
            // outside the physical reference. Keep its far bounds rasterizable
            // even when those bounds exceed the camera far plane. Analytic
            // surface hits are clipped to the real camera range below.
            #if UNITY_REVERSED_Z
                clipPosition.z = max(clipPosition.z, clipPosition.w * .00001);
            #else
                clipPosition.z = min(clipPosition.z, clipPosition.w * .99999);
            #endif
            return clipPosition;
        }

        void CloudSphereHit(float3 rasterPositionWS, out float3 hitWS, out float3 normalWS, out float deviceDepth)
        {
            float radius = max(.000001, _PlanetRadius);
            float3 origin = (_WorldSpaceCameraPos - _PlanetCenter.xyz) / radius;
            float3 ray = normalize(rasterPositionWS - _WorldSpaceCameraPos);
            float b = dot(origin, ray);
            float c = dot(origin, origin) - 1;
            float discriminant = b * b - c;
            clip(discriminant);
            float t = -b - sqrt(max(0, discriminant));
            clip(t);
            normalWS = normalize(origin + ray * t);
            hitWS = _WorldSpaceCameraPos + ray * (t * radius);
            float4 clipPosition = TransformWorldToHClip(hitWS);
            clip(clipPosition.w - _ProjectionParams.y);
            clip(_ProjectionParams.z - clipPosition.w);
            deviceDepth = clipPosition.z / clipPosition.w;
            #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                deviceDepth = deviceDepth * .5 + .5;
            #endif
        }
        ENDHLSL
        Pass
        {
            Name "CloudLighting"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz * _RasterInflation);
                output.positionCS = CloudRasterPosition(output.positionWS);
                return output;
            }

            float3 CloudPattern(float3 p)
            {
                float latitude = asin(clamp(p.y, -.9999, .9999));
                float pixelFootprint = max(length(ddx(p)), length(ddy(p)));
                float3 offset = float3(_Seed * .17, _Seed * .31, _Seed * .73);
                // Each jet shears at its own rate. All fields remain periodic and
                // seed/time explicit; no fixed raster or shader wall clock is used.
                float wind = sin(latitude * _BandCount * .65) * sin(_WindPhase) * .6;
                float sineWind = sin(wind), cosineWind = cos(wind);
                float3 advected = float3(p.x * cosineWind - p.z * sineWind, p.y,
                    p.x * sineWind + p.z * cosineWind);
                float stormMask = 0, stormEye = 0;
                // Warp the underlying jet/cloud coordinate field into a coherent
                // vortex instead of painting a circular sine-wave target on top.
                [unroll] for (int storm = 0; storm < 3; storm++)
                {
                    float seed = _Seed + storm * 131.71;
                    float stormLatitude = (.22 + .30 * sin(seed * .77)) * (storm == 0 ? -1 : 1);
                    float stormLongitude = seed * 1.37 + sin(_WindPhase + seed) * .17;
                    float3 center = float3(cos(stormLatitude) * cos(stormLongitude), sin(stormLatitude), cos(stormLatitude) * sin(stormLongitude));
                    float3 east = float3(-sin(stormLongitude), 0, cos(stormLongitude));
                    float3 north = cross(center, east);
                    float size = max(.015, _StormSize) * (storm == 0 ? 1 : .55);
                    float2 local = float2(dot(advected, east) * .6, dot(advected, north)) / size;
                    float radiusSquared = dot(local, local);
                    float mask = exp(-radiusSquared * .85) * step(0, dot(center, advected));
                    float angle = _StormStrength * mask * 5.5 * (storm == 0 ? 1 : -1);
                    float sineAngle = sin(angle), cosineAngle = cos(angle);
                    float2 twisted = float2(local.x * cosineAngle - local.y * sineAngle, local.x * sineAngle + local.y * cosineAngle);
                    float3 vortex = normalize(center + east * twisted.x * size / .6 + north * twisted.y * size);
                    advected = normalize(lerp(advected, vortex, smoothstep(.015, .35, mask)));
                    stormMask = max(stormMask, mask * _StormStrength);
                    stormEye = max(stormEye, exp(-radiusSquared * 5) * _StormStrength);
                }

                pixelFootprint = max(length(ddx(advected)), length(ddy(advected)));
                // Zonal flow shears turbulence into streaks: sample the domain stretched
                // across latitude so structures are long in longitude and thin in latitude.
                float3 zonal = float3(advected.x, advected.y * 4, advected.z);
                float zonalFootprint = pixelFootprint * 4;
                float broad = CelestialFbm(zonal * 3 + offset, zonalFootprint * 3, 3);
                float shear = CelestialFbm(zonal * 9 + offset + 61.2, zonalFootprint * 9, 4);
                float3 warped = normalize(advected + (float3(broad, shear,
                    CelestialNoise(advected * 11 + offset + 31)) - .5) * _CloudTurbulence * .06);
                float warpedFootprint = max(length(ddx(warped)), length(ddy(warped)));
                float warpedLatitude = asin(clamp(warped.y, -.9999, .9999));
                // Belts and zones: a periodic jet structure whose boundaries are
                // displaced by the sheared turbulence, plus a seeded tone per band.
                float phase = warpedLatitude * _BandCount * 1.25 + (shear - .5) * _CloudTurbulence * 4.6 + (broad - .5) * 1.1;
                // Jets give belts fairly sharp edges; steepen the profile without aliasing it.
                float belt = smoothstep(.12, .88, .5 + .5 * CelestialFilteredSin(phase));
                float subJet = CelestialFilteredSin(phase * 2.71 + shear * 5);
                float bandTone = CelestialNoise(float3(warpedLatitude * _BandCount * .55, _Seed * .13, 3.7));
                float bandHue = CelestialNoise(float3(warpedLatitude * _BandCount * .38, _Seed * .29, 9.1));
                float3 streakDomain = float3(warped.x * 28, warped.y * 240, warped.z * 28) + offset;
                float streaks = CelestialFbm(streakDomain, warpedFootprint * 240, 4);
                float cells = CelestialFbm(warped * 95 + offset, warpedFootprint * 95, 3);
                float polar = smoothstep(.72, .97, abs(p.y));
                float pattern = .54 + (belt - .5) * _BandContrast * lerp(.95, .2, polar) * 1.15;
                pattern += (bandTone - .5) * .42 * (1 - polar * .6);
                pattern += (streaks - .5) * .44 + (cells - .5) * (.16 + polar * .45) + subJet * .05;
                pattern += (broad - .5) * .12 - stormEye * .20;
                // Bridge synoptic belts and kilometre cells with unresolved-to-
                // mean mesoscale cloud structure. Near views should reveal a
                // hierarchy, rather than a smooth brown plane between scales.
                float meso = CelestialFbm(warped * 512 + offset + 73.1, warpedFootprint * 512, 3);
                pattern += (meso - .5) * .45 * _DetailStrength;
                pattern = lerp(pattern, .64 + (pattern - .5) * .34, _IceFraction);
                // Small cells continue down to a kilometre scale on approach;
                // footprint filtering suppresses unresolved orbital detail.
                float frequency = max(512, _DetailFrequency);
                float amplitude = .45;
                float detail = 0;
                [loop] for (int octave = 0; octave < 8; octave++)
                {
                    if (octave >= (int)_DetailLayers) break;
                    float visibility = 1 - smoothstep(.15, .65, warpedFootprint * frequency);
                    if (visibility <= 0) break;
                    detail += (CelestialNoise(warped * frequency + offset + octave * 17.31) - .5) * amplitude * visibility;
                    frequency *= 2;
                    amplitude *= .62;
                }
                // z: seeded hue of the band (rusty belts versus pale zones), and polar haze in sign.
                return float3(saturate(pattern + detail * _DetailStrength), stormMask, bandHue * (1 - belt * .55) - polar * .5);
            }

            float RingTransmission(float3 positionWS, float3 sun)
            {
                if (_RingOuterRatio <= _RingInnerRatio || _RingInnerRatio <= 1) return 1;
                float3 normal = normalize(_RingNormal.xyz);
                float denominator = dot(normal, sun);
                if (abs(denominator) < .0001) return 1;
                float3 p = (positionWS - _PlanetCenter.xyz) / max(.000001, _PlanetRadius);
                float t = -dot(p, normal) / denominator;
                float ringRadius = length(p + sun * t);
                float density = CelestialRingDensity(ringRadius, _RingInnerRatio, _RingOuterRatio, _Seed);
                float transmission = exp(-density * _RingOpticalDepth / max(.06, abs(denominator)));
                // Evaluate derivatives before the per-pixel visibility selection.
                return t > .0001 ? transmission : 1;
            }

            struct CloudOutput { half4 color : SV_Target; float depth : SV_Depth; };
            CloudOutput Frag(Varyings input)
            {
                CloudOutput output;
                float3 hitWS, normal;
                CloudSphereHit(input.positionWS, hitWS, normal, output.depth);
                float3 sun = normalize(_SunDirection.xyz);
                float3 view = GetWorldSpaceNormalizeViewDir(hitWS);
                float diffuse = saturate(dot(normal, sun));
                float transmission = RingTransmission(hitWS, sun);
                float3 directionOS = normalize(mul((float3x3)GetWorldToObjectMatrix(), normal));
                float3 pattern = CloudPattern(directionOS);
                float3 albedo = lerp(_DarkCloudColor.rgb, _LightCloudColor.rgb, smoothstep(.08, .92, pattern.x));
                // Chromophore variation between bands (stronger in warm giants) and grey-blue polar haze.
                float hue = saturate(pattern.z) * (1 - _IceFraction * .7);
                // Colour concentrates in the darker belts; bright zones stay cream.
                float beltWeight = 1 - smoothstep(.35, .75, pattern.x);
                albedo = lerp(albedo, albedo * float3(1.25, .86, .6), hue * (.35 + .65 * beltWeight));
                albedo = lerp(albedo, dot(albedo, float3(.3, .5, .2)) * float3(.86, .92, 1.02), saturate(-pattern.z) * .7);
                float3 warmVortex = lerp(albedo, float3(.67, .30, .16), .3 * (1 - _IceFraction));
                albedo = lerp(albedo, warmVortex, pattern.y);
                float limb = pow(1 - saturate(dot(normal, view)), 3);
                float softCloud = pow(saturate(dot(normal, SafeNormalize(sun + view))), lerp(48, 4, _Roughness));
                float3 irradiance = _SunColor.rgb * _SunIntensity * transmission;
                float3 color = albedo * (_AmbientFloor + irradiance * diffuse);
                color += irradiance * diffuse * (.035 * softCloud + .045 * limb);
                output.color = half4(max(0, color), 1);
                return output;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            struct DepthVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            DepthVaryings DepthVert(float4 positionOS : POSITION)
            {
                DepthVaryings output;
                output.positionWS = TransformObjectToWorld(positionOS.xyz * _RasterInflation);
                output.positionCS = CloudRasterPosition(output.positionWS);
                return output;
            }
            struct DepthOutput { half color : SV_Target; float depth : SV_Depth; };
            DepthOutput DepthFrag(DepthVaryings input)
            {
                DepthOutput output;
                float3 hitWS, normal;
                CloudSphereHit(input.positionWS, hitWS, normal, output.depth);
                output.color = 0;
                return output;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            struct NormalVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            NormalVaryings NormalsVert(float4 positionOS : POSITION)
            {
                NormalVaryings output;
                output.positionWS = TransformObjectToWorld(positionOS.xyz * _RasterInflation);
                output.positionCS = CloudRasterPosition(output.positionWS);
                return output;
            }
            struct NormalOutput
            {
                half4 normalWS : SV_Target0;
                #ifdef _WRITE_RENDERING_LAYERS
                    uint renderingLayers : SV_Target1;
                #endif
                float depth : SV_Depth;
            };
            NormalOutput NormalsFrag(NormalVaryings input)
            {
                NormalOutput output;
                float3 hitWS, normal;
                CloudSphereHit(input.positionWS, hitWS, normal, output.depth);
                // Match URP's official UnlitDepthNormalsPass encoding. Forward+
                // SSAO requests this prepass instead of the DepthOnly pass.
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(normal);
                    float2 remapped = saturate(octNormal * .5 + .5);
                    output.normalWS = half4(half3(PackFloat2To888(remapped)),0);
                #else
                    output.normalWS = half4(normal,0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                    output.renderingLayers = EncodeMeshRenderingLayer();
                #endif
                return output;
            }
            ENDHLSL
        }
    }
}
