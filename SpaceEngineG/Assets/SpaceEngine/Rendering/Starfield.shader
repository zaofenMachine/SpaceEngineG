Shader "SpaceEngine/Starfield"
{
    // All inputs are global (StarfieldRenderer) and shared with the black-hole lens.
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-15" }
        Pass
        {
            Name "UnresolvedStars"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Starfield.hlsl"
            struct Attributes { float3 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                #if UNITY_REVERSED_Z
                output.positionCS=float4(input.positionOS.xy,0,1);
                #else
                output.positionCS=float4(input.positionOS.xy,1,1);
                #endif
                output.uv=input.positionOS.xy*.5+.5;
                #if UNITY_UV_STARTS_AT_TOP
                output.uv.y=1-output.uv.y;
                #endif
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float3 nearWorld=ComputeWorldSpacePosition(input.uv,UNITY_NEAR_CLIP_VALUE,UNITY_MATRIX_I_VP);
                float3 direction=normalize(nearWorld-_WorldSpaceCameraPos);
                float3 band=SAMPLE_TEXTURE2D_LOD(_GalaxyRadiance,sampler_GalaxyRadiance,input.uv,0).rgb;
                float luminance=dot(band,float3(.2126,.7152,.0722));
                return half4(Starfield(direction,luminance)+DistantGalaxies(direction,luminance)*_StarfieldOptics.y,0);
            }
            ENDHLSL
        }
    }
}
