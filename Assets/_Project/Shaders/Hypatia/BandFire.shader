// One burning floor band, on ArcMesh.Annulus (BandFireView). The mesh is exactly the band, so what
// looks dangerous is exactly what the fire rule hurts; nothing here is allowed to bleed past it.
//
// Warning: the whole band tints red at once (this is the area that will burn), and a burn front creeps
// in from the outer edge, leaving char and glowing veins, reaching the inner edge at the moment of
// ignition. Burning: char with slow molten veins. Ash: the veins cool and the char stays.
// The inner edge is the line that matters most ("the fire stops here"), so it is drawn hot, hottest
// on the current boundary of the safe ground (_Frontier).
//
// Premultiplied alpha: alpha darkens the plate to char and colour adds light, in one pass.
// All coordinates are metres on the plate, so the noise runs unbroken across neighbouring bands.
// Needs no depth or opaque texture, so it costs the same on the mobile render settings.
Shader "Pickball/Hypatia/Band Fire"
{
    Properties
    {
        _Noise ("Noise (tiling RGB)", 2D) = "gray" {}
        _Inner ("Inner Radius (m)", Float) = 6
        _Outer ("Outer Radius (m)", Float) = 8
        _Warning ("Warning Progress", Range(0, 1)) = 0
        _Burning ("Burning (0 or 1)", Float) = 0
        _Ash ("Ash", Range(0, 1)) = 0
        _Frontier ("Is the Safe Boundary", Range(0, 1)) = 0
        _Ignite ("Ignition Flash", Range(0, 1)) = 0
        [HDR] _EmberColor ("Ember Colour", Color) = (1.7, 0.36, 0.05, 1)
        [HDR] _HotColor ("Hot Colour", Color) = (3.2, 1.6, 0.4, 1)
        _CharColor ("Char Colour", Color) = (0.03, 0.022, 0.028, 1)
        _CharAlpha ("Char Darkness", Range(0, 1)) = 0.97
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-20" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BandFire"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
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
                float _Inner;
                float _Outer;
                float _Warning;
                float _Burning;
                float _Ash;
                float _Frontier;
                float _Ignite;
                float4 _EmberColor;
                float4 _HotColor;
                float4 _CharColor;
                float _CharAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 metres : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.metres = input.positionOS.xz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.metres;
                float r = length(p);
                float time = _Time.y;

                float3 broad = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, p * 0.05, 0).rgb;
                float3 middle = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, p * 0.13 + float2(time * 0.006, 0.0), 0).rgb;
                float3 fine = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, p * 0.37 - float2(0.0, time * 0.02), 0).rgb;

                // ── How far the burn has reached ──
                float charred;
                if (_Burning > 0.5)
                {
                    charred = 1.0;
                }
                else
                {
                    // The front starts past the outer edge and ends past the inner one, so
                    // even with its wobble the band is wholly reached by the end of the warning.
                    float wobble = (broad.g - 0.5) * 1.6;
                    float front = lerp(_Outer + 1.0, _Inner - 1.0, saturate(_Warning)) + wobble;
                    charred = smoothstep(front - 0.5, front + 0.5, r);
                }

                // ── Molten veins: ridges of the noise, thin and branching ──
                float ridge = 1.0 - abs(middle.r * 2.0 - 1.0);
                float ridge2 = 1.0 - abs(broad.b * 2.0 - 1.0);
                float veins = smoothstep(0.94, 0.995, ridge) + 0.45 * smoothstep(0.96, 0.997, ridge2);
                float pulse = 0.35 + 0.65 * fine.g;
                float glowing = veins * pulse * (1.0 - _Ash * 0.97);

                float3 colour = _EmberColor.rgb * glowing * 0.8 * charred;
                colour += _HotColor.rgb * glowing * glowing * 0.2 * charred;

                // A low bed of heat under the flames while it burns. Char must stay the
                // floor's main colour, or the player and the floor's telegraphs get lost.
                float bed = (0.004 + 0.03 * fine.r * broad.r) * _Burning * (1.0 - _Ash);
                colour += _EmberColor.rgb * bed;

                // ── The warning: the whole band reads as doomed from the first frame ──
                float warning = (1.0 - _Burning) * step(0.0001, _Warning);
                float beat = 0.55 + 0.45 * sin(time * (6.0 + 12.0 * _Warning));
                colour += _EmberColor.rgb * warning * (0.05 + 0.1 * beat) * (0.5 + _Warning);

                // The burn front's own hot line.
                if (_Burning < 0.5 && _Warning > 0.0)
                {
                    float wobble = (broad.g - 0.5) * 1.6;
                    float front = lerp(_Outer + 1.0, _Inner - 1.0, saturate(_Warning)) + wobble;
                    float d = (r - front) / 0.28;
                    colour += _HotColor.rgb * exp(-d * d) * (0.5 + 0.8 * fine.b);
                }

                // ── The inner edge: where the fire stops ──
                float inside = max(r - _Inner, 0.0);
                float lit = saturate(_Burning + _Warning * 2.0) * (1.0 - _Ash);
                float edgeLine = exp(-inside / 0.05);
                float edgeWash = exp(-inside / 0.75);
                colour += _HotColor.rgb * lit * edgeLine * lerp(0.1, 1.5, _Frontier);
                colour += _EmberColor.rgb * lit * edgeWash * lerp(0.02, 0.24, _Frontier) * (0.6 + 0.7 * fine.r);

                // ── Ignition: the whole band catches at once ──
                colour += _EmberColor.rgb * _Ignite * (0.12 + 0.5 * middle.g);

                float alpha = charred * _CharAlpha * lerp(0.75, 1.0, broad.r);
                colour += _CharColor.rgb * alpha;
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
