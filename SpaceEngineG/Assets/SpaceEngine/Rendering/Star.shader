Shader "SpaceEngine/Star"
{
    Properties
    {
        [HDR] _BaseColor ("Stellar photosphere color", Color) = (1,0.64,0.2,1)
        _Intensity ("Photosphere radiance", Float) = 4
        _NoiseScale ("Granulation frequency", Float) = 35
        _GranulationStrength ("Granulation contrast", Range(0,1)) = 0.35
        _LimbDarkening ("Limb darkening", Range(0,1)) = 0.5
        _SpotCoverage ("Starspot coverage", Range(0,1)) = 0.35
        _Seed ("Deterministic pattern seed", Float) = 1
        _StarTime ("Simulation time for plasma motion", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Intensity, _NoiseScale, _GranulationStrength, _LimbDarkening, _SpotCoverage, _Seed, _StarTime;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.directionOS = normalize(input.positionOS.xyz);
                return output;
            }
            float3 Hash3(float3 p)
            {
                p = frac(p * float3(.1031, .1030, .0973) + frac(_Seed * .012345));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }
            float Hash(float3 p) { return Hash3(p).x; }
            float Noise(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash(cell), Hash(cell + float3(1,0,0)), f.x);
                float b = lerp(Hash(cell + float3(0,1,0)), Hash(cell + float3(1,1,0)), f.x);
                float c = lerp(Hash(cell + float3(0,0,1)), Hash(cell + float3(1,0,1)), f.x);
                float d = lerp(Hash(cell + float3(0,1,1)), Hash(cell + float3(1,1,1)), f.x);
                return lerp(lerp(a,b,f.y), lerp(c,d,f.y), f.z);
            }
            // Convection cells: bright upwelling centres separated by dark intergranular lanes.
            float Granules(float3 p, float time)
            {
                float3 cell = floor(p), f = frac(p);
                float first = 8, second = 8;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 offset = float3(x, y, z);
                    float3 h = Hash3(cell + offset);
                    float3 centre = offset + .5 + .38 * sin(time + 6.2831 * h);
                    float distanceSquared = dot(centre - f, centre - f);
                    if (distanceSquared < first) { second = first; first = distanceSquared; }
                    else if (distanceSquared < second) second = distanceSquared;
                }
                return saturate((sqrt(second) - sqrt(first)) * 1.6);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionOS);
                float3 normal = normalize(input.normalWS);
                float mu = saturate(dot(normal, GetWorldSpaceNormalizeViewDir(input.positionWS)));
                float pixelFootprint = max(length(ddx(direction)), length(ddy(direction)));
                float time = _StarTime * .002;

                // Granulation fades to its mean when cells shrink below a few pixels.
                float frequency = _NoiseScale * 4;
                float granuleVisibility = 1 - smoothstep(.12, .45, pixelFootprint * frequency);
                float granules = lerp(.55, Granules(direction * frequency, time), granuleVisibility);
                float super = Noise(direction * _NoiseScale * .45 + time * .3);
                float granulation = (granules - .55) * .55 + (super - .5) * .35;
                float surface = 1 + granulation * _GranulationStrength * 1.6;

                // Cool, convective photospheres carry spots in activity belts.
                float latitude = abs(direction.y);
                float belt = smoothstep(.06, .2, latitude) * (1 - smoothstep(.42, .62, latitude));
                float spotField = Noise(direction * 7 + _Seed * .37) * .65 + Noise(direction * 19 + 11.3) * .35;
                float threshold = 1 - .32 * _SpotCoverage * belt;
                float penumbra = smoothstep(threshold - .05, threshold, spotField);
                float umbra = smoothstep(threshold + .015, threshold + .045, spotField);
                surface *= 1 - .45 * penumbra - .4 * umbra;
                // Faculae: bright magnetic network that shows best near the limb.
                float faculae = smoothstep(.62, .82, Noise(direction * _NoiseScale * .9 + 5.7)) * pow(1 - mu, 1.5);
                surface += faculae * .35 * _GranulationStrength;

                // Quadratic limb-darkening law with a redder, cooler limb.
                float edge = 1 - mu;
                float a = .47 * _LimbDarkening * 2, b = .23 * _LimbDarkening * 2;
                float limb = max(.28, 1 - a * edge - b * edge * edge);
                float3 limbTint = lerp(float3(1, .78, .6), 1, sqrt(mu));
                // Soft highlight roll-off: the disk keeps limb colour and granulation while
                // its core still exceeds display white and blooms. Chromaticity is preserved.
                float radiance = _Intensity * surface * limb;
                float display = radiance / (1 + .42 * radiance);
                float3 color = _BaseColor.rgb * display * limbTint;
                return half4(max(0, color), 1.0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            ZWrite On
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Intensity, _NoiseScale, _GranulationStrength, _LimbDarkening, _SpotCoverage, _Seed, _StarTime;
            CBUFFER_END
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
