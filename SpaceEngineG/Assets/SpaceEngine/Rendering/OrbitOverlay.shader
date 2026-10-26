Shader "SpaceEngine/OrbitOverlay"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+35" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _PixelWidth;
            CBUFFER_END
            struct A { float3 positionOS:POSITION; float3 other:TEXCOORD1; float2 side:TEXCOORD0; float4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float4 color:COLOR; };
            V Vert(A i)
            {
                V o;float4 a=TransformWorldToHClip(i.positionOS),b=TransformWorldToHClip(i.other);
                float2 tangent=(b.xy/max(.0001,b.w)-a.xy/max(.0001,a.w))*_ScreenParams.xy;
                tangent=normalize(tangent+float2(1e-10,0));
                float2 normal=float2(-tangent.y,tangent.x);
                a.xy+=normal*i.side.x*_PixelWidth*2/_ScreenParams.xy*a.w;
                o.positionCS=a;o.color=i.color;return o;
            }
            half4 Frag(V i):SV_Target { return i.color; }
            ENDHLSL
        }
    }
}
