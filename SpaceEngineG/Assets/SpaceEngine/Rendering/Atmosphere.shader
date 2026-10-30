Shader "SpaceEngine/Atmosphere"
{
    Properties
    {
        _PlanetCenter ("Planet center (render units)", Vector) = (0,0,0,0)
        _PlanetRadius ("Planet radius (render units)", Float) = 1
        _AtmosphereRadius ("Outer radius (render units)", Float) = 1.0157
        _SunDirection ("Direction toward sun", Vector) = (1,0,0,0)
        _Rayleigh ("Rayleigh coefficients per planet radius", Vector) = (37,86,211,0)
        _Mie ("Mie coefficients per planet radius", Vector) = (25.5,25.5,25.5,0)
        _MieG ("Mie forward anisotropy", Range(0,0.95)) = 0.76
        _DensityFalloff ("Rayleigh density falloff across shell", Float) = 12.5
        _MieDensityFalloff ("Mie density falloff across shell", Float) = 80
        _SunIntensity ("Relative solar irradiance", Float) = 2
        _Exposure ("Scattering radiance multiplier", Float) = 1
        _SurfaceFloor ("Lowest visible surface / planet radius (0 = trust depth only)", Float) = 0
        _ViewSamples ("View integration samples", Range(4,48)) = 16
        _LightSamples ("Light integration samples", Range(2,16)) = 6
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+101" "RenderType"="Transparent" }
        Pass
        {
            Name "PhysicalAtmosphere"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ColorMask RGB
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            // Both variants are selected on runtime-created materials.
            #pragma multi_compile_local _ _ATMOSPHERE_EXTINCTION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PlanetCenter;
                float4 _SunDirection;
                float4 _Rayleigh;
                float4 _Mie;
                float _PlanetRadius, _AtmosphereRadius, _MieG, _DensityFalloff;
                float _MieDensityFalloff, _SunIntensity, _Exposure, _SurfaceFloor;
                int _ViewSamples, _LightSamples;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.positionOS.xy, 0.0, 1.0);
                output.uv = input.positionOS.xy * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    output.uv.y = 1.0 - output.uv.y;
                #endif
                return output;
            }

            // Origin is planet-relative; all distances are divided by planet radius.
            float2 SphereIntersection(float3 origin, float3 direction, float radius)
            {
                float b = dot(origin, direction);
                float c = dot(origin, origin) - radius * radius;
                float d = b * b - c;
                if (d < 0.0) return float2(1.0, -1.0);
                float root = sqrt(max(d, 0.0));
                return float2(-b - root, -b + root);
            }

            float2 Density(float3 samplePosition, float shellHeight)
            {
                float altitude = max(0.0, length(samplePosition) - 1.0) / shellHeight;
                // Smoothly truncate the otherwise infinite exponential at the outer
                // sphere to keep the boundary continuous while flying through it.
                float cutoff = saturate((1.0 - altitude) * 10.0);
                return exp(-altitude * float2(_DensityFalloff, _MieDensityFalloff)) * cutoff;
            }

            float2 LightOpticalDepth(float3 samplePosition, float3 lightDirection, float outer, float shellHeight)
            {
                float2 ground = SphereIntersection(samplePosition, lightDirection, 1.0);
                if (ground.x > 0.000001 && ground.y > 0.0) return float2(1e6, 1e6);
                float distanceToEdge = max(0.0, SphereIntersection(samplePosition, lightDirection, outer).y);
                float stepLength = distanceToEdge / max(2, _LightSamples);
                float2 opticalDepth = 0.0;
                [loop] for (int j = 0; j < _LightSamples; j++)
                    opticalDepth += Density(samplePosition + lightDirection * ((j + 0.5) * stepLength), shellHeight) * stepLength;
                return opticalDepth;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float radius = max(_PlanetRadius, 0.000001);
                float outer = max(_AtmosphereRadius / radius, 1.0001);
                float shellHeight = outer - 1.0;
                float3 origin = (_WorldSpaceCameraPos - _PlanetCenter.xyz) / radius;
                float3 nearWorld = ComputeWorldSpacePosition(input.uv, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 direction = normalize(nearWorld - _WorldSpaceCameraPos);
                float2 shellHit = SphereIntersection(origin, direction, outer);
                float start = max(0.0, shellHit.x);
                float end = shellHit.y;

                float rawDepth = SampleSceneDepth(input.uv);
                #if UNITY_REVERSED_Z
                    bool hasDepth = rawDepth > 0.0;
                #else
                    bool hasDepth = rawDepth < 1.0;
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                if (hasDepth)
                {
                    float3 sceneWorld = ComputeWorldSpacePosition(input.uv, rawDepth, UNITY_MATRIX_I_VP);
                    end = min(end, distance(sceneWorld, _WorldSpaceCameraPos) / radius);
                }
                // Actual terrain depth is authoritative: procedural relief may sit
                // above OR below the reference density/sea-level sphere. Use the
                // analytic sphere only when no opaque surface was rasterized.
                float2 groundHit = SphereIntersection(origin, direction, 1.0);
                if (!hasDepth && groundHit.x > 0.0) end = min(end, groundHit.x);
                // Coarse terrain facets sag far below the true surface while patches are still
                // streaming in (a 7.5 degree chord dips ~0.2% of the radius); marching to their depth
                // added haze at facet centres and printed the patch grid. No visible surface lies
                // below the sea (ocean worlds) or the deepest terrain, so the ray stops there.
                if (_SurfaceFloor > 0.0)
                {
                    float2 floorHit = SphereIntersection(origin, direction, _SurfaceFloor);
                    if (floorHit.x > 0.0) end = min(end, floorHit.x);
                }

                #if defined(_ATMOSPHERE_EXTINCTION)
                    if (end <= start) return 1.0;
                #else
                    if (end <= start) return 0.0;
                #endif

                float stepLength = (end - start) / max(4, _ViewSamples);
                float2 viewDepth = 0.0;
                float3 rayleighSum = 0.0;
                float3 mieSum = 0.0;
                float3 lightDirection = normalize(_SunDirection.xyz);
                [loop] for (int i = 0; i < _ViewSamples; i++)
                {
                    float3 samplePosition = origin + direction * (start + (i + 0.5) * stepLength);
                    float2 localDensity = Density(samplePosition, shellHeight);
                    #if !defined(_ATMOSPHERE_EXTINCTION)
                        float2 lightDepth = LightOpticalDepth(samplePosition, lightDirection, outer, shellHeight);
                        float2 combinedDepth = viewDepth + localDensity * stepLength * 0.5 + lightDepth;
                        float3 transmission = exp(-(_Rayleigh.rgb * combinedDepth.x + _Mie.rgb * combinedDepth.y));
                        rayleighSum += localDensity.x * transmission * stepLength;
                        mieSum += localDensity.y * transmission * stepLength;
                    #endif
                    viewDepth += localDensity * stepLength;
                }
                #if defined(_ATMOSPHERE_EXTINCTION)
                    return float4(exp(-(_Rayleigh.rgb * viewDepth.x + _Mie.rgb * viewDepth.y)), 1.0);
                #else
                    float mu = dot(direction, lightDirection);
                    const float pi = 3.14159265359;
                    float rayleighPhase = 3.0 * (1.0 + mu * mu) / (16.0 * pi);
                    float g = _MieG;
                    float miePhase = (1.0 - g * g) / (4.0 * pi * pow(max(0.001, 1.0 + g * g - 2.0 * g * mu), 1.5));
                    float3 radiance = _SunIntensity * _Exposure *
                        (_Rayleigh.rgb * rayleighSum * rayleighPhase + _Mie.rgb * mieSum * miePhase);
                    return float4(max(0.0, radiance), 1.0);
                #endif
            }
            ENDHLSL
        }
    }
}
