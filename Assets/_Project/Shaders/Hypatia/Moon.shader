// One of Hypatia's moons. Unlit in Unity's sense: it takes its light from _SunPosition alone (her,
// from MoonLook), so it shows true phases as it orbits, whatever the scene's lights are doing.
//
// The surface comes from a texture baked by MoonSurface: RGB = object-space normal, A = brightness,
// longitude-latitude. Its coordinates are worked out here from the sphere's own position instead of
// the mesh's UVs, so the texture and mesh cannot disagree, and the mip level comes from the direction's
// rate of change, which has no jump at the longitude wrap (a plain sample would draw a seam there).
//
// The dark side keeps some earthshine and a blue rim: a moon you are about to be hit by, or want to
// bat, must never vanish against the night sky.
Shader "Pickball/Hypatia/Moon"
{
    Properties
    {
        _Surface ("Surface (RGB normal, A brightness)", 2D) = "white" {}
        [HDR] _LitColor ("Sunlit Colour", Color) = (1.3, 1.27, 1.2, 1)
        [HDR] _Earthshine ("Dark Side Colour", Color) = (0.11, 0.14, 0.26, 1)
        [HDR] _RimColor ("Rim Colour", Color) = (0.35, 0.55, 1.25, 1)
        _RimPower ("Rim Tightness", Float) = 3.5
        _SunPosition ("Sun Position (world)", Vector) = (0, 3, 0, 0)
        _Boost ("Extra Brightness", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Moon"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MOON_PI 3.14159265359

            TEXTURE2D(_Surface);
            SAMPLER(sampler_Surface);

            CBUFFER_START(UnityPerMaterial)
                float4 _Surface_ST;
                float4 _Surface_TexelSize;
                float4 _LitColor;
                float4 _Earthshine;
                float4 _RimColor;
                float _RimPower;
                float4 _SunPosition;
                float _Boost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;  // object space, from the centre
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.direction = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float2 uv = float2(atan2(d.z, d.x) / (2.0 * MOON_PI) + 0.5, asin(clamp(d.y, -1.0, 1.0)) / MOON_PI + 0.5);
                // Texels of equator crossed per pixel. From d, which is continuous at the wrap.
                float change = max(length(ddx(d)), length(ddy(d)));
                float lod = log2(max(change * _Surface_TexelSize.z / (2.0 * MOON_PI), 1e-4));
                float4 surface = SAMPLE_TEXTURE2D_LOD(_Surface, sampler_Surface, uv, max(lod, 0.0));

                float3 bumped = TransformObjectToWorldNormal(normalize(surface.rgb * 2.0 - 1.0));
                float3 geometric = normalize(input.normalWS);
                float3 toSun = normalize(_SunPosition.xyz - input.positionWS);
                float3 toCamera = normalize(GetCameraPositionWS() - input.positionWS);

                // A hard terminator is what makes it read as a moon and not a shaded ball.
                float lit = smoothstep(-0.02, 0.28, dot(bumped, toSun));
                float rim = pow(1.0 - saturate(dot(geometric, toCamera)), max(_RimPower, 0.01));

                float3 colour = surface.a * (_LitColor.rgb * lit + _Earthshine.rgb) + _RimColor.rgb * rim;
                colour *= 1.0 + _Boost;
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // An opaque object with no depth pass disappears from the depth prepass and can be
        // dropped entirely under depth priming.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
