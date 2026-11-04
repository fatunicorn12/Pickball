// The light on her body, on a camera-facing quad: while the pulse charges, rays flicker round a
// swelling core and a thin ring tightens onto her; when it fires, a white-hot flash (PulseView).
Shader "Pickball/Hypatia/Pulse Core"
{
    Properties
    {
        _Noise ("Noise (tiling RGB)", 2D) = "gray" {}
        [HDR] _Color ("Charge Colour", Color) = (0.9, 1.25, 2.6, 1)
        [HDR] _FlashColor ("Flash Colour", Color) = (3.2, 3.8, 6, 1)
        _Charge ("Charge", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0
        _Spin ("Ray Drift", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "PulseCore"
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

            #define PULSE_TWO_PI 6.28318530718

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST;
                float4 _Color;
                float4 _FlashColor;
                float _Charge;
                float _Flash;
                float _Spin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 centred : TEXCOORD0;   // -1..1 across the quad
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.centred = input.uv * 2.0 - 1.0;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.centred);
                float turn = atan2(input.centred.x, input.centred.y) / PULSE_TWO_PI;
                float edge = saturate(1.0 - r);

                float core = exp(-r * r * 22.0);
                float halo = exp(-r * r * 5.0);

                // Rays: noise that varies round the circle, not along it.
                float3 noise = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, float2(turn * 7.0, _Spin * 0.11), 0).rgb;
                float rays = pow(saturate(noise.r * 1.5), 4.0) * edge * edge;

                float ringAt = lerp(0.92, 0.1, _Charge);
                float ring = exp(-pow((r - ringAt) / 0.03, 2.0));

                float3 colour = _Color.rgb * edge *
                    (core * (0.8 + 2.4 * _Charge) + halo * 0.35 * _Charge) +
                    _Color.rgb * (rays * 1.3 * _Charge + ring * 1.1 * step(_Charge, 0.995));
                colour *= step(0.0001, _Charge);

                // The flash replaces the charge: one blown-out frame, then a quick fall.
                colour = lerp(colour, float3(0.0, 0.0, 0.0), saturate(_Flash * 4.0));
                colour += _FlashColor.rgb * edge * edge * (core * 1.4 + halo * halo * 0.5) * _Flash;
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
