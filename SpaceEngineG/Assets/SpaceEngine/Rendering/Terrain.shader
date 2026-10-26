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
        _MaterialDetailStrength ("Physical surface material detail", Range(0,2)) = 0.65
        _SurfaceNormalStrength ("Filtered geological normals", Range(0,2)) = 0.7
        _OceanRoughness ("Ocean roughness", Range(0.03,0.4)) = 0.12
        _OceanSpecularStrength ("Ocean reflection", Range(0,2)) = 0.8
        [HideInInspector] _SurfaceHeightMap ("Seed exact surface height", 2DArray) = "" {}
        [HideInInspector] _SurfaceMapReady ("Surface map ready", Float) = 0
        [HideInInspector] _SurfaceTint ("Body surface tint", Color) = (0.4,0.4,0.4,1)
        [HideInInspector] _TerrainRadius ("Physical radius", Float) = 6371000
        [HideInInspector] _TerrainAmplitude ("Relief envelope", Float) = 12000
        [HideInInspector] _PlanetKind ("Planet kind", Float) = 0
        [HideInInspector] _HasOcean ("Ocean datum", Float) = 0
        [HideInInspector] _MaterialMapResolution ("Surface map resolution", Float) = 1024
        [HideInInspector] _CameraAltitudeMetres ("Observer altitude", Float) = 1000000
        [HideInInspector] _RenderUnitsPerMetre ("Camera relative render scale", Float) = 1
        [HideInInspector] _PatchDetailAnchor ("Periodic physical detail anchor", Vector) = (0,0,0,0)
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
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PlanetTint, _SunDirection, _SunColor, _PlanetCenter, _FogColor, _LavaSeed, _PatchAnchor, _SurfaceTint, _PatchDetailAnchor;
                float _SunIntensity, _AmbientFloor, _Roughness, _SpecularStrength, _EmissionStrength;
                float _FogStrength, _FogDensity, _LavaFrequency, _LavaFissureWidth, _IsLava;
                float _MaterialDetailStrength, _SurfaceNormalStrength, _OceanRoughness, _OceanSpecularStrength;
                float _SurfaceMapReady, _TerrainRadius, _TerrainAmplitude, _PlanetKind, _HasOcean, _MaterialMapResolution, _CameraAltitudeMetres, _RenderUnitsPerMetre;
            CBUFFER_END
            #include "TerrainSurface.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 surfaceData : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 planetPosition : TEXCOORD2;
                float4 color : COLOR;
                float3 detailPosition : TEXCOORD3;
                float rawHeight : TEXCOORD4;
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
                output.detailPosition = input.positionOS.xyz + _PatchDetailAnchor.xyz;
                output.rawHeight = input.surfaceData.x;
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
                float3 albedo = max(0.0, input.color.rgb * _PlanetTint.rgb);
                float water = 0;
                if (_SurfaceMapReady > .5)
                {
                    EvaluateSurface(normalize(input.planetPosition), input.positionWS, input.detailPosition, input.rawHeight,
                        normal, albedo, normal, water);
                    albedo *= _PlanetTint.rgb;
                }
                float3 lightDirection = normalize(_SunDirection.xyz);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 halfDirection = SafeNormalize(viewDirection + lightDirection);
                float nDotL = saturate(dot(normal, lightDirection));
                float nDotV = saturate(dot(normal, viewDirection));
                float nDotH = saturate(dot(normal, halfDirection));
                float lDotH = saturate(dot(lightDirection, halfDirection));
                float roughness = max(0.04, lerp(_Roughness, _OceanRoughness, water));
                // Add unresolved normal variance before evaluating the narrow
                // water lobe. This controls distant sparkle without flattening
                // every ocean highlight with an oversized GGX denominator clamp.
                float normalVariance = max(dot(ddx(normal), ddx(normal)), dot(ddy(normal), ddy(normal)));
                roughness = sqrt(saturate(roughness * roughness + normalVariance * 2));
                float a2 = roughness * roughness * roughness * roughness;
                float denominator = nDotH * nDotH * (a2 - 1.0) + 1.0;
                float distribution = a2 / max(0.000000000001, 3.14159265 * denominator * denominator);
                float k = (roughness + 1.0) * (roughness + 1.0) * 0.125;
                float geometry = nDotL / max(0.001, nDotL * (1.0 - k) + k);
                geometry *= nDotV / max(0.001, nDotV * (1.0 - k) + k);
                float fresnel = 0.04 + 0.96 * pow(1.0 - lDotH, 5.0);
                float specular = distribution * geometry * fresnel / max(0.001, 4.0 * nDotL * nDotV);
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
                color += irradiance * nDotL * specular * lerp(_SpecularStrength, _OceanSpecularStrength, water);
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
            // SSAO's normal source requests this prepass instead of DepthOnly.
            // Omitting it left _CameraDepthTexture empty even while opaque
            // terrain was visible, so aerial perspective used the sea sphere.
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalVert
            #pragma fragment DepthNormalFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _PlanetTint, _SunDirection, _SunColor, _PlanetCenter, _FogColor, _LavaSeed, _PatchAnchor, _SurfaceTint, _PatchDetailAnchor;
                float _SunIntensity, _AmbientFloor, _Roughness, _SpecularStrength, _EmissionStrength;
                float _FogStrength, _FogDensity, _LavaFrequency, _LavaFissureWidth, _IsLava;
                float _MaterialDetailStrength, _SurfaceNormalStrength, _OceanRoughness, _OceanSpecularStrength;
                float _SurfaceMapReady, _TerrainRadius, _TerrainAmplitude, _PlanetKind, _HasOcean, _MaterialMapResolution, _CameraAltitudeMetres, _RenderUnitsPerMetre;
            CBUFFER_END
            struct DepthNormalInput { float4 positionOS:POSITION;float3 normalOS:NORMAL; };
            struct DepthNormalOutput { float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0; };
            DepthNormalOutput DepthNormalVert(DepthNormalInput input)
            {
                DepthNormalOutput output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            struct DepthNormalFragmentOutput
            {
                half4 normalWS:SV_Target0;
                #ifdef _WRITE_RENDERING_LAYERS
                    uint renderingLayers:SV_Target1;
                #endif
            };
            DepthNormalFragmentOutput DepthNormalFrag(DepthNormalOutput input)
            {
                DepthNormalFragmentOutput output;
                float3 normal=normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octEncoded=saturate(PackNormalOctQuadEncode(normal)*.5+.5);
                    output.normalWS=half4(PackFloat2To888(octEncoded),0);
                #else
                    output.normalWS=half4(normal,0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                    output.renderingLayers=EncodeMeshRenderingLayer();
                #endif
                return output;
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
                float4 _PlanetTint, _SunDirection, _SunColor, _PlanetCenter, _FogColor, _LavaSeed, _PatchAnchor, _SurfaceTint, _PatchDetailAnchor;
                float _SunIntensity, _AmbientFloor, _Roughness, _SpecularStrength, _EmissionStrength;
                float _FogStrength, _FogDensity, _LavaFrequency, _LavaFissureWidth, _IsLava;
                float _MaterialDetailStrength, _SurfaceNormalStrength, _OceanRoughness, _OceanSpecularStrength;
                float _SurfaceMapReady, _TerrainRadius, _TerrainAmplitude, _PlanetKind, _HasOcean, _MaterialMapResolution, _CameraAltitudeMetres, _RenderUnitsPerMetre;
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
