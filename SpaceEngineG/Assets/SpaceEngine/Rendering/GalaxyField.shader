Shader "SpaceEngine/GalaxyField"
{
    Properties
    {
        _Exposure("Galaxy radiance exposure",Range(0,8))=1
        _KernelScale("Spatial kernel size",Range(.2,5))=1
        _MaximumAngularRadius("Maximum stellar accent radius (rad)",Range(.0001,.002))=.00075
        _MaximumFootprintCompensation("Maximum capped-kernel radiance compensation",Range(1,8))=2
        _DustStrength("Galactic dust attenuation",Range(0,5))=1
        _DustScaleHeightLy("Dust vertical scale height (ly)",Float)=220
        _DustAttenuationLengthLy("Dust attenuation length (ly)",Float)=1200
        _LocalFadeLy("Aggregate to resolved star transition (ly)",Float)=100
        _DensityCompensation("Representative count compensation",Float)=1
        _GalaxyRadiusLy("Disk radius (ly)",Float)=52000
        _ArmPitch("Log spiral pitch (rad)",Float)=.22
        _ArmCount("Spiral arms",Float)=4
        _ArmPhase("Arm angle at 10000 ly",Float)=0
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10"}
        Pass
        {
            Tags{"LightMode"="SRPDefaultUnlit"}
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
                float4 _ObserverLy;
                float _Exposure,_KernelScale,_MaximumAngularRadius,_MaximumFootprintCompensation,_DustStrength,_LocalFadeLy,_DensityCompensation;
                float _DustScaleHeightLy,_DustAttenuationLengthLy;
                float _GalaxyRadiusLy,_ArmPitch,_ArmCount,_ArmPhase;
            CBUFFER_END
            struct Attributes{float3 positionOS:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float4 data:TEXCOORD1;};
            struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            float DustDensity(float3 p)
            {
                float r=length(p.xz);
                float armAngle=_ArmPhase+log(max(1,r)/10000)/tan(_ArmPitch);
                float phase=(atan2(p.z,p.x)-armAngle)*_ArmCount;
                float lane=pow(saturate(.5+.5*cos(phase+.25)),10);
                return exp(-abs(p.y)/max(1,_DustScaleHeightLy))*exp(-r/22000)*smoothstep(1800,5000,r)*(1-smoothstep(_GalaxyRadiusLy*.8,_GalaxyRadiusLy,r))*(.2+lane);
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 delta=input.positionOS-_ObserverLy.xyz;
                float distanceLy=max(.001,length(delta));float3 direction=delta/distanceLy;
                float referenceSize=clamp(input.data.x*_KernelScale/distanceLy,.00009,.035);
                float angularSize=min(referenceSize,clamp(_MaximumAngularRadius,.00009,.035));
                // Concentrate nearby aggregate footprints with bounded display compensation.
                // Full area compensation created bright clipped points when a nearby kernel shrank.
                float footprintCompensation=(referenceSize/angularSize)*(referenceSize/angularSize);
                footprintCompensation=min(footprintCompensation,max(1,_MaximumFootprintCompensation));
                float shell=14500000;
                float3 right=UNITY_MATRIX_I_V._m00_m10_m20;
                float3 up=UNITY_MATRIX_I_V._m01_m11_m21;
                float3 position=direction*shell+(right*input.uv.x+up*input.uv.y)*angularSize*shell;
                output.positionCS=TransformWorldToHClip(position);output.uv=input.uv;
                float localFade=smoothstep(_LocalFadeLy,_LocalFadeLy*2,distanceLy);
                // Kernels represent an unresolved stellar population. Integrated surface brightness
                // is normalized by count; this display exposure is not absolute photometry.
                float brightness=input.data.y*_Exposure*_DensityCompensation*.018*footprintCompensation;
                // Restrict quadrature to the thin dust layer. Uniform samples along a
                // 100,000 ly overview ray miss a thin dust disc completely.
                // Five scale heights contain >99% of the vertical exponential column.
                float dustHalfThickness=5*max(1,_DustScaleHeightLy);
                float tStart=0,tEnd=1;
                if(abs(delta.y)>.00001)
                {
                    float atLower=(-dustHalfThickness-_ObserverLy.y)/delta.y;
                    float atUpper=(dustHalfThickness-_ObserverLy.y)/delta.y;
                    tStart=max(0,min(atLower,atUpper));
                    tEnd=min(1,max(atLower,atUpper));
                }
                else if(abs(_ObserverLy.y)>dustHalfThickness)tEnd=0;
                float span=max(0,tEnd-tStart);
                float dust=0;
                [unroll]for(int j=0;j<10;j++)dust+=DustDensity(_ObserverLy.xyz+delta*(tStart+span*((j+.5)/10.0)));
                float column=dust*min(distanceLy*span,80000)/10/max(1,_DustAttenuationLengthLy)*_DustStrength;
                float3 transmittance=exp(-float3(.55,.85,1.4)*column);
                output.color=float4(input.color.rgb*brightness*localFade*transmittance,1);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float r2=dot(input.uv,input.uv);clip(1-r2);
                float kernel=exp(-r2*5)*(1-smoothstep(.8,1,r2));
                return half4(input.color.rgb*kernel,0);
            }
            ENDHLSL
        }
    }
}
