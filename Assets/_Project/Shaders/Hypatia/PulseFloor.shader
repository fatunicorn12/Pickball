// The knockback pulse on the plate, one quad for both halves of the attack (PulseView).
//
// Charge: an astrolabe dial is engraved in light at exactly Pulse Reach. A clock hand sweeps it
// once over the charge, so "when the hand comes round it fires" and "this is how far it reaches"
// read from the floor alone; the inner circles spin up and pull toward her.
// Wave: a hot, noise-wobbled front races out, trailing eroding streaks of energy, and the dial
// flares brass as the front passes over it.
//
// All distances are metres from the pulse's centre. Lines are distance fields kept at least about
// a pixel wide and dimmed to match, so the dial does not shimmer at the far side of the plate.
// Additive and HDR on purpose: anything past the bloom threshold is what makes it read as light.
// Needs no depth or opaque texture, so it costs the same on the mobile render settings.
Shader "Pickball/Hypatia/Pulse Floor"
{
    Properties
    {
        _Noise ("Noise (tiling RGB)", 2D) = "gray" {}
        _Extent ("Half Width of the Quad (m)", Float) = 12
        _Reach ("Pulse Reach (m)", Float) = 10
        _WaveRadius ("Wave Radius (m)", Float) = 0
        _WaveFade ("Wave Fade", Range(0, 1)) = 0
        _Charge ("Charge", Range(0, 1)) = 0
        _SigilAlpha ("Dial Alpha", Range(0, 1)) = 0
        _Spin ("Dial Spin (radians)", Float) = 0
        _Flash ("Flash", Range(0, 1)) = 0
        _Seed ("Noise Seed", Float) = 0
        [HDR] _BrassColor ("Dial Colour", Color) = (2.6, 1.7, 0.6, 1)
        [HDR] _WaveColor ("Wave Colour", Color) = (0.7, 1.25, 3.4, 1)
        _EdgeWidth ("Front Width (m)", Float) = 0.3
        _TrailLength ("Trail Length (m)", Float) = 3.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "PulseFloor"
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
                float _Extent;
                float _Reach;
                float _WaveRadius;
                float _WaveFade;
                float _Charge;
                float _SigilAlpha;
                float _Spin;
                float _Flash;
                float _Seed;
                float4 _BrassColor;
                float4 _WaveColor;
                float _EdgeWidth;
                float _TrailLength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 metres : TEXCOORD0;   // x = world X, y = world Z, from the centre
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.metres = (input.uv - 0.5) * 2.0 * _Extent;
                return output;
            }

            // Coverage of a line: dist and halfWidth in metres, aa = metres per pixel.
            float Stroke(float dist, float halfWidth, float aa)
            {
                float w = max(halfWidth, aa * 0.7);
                return (1.0 - smoothstep(w, w + aa, dist)) * saturate(halfWidth / w);
            }

            // Distance along the arc to the nearest of 'count' evenly spaced spokes.
            float ToSpoke(float turn, float r, float count)
            {
                float f = frac(turn * count + 0.5) - 0.5;
                return abs(f) / count * PULSE_TWO_PI * r;
            }

            float Between(float r, float inner, float outer)
            {
                return step(inner, r) * step(r, outer);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.metres;
                float r = length(p);
                float aa = max(length(float2(ddx(r), ddy(r))), 1e-4);
                float R = max(_Reach, 0.01);
                // 0 at azimuth 0 (+Z), growing toward +X, one per turn.
                float turn = frac(atan2(p.x, p.y) / PULSE_TWO_PI + 1.0);

                // ── The dial ──
                float rim = Stroke(abs(r - R), 0.06, aa);
                rim += Stroke(ToSpoke(turn, r, 72.0), 0.022, aa) * Between(r, R * 0.955, R);
                rim += Stroke(ToSpoke(turn, r, 12.0), 0.05, aa) * Between(r, R * 0.89, R);

                float pull = lerp(1.0, 0.42, _Charge * _Charge);   // the inner works gather toward her
                float inner = Stroke(abs(r - 0.72 * R * pull), 0.03, aa);
                inner += Stroke(abs(r - 0.46 * R * pull), 0.022, aa);
                float spunTurn = frac(turn + _Spin / PULSE_TWO_PI);
                inner += Stroke(ToSpoke(spunTurn, r, 16.0), 0.028, aa) * Between(r, 0.46 * R * pull, 0.72 * R * pull);
                // A rete's off-centre circles, turning opposite ways.
                float2 c1 = float2(sin(_Spin), cos(_Spin)) * 0.21 * R * pull;
                inner += Stroke(abs(length(p - c1) - 0.55 * R * pull), 0.034, aa);
                float a2 = 1.2 - _Spin * 0.6;
                float2 c2 = float2(sin(a2), cos(a2)) * 0.12 * R * pull;
                inner += Stroke(abs(length(p - c2) - 0.3 * R * pull), 0.024, aa);

                // The clock hand reveals the rim as it sweeps; one turn per charge.
                float sweeping = 1.0 - step(0.999, _Charge);
                float revealed = smoothstep(0.0, 0.015, _Charge - turn);
                float behindHand = frac(_Charge - turn);
                float insideDial = step(r, R);
                float hand = Stroke(ToSpoke(frac(turn - _Charge), r, 1.0), 0.07, aa) * insideDial * step(0.001, _Charge);
                float wake = exp(-behindHand * 34.0) * insideDial * 0.05 * step(0.001, _Charge);

                float dialGlow = _SigilAlpha * (0.3 + 0.7 * _Charge);
                float3 colour = _BrassColor.rgb * dialGlow *
                    (rim * max(revealed, 1.0 - sweeping) + inner * _Charge * 0.6 + (hand * 2.2 + wake) * sweeping);

                // ── The wave ──
                if (_WaveFade > 0.0)
                {
                    float2 uvA = float2(turn * 5.0, r * 0.085 - _WaveRadius * 0.05) + _Seed;
                    float2 uvB = float2(turn * 22.0, r * 0.02 - _WaveRadius * 0.045) + _Seed * 1.7;
                    float3 nA = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uvA, 0).rgb;
                    float3 nB = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uvB, 0).rgb;

                    float d = r - _WaveRadius + (nA.g - 0.5) * 0.5;   // the front wobbles
                    float w = max(_EdgeWidth, 0.01);
                    float front = exp(-(d * d) / (w * w));
                    float behind = d < 0.0 ? exp(d / max(_TrailLength, 0.01)) : 0.0;

                    // Erosion: the trail starts thin and is eaten away as the wave fades.
                    float threshold = lerp(0.78, 0.44, _WaveFade);
                    float eroded = smoothstep(threshold, threshold + 0.2, nA.r * 0.55 + nB.b * 0.45);
                    float streaks = 0.25 + 1.5 * nB.r * nB.r;

                    // The front is the hit itself: it dies fast once it has arrived, so it never
                    // parks at the rim as a thick ring. The trail lingers a moment longer.
                    colour += _WaveColor.rgb * _WaveFade * (front * 1.25 * _WaveFade * _WaveFade + behind * eroded * streaks * 0.42);
                    // The dial flares as the front passes over it.
                    colour += _BrassColor.rgb * _WaveFade * behind * (rim * 1.3 + inner * 0.8);
                }

                // The flash on the plate under her.
                colour += _WaveColor.rgb * _Flash * 0.7 * exp(-(r * r) / 5.0);

                // Never show the quad's square edge.
                colour *= saturate((_Extent - r) / 0.6);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
