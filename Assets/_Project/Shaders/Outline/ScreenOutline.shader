// Draws thick cartoon lines over the opaque image, wherever depth or surface direction jumps. Each
// pixel compares itself with a ring of eight neighbours, so the line straddles the edge and is about
// twice the ring radius wide.
//
// A depth jump is measured from the surfaces' own planes, not as a plain difference in depth. A floor
// seen at a low angle changes depth quickly from pixel to pixel but stays on one plane, so it draws
// no false lines, which a plain depth difference would.
//
// Counting how many neighbours found an edge softens the line's rim without needing MSAA.
Shader "Hidden/Pickleball/Screen Outline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "Screen Outline"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define RING_SAMPLES 8

            TEXTURE2D_X_FLOAT(_OutlineDepth);
            TEXTURE2D_X_FLOAT(_OutlineNormals);

            float _OutlineRadius;              // pixels
            float4 _OutlineColor;
            float4 _OutlineDepthThresholds;    // x = metres, y = metres per metre from the camera
            float _OutlineNormalThreshold;     // 1 - cos(angle)
            float4 _OutlineFade;               // x = start, y = end, in metres from the camera

            static const float2 Ring[RING_SAMPLES] =
            {
                float2(1, 0), float2(0.7071, 0.7071), float2(0, 1), float2(-0.7071, 0.7071),
                float2(-1, 0), float2(-0.7071, -0.7071), float2(0, -1), float2(0.7071, -0.7071),
            };

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            // One pixel's surface, read from the depth and normals textures.
            struct Surface
            {
                bool isSky;
                float3 position;
                float3 normal;
            };

            Surface LoadSurface(int2 pixel)
            {
                int2 size = int2(_ScaledScreenParams.xy);
                pixel = clamp(pixel, int2(0, 0), size - 1);

                Surface surface;
                float depth = LOAD_TEXTURE2D_X(_OutlineDepth, pixel).r;

                #if UNITY_REVERSED_Z
                    surface.isSky = depth <= 0.000001;
                #else
                    surface.isSky = depth >= 0.999999;
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif

                float2 uv = (pixel + 0.5) / _ScaledScreenParams.xy;
                surface.position = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float4 normals = LOAD_TEXTURE2D_X(_OutlineNormals, pixel);
                surface.normal = SafeNormalize(normals.xyz);
                // A shader can opt a surface out by writing alpha above 0.5 in its DepthNormals pass.
                // It then counts as sky: it gets no lines of its own, and whatever stands in front of
                // it still gets a silhouette. The cutscene void uses this to keep its set hidden.
                surface.isSky = surface.isSky || normals.a > 0.5;
                return surface;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int2 pixel = int2(input.positionCS.xy);
                Surface center = LoadSurface(pixel);

                float edgeSamples = 0.0;
                float nearestEdge = 1e6;

                for (int i = 0; i < RING_SAMPLES; i++)
                {
                    Surface other = LoadSurface(pixel + int2(round(Ring[i] * _OutlineRadius)));

                    if (center.isSky && other.isSky) continue;

                    bool isEdge;
                    float distance;
                    if (center.isSky != other.isSky)
                    {
                        isEdge = true;
                        distance = length((center.isSky ? other.position : center.position) - _WorldSpaceCameraPos);
                    }
                    else
                    {
                        // Measure at the nearer surface: it's the one whose outline this is.
                        float centerDistance = length(center.position - _WorldSpaceCameraPos);
                        float otherDistance = length(other.position - _WorldSpaceCameraPos);
                        distance = min(centerDistance, otherDistance);

                        // Measured from both surfaces. Near a silhouette a surface turns side-on, so its
                        // plane runs along the view ray and whatever is behind it looks "on the plane";
                        // from that side alone the line would split into two with a gap between.
                        float3 offset = other.position - center.position;
                        float planeGap = max(abs(dot(center.normal, offset)), abs(dot(other.normal, offset)));
                        float depthLimit = _OutlineDepthThresholds.x + _OutlineDepthThresholds.y * distance;
                        float turn = 1.0 - dot(center.normal, other.normal);
                        isEdge = planeGap > depthLimit || turn > _OutlineNormalThreshold;
                    }

                    if (isEdge)
                    {
                        edgeSamples += 1.0;
                        nearestEdge = min(nearestEdge, distance);
                    }
                }

                if (edgeSamples <= 0.0) return float4(0, 0, 0, 0);

                // One neighbour on the edge is the soft rim of the line; three is its solid middle.
                float coverage = saturate(edgeSamples / 3.0);
                float fade = 1.0 - smoothstep(_OutlineFade.x, _OutlineFade.y, nearestEdge);
                return float4(_OutlineColor.rgb, _OutlineColor.a * coverage * fade);
            }
            ENDHLSL
        }
    }
}
