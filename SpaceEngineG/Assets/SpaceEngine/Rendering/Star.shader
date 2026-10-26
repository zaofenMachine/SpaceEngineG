Shader "SpaceEngine/Star"
{
    Properties
    {
        [HDR] _BaseColor ("Stellar photosphere color", Color) = (1,0.64,0.2,1)
        _Intensity ("Photosphere radiance", Float) = 4
        _NoiseScale ("Granulation frequency", Float) = 35
        _GranulationStrength ("Granulation contrast", Range(0,1)) = 0.35
        _LimbDarkening ("Limb darkening", Range(0,1)) = 0.5
        _Seed ("Deterministic pattern seed", Float) = 1
        _StarTime ("Simulation time for plasma motion", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Intensity, _NoiseScale, _GranulationStrength, _LimbDarkening, _Seed, _StarTime;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.directionOS = normalize(input.positionOS.xyz);
                return output;
            }
            float Hash(float3 p)
            {
                p = frac(p * 0.1031 + frac(_Seed * 0.012345));
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float Noise(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash(cell), Hash(cell + float3(1,0,0)), f.x);
                float b = lerp(Hash(cell + float3(0,1,0)), Hash(cell + float3(1,1,0)), f.x);
                float c = lerp(Hash(cell + float3(0,0,1)), Hash(cell + float3(1,0,1)), f.x);
                float d = lerp(Hash(cell + float3(0,1,1)), Hash(cell + float3(1,1,1)), f.x);
                return lerp(lerp(a,b,f.y), lerp(c,d,f.y), f.z);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = normalize(input.directionOS) * _NoiseScale;
                p += float3(_StarTime * 0.015, _StarTime * 0.008, 0.0);
                float granulation = Noise(p) * 0.7 + Noise(p * 2.3) * 0.3;
                float modulation = lerp(1.0, 0.4 + granulation, _GranulationStrength);
                float mu = saturate(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                float limb = 1.0 - _LimbDarkening * (1.0 - mu);
                float3 color = _BaseColor.rgb * _Intensity * modulation * limb;
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
                float4 _BaseColor;
                float _Intensity, _NoiseScale, _GranulationStrength, _LimbDarkening, _Seed, _StarTime;
            CBUFFER_END
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
