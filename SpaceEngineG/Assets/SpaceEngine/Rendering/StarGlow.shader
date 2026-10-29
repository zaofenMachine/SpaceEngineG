Shader "SpaceEngine/StarGlow"
{
    // Corona and optical glare around the local star. Drawn at the far plane so any
    // opaque body in front (the photosphere itself, planets, moons) occludes it.
    Properties
    {
        _GlowDirection ("Camera-relative direction to star", Vector) = (0,0,1,0)
        [HDR] _GlowColor ("Photosphere radiance", Color) = (4,4,4,1)
        _GlowShape ("Angular radius, pixel angle, unresolved boost, streamer seed", Vector) = (.01,.001,1,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-5" }
        Pass
        {
            Name "StellarCorona"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _GlowDirection, _GlowColor, _GlowShape;
            CBUFFER_END
            // Global, 0 unless an observer sits in a sunlit atmosphere: the sky then hides the corona.
            float _SkyDaylight;
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
                float3 ray=normalize(nearWorld-_WorldSpaceCameraPos);
                float3 star=normalize(_GlowDirection.xyz);
                float cosine=dot(ray,star);
                float theta=acos(clamp(cosine,-1,1));
                float rho=_GlowShape.x,pixel=_GlowShape.y;
                // Unresolved photospheres keep a visible point: a fixed-size core whose
                // amplitude is the compressed flux ratio supplied by the CPU.
                float resolved=max(rho,pixel*1.4);
                float x=theta/resolved;
                if(x>60||cosine<0)return 0;
                float3 up=abs(star.y)<.95?float3(0,1,0):float3(1,0,0);
                float3 east=normalize(cross(up,star)),north=cross(star,east);
                float3 offset=ray-star*cosine;
                float azimuth=atan2(dot(offset,north),dot(offset,east));
                float seed=_GlowShape.w;
                float streamers=.55+.45*sin(azimuth*3+seed)*sin(azimuth*7.3+seed*1.7)+.2*sin(azimuth*17+seed*2.3);
                float outside=max(0,x-1);
                // Start below the limb's own display brightness so the disc edge reads as
                // the brightest boundary; bloom supplies the near-limb glare.
                float inner=exp(-outside*10)*.08;
                float corona=pow(max(1,x),-2.4)*.06*(.7+.6*streamers);
                float halo=pow(max(1,x),-1.35)*.01;
                float core=rho<pixel*1.4?exp(-theta*theta/(2*pixel*pixel*.5))*1.2:0;
                float amount=(inner+(corona+halo)*(1-_SkyDaylight))*step(1,x)+core;
                return half4(_GlowColor.rgb*amount*_GlowShape.z,0);
            }
            ENDHLSL
        }
    }
}
