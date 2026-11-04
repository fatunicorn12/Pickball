// A twinkling four-point star, drawn per particle with no texture. Each star twinkles at its own
// rate and phase, taken from the particle's stable random (custom vertex stream, set up by
// HypatiaFightCelestialArt), so a trail shimmers instead of pulsing in unison.
Shader "Pickball/Hypatia/Star Trail"
{
    Properties
    {
        _Intensity ("Brightness (over 1 blooms)", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "StarTrail"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 uvAndRandom : TEXCOORD0;   // xy = uv, zw = stable random
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float4 uvAndRandom : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uvAndRandom = input.uvAndRandom;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 c = input.uvAndRandom.xy * 2.0 - 1.0;
                float r = length(c);
                float edge = saturate(1.0 - r);

                float twinkle = 0.5 + 0.5 * sin(_Time.y * (5.0 + input.uvAndRandom.z * 13.0) + input.uvAndRandom.w * 6.2832);
                float core = exp(-r * r * 34.0);
                float spikes = exp(-abs(c.x) * 20.0) * exp(-abs(c.y) * 3.2) + exp(-abs(c.y) * 20.0) * exp(-abs(c.x) * 3.2);

                float shape = (core * (0.7 + 0.5 * twinkle) + spikes * (0.2 + 0.8 * twinkle)) * edge;
                return half4(input.color.rgb * input.color.a * shape * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
