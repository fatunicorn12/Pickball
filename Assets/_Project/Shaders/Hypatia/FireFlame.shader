// One tongue of flame on an upright particle quad, shaped here instead of by a glow sprite: a body
// that narrows upward, torn by noise scrolling up through it, burning from white-hot at its heart
// through orange to red at its edges, and thinning out as the particle ages.
//
// Per-particle randomness and age come from custom vertex streams set up by HypatiaFightFireArt;
// the two must stay in step. Additive, so flames need no sorting.
Shader "Pickball/Hypatia/Fire Flame"
{
    Properties
    {
        _Noise ("Noise (tiling RGB)", 2D) = "gray" {}
        [HDR] _RedColor ("Edge Colour", Color) = (1.1, 0.12, 0.015, 1)
        [HDR] _OrangeColor ("Body Colour", Color) = (1.7, 0.42, 0.04, 1)
        [HDR] _WhiteColor ("Heart Colour", Color) = (2.5, 1.35, 0.35, 1)
        _Rise ("Rise Speed", Float) = 1.1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "FireFlame"
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

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST;
                float4 _RedColor;
                float4 _OrangeColor;
                float4 _WhiteColor;
                float _Rise;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 uvAndRandom : TEXCOORD0;  // xy = uv, zw = stable random
                float age : TEXCOORD1;           // 0..1 through the particle's life
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float4 uvAndRandom : TEXCOORD0;
                float age : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                output.uvAndRandom = input.uvAndRandom;
                output.age = input.age;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uvAndRandom.xy;
                float2 random = input.uvAndRandom.zw;
                float y = uv.y;
                float time = _Time.y;

                // Noise rises through the flame; each flame samples its own stretch of it.
                float2 uvA = float2(uv.x * 0.42 + random.x * 7.0, y * 0.5 - time * _Rise * (0.8 + 0.4 * random.y) * 0.5);
                float2 uvB = float2(uv.x * 0.9 + random.y * 5.0, y * 1.1 - time * _Rise * 0.85);
                float3 a = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uvA, 0).rgb;
                float3 b = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uvB, 0).rgb;

                // A broad body that stays wide most of the way up, then gives out, and sways
                // more the higher it gets. A straight taper reads as a row of spikes; this,
                // torn by the noise and overlapping its neighbours, reads as a sheet of fire.
                float x = (uv.x - 0.5) * 2.0 + (a.g - 0.5) * 1.5 * y;
                float width = 0.92 * (1.0 - pow(saturate(y), 1.7));
                float body = 1.0 - smoothstep(width * 0.12, width, abs(x));
                float base = smoothstep(0.0, 0.12, y);

                // Heat falls with height and age, and is torn by the noise.
                float heat = body * base * (0.22 + 1.2 * (a.r * 0.6 + b.b * 0.4)) - y * 0.5 - input.age * input.age * 0.7;
                heat = saturate(heat);

                float3 colour = lerp(_RedColor.rgb, _OrangeColor.rgb, smoothstep(0.14, 0.5, heat));
                colour = lerp(colour, _WhiteColor.rgb, smoothstep(0.7, 1.0, heat));
                float visible = smoothstep(0.06, 0.28, heat) * smoothstep(0.0, 0.1, input.age);
                // Fade close to the camera, so a flame right in front of it never hides the player.
                float nearFade = saturate((distance(GetCameraPositionWS(), input.positionWS) - 1.5) / 3.5);
                return half4(colour * visible * input.color.a * nearFade * 0.62, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
