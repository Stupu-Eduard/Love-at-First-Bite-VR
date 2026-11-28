Shader "LoveAtFirstBite/IngredientSear"
{
    // URP lit shader that sears an ingredient per side. The bottom (pan-facing) surface
    // darkens by _CookDown, the top by _CookUp, blending raw -> cooked -> burnt color.
    // A runtime SearCooking component raises _CookDown/_CookUp for whichever side faces the pan.
    Properties
    {
        _BaseMap ("Base Texture", 2D) = "white" {}
        _RawColor ("Raw Color", Color) = (0.75, 0.25, 0.25, 1)
        _CookedColor ("Cooked Color", Color) = (0.45, 0.27, 0.15, 1)
        _BurntColor ("Burnt Color", Color) = (0.08, 0.05, 0.04, 1)

        _CookDown ("Cook Amount Down (0-2)", Range(0,2)) = 0
        _CookUp ("Cook Amount Up (0-2)", Range(0,2)) = 0

        _Smoothness ("Smoothness", Range(0,1)) = 0.2
        _Metallic ("Metallic", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP lighting keywords required for UniversalFragmentPBR to light correctly.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  worldUp     : TEXCOORD3; // +1 facing up, -1 facing down
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _RawColor;
                float4 _CookedColor;
                float4 _BurntColor;
                float _CookDown;
                float _CookUp;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = normInputs.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                // How much this surface faces up (+1) vs down (-1) in world space.
                OUT.worldUp = dot(normalize(normInputs.normalWS), float3(0,1,0));
                return OUT;
            }

            // Blend raw -> cooked -> burnt by a 0..2 cook amount (1 = cooked, 2 = burnt).
            float3 CookColor(float amount)
            {
                float3 c = lerp(_RawColor.rgb, _CookedColor.rgb, saturate(amount));
                c = lerp(c, _BurntColor.rgb, saturate(amount - 1.0));
                return c;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Pick this fragment's cook amount from whichever side it faces.
                float up = saturate(IN.worldUp * 0.5 + 0.5);       // 0 at bottom, 1 at top
                float sideCook = lerp(_CookDown, _CookUp, up);

                float3 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                float3 albedo = CookColor(sideCook) * baseTex;

                float3 normalWS = normalize(IN.normalWS);

                // --- Explicit, robust lighting so point lights (URP additional lights) always
                //     contribute. The full PBR path was returning black in this scene.
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

                // Ambient floor (scene ambient is dark, so add a small constant so nothing is pure black).
                float3 lighting = SampleSH(normalWS) + 0.15;

                // Main (directional) light.
                Light mainLight = GetMainLight(shadowCoord);
                lighting += mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation
                            * saturate(dot(normalWS, mainLight.direction)));

                // Additional lights (point/spot) — these are the kitchen's warm point lights.
                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; li++)
                {
                    Light l = GetAdditionalLight(li, IN.positionWS);
                    lighting += l.color * (l.distanceAttenuation * l.shadowAttenuation
                                * saturate(dot(normalWS, l.direction)));
                }
                #endif

                float3 litColor = albedo * lighting;
                litColor = MixFog(litColor, ComputeFogFactor(IN.positionHCS.z));
                return half4(litColor, 1.0);
            }
            ENDHLSL
        }

        // Shadow caster so the ingredient still casts shadows.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings  { float4 positionCS : SV_POSITION; };

            Varyings shadowVert (Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 shadowFrag (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
