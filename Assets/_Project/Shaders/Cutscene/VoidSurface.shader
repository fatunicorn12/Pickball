// The white void's dome and ground disc (scene 1). Unlit, shadowless white that dissolves with
// the radial colour flood (CutsceneVoid.hlsl): opaque where the flood has not reached, clear
// where it has, with a soft gold front between. The dome draws its inside (Cull Front).
//
// The set builder puts the dome and disc at queue 3500/3501: after every ordinary transparent
// (the greybox's floating signs, glass and veils would otherwise show through the white), and
// before the gold construction lines (3600), which must draw over the void.
Shader "Pickball/Cutscene/Void Surface"
{
    Properties
    {
        [MainColor] _Color ("Colour", Color) = (1, 1, 1, 1)
        _RiseWeight ("Flood Rise Weight", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _SkyFade ("Fades With The Look Blend", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "VoidSurface"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CutsceneVoid.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _RiseWeight;
                float _SkyFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d = CutsceneVoidDistance(input.positionWS, _RiseWeight);
                half amount = CutsceneVoidAmount(d);
                half front = CutsceneVoidFront(d) * _CutsceneVoidEdgeColour.a;
                half3 colour = lerp(_Color.rgb * CutsceneVoidWhite(), _CutsceneVoidEdgeColour.rgb, saturate(front * (1.0h - amount) * 2.0h));
                half alpha = _Color.a * saturate(max(amount, front));
                alpha *= 1.0h - (half)(_SkyFade * _CutsceneVoidSky);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
