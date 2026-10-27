// The training stage's measuring grid, in the style of Smash Bros' training
// stage: a pale surface ruled with fine lines, darker lines every metre, blue
// lines every 5 m and red lines along the world axes.
//
// The grid is projected from WORLD position, not UVs, so it lines up across
// every floor, block and wall in the stage and always reads in real metres:
// a line on a pillar's side is the same height as the line on the wall
// behind it, which is what makes jump heights and distances readable at a
// glance. Each face picks the projection of its dominant normal axis (floor
// XZ, side walls ZY, front/back walls XY), which suits the stage's
// axis-aligned blocks and ramps.
//
// Lines are anti-aliased with screen-space derivatives and fade to their
// average tint once they get thinner than a pixel (Ben Golus' "pristine
// grid"), so distant floor does not shimmer into moire.
//
// Lighting is deliberately flat, like the reference: the colour only darkens
// on faces turned from the sun and where a realtime shadow lands, which is
// what you need to judge where you will land.
Shader "Pickball/Training Grid"
{
    Properties
    {
        [MainColor] _BaseColor ("Base", Color) = (0.93, 0.93, 0.94, 1)
        _FineColor ("Fine Lines", Color) = (0.84, 0.85, 0.87, 1)
        _MinorColor ("Metre Lines", Color) = (0.66, 0.68, 0.72, 1)
        _MajorColor ("5 m Lines", Color) = (0.3, 0.46, 0.93, 1)
        _AxisColor ("Axis Lines", Color) = (0.93, 0.2, 0.22, 1)

        _FineSpacing ("Fine Spacing (m)", Float) = 0.25
        _MinorSpacing ("Metre Spacing (m)", Float) = 1
        _MajorSpacing ("Major Spacing (m)", Float) = 5

        _FineWidth ("Fine Width (m)", Float) = 0.01
        _MinorWidth ("Metre Width (m)", Float) = 0.025
        _MajorWidth ("Major Width (m)", Float) = 0.05
        _AxisWidth ("Axis Width (m)", Float) = 0.08

        _ShadeColor ("Shade Color", Color) = (0.66, 0.69, 0.78, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.85
        _AOStrength ("SSAO Strength", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _FineColor;
            half4 _MinorColor;
            half4 _MajorColor;
            half4 _AxisColor;
            float _FineSpacing;
            float _MinorSpacing;
            float _MajorSpacing;
            float _FineWidth;
            float _MinorWidth;
            float _MajorWidth;
            float _AxisWidth;
            half4 _ShadeColor;
            half _ShadowStrength;
            half _AOStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

            // Repeating lines every cell of `uv` (already divided by the
            // spacing), `lineWidth` as a fraction of a cell. Returns coverage.
            float PristineGrid(float2 uv, float2 lineWidth)
            {
                float4 uvDDXY = float4(ddx(uv), ddy(uv));
                float2 uvDeriv = float2(length(uvDDXY.xz), length(uvDDXY.yw));
                float2 drawWidth = clamp(lineWidth, uvDeriv, 0.5);
                float2 lineAA = uvDeriv * 1.5;
                float2 gridUV = 1.0 - abs(frac(uv) * 2.0 - 1.0);
                float2 grid2 = smoothstep(drawWidth + lineAA, drawWidth - lineAA, gridUV);
                // Thinner than a pixel: draw a pixel's worth, dimmed to match,
                // then blend to the average once a cell is under a pixel.
                grid2 *= saturate(lineWidth / drawWidth);
                grid2 = lerp(grid2, lineWidth, saturate(uvDeriv * 2.0 - 1.0));
                return lerp(grid2.x, 1.0, grid2.y);
            }

            float Lines(float2 coord, float spacing, float width)
            {
                // gridUV above runs 0..1 over half a cell, so a line `width`
                // metres wide is width/spacing of it.
                return PristineGrid(coord / spacing, (width / spacing).xx);
            }

            // The single line where a coordinate crosses zero (the world axis).
            float AxisLine(float c, float width)
            {
                float fw = max(fwidth(c), 1e-5);
                float halfWidth = max(width * 0.5, fw * 0.5);
                float cover = 1.0 - smoothstep(halfWidth, halfWidth + fw * 1.5, abs(c));
                return cover * saturate(width / (halfWidth * 2.0));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 n = abs(normalWS);
                float3 p = input.positionWS;

                // Project along the dominant axis: floors XZ, X-facing walls
                // ZY, Z-facing walls XY. Every projection keeps world units.
                float2 coord = n.y >= n.x && n.y >= n.z ? p.xz
                             : n.x >= n.z ? p.zy
                             : p.xy;

                half3 color = _BaseColor.rgb;
                color = lerp(color, _FineColor.rgb, Lines(coord, _FineSpacing, _FineWidth) * _FineColor.a);
                color = lerp(color, _MinorColor.rgb, Lines(coord, _MinorSpacing, _MinorWidth) * _MinorColor.a);
                color = lerp(color, _MajorColor.rgb, Lines(coord, _MajorSpacing, _MajorWidth) * _MajorColor.a);
                float axis = max(AxisLine(coord.x, _AxisWidth), AxisLine(coord.y, _AxisWidth));
                color = lerp(color, _AxisColor.rgb, axis * _AxisColor.a);

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(p));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(p);
                #endif
                Light mainLight = GetMainLight(shadowCoord, p, half4(1, 1, 1, 1));

                // Flat shading: a face turned toward the sun keeps its full
                // colour, one turned away (or in shadow) takes the shade tint.
                half facing = saturate(dot(normalWS, mainLight.direction) * 1.6);
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
                color *= lerp(_ShadeColor.rgb, half3(1, 1, 1), facing * shadow);

                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    half ao = GetScreenSpaceAmbientOcclusion(screenUV).indirectAmbientOcclusion;
                    color *= lerp(1.0h, ao, _AOStrength);
                #endif

                color = MixFog(color, InitializeInputDataFog(float4(p, 1.0), input.fogFactor));
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Platforms overhead throw shadows on the floor: that shadow is how you
        // line up a landing in 3D.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set per shadow-casting light by URP (ShadowUtils); normally
            // declared by URP's own ShadowCasterPass.hlsl.
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
            #pragma target 2.0
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

        // Feeds SSAO (the PC renderer's SSAO samples depth + normals).
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
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
