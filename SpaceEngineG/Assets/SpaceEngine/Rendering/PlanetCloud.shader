Shader "SpaceEngine/PlanetCloud"
{
    Properties
    {
        _CloudColor ("Cloud single-scattering albedo", Color) = (.96,.97,1,1)
        _SunDirection ("Direction toward sun", Vector) = (1,0,0,0)
        _SunColor ("Stellar light color", Color) = (1,.96,.88,1)
        _SunIntensity ("Stellar irradiance multiplier", Range(0,8)) = 1.5
        _AmbientFloor ("Sky ambient approximation", Range(0,.2)) = .018
        _Coverage ("Weather coverage", Range(0,1)) = .42
        _Opacity ("Vertical optical depth", Range(0,2)) = .65
        _ScatteringScale ("Cloud radiance normalization", Range(.1,1)) = .55
        _WeatherScale ("Synoptic weather frequency / radius", Range(1,20)) = 5
        _DetailStrength ("Cloud edge erosion", Range(0,1)) = .45
        _Seed ("Seed offset", Float) = 0
        _WindPhase ("Simulation weather advection", Float) = 0
        _ObserverFade ("Fade before entering thin layer", Range(0,1)) = 1
        _SurfaceRadiusRatio ("Opaque reference radius / weather radius", Float) = .998
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20" }
        Pass
        {
            Name "SeededThinWeather"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CelestialProcedural.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _CloudColor, _SunDirection, _SunColor;
                float _SunIntensity, _AmbientFloor, _Coverage, _Opacity, _WeatherScale;
                float _DetailStrength, _Seed, _WindPhase, _ObserverFade, _SurfaceRadiusRatio, _ScatteringScale;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 directionOS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.directionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            float CloudDensity(float3 direction, float footprint)
            {
                float sineWind = sin(_WindPhase), cosineWind = cos(_WindPhase);
                float3 p = float3(direction.x * cosineWind - direction.z * sineWind,
                    direction.y, direction.x * sineWind + direction.z * cosineWind);
                float3 seed = float3(.17, .31, .73) * _Seed;
                float3 synoptic = p * max(1, _WeatherScale);
                float front = CelestialFbm(synoptic + seed, footprint * _WeatherScale, 4);
                float curl = CelestialNoise(synoptic * 1.9 + seed + 43.1);
                float3 warped = p + (float3(front, curl, 1 - front) - .5) * .13;
                float cells = CelestialFbm(warped * 72 + seed, footprint * 72, 4);
                float wisps = CelestialFbm(warped * 217 + seed + 9.3, footprint * 217, 3);
                float field = front + (cells - .5) * .24 + (wisps - .5) * _DetailStrength * .13;
                float threshold = lerp(.79, .23, _Coverage);
                return smoothstep(threshold - .055, threshold + .095, field);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionOS);
                float3 normal = normalize(input.normalWS);
                float3 sun = normalize(_SunDirection.xyz);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float footprint = max(length(ddx(direction)), length(ddy(direction)));
                float density = CloudDensity(direction, footprint);
                float mu = dot(normal, sun), nv = saturate(dot(normal, view));
                // At cloud altitude the spherical planet casts its shadow slightly
                // beyond the ground terminator. This is analytic single scattering,
                // not a volumetric weather/pressure simulation.
                float horizon = sqrt(saturate(1 - _SurfaceRadiusRatio * _SurfaceRadiusRatio));
                float day = smoothstep(-horizon, .025, mu);
                float forward = pow(saturate(dot(view, sun)), 8) * .10;
                float lighting = day * (.16 + .84 * saturate(mu)) + forward * day;
                // A thin/feathered edge transmits the terrain below. Dense weather
                // cores remain brighter without saturating broad white blankets.
                float opticalDepth = pow(saturate(density), 1.35) * max(0, _Opacity) * 2;
                float alpha = (1 - exp(-opticalDepth / max(.24, nv))) * _ObserverFade;
                alpha *= smoothstep(0, .035, nv);
                float shadow = lerp(.68, 1, sqrt(saturate(density)));
                float3 light = _CloudColor.rgb * (_AmbientFloor * float3(.64,.76,1)
                    + _SunColor.rgb * _SunIntensity * lighting * shadow * _ScatteringScale);
                return half4(max(0, light) * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
