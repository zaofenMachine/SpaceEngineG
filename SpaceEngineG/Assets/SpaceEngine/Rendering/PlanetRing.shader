Shader "SpaceEngine/PlanetRing"
{
    Properties
    {
        _RingColor ("Particle albedo", Color) = (.85,.79,.64,1)
        _SunDirection ("Direction toward sun", Vector) = (1,0,0,0)
        _SunColor ("Stellar light color", Color) = (1,.96,.88,1)
        _SunIntensity ("Stellar irradiance multiplier", Range(0,8)) = 1.5
        _AmbientFloor ("Ambient illumination", Range(0,.3)) = .025
        _RingInnerRatio ("Inner radius / planet radius", Float) = 1.3
        _RingOuterRatio ("Outer radius / planet radius", Float) = 2.4
        _OpticalDepth ("Particle sheet optical depth", Range(0,3)) = .8
        _ForwardScatter ("Dust forward scattering", Range(0,1)) = .3
        _ShadowSoftness ("Planet shadow penumbra in radius units", Range(.001,.2)) = .025
        _PlanetShadowStrength ("Analytic planet shadow", Range(0,1)) = 1
        _Seed ("Radial band seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Name "RingScattering"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CelestialProcedural.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _RingColor, _SunDirection, _SunColor;
                float _SunIntensity, _AmbientFloor, _RingInnerRatio, _RingOuterRatio;
                float _OpticalDepth, _ForwardScatter, _ShadowSoftness, _PlanetShadowStrength, _Seed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(float3(0,1,0));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 sun = normalize(_SunDirection.xyz);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 normal = normalize(input.normalWS);
                float3 localSun = normalize(mul((float3x3)GetWorldToObjectMatrix(), sun));
                float radius = length(input.positionOS.xz);
                float density = CelestialRingDensity(radius, _RingInnerRatio, _RingOuterRatio, _Seed);
                // Infinite directional sunlight; the planet is a unit sphere in ring object coordinates.
                float t = -dot(input.positionOS, localSun);
                float distanceToAxis = length(input.positionOS + localSun * max(0, t));
                float shadowWidth = max(_ShadowSoftness, fwidth(distanceToAxis));
                float shadow = t > 0 ? smoothstep(1 - shadowWidth, 1 + shadowWidth, distanceToAxis) : 1;
                shadow = lerp(1, shadow, _PlanetShadowStrength);
                float lightElevation = abs(dot(normal, sun));
                float viewingElevation = max(.025, abs(dot(normal, view)));
                float opacity = 1 - exp(-density * _OpticalDepth / viewingElevation);
                float forward = pow(saturate(dot(-sun, view)), 8) * _ForwardScatter;
                float reflected = .23 + .77 * lightElevation;
                float3 color = _RingColor.rgb * (_AmbientFloor + _SunColor.rgb * _SunIntensity * shadow * (reflected + forward));
                color *= .78 + .22 * CelestialFilteredSin(radius * 167 + _Seed);
                return half4(max(0, color), saturate(opacity));
            }
            ENDHLSL
        }
    }
}
