Shader "SpaceEngine/GalaxyVolume"
{
    Properties
    {
        _Exposure("Stellar population exposure",Range(0,8))=1
        _Turbulence("Tileable interstellar turbulence (set by GalaxyVolume)",3D)=""{}
        _DustStrength("Dust optical-depth multiplier",Range(0,5))=1.35
        _DustScaleHeightLy("Dust vertical scale height (ly)",Float)=220
        _DustAttenuationLengthLy("Dust attenuation length (ly)",Float)=1200
        _SampleCount("Stellar volume quadrature budget",Int)=128
        _DetailStrength("Physical fine dust and star-formation structure",Range(0,2))=1
        _DetailScaleLy("Fine molecular-cloud cell scale (ly)",Float)=360
        _NearDetailDistanceLy("Near-observer high-quality integration distance (ly)",Float)=8000
        _HaloRadiusLy("Finite stellar halo radius (ly)",Float)=160000
        _PopulationScale("Relative stellar number-density normalization",Float)=1
        _InteriorExposure("Inside-disk display exposure multiplier",Range(.01,2))=.8
        _ExteriorExposure("Exterior display exposure multiplier",Range(.1,4))=1.6
        _Contrast("Night-sky display contrast",Range(1,3))=2
        _YoungArmStartBarFraction("Young blue arms inner fade / bar half-length",Range(.5,1.2))=.75
        _YoungArmFullBarFraction("Young blue arms full light / bar half-length",Range(.8,1.8))=1.1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "StellarVolumeIntegration"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ObserverLy,_ViewRight,_ViewUp,_ViewForward,_ViewShape;
                float4 _DiskScales,_CentralScales,_ArmScales,_SeedOffset;
                float _Exposure,_DustStrength,_DustScaleHeightLy,_DustAttenuationLengthLy,_HaloRadiusLy,_PopulationScale;
                float _InteriorExposure,_ExteriorExposure,_Contrast;
                float _YoungArmStartBarFraction,_YoungArmFullBarFraction;
                float _DetailStrength,_DetailScaleLy,_NearDetailDistanceLy;
                float _Equirect;
                int _SampleCount;
            CBUFFER_END
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output; output.positionCS=TransformObjectToHClip(input.positionOS); output.uv=input.uv; return output;
            }
            float Hash(float3 p)
            {
                p=frac(p*.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z);
            }
            float Noise(float3 p)
            {
                float3 cell=floor(p),f=frac(p); f=f*f*(3-2*f);
                float a=lerp(Hash(cell),Hash(cell+float3(1,0,0)),f.x);
                float b=lerp(Hash(cell+float3(0,1,0)),Hash(cell+float3(1,1,0)),f.x);
                float c=lerp(Hash(cell+float3(0,0,1)),Hash(cell+float3(1,0,1)),f.x);
                float d=lerp(Hash(cell+float3(0,1,1)),Hash(cell+1),f.x);
                return lerp(lerp(a,b,f.y),lerp(c,d,f.y),f.z);
            }
            TEXTURE3D(_Turbulence); SAMPLER(sampler_Turbulence);
            // Interstellar-medium turbulence: domain-warped multi-octave gradient noise from a
            // tileable 3D texture. x drives molecular dust, y the stellar cloud brightness.
            // Clouds in a thin disc are sheets, so the vertical axis is compressed, and octave
            // rotations stay within the plane. Octaves finer than the integration or pixel
            // footprint fade to their mean, so unresolved structure cannot alias.
            float2 Turbulence(float3 p,float footprintLy)
            {
                float3 q=float3(p.x,p.y*1.25,p.z)/2600+_SeedOffset.xyz*.37;
                float2 warp=SAMPLE_TEXTURE3D_LOD(_Turbulence,sampler_Turbulence,q*(.32/8),0).rg-.5;
                q+=float3(warp.x,0,warp.y)*1.7;
                float2 sum=0;float amplitude=.5,norm=0,wavelength=2600;
                [loop] for(int octave=0;octave<9;octave++)
                {
                    float visible=1-smoothstep(.3,.9,footprintLy/wavelength);
                    if(visible<=0)break;
                    float2 n=SAMPLE_TEXTURE3D_LOD(_Turbulence,sampler_Turbulence,q/8,0).rg;
                    sum+=amplitude*lerp(.5,n,visible);norm+=amplitude;
                    q=float3(.8*q.x-.6*q.z,q.y*1.04,.6*q.x+.8*q.z)*2.07+float3(3.1,1.7,5.3);
                    amplitude*=.6;wavelength/=2.07;
                }
                return norm>0?sum/norm:.5;
            }
            float Wrapped(float phase) { return phase-6.28318530718*floor((phase+3.14159265359)/6.28318530718); }

            // The six dimensionless component profiles mirror GalaxyModel.RawDensity.
            // Component colours and luminosity-per-star are display parameters, not a survey.
            void Population(float3 p,float footprintLy,out float3 emission,out float dust)
            {
                float radius=length(p.xz),height=abs(p.y),sphere=length(p);
                emission=0;dust=0;
                if(sphere>=_HaloRadiusLy)return;
                float thin=0,thick=0,arm=0,bulge=0,bar=0,armCore=0;
                if(radius<_DiskScales.x)
                {
                    float taper=1-smoothstep(_DiskScales.x*.88,_DiskScales.x,radius);
                    float radial=exp((_CentralScales.w-radius)/_DiskScales.y)*taper;
                    thin=radial*exp(-height/_DiskScales.z);
                    thick=.1*exp((_CentralScales.w-radius)/(_DiskScales.y*1.2))*exp(-height/_DiskScales.w)*taper;
                    float angle=_ArmScales.w+log(max(1,radius)/10000)/tan(_ArmScales.y);
                    // Coherent displacement and luminosity knots break exact analytic
                    // rings while preserving the configured logarithmic arm skeleton.
                    float warp=Noise(float3(p.x/6500,0,p.z/6500)+_SeedOffset.xyz)-.5;
                    angle+=warp*.42;
                    float delta=Wrapped((atan2(p.z,p.x)-angle)*_ArmScales.x)/_ArmScales.x;
                    float armDistance=radius*delta*sin(_ArmScales.y);
                    armCore=exp(-.5*armDistance*armDistance/(_ArmScales.z*_ArmScales.z));
                    float inner=max(1,_CentralScales.y*.6);
                    arm=.65*radial*armCore*exp(-height/(_DiskScales.z*.5))*smoothstep(inner,inner*1.25,radius);
                    // Molecular dust is a thinner, offset, clumpy spiral medium.
                    float laneDistance=armDistance+_ArmScales.z*.38;
                    float lane=exp(-.5*laneDistance*laneDistance/(.22*_ArmScales.z*_ArmScales.z));
                    float2 medium=Turbulence(p,footprintLy);
                    // Expand the fBm's narrow distribution into cloud and void.
                    float clump=saturate(.5+(medium.x-.5)*3.1);
                    float stars=saturate(.5+(medium.y-.5)*2.3);
                    // Molecular clouds are nearly opaque over a few hundred light years and the
                    // voids between them nearly clear; nearby clouds then break up the whole band.
                    float molecular=.04+16*pow(clump,3.2);
                    dust=exp(-height/max(10,_DustScaleHeightLy))*exp(-radius/22000)*taper*
                        smoothstep(1800,5000,radius)*(.32+lane*1.8)*molecular;
                    float stellarCloud=.32+1.75*pow(stars,1.4);
                    thin*=stellarCloud;
                    arm*=pow(armCore,1.6)*(.08+3.8*smoothstep(.24,.78,clump));
                    // Young luminous tracers begin near the ends of the bar. The
                    // unchanged number-density skeleton may contain inner winding
                    // arms, but old smooth bulge/bar stars dominate their appearance.
                    // This suppresses tightly packed bright concentric-looking loops.
                    float armStart=_CentralScales.y*_YoungArmStartBarFraction;
                    float armFull=_CentralScales.y*max(_YoungArmFullBarFraction,_YoungArmStartBarFraction+.01);
                    arm*=smoothstep(armStart,armFull,radius);
                    // Distinct physical frequency band: thin molecular filaments and
                    // compact star-forming associations. These exist in galactic 3D
                    // coordinates and reveal parallax as the observer approaches.
                    // Pixel and integration footprints continuously remove frequencies
                    // that cannot be resolved; no screen-space sharpening is applied.
                    float detailScale=max(80,_DetailScaleLy);
                    float resolved=saturate((detailScale-footprintLy)/(detailScale*.75))*_DetailStrength;
                    if(resolved>.001)
                    {
                        float3 detailPoint=float3(p.x,p.y*2.8,p.z)/detailScale+_SeedOffset.xyz*2.17;
                        detailPoint+=float3(.71,-.39,.93)*(clump-.5)*2.4;
                        float fine=Noise(detailPoint);
                        float filamentWidth=.025+min(.2,footprintLy/(detailScale*4));
                        float filament=1-smoothstep(filamentWidth,filamentWidth+.075,abs(fine-.5));
                        float association=smoothstep(.43,.78,Noise(detailPoint*.71+23.17));
                        float fineDust=.4+2.1*filament*filament;
                        float compactLight=.24+2.7*association*association;
                        dust*=max(.05,lerp(1,fineDust,resolved));
                        arm*=max(.05,lerp(1,compactLight,resolved));
                    }
                    // Small warm hydrogen regions trace the same seeded three-dimensional field.
                    float nebula=smoothstep(.65,.82,clump)*arm;
                    emission+=float3(1,.25,.36)*nebula*.35;
                }
                float b=length(float3(p.x,p.y/.65,p.z))/_CentralScales.x;
                if(b<1)bulge=24*exp(-3*b)*(1-smoothstep(.8,1,b));
                float c=cos(_CentralScales.z),s=sin(_CentralScales.z);
                float barX=p.x*c+p.z*s,barZ=-p.x*s+p.z*c;
                float barRadius=length(float3(barX,barZ/sqrt(.12),p.y/.3))/_CentralScales.y;
                if(barRadius<1)bar=15*exp(-3*barRadius)*(1-smoothstep(.8,1,barRadius));
                float halo=.002*pow((_CentralScales.w+_CentralScales.x)/(sphere+_CentralScales.x),3)*
                    (1-smoothstep(_HaloRadiusLy*.8,_HaloRadiusLy,sphere));
                emission+=thin*float3(.78,.73,.63)+thick*float3(.8,.53,.32)*.3+
                    arm*float3(.45,.65,1)*3.7+bulge*float3(1,.72,.44)+bar*float3(1,.67,.39)+halo*float3(.65,.72,1);
                emission*=_PopulationScale/15000;
            }
            bool BoxInterval(float3 origin,float3 direction,float3 extent,out float start,out float end)
            {
                float3 safe=direction;
                safe.x=abs(safe.x)<1e-7?(safe.x<0?-1e-7:1e-7):safe.x;
                safe.y=abs(safe.y)<1e-7?(safe.y<0?-1e-7:1e-7):safe.y;
                safe.z=abs(safe.z)<1e-7?(safe.z<0?-1e-7:1e-7):safe.z;
                float3 a=(-extent-origin)/safe,b=(extent-origin)/safe;
                float3 low=min(a,b),high=max(a,b);
                start=max(0,max(low.x,max(low.y,low.z)));end=min(high.x,min(high.y,high.z));
                return end>start;
            }
            // geometric > 0 spaces samples exponentially from the start, so structure close to an
            // embedded observer (which subtends the largest angles) is integrated finely.
            void March(float3 origin,float3 direction,float start,float end,int count,float geometric,inout float3 result,inout float3 transmittance)
            {
                if(end<=start||count<1)return;
                float span=end-start,scale=geometric>0?1/(exp(geometric)-1):0;
                [loop]for(int i=0;i<count;i++)
                {
                    float3 emission;float density;
                    float u0=(float)i/count,u1=(float)(i+1)/count;
                    float t0=geometric>0?(exp(geometric*u0)-1)*scale:u0;
                    float t1=geometric>0?(exp(geometric*u1)-1)*scale:u1;
                    float ds=span*(t1-t0);
                    float distanceLy=start+span*(t0+t1)*.5;
                    float pixelFootprint=distanceLy*(_ViewShape.x*2/max(1,_ViewShape.w));
                    float footprint=max(pixelFootprint,ds*.65);
                    Population(origin+direction*distanceLy,footprint,emission,density);
                    // Stars within a few thousand light years are drawn individually by the
                    // starfield and catalogue; their diffuse glow would double-count them and
                    // grey the off-plane sky. Their dust still absorbs.
                    emission*=smoothstep(350,3000,distanceLy);
                    float3 opticalDepth=float3(.7,.93,1.3)*density*_DustStrength*ds/max(1,_DustAttenuationLengthLy);
                    float3 segment=exp(-opticalDepth);
                    // Integrate constant emission/absorption analytically within each segment.
                    // Stable transparent limit prevents a zero-dust division discontinuity.
                    float3 weight=(1-segment)/max(opticalDepth,1e-5);
                    weight=lerp(weight,1,step(opticalDepth,1e-5));
                    result+=transmittance*emission*ds*weight;
                    transmittance*=segment;
                }
            }
            void MarchRange(float3 origin,float3 direction,float start,float end,int count,inout float3 result,inout float3 transmittance)
            {
                // Nearby dust dominates foreground extinction for an embedded
                // observer. Resolve that finite physical region instead of wasting
                // uniform samples over the entire far side of a 100 kly disk.
                // Sample counts and spacing must vary continuously with the ray direction: any
                // branch that switches allocation at some elevation changes the step size, hence the
                // footprint filter, and prints a straight seam along the band. A segment that ends
                // before the split simply leaves its far samples on an empty interval.
                float split=clamp(_NearDetailDistanceLy,start,end);
                if(count>=24&&start<_NearDetailDistanceLy)
                {
                    int nearCount=max(16,(int)(count*.65));
                    nearCount=min(count-8,nearCount);
                    March(origin,direction,start,split,nearCount,start<1?4.5:0,result,transmittance);
                    March(origin,direction,split,end,count-nearCount,0,result,transmittance);
                }
                else March(origin,direction,start,end,count,start<1?4.5:0,result,transmittance);
            }
            float HaloDensity(float3 p)
            {
                float sphere=length(p);
                return .002*pow((_CentralScales.w+_CentralScales.x)/(sphere+_CentralScales.x),3)*
                    (1-smoothstep(_HaloRadiusLy*.8,_HaloRadiusLy,sphere));
            }
            // Halo emission over [a, b], spaced geometrically from a (the density falls as r^-3).
            float HaloSegment(float3 origin,float3 direction,float a,float b,int count)
            {
                if(b<=a)return 0;
                float span=b-a,sum=0,scale=1/(exp(3.0)-1);
                [loop]for(int j=0;j<count;j++)
                {
                    float t0=(exp(3.0*j/count)-1)*scale,t1=(exp(3.0*(j+1)/count)-1)*scale;
                    sum+=HaloDensity(origin+direction*(a+span*(t0+t1)*.5))*span*(t1-t0);
                }
                return sum;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float3 direction;
                if(_Equirect>.5)
                {
                    float longitude=(input.uv.x*2-1)*PI,latitude=(input.uv.y-.5)*PI;
                    direction=float3(cos(latitude)*cos(longitude),sin(latitude),cos(latitude)*sin(longitude));
                }
                else
                {
                    float2 plane=(input.uv*2-1)*float2(_ViewShape.x*_ViewShape.y,_ViewShape.x);
                    direction=normalize(_ViewForward.xyz+plane.x*_ViewRight.xyz+plane.y*_ViewUp.xyz);
                }
                float3 origin=_ObserverLy.xyz;
                float3 result=0,transmittance=1;
                float start,end;
                float height=max(_DiskScales.w*5,max(_CentralScales.x*.65,_CentralScales.y*.3));
                float3 extent=float3(_DiskScales.x,height,_DiskScales.x);
                bool inBox=BoxInterval(origin,direction,extent,start,end);
                if(inBox)
                {
                    float splitStart=start,splitEnd=end;
                    // Explicitly allocate most samples to the thin dust slab. Uniform steps
                    // through a 100 kly exterior ray otherwise skip it and lose the dust lane.
                    if(abs(direction.y)>1e-7)
                    {
                        float a=(-_DustScaleHeightLy*5-origin.y)/direction.y;
                        float b=(_DustScaleHeightLy*5-origin.y)/direction.y;
                        splitStart=clamp(min(a,b),start,end);splitEnd=clamp(max(a,b),start,end);
                    }
                    int outer=max(8,_SampleCount/5),inner=max(16,_SampleCount-outer*2);
                    if(abs(origin.y)<_DustScaleHeightLy*5)
                    {
                        // Embedded observer: every ray starts inside the slab. Fixed budgets keep the
                        // allocation continuous in direction; switching to a single march for rays that
                        // never leave the slab changed the step size at one elevation and printed a
                        // straight seam along the band.
                        MarchRange(origin,direction,start,splitEnd,inner+outer,result,transmittance);
                        // The tail starts where the ray leaves the slab, which varies with elevation;
                        // MarchRange would switch its near/far split as that start crosses the near
                        // distance, so the tail is marched uniformly.
                        March(origin,direction,splitEnd,end,outer,0,result,transmittance);
                    }
                    else if(splitEnd-splitStart<.001||(splitStart==start&&splitEnd==end))MarchRange(origin,direction,start,end,_SampleCount,result,transmittance);
                    else
                    {
                        MarchRange(origin,direction,start,splitStart,outer,result,transmittance);
                        MarchRange(origin,direction,splitStart,splitEnd,inner,result,transmittance);
                        MarchRange(origin,direction,splitEnd,end,outer,result,transmittance);
                    }
                }
                // The very faint, optically thin outer stellar halo has a separate 16-step
                // integral, avoiding 300 kly empty spans in the high-quality thin-disk budget.
                float projection=dot(origin,direction),disc=projection*projection-dot(origin,origin)+_HaloRadiusLy*_HaloRadiusLy;
                // Only the intervals outside the box are integrated here (inside it the halo is part of
                // Population). Testing each fixed sample against the box instead made whole samples
                // switch on at particular elevations: straight seams along the band.
                if(disc>0)
                {
                    float root=sqrt(disc),nearHalo=max(0,-projection-root),farHalo=-projection+root;
                    float halo=inBox?HaloSegment(origin,direction,nearHalo,min(start,farHalo),4)+
                        HaloSegment(origin,direction,max(end,nearHalo),farHalo,12):
                        HaloSegment(origin,direction,nearHalo,farHalo,16);
                    result+=halo*float3(.65,.72,1)*_PopulationScale/15000;
                }
                // An observer embedded in the stellar disk has a much brighter local
                // integrated foreground than an exterior view. A continuous display
                // adaptation and filmic toe retain a black night sky above the band;
                // they do not remove a physical density component or paint a sky mask.
                float inside=(1-smoothstep(_DiskScales.x*.95,_DiskScales.x*1.1,length(origin.xz)))*
                    (1-smoothstep(_DiskScales.w*2,_DiskScales.w*5,abs(origin.y)));
                float displayExposure=_Exposure*lerp(_ExteriorExposure,_InteriorExposure,inside);
                // Compress intensity rather than each colour independently. Per-
                // channel saturation bleached the high-column warm bulge to white.
                // Peak normalization keeps the finite display gamut and RGB ratios.
                float3 radiance=max(0,result);
                float intensity=dot(radiance,float3(.2126,.7152,.0722));
                float brightness=pow(1-exp(-intensity*displayExposure),max(1,_Contrast));
                float3 mapped=radiance/max(1e-6,intensity)*brightness;
                mapped/=max(1,max(mapped.r,max(mapped.g,mapped.b)));
                return half4(mapped,1);
            }
            ENDHLSL
        }
    }
}
