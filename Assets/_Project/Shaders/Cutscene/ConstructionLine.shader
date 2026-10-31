// Gold construction lines (compass arcs, ruled lines, tick marks) drawn as camera-facing
// ribbons, revealed stroke by stroke (ConstructionLineResolve, Scripts/Cutscene/Sets).
//
// Mesh layout, baked by the set builder: each segment is a quad whose vertices sit ON the
// stroke's centreline.
//   POSITION  centreline point          NORMAL    stroke tangent (object space)
//   TEXCOORD0 x: 0..1 along the stroke, y: side (-1 / +1)
//   TEXCOORD1 x: stroke index,          y: half-width (m)
//   COLOR     tint (gold), alpha = per-stroke brightness
// The vertex shader pushes each vertex sideways, perpendicular to both the tangent and the
// view direction, so the line keeps its width from any angle, never thinner than about a
// pixel (thinner lines are widened and dimmed to match, so far lines don't shimmer).
// _StrokeVisible[i] (0..1, per renderer) hides everything past that fraction of stroke i;
// _LineAlpha dims every stroke at once (the fade to the faint permanent lines).
// Unlit and unfogged: gold reads cleanly against the white void and in the golden-hour haze.
Shader "Pickball/Cutscene/Construction Line"
{
    Properties
    {
        _LineColor ("Line Colour", Color) = (1, 0.78, 0.3, 1)
        _LineAlpha ("Line Alpha", Range(0, 1)) = 1
        _MinPixelHalfWidth ("Minimum Half-Width (px)", Float) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ConstructionLine"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LineColor;
                half _LineAlpha;
                float _MinPixelHalfWidth;
            CBUFFER_END

            // Per renderer, from a MaterialPropertyBlock (ConstructionLineResolve.MaxStrokes).
            float _StrokeVisible[160];

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 tangentOS : NORMAL;
                half4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : TEXCOORD0;
                float along : TEXCOORD1;
                float side : TEXCOORD2;
                float visible : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 tangentWS = TransformObjectToWorldDir(input.tangentOS);
                float3 toCamera = GetCameraPositionWS() - positionWS;
                float distance = max(length(toCamera), 1e-4);
                float3 sideDir = cross(tangentWS, toCamera / distance);
                sideDir *= rsqrt(max(dot(sideDir, sideDir), 1e-8));

                // World size of one pixel at this distance: 2·d / (P11 · screen height).
                float pixel = 2.0 * distance / (UNITY_MATRIX_P[1][1] * _ScreenParams.y);
                float halfWidth = input.uv1.y;
                float minHalf = _MinPixelHalfWidth * pixel;
                float widened = max(halfWidth, minHalf);
                positionWS += sideDir * input.uv0.y * widened;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                output.color.a *= (half)saturate(halfWidth / widened);
                output.along = input.uv0.x;
                output.side = input.uv0.y;
                int index = clamp((int)round(input.uv1.x), 0, 95);
                output.visible = _StrokeVisible[index];
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.visible - input.along - 1e-4);
                half edge = 1.0h - smoothstep(0.55h, 1.0h, (half)abs(input.side));
                half alpha = input.color.a * _LineColor.a * _LineAlpha * edge;
                return half4(_LineColor.rgb * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
