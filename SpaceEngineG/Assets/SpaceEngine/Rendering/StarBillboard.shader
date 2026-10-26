Shader "SpaceEngine/StarBillboard"
{
    Properties
    {
        [HDR] _BaseColor ("Stellar tint", Color) = (1,1,1,1)
        _Intensity ("Radiance multiplier", Float) = 2
        _CoreSize ("Core radius", Range(0.01,0.6)) = 0.1
        _Softness ("Halo falloff", Range(0.5,8)) = 2.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Intensity, _CoreSize, _Softness;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float radius = length(input.uv * 2.0 - 1.0);
                float halo = pow(saturate(1.0 - radius), _Softness);
                float core = exp(-radius * radius / max(0.0001, _CoreSize * _CoreSize));
                float luminance = (core + halo * 0.25) * _Intensity * input.color.a;
                return half4(input.color.rgb * _BaseColor.rgb * luminance, 0.0);
            }
            ENDHLSL
        }
    }
}
