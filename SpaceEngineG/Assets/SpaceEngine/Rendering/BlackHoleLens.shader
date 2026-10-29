Shader "SpaceEngine/BlackHoleLens"
{
    // Schwarzschild lensing of the sky behind a black hole. Rays with impact parameter
    // below 3*sqrt(3)/2 Rs are captured (the shadow); others are bent toward the hole by
    // the exact null-geodesic deflection angle from a precomputed table, and the
    // procedural starfield and integrated galactic radiance are re-evaluated along the
    // bent direction. Resolved catalogue stars and opaque bodies are not re-traced.
    // With an accretion disk, rays that pass near it are integrated as null geodesics in
    // their orbital plane, so the far side of the disk is imaged above and below the shadow.
    Properties
    {
        _LensCentre ("Camera-relative black hole position (render units)", Vector) = (0,0,10,0)
        _LensShape ("Schwarzschild radius, pixel angle, table min log, table span", Vector) = (1,.001,-9.21,14.5)
        _DeflectionTable ("Deflection angle by log(b - b_crit)", 2D) = "black" {}
        _DiskNormal ("Disk normal (prograde rotation), enabled", Vector) = (0,1,0,0)
        _DiskShape ("Disk inner/outer radius (Rs), peak radiance, time (Rs/c)", Vector) = (3,14,4,0)
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
                float4 _LensCentre, _LensShape, _DiskNormal, _DiskShape;
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

            float DiskHash(float3 p)
            {
                p=frac(p*.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z);
            }
            float DiskNoise(float3 p)
            {
                float3 i=floor(p),f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(DiskHash(i),DiskHash(i+float3(1,0,0)),f.x),lerp(DiskHash(i+float3(0,1,0)),DiskHash(i+float3(1,1,0)),f.x),f.y),
                            lerp(lerp(DiskHash(i+float3(0,0,1)),DiskHash(i+float3(1,0,1)),f.x),lerp(DiskHash(i+float3(0,1,1)),DiskHash(i+1),f.x),f.y),f.z);
            }
            float3 DiskTint(float kelvin)
            {
                float t=clamp(kelvin,1000,40000)/100;
                float3 c;
                c.r=t<=66?1:saturate(1.29293618*pow(t-60,-.1332047592));
                c.g=t<=66?saturate(.39008157*log(t)-.63184144):saturate(1.12989086*pow(t-60,-.0755148492));
                c.b=t>=66?1:(t<=19?0:saturate(.54320679*log(t-10)-1.19625409));
                return c;
            }
            // Emission (rgb, premultiplied by coverage) and coverage of the thin disk at hole-relative
            // point p (Rs units). k: photon direction toward the observer at that point.
            float4 DiskSample(float3 p,float3 k,float observerRadius)
            {
                float r=length(p),inner=_DiskShape.x,outer=_DiskShape.y;
                float3 n=_DiskNormal.xyz;
                // Thin disk with zero torque at the inner edge: F ~ r^-3 (1 - sqrt(r_in/r)), peak 1.
                float x=inner/r;
                float flux=x*x*x*(1-sqrt(saturate(x)))*17.6;
                // Keplerian circular orbit (Schwarzschild, Rs = 1): local speed sqrt(M/(r-2M)).
                float beta=sqrt(.5/max(1.06,r-1));
                float3 velocity=normalize(cross(n,p));
                float doppler=sqrt(1-beta*beta)/(1-beta*dot(velocity,k));
                float gravity=sqrt(saturate(1-1/r)/saturate(1-1/max(1.01,observerRadius)));
                float g=doppler*gravity;
                // Turbulent streaks on circles, sheared by differential rotation over time.
                float3 t1=normalize(cross(n,abs(n.y)<.9?float3(0,1,0):float3(1,0,0))),t2=cross(n,t1);
                float phase=atan2(dot(p,t2),dot(p,t1))-sqrt(.5/(r*r*r))*_DiskShape.w;
                float3 q=float3(cos(phase)*2.2,sin(phase)*2.2,log(r)*26);
                float streaks=DiskNoise(q)*.5+DiskNoise(q*2.03+7.1)*.3+DiskNoise(q*4.1+3.3)*.2;
                float lanes=DiskNoise(float3(cos(phase)*.9,sin(phase)*.9,log(r)*7));
                float edge=smoothstep(inner*.97,inner*1.06,r)*(1-smoothstep(outer*.55,outer,r));
                float coverage=saturate((.35+streaks*.9-lanes*.25)*edge*1.5);
                // Specific intensity scales as g^3; colour follows the shifted temperature. The
                // display temperature scale is chosen for a legible white-orange-red ramp.
                float kelvin=4600*pow(max(flux,1e-4),.25)*g;
                float radiance=flux*g*g*g*_DiskShape.z*(.55+.9*streaks);
                float display=radiance/(1+.3*radiance);
                float3 tint=pow(DiskTint(kelvin),1.7);
                return float4(tint*display*coverage,coverage);
            }
            // Integrate the photon orbit u'' + u = 3/2 u^2 (Rs = 1) backward from the observer,
            // compositing disk crossings front to back. Returns emitted radiance.
            float3 TraceDisk(float3 ray,float3 holeRender,float rs,out float transmittance,out float3 exitDirection,out bool captured)
            {
                float3 observer=-holeRender/rs;
                float r0=length(observer);
                float3 e1=observer/r0;
                float radial=dot(ray,e1);
                float3 tangentialPart=ray-e1*radial;
                float tangential=length(tangentialPart);
                float3 e2=tangential>1e-6?tangentialPart/tangential:normalize(cross(e1,abs(e1.y)<.9?float3(0,1,0):float3(1,0,0)));
                tangential=max(tangential,1e-6);
                float u=1/r0,w=-u*radial/tangential,phi=0;
                float a=dot(e1,_DiskNormal.xyz),b=dot(e2,_DiskNormal.xyz);
                float side=a;
                // Beyond 60 Rs the rest of an outgoing path is weak-field; finish it analytically.
                const float tail=1/60.0;
                float escape=min(u*.98,tail);
                float3 color=0;transmittance=1;captured=false;
                [loop] for(int i=0;i<420;i++)
                {
                    // Limit each step to a 25% change in u: geometric steps far away, fine ones near the hole.
                    float h=min(.022+.055*saturate(1-u*3),.25*u/max(abs(w),1e-6));
                    float k1u=w,k1w=-u+1.5*u*u;
                    float u2=u+.5*h*k1u,w2=w+.5*h*k1w;
                    float k2u=w2,k2w=-u2+1.5*u2*u2;
                    float u3=u+.5*h*k2u,w3=w+.5*h*k2w;
                    float k3u=w3,k3w=-u3+1.5*u3*u3;
                    float u4=u+h*k3u,w4=w+h*k3w;
                    float k4u=w4,k4w=-u4+1.5*u4*u4;
                    float nextU=u+h/6*(k1u+2*k2u+2*k3u+k4u);
                    float nextW=w+h/6*(k1w+2*k2w+2*k3w+k4w);
                    float nextPhi=phi+h;
                    float nextSide=a*cos(nextPhi)+b*sin(nextPhi);
                    if(side*nextSide<0)
                    {
                        float f=side/(side-nextSide);
                        float crossingU=max(1e-6,lerp(u,nextU,f)),crossingW=lerp(w,nextW,f),crossingPhi=phi+h*f;
                        float radius=1/crossingU;
                        if(radius>_DiskShape.x*.95&&radius<_DiskShape.y)
                        {
                            float3 radialDirection=cos(crossingPhi)*e1+sin(crossingPhi)*e2;
                            float3 along=-sin(crossingPhi)*e1+cos(crossingPhi)*e2;
                            float3 travel=normalize(-crossingW/(crossingU*crossingU)*radialDirection+radius*along);
                            float4 emission=DiskSample(radius*radialDirection,-travel,r0);
                            color+=transmittance*emission.rgb;
                            transmittance*=1-emission.a;
                        }
                    }
                    side=nextSide;u=nextU;w=nextW;phi=nextPhi;
                    if(transmittance<.004)break;
                    if(u>1){captured=true;break;}
                    if(w<0&&u<escape)break;
                }
                float3 outward=cos(phi)*e1+sin(phi)*e2,turn=-sin(phi)*e1+cos(phi)*e2;
                float3 heading=normalize(-w/max(u*u,1e-12)*outward+(1/max(u,1e-6))*turn);
                // Remaining weak-field bending toward the hole from radius 1/u to infinity:
                // (1/b)(1 - sqrt(1 - b^2 u^2)), with b from the first integral u'^2 + u^2 - u^3 = 1/b^2.
                float inverseImpact=sqrt(max(1e-12,w*w+u*u-u*u*u));
                float bu=saturate(u/inverseImpact);
                float remaining=inverseImpact*(1-sqrt(1-bu*bu));
                float3 towardHole=normalize(-outward-heading*dot(-outward,heading)+1e-7);
                exitDirection=normalize(heading*cos(remaining)+towardHole*sin(remaining));
                return color;
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
                float observerRadius=distanceToHole/rs;
                if(_DiskNormal.w>.5&&((cosine>0&&impact<_DiskShape.y*1.5+4)||observerRadius<_DiskShape.y*1.3))
                {
                    float transmittance;float3 exitDirection;bool captured;
                    float3 disk=TraceDisk(ray,_LensCentre.xyz,rs,transmittance,exitDirection,captured);
                    return half4(disk+(captured?0:transmittance*Sky(exitDirection)),1);
                }
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
