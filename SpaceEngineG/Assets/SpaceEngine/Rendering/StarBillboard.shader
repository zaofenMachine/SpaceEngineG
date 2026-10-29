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
            // uv: quad corner in [0,1]; uv2: (half size in pixels, display flux).
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 shape : TEXCOORD1; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 shape : TEXCOORD1; float4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.shape = input.shape;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Pixel-scale point spread: a sharp core holding most of the flux,
                // plus a faint halo that grows with brightness. Bloom adds the glare.
                float r = length(input.uv * 2.0 - 1.0) * input.shape.x;
                float flux = input.shape.y * input.color.a;
                float sigma = .62 + .18 * saturate(flux);
                float core = exp(-.5 * r * r / (sigma * sigma)) / (6.2831853 * sigma * sigma);
                float haloScale = 1.1 + 1.2 * sqrt(flux);
                float halo = exp(-r / haloScale) / (6.2831853 * haloScale * haloScale) * .18;
                float fade = 1 - smoothstep(input.shape.x * .7, input.shape.x, r);
                float luminance = (core + halo) * flux * fade * _Intensity * .5;
                return half4(input.color.rgb * _BaseColor.rgb * luminance, 0.0);
            }
            ENDHLSL
        }
    }
}
