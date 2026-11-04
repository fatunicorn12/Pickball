// The heat ripple that travels with the pulse's dome: redraws the opaque scene through the shell's
// silhouette, bent along its normal, so the world visibly warps as the wave passes.
//
// Reads _CameraOpaqueTexture, which only the PC render settings produce. PulseView turns this
// renderer off when that texture is missing, so the mobile settings just lose the ripple.
// Queued just before the other transparents: the opaque copy holds no effects, so anything drawn
// earlier would be painted over.
Shader "Pickball/Hypatia/Pulse Ripple"
{
    Properties
    {
        _Noise ("Noise (tiling RGB)", 2D) = "gray" {}
        _Fade ("Fade", Range(0, 1)) = 0
        _Scroll ("Noise Scroll", Float) = 0
        _Strength ("Bend (fraction of the screen)", Float) = 0.035
        _RimPower ("Rim Tightness", Float) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "PulseRipple"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST;
                float _Fade;
                float _Scroll;
                float _Strength;
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

                float2 uv = input.uv * float2(8.0, 3.0) + float2(0.0, _Scroll);
                float3 noise = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uv, 0).rgb;

                float2 bend = TransformWorldToViewDir(n).xy + (noise.gb - 0.5) * 0.8;
                float2 screen = GetNormalizedScreenSpaceUV(input.positionCS) + bend * rim * _Strength * _Fade;
                float3 scene = SampleSceneColor(saturate(screen));
                return half4(scene, saturate(rim * _Fade * 5.0));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
