// The pulse's flattened dome: only its silhouette glows (fresnel), broken up by scrolling noise.
// The camera is usually INSIDE this shell, so a surface facing the viewer has to stay dark or it
// would fill the screen; abs(N.V) makes the inside read the same as the outside (Cull Off).
Shader "Pickball/Hypatia/Pulse Shell"
{
    Properties
    {
        _Noise ("Noise (tiling RGB)", 2D) = "gray" {}
        [HDR] _Color ("Rim Colour", Color) = (0.25, 0.38, 1, 1)
        _Fade ("Fade", Range(0, 1)) = 0
        _Scroll ("Noise Scroll", Float) = 0
        _RimPower ("Rim Tightness", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "PulseShell"
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
                float4 _Color;
                float _Fade;
                float _Scroll;
                float _RimPower;
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
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
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

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetCameraPositionWS() - input.positionWS);
                float rim = pow(1.0 - abs(dot(n, v)), max(_RimPower, 0.01));

                // Integer tile counts round the sphere's u, so the noise closes at its seam.
                float2 uv = input.uv * float2(6.0, 2.0) + float2(0.0, _Scroll);
                float3 noise = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uv, 0).rgb;
                float threshold = lerp(0.85, 0.42, _Fade);
                float eroded = smoothstep(threshold, threshold + 0.25, noise.r);

                return half4(_Color.rgb * rim * eroded * _Fade, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
