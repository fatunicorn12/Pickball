// Painterly lit surface for the cutscene sets (Outer Shrine, olive path, south crest).
//
// Flat hand-painted colour: base colour × optional texture × VERTEX COLOUR, where the set
// builder paints soft gradients and low-frequency patches into the vertex colours (no
// photoreal noise). Lighting is a soft wrapped Lambert from the main light with realtime
// shadows, plus the scene's ambient tinted toward a cool shade colour, so shadowed sides go
// blue-violet the way a painter would, not grey. Realtime only: no lightmaps.
//
// It also reads the scene-1 void flood (CutsceneVoid.hlsl): while CutsceneLook is in its void
// state, everything beyond the flood radius renders pure white, which is how the shrine is
// hidden in the void and revealed by the radial colour flood. With no CutsceneLook in the
// scene the flood globals are 0 and this is an ordinary lit shader.
Shader "Pickball/Cutscene/Painterly"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
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
            float4 _BaseMap_ST;
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
            #include "CutsceneVoid.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : TEXCOORD3;
                half fogFactor : TEXCOORD4;
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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 p = input.positionWS;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb * input.color.rgb;

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
                colour = ApplyCutsceneVoid(colour, p);
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
