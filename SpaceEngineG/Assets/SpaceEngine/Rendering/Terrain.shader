Shader "SpaceEngine/Terrain"
{
    Properties
    {
        _PlanetTint ("Biome tint", Color) = (1,1,1,1)
        _SunDirection ("Direction toward sun", Vector) = (1,0,0,0)
        _SunColor ("Stellar light color", Color) = (1,0.96,0.88,1)
        _SunIntensity ("Stellar irradiance multiplier", Range(0,8)) = 1.5
        _AmbientFloor ("Ambient illumination", Range(0,1)) = 0.035
        _Roughness ("Surface roughness", Range(0.04,1)) = 0.8
        _SpecularStrength ("Dielectric reflection", Range(0,1)) = 0.12
        _EmissionStrength ("Lava fissure emission", Range(0,8)) = 1.5
        _LavaFrequency ("Lava plate frequency", Range(4,80)) = 24
        _LavaFissureWidth ("Lava fissure width", Range(0.005,0.15)) = 0.035
        [HideInInspector] _IsLava ("Lava body flag", Float) = 0
        [HideInInspector] _LavaSeed ("Lava seed low/high words", Vector) = (0,0,0,0)
        [HideInInspector] _PatchAnchor ("Patch anchor / radius, inverse radius", Vector) = (0,0,1,1)
        _PlanetCenter ("Planet center (render units)", Vector) = (0,0,0,0)
        _FogStrength ("Optional artistic aerial fog (off with physical atmosphere)", Range(0,1)) = 0
        _FogColor ("Optional aerial fog color", Color) = (0.2,0.4,0.7,1)
        _FogDensity ("Optional aerial fog density per render unit", Float) = 0.0001
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "TerrainLighting"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PlanetTint, _SunDirection, _SunColor, _PlanetCenter, _FogColor, _LavaSeed, _PatchAnchor;
                float _SunIntensity, _AmbientFloor, _Roughness, _SpecularStrength, _EmissionStrength;
                float _FogStrength, _FogDensity, _LavaFrequency, _LavaFissureWidth, _IsLava;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 planetPosition : TEXCOORD2;
                float4 color : COLOR;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                // Bounded planet-relative coordinates are reconstructed from the double CPU anchor.
                // Never subtract two large world-space floats to obtain a surface direction.
                output.planetPosition = _PatchAnchor.xyz + input.positionOS.xyz * _PatchAnchor.w;
                output.color = input.color;
                return output;
            }

            float LavaHash(int3 cell, uint seed)
            {
                uint3 c = asuint(cell);
                uint h = c.x * 0x8da6b343u ^ c.y * 0xd8163841u ^ c.z * 0xcb1ab31fu ^ seed;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (h >> 8) * (1.0 / 16777216.0);
            }

            float LavaNoise(float3 p, uint seed)
            {
                int3 cell = (int3)floor(p);
                float3 f = frac(p);
                float3 w = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float x00 = lerp(LavaHash(cell, seed), LavaHash(cell + int3(1,0,0), seed), w.x);
                float x10 = lerp(LavaHash(cell + int3(0,1,0), seed), LavaHash(cell + int3(1,1,0), seed), w.x);
                float x01 = lerp(LavaHash(cell + int3(0,0,1), seed), LavaHash(cell + int3(1,0,1), seed), w.x);
                float x11 = lerp(LavaHash(cell + int3(0,1,1), seed), LavaHash(cell + int3(1,1,1), seed), w.x);
                return lerp(lerp(x00, x10, w.y), lerp(x01, x11, w.y), w.z) * 2.0 - 1.0;
            }

            float LavaFissures(float3 direction)
            {
                // Integer seed words preserve all 32 CPU seed bits through float material uniforms.
                uint seed = (uint)_LavaSeed.x | ((uint)_LavaSeed.y << 16);
                float field = LavaNoise(direction * _LavaFrequency, seed)
                    + 0.16 * LavaNoise(direction * (_LavaFrequency * 2.2916667), seed ^ 0x9e3779b9u);
                float aa = max(0.0005, fwidth(field));
                return 1.0 - smoothstep(max(0.0, _LavaFissureWidth - aa), _LavaFissureWidth + aa, abs(field));
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 lightDirection = normalize(_SunDirection.xyz);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 halfDirection = SafeNormalize(viewDirection + lightDirection);
                float nDotL = saturate(dot(normal, lightDirection));
                float nDotV = saturate(dot(normal, viewDirection));
                float nDotH = saturate(dot(normal, halfDirection));
                float lDotH = saturate(dot(lightDirection, halfDirection));
                float roughness = max(0.04, _Roughness);
                float a2 = roughness * roughness * roughness * roughness;
                float denominator = nDotH * nDotH * (a2 - 1.0) + 1.0;
                float distribution = a2 / max(0.0001, 3.14159265 * denominator * denominator);
                float k = (roughness + 1.0) * (roughness + 1.0) * 0.125;
                float geometry = nDotL / max(0.001, nDotL * (1.0 - k) + k);
                geometry *= nDotV / max(0.001, nDotV * (1.0 - k) + k);
                float fresnel = 0.04 + 0.96 * pow(1.0 - lDotH, 5.0);
                float specular = distribution * geometry * fresnel / max(0.001, 4.0 * nDotL * nDotV);
                float3 albedo = max(0.0, input.color.rgb * _PlanetTint.rgb);
                float fissure = 0;
                if (_IsLava > 0.5)
                {
                    fissure = LavaFissures(normalize(input.planetPosition));
                    // CPU colors remain useful diagnostic data, while visible lava is evaluated per
                    // fragment so plate boundaries do not inherit the terrain vertex grid or LOD.
                    albedo = lerp(float3(0.045,0.028,0.023), float3(0.58,0.085,0.008), fissure) * _PlanetTint.rgb;
                }
                float3 irradiance = _SunColor.rgb * _SunIntensity;
                float3 color = albedo * (_AmbientFloor + irradiance * nDotL);
                color += irradiance * nDotL * specular * _SpecularStrength;
                color += float3(1.0, 0.08, 0.005) * fissure * _EmissionStrength;
                float distanceToCamera = distance(input.positionWS, _WorldSpaceCameraPos);
                float fog = (1.0 - exp(-distanceToCamera * max(0.0, _FogDensity))) * _FogStrength;
                color = lerp(color, _FogColor.rgb, saturate(fog));
                return half4(color, 1.0);
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
                float4 _PlanetTint, _SunDirection, _SunColor, _PlanetCenter, _FogColor, _LavaSeed, _PatchAnchor;
                float _SunIntensity, _AmbientFloor, _Roughness, _SpecularStrength, _EmissionStrength;
                float _FogStrength, _FogDensity, _LavaFrequency, _LavaFissureWidth, _IsLava;
            CBUFFER_END
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }
            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
