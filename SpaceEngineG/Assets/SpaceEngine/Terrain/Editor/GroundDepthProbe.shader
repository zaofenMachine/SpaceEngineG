Shader "Hidden/SpaceEngine/GroundDepthProbe"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 Vert(float4 positionOS:POSITION):SV_POSITION { return float4(positionOS.xy,0,1); }
            float4 Frag(float4 positionCS:SV_POSITION):SV_Target
            {
                float2 uv=GetNormalizedScreenSpaceUV(positionCS);
                float depth=SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    float occupied=depth>0?1:0;
                #else
                    float occupied=depth<1?1:0;
                #endif
                return float4(occupied,saturate(LinearEyeDepth(depth,_ZBufferParams)/1000),0,1);
            }
            ENDHLSL
        }
    }
}
