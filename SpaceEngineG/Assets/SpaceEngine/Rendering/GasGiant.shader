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
        _CloudTurbulence ("Band turbulence", Range(0,1)) = .22
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
            float _DetailStrength, _DetailFrequency, _DetailLayers;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "CloudLighting"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
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
                float3 normalWS : TEXCOORD1;
                float3 directionOS : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.directionOS = input.positionOS.xyz;
                return output;
            }

            float CloudPattern(float3 p)
            {
                float latitude = asin(clamp(p.y, -.9999, .9999));
                float longitude = atan2(p.z, p.x);
                // Differential advection is periodic, seam-free in 3D, and keyed by simulation time.
                float wind = sin(latitude * _BandCount * .65) * sin(_WindPhase) * .6;
                float sineWind = sin(wind), cosineWind = cos(wind);
                float3 advected = float3(p.x * cosineWind - p.z * sineWind, p.y,
                    p.x * sineWind + p.z * cosineWind);
                float3 offset = float3(_Seed * .17, _Seed * .31, _Seed * .73);
                float turbulence = CelestialNoise(advected * 12 + offset) * .67;
                turbulence += CelestialNoise(advected * 29 + offset) * .23;
                turbulence += CelestialNoise(advected * 63 + offset) * .1;
                float band = sin(latitude * _BandCount * 2 + (turbulence - .5) * _CloudTurbulence * 8);
                float fineBand = sin(latitude * _BandCount * 6 + turbulence * 3 + _Seed);
                float pattern = .5 + (.37 * band + .13 * fineBand) * _BandContrast;
                pattern = lerp(pattern, .64 + (pattern - .5) * .38, _IceFraction);
                // Seeded elliptical vortices use angular differences, avoiding a longitude texture seam.
                [unroll] for (int storm = 0; storm < 2; storm++)
                {
                    float seed = _Seed + storm * 131.71;
                    float stormLatitude = (.20 + .24 * sin(seed * .77)) * (storm == 0 ? -1 : 1);
                    float stormLongitude = seed * 1.37 + sin(_WindPhase + seed) * (storm == 0 ? .55 : -.31);
                    float delta = atan2(sin(longitude - stormLongitude), cos(longitude - stormLongitude));
                    float2 ellipse = float2(delta * cos(stormLatitude) * .52, latitude - stormLatitude) / max(.01, _StormSize);
                    float radius = length(ellipse);
                    float mask = (1 - smoothstep(.6, 1.3, radius)) * _StormStrength;
                    float swirl = sin(atan2(ellipse.y, ellipse.x) * 3 + radius * 19 - _WindPhase * 3);
                    float stormAlbedo = .22 + .13 * swirl + .18 * turbulence;
                    pattern = lerp(pattern, stormAlbedo, mask * lerp(1, .55, _IceFraction));
                }
                // The original global bands resolve thousand-kilometre structures only.
                // Additional seeded direction-space cells resolve kilometre-scale clouds
                // during approach. Derivative filtering removes them continuously in orbit;
                // this is appearance detail on a non-solid cloud reference, not landable ground.
                float pixelFootprint = max(length(ddx(advected)), length(ddy(advected)));
                float frequency = max(512, _DetailFrequency);
                float amplitude = .45;
                float detail = 0;
                [loop] for (int octave = 0; octave < 8; octave++)
                {
                    if (octave >= (int)_DetailLayers) break;
                    float visibility = 1 - smoothstep(.15, .65, pixelFootprint * frequency);
                    if (visibility <= 0) break;
                    detail += (CelestialNoise(advected * frequency + offset + octave * 17.31) - .5) * amplitude * visibility;
                    frequency *= 2;
                    amplitude *= .62;
                }
                return saturate(pattern + (turbulence - .5) * .16 + detail * _DetailStrength);
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

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 sun = normalize(_SunDirection.xyz);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float diffuse = saturate(dot(normal, sun));
                float transmission = RingTransmission(input.positionWS, sun);
                float pattern = CloudPattern(normalize(input.directionOS));
                float3 albedo = lerp(_DarkCloudColor.rgb, _LightCloudColor.rgb, pattern);
                float limb = pow(1 - saturate(dot(normal, view)), 3);
                float softCloud = pow(saturate(dot(normal, SafeNormalize(sun + view))), lerp(48, 4, _Roughness));
                float3 irradiance = _SunColor.rgb * _SunIntensity * transmission;
                float3 color = albedo * (_AmbientFloor + irradiance * diffuse);
                color += irradiance * diffuse * (.035 * softCloud + .045 * limb);
                return half4(max(0, color), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
