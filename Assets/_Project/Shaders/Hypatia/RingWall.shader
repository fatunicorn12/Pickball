// A Celestial Ring's wall, on ArcMesh.Wall (u along the arc from one gap edge to the other, v up).
//
// It has to look grand without hiding the answer, so the look is built round what the player needs:
//   - The gap edges are the brightest thing on it, in the same cyan as the floor's gap forecast.
//   - A low ring ends in a crisp line at its real height: this is what you must clear.
//   - A tall ring has no top. It dissolves upward into an aurora: you cannot clear this.
//   - The face of the wall stays mostly see-through, so she and the moons stay visible behind it.
//
// Rings expand straight through the camera, so the wall fades as it gets close to it; without that
// an HDR wall would flash the whole screen. Additive, so overlapping rings need no sorting.
// Needs no depth or opaque texture, so it costs the same on the mobile render settings.
Shader "Pickball/Hypatia/Ring Wall"
{
    Properties
    {
        _Noise ("Noise (tiling RGB)", 2D) = "gray" {}
        [HDR] _Color ("Wall Colour", Color) = (0.32, 0.5, 1.1, 1)
        [HDR] _EdgeColor ("Gap Edge Colour", Color) = (0.7, 2.2, 2.6, 1)
        _Height ("Drawn Height (m)", Float) = 1
        _SolidHeight ("Solid Height (m)", Float) = 1
        _Radius ("Radius (m)", Float) = 1
        _GapDegrees ("Gap Width (degrees)", Float) = 30
        _Tall ("Tall (0 or 1): dissolves upward instead of ending", Float) = 0
        _Fire ("Burning (0 or 1)", Float) = 0
        _Seed ("Noise Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "RingWall"
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

            #define WALL_TWO_PI 6.28318530718

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST;
                float4 _Color;
                float4 _EdgeColor;
                float _Height;
                float _SolidHeight;
                float _Radius;
                float _GapDegrees;
                float _Tall;
                float _Fire;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            float Stroke(float dist, float halfWidth, float aa)
            {
                float w = max(halfWidth, aa * 0.7);
                return (1.0 - smoothstep(w, w + aa, dist)) * saturate(halfWidth / w);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float height = max(_Height, 0.01);
                float solid = max(_SolidHeight, 0.01);
                float y = input.uv.y * height;
                float along = input.uv.x;

                float arcDegrees = 360.0 - _GapDegrees;
                float degrees = along * arcDegrees;
                float metresPerDegree = WALL_TWO_PI * max(_Radius, 0.01) / 360.0;
                float toGapEdge = min(along, 1.0 - along) * arcDegrees * metresPerDegree;
                float aa = max(fwidth(y), 1e-4);

                // Noise is tied to the angle, so the pattern stays on the ring as it grows.
                float time = _Time.y;
                float rise = lerp(0.3, 0.9, _Fire);
                float2 uvBroad = float2(along * 7.0, y * 0.09 - time * rise * 0.2) + _Seed;
                float2 uvFine = float2(along * 24.0, y * 0.06 - time * rise * 0.32) + _Seed * 1.7;
                float3 broad = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uvBroad, 0).rgb;
                float3 fine = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uvFine, 0).rgb;

                // ── Height: where this ring's body ends ──
                float upward = saturate(y / height);
                float bodyFall = exp(-y / max(solid * lerp(0.75, 0.42, _Tall), 0.01));

                // Tall rings erode away toward the top; the threshold climbs with height.
                float threshold = lerp(lerp(0.0, 0.3, _Fire), lerp(0.62, 0.95, _Tall), upward);
                float mass = broad.r * 0.6 + fine.g * 0.4;
                float eroded = smoothstep(threshold, threshold + 0.24, mass);

                // ── The chart engraved on it ──
                float toMeridian = abs(frac(degrees / 10.0 + 0.5) - 0.5) * 10.0 * metresPerDegree;
                float toParallel = abs(frac(y + 0.5) - 0.5);
                float chart = Stroke(toMeridian, 0.018, aa) * 0.8 + Stroke(toParallel, 0.012, aa) * 0.45;
                chart *= 1.0 - _Fire;
                float toTick = abs(frac(degrees / 2.5 + 0.5) - 0.5) * 2.5 * metresPerDegree;
                float ticks = Stroke(toTick, 0.016, aa) * step(y, 0.26);

                // Mostly see-through in the middle; a cylinder reads as one when its turning-away
                // sides are brighter than the part facing you.
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetCameraPositionWS() - input.positionWS);
                float grazing = pow(1.0 - abs(dot(n, v)), 2.5);

                float face = eroded * (0.2 + 0.85 * fine.r * broad.g) * bodyFall;
                float lines = (chart * (0.3 + 0.7 * bodyFall) * saturate(1.0 - y / max(solid, 0.01) * 0.75) + ticks * 1.3);

                // Fire runs from white-hot at the floor to its ember colour above.
                float3 tint = lerp(_Color.rgb, lerp(float3(2.6, 1.6, 0.6), _Color.rgb, saturate(y * 0.9)), _Fire);
                float3 colour = tint * (face * (1.0 + 2.0 * grazing) + lines);

                // The floor line grounds it, and the bloom off it lights the plate.
                colour += tint * 2.2 * exp(-(y * y) / 0.012);

                // A low ring's real top: clear this line and you clear the ring.
                colour += tint * 1.5 * (1.0 - _Tall) * Stroke(abs(y - solid), 0.02, aa);
                // Nothing of a low ring above that line.
                colour *= lerp(step(y, solid + 0.05), 1.0, _Tall);

                // ── The gap edges: always the brightest thing on the ring ──
                // Metres, not a fraction of the arc, so it stays an edge at any radius.
                float edge = exp(-toGapEdge / 0.09) + 0.1 * exp(-toGapEdge / 0.32);
                float edgeRise = lerp(1.0 - saturate(y / solid - 1.0), exp(-upward * 2.4), _Tall);
                colour += _EdgeColor.rgb * edge * edgeRise * (0.8 + 0.5 * fine.b);

                // Fade as it reaches the camera, so a ring passing through it never flashes the screen.
                colour *= saturate((distance(GetCameraPositionWS(), input.positionWS) - 0.8) / 3.2);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
