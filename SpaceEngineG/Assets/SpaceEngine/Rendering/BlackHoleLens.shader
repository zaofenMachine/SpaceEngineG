Shader "SpaceEngine/BlackHoleLens"
{
    // Schwarzschild lensing of the sky behind a black hole. Rays with impact parameter
    // below 3*sqrt(3)/2 Rs are captured (the shadow); others are bent toward the hole by
    // the exact null-geodesic deflection angle from a precomputed table, and the
    // procedural starfield and integrated galactic radiance are re-evaluated along the
    // bent direction. Resolved catalogue stars and opaque bodies are not re-traced.
    Properties
    {
        _LensCentre ("Camera-relative black hole position (render units)", Vector) = (0,0,10,0)
        _LensShape ("Schwarzschild radius, pixel angle, table min log, table span", Vector) = (1,.001,-9.21,14.5)
        _DeflectionTable ("Deflection angle by log(b - b_crit)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+1" }
        Pass
        {
            Name "GravitationalLens"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend Off
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Starfield.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _LensCentre, _LensShape;
            CBUFFER_END
            TEXTURE2D(_DeflectionTable); SAMPLER(sampler_DeflectionTable);
            float _SkyDaylight;
            static const float CriticalImpact = 2.59807621;
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
            float Deflection(float impact)
            {
                if(impact>400)return 2/impact+2.94524311/(impact*impact);
                float coordinate=(log(max(1e-7,impact-CriticalImpact))-_LensShape.z)/_LensShape.w;
                return SAMPLE_TEXTURE2D_LOD(_DeflectionTable,sampler_DeflectionTable,float2(saturate(coordinate),.5),0).r;
            }
            // Whole-sky radiance (equirectangular) for bent rays that leave the screen.
            TEXTURE2D(_GalaxySkyMap); SAMPLER(sampler_GalaxySkyMap);
            float3 SkyMap(float3 direction)
            {
                float2 uv=float2(atan2(direction.z,direction.x)/(2*PI)+.5,asin(clamp(direction.y,-1,1))/PI+.5);
                return SAMPLE_TEXTURE2D_LOD(_GalaxySkyMap,sampler_GalaxySkyMap,uv,0).rgb;
            }
            float3 Sky(float3 direction)
            {
                float3 forward=-UNITY_MATRIX_V[2].xyz;
                float2 uv=ComputeNormalizedDeviceCoordinates(_WorldSpaceCameraPos+direction*1000,UNITY_MATRIX_VP);
                // The screen-resolution radiance is sharper; blend to the sky map near the frame edge.
                float2 inside=min(uv,1-uv);
                float onScreen=saturate(min(inside.x,inside.y)*25)*saturate((dot(direction,forward)-.05)*8);
                float3 screen=SAMPLE_TEXTURE2D_LOD(_GalaxyRadiance,sampler_GalaxyRadiance,saturate(uv),0).rgb;
                float3 galaxy=lerp(SkyMap(direction),screen,onScreen);
                float luminance=dot(galaxy,float3(.2126,.7152,.0722));
                return galaxy*(1-_SkyDaylight)+Starfield(direction,luminance);
            }
            half4 Frag(Varyings input):SV_Target
            {
                float3 nearWorld=ComputeWorldSpacePosition(input.uv,UNITY_NEAR_CLIP_VALUE,UNITY_MATRIX_I_VP);
                float3 ray=normalize(nearWorld-_WorldSpaceCameraPos);
                float distanceToHole=length(_LensCentre.xyz);
                float3 hole=_LensCentre.xyz/max(1e-6,distanceToHole);
                float cosine=dot(ray,hole);
                float3 across=ray-hole*cosine;
                float sine=length(across);
                float rs=_LensShape.x;
                float impact=cosine>0?distanceToHole*sine/rs:1e9;
                if(cosine>0&&impact<CriticalImpact)return half4(0,0,0,1);
                float alpha=Deflection(impact);
                // Skip pixels whose image moves by less than a third of a pixel.
                clip(alpha-_LensShape.y*.33);
                // Rotate the ray toward the hole by alpha within the ray-hole plane.
                float3 toward=sine>1e-6?(hole-ray*cosine)/sine:float3(0,0,0);
                float3 bent=normalize(ray*cos(alpha)+toward*sin(alpha));
                return half4(Sky(bent),1);
            }
            ENDHLSL
        }
    }
}
