// Painterly lit surface for the playable world.
//
// The greybox is scaled boxes and cylinders, and their UVs stretch one texture over each whole
// face. So the base map is projected from WORLD position instead, in real metres: a texture lines
// up across neighbouring boxes and never stretches, and no mesh needs UV work. Walls always use Y
// as their V, so a texture's "up" (block courses, bevels) is up on every wall.
//
// Mobile cost: a flat, axis-aligned face samples the texture once. The weights only overlap on
// curved or slanted surfaces, where it samples twice (three times at most near a diagonal corner).
// Sampling sits behind branches, so it needs explicit gradients to keep mips correct.
//
// A tiling texture shows its repeat from far away, so _VariationDark/_VariationLight tint it with
// large soft patches, also from world position. They come from a few layered sines rather than a
// noise texture: no extra sample, and no repeat of their own.
//
// Lighting matches Pickball/Cutscene/Painterly: a soft wrapped Lambert with realtime shadows, and
// ambient tinted so the shaded side goes blue-violet, not grey. Realtime only: no lightmaps.
//
// Known limit: the projection is world space, so a texture slides across anything that moves.
// Moving parts need their own material.
Shader "Pickball/World Painterly"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [MainTexture][NoScaleOffset] _BaseMap ("Base Map (tiling, colour only)", 2D) = "white" {}
        _TileSize ("Tile Size (m per repeat)", Float) = 4
        _BlendSharpness ("Projection Blend Sharpness", Range(1, 16)) = 4

        _VariationDark ("Patch Tint: Dark", Color) = (0.92, 0.92, 0.92, 1)
        _VariationLight ("Patch Tint: Light", Color) = (1.04, 1.04, 1.04, 1)
        _VariationSize ("Patch Size (m)", Float) = 8

        _ShadeColor ("Shade Colour (ambient tint)", Color) = (0.82, 0.86, 1.0, 1)
        _Wrap ("Light Wrap", Range(0, 1)) = 0.35
        _Softness ("Terminator Softness", Range(0.01, 1)) = 0.55
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.85
        _AOStrength ("SSAO Strength", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _TileSize;
            half _BlendSharpness;
            half4 _VariationDark;
            half4 _VariationLight;
            float _VariationSize;
            half4 _ShadeColor;
            half _Wrap;
            half _Softness;
            half _ShadowStrength;
            half _AOStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            // The base map projected along world axes. A unit normal always has one component
            // of at least 0.577, so subtracting 0.5 leaves at least one weight above zero.
            half3 SampleProjected(float3 positionWS, float3 normalWS)
            {
                float3 q = positionWS / _TileSize;
                float3 dx = ddx(q);
                float3 dy = ddy(q);

                float3 weights = saturate(abs(normalWS) - 0.5) * 2.0;
                weights = pow(weights, _BlendSharpness);
                weights /= weights.x + weights.y + weights.z;

                half3 colour = 0;
                if (weights.y > 0.0)
                    colour += SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, q.xz, dx.xz, dy.xz).rgb * (half)weights.y;
                if (weights.x > 0.0)
                    colour += SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, q.zy, dx.zy, dy.zy).rgb * (half)weights.x;
                if (weights.z > 0.0)
                    colour += SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, q.xy, dx.xy, dy.xy).rgb * (half)weights.z;
                return colour;
            }

            // Large soft patches in 0..1: layered, warped sines at unrelated frequencies.
            half WorldPatches(float3 positionWS)
            {
                float3 p = positionWS / _VariationSize;
                float a = sin(p.x * 2.1 + sin(p.z * 1.53 + 1.3) * 1.7);
                float b = sin(p.z * 2.7 + sin(p.y * 1.83 + p.x * 0.87) * 1.9 + 2.1);
                float c = sin((p.x + p.z) * 1.19 + p.y * 1.97 + a * 1.2);
                return (half)saturate(0.5 + (a + b + c) * 0.25);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 p = input.positionWS;

                half3 albedo = SampleProjected(p, normalWS) * _BaseColor.rgb;
                albedo *= lerp(_VariationDark.rgb, _VariationLight.rgb, WorldPatches(p));

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(p));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(p);
                #endif
                Light mainLight = GetMainLight(shadowCoord, p, half4(1, 1, 1, 1));

                // Soft painted terminator: wrapped Lambert through a smoothstep.
                half ndl = dot(normalWS, mainLight.direction);
                half wrapped = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                half diffuse = smoothstep(0.0h, _Softness, wrapped) * wrapped;
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
                half3 direct = mainLight.color * (diffuse * shadow);

                half3 ambient = SampleSH(normalWS) * _ShadeColor.rgb;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    half ao = GetScreenSpaceAmbientOcclusion(screenUV).indirectAmbientOcclusion;
                    ambient *= lerp(1.0h, ao, _AOStrength);
                #endif

                half3 colour = albedo * (ambient + direct);
                colour = MixFog(colour, InitializeInputDataFog(float4(p, 1.0), input.fogFactor));
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    half3 packedNormalWS = PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5));
                    return half4(packedNormalWS, 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
