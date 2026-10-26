Shader "Hidden/SpaceEngine/GasDepthProbe"
{
    Properties { _DepthNormalization ("Depth normalization", Float) = .1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+90" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _DepthNormalization;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.positionOS.xy,0,1);
                output.uv = input.positionOS.xy * .5 + .5;
                #if UNITY_UV_STARTS_AT_TOP
                    output.uv.y = 1-output.uv.y;
                #endif
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float eyeDepth = LinearEyeDepth(SampleSceneDepth(input.uv),_ZBufferParams);
                float3 normal = SampleSceneNormals(input.uv);
                return float4(eyeDepth/max(.000001,_DepthNormalization),normal.y*.5+.5,normal.z*.5+.5,1);
            }
            ENDHLSL
        }
    }
}
