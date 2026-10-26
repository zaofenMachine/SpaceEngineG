Shader "SpaceEngine/GalaxyVolumeComposite"
{
    Properties { _MainTex("Integrated galactic radiance",2D)="black"{} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                #if UNITY_REVERSED_Z
                output.positionCS=float4(input.positionOS.xy,0,1);
                #else
                output.positionCS=float4(input.positionOS.xy,1,1);
                #endif
                output.uv=input.uv;
                #if UNITY_UV_STARTS_AT_TOP
                // A Graphics.Blit target and a scene mesh have opposite raster Y
                // conventions on D3D. Preserve the integrated viewing rays here.
                output.uv.y=1-output.uv.y;
                #endif
                return output;
            }
            half4 Frag(Varyings input):SV_Target { return half4(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).rgb,0); }
            ENDHLSL
        }
    }
}
