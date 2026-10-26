Shader "SpaceEngine/Terrain"
{
    Properties
    {
        _PlanetTint ("Biome tint", Color) = (1,1,1,1)
        _SunDirection ("Direction toward sun", Vector) = (1,0,0,0)
        _SunColor ("Stellar light color", Color) = (1,0.96,0.88,1)
        _SunIntensity ("Stellar irradiance multiplier", Range(0,8)) = 1.5
        _AmbientFloor ("Ambient illumination", Range(0,1)) = 0.035
        _Roughness ("Surface roughness", Range(0.04,1)) = 0.8
        _SpecularStrength ("Dielectric reflection", Range(0,1)) = 0.12
        _PlanetCenter ("Planet center (render units)", Vector) = (0,0,0,0)
        _FogStrength ("Optional artistic aerial fog (off with physical atmosphere)", Range(0,1)) = 0
        _FogColor ("Optional aerial fog color", Color) = (0.2,0.4,0.7,1)
        _FogDensity ("Optional aerial fog density per render unit", Float) = 0.0001
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "TerrainLighting"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PlanetTint, _SunDirection, _SunColor, _PlanetCenter, _FogColor;
                float _SunIntensity, _AmbientFloor, _Roughness, _SpecularStrength;
                float _FogStrength, _FogDensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 lightDirection = normalize(_SunDirection.xyz);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 halfDirection = SafeNormalize(viewDirection + lightDirection);
                float nDotL = saturate(dot(normal, lightDirection));
                float nDotV = saturate(dot(normal, viewDirection));
                float nDotH = saturate(dot(normal, halfDirection));
                float lDotH = saturate(dot(lightDirection, halfDirection));
                float roughness = max(0.04, _Roughness);
                float a2 = roughness * roughness * roughness * roughness;
                float denominator = nDotH * nDotH * (a2 - 1.0) + 1.0;
                float distribution = a2 / max(0.0001, 3.14159265 * denominator * denominator);
                float k = (roughness + 1.0) * (roughness + 1.0) * 0.125;
                float geometry = nDotL / max(0.001, nDotL * (1.0 - k) + k);
                geometry *= nDotV / max(0.001, nDotV * (1.0 - k) + k);
                float fresnel = 0.04 + 0.96 * pow(1.0 - lDotH, 5.0);
                float specular = distribution * geometry * fresnel / max(0.001, 4.0 * nDotL * nDotV);
                float3 albedo = max(0.0, input.color.rgb * _PlanetTint.rgb);
                float3 irradiance = _SunColor.rgb * _SunIntensity;
                float3 color = albedo * (_AmbientFloor + irradiance * nDotL);
                color += irradiance * nDotL * specular * _SpecularStrength;
                float distanceToCamera = distance(input.positionWS, _WorldSpaceCameraPos);
                float fog = (1.0 - exp(-distanceToCamera * max(0.0, _FogDensity))) * _FogStrength;
                color = lerp(color, _FogColor.rgb, saturate(fog));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            ZWrite On
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _PlanetTint, _SunDirection, _SunColor, _PlanetCenter, _FogColor;
                float _SunIntensity, _AmbientFloor, _Roughness, _SpecularStrength;
                float _FogStrength, _FogDensity;
            CBUFFER_END
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }
            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
