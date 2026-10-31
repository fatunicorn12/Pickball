// The radial colour flood that dissolves the scene-1 white void outward from MC_Start.
// CutsceneLook (Scripts/Cutscene/Sets) publishes these globals; every set material, the void
// dome and the void disc read them, so they reveal in step without referencing each other.
// Unset globals are 0, so with no CutsceneLook in the scene nothing is whitened.
// The C# mirror for tests is VoidFlood.cs.
#ifndef PICKBALL_CUTSCENE_VOID_INCLUDED
#define PICKBALL_CUTSCENE_VOID_INCLUDED

float4 _CutsceneVoidCentre;   // xyz: flood centre, world space
float4 _CutsceneVoidParams;   // x: radius (m), y: front width (m), z: strength 0..1, w: rise weight
half4 _CutsceneVoidEdgeColour;
float _CutsceneVoidWhite;
float _CutsceneVoidSky;       // 0..1: how far the void's sky dome has faded (CutsceneLook, over the flood's second half)     // HDR brightness of the void's white (0 when unset reads as 1)

// The void's white: above the gameplay tonemapper's white level, so it reads pure white after post.
half CutsceneVoidWhite()
{
    return (half)max(_CutsceneVoidWhite, 1.0);
}

// Horizontal distance from the centre plus `rise` × height above it.
float CutsceneVoidDistance(float3 positionWS, float rise)
{
    float3 c = _CutsceneVoidCentre.xyz;
    return length(positionWS.xz - c.xz) + rise * max(0.0, positionWS.y - c.y);
}

// 1 = still white (outside the flood), 0 = revealed.
half CutsceneVoidAmount(float d)
{
    float e = max(_CutsceneVoidParams.y, 1e-3);
    return (half)(_CutsceneVoidParams.z * smoothstep(_CutsceneVoidParams.x - e, _CutsceneVoidParams.x, d));
}

// The gold front: 0 outside the band, peaking in its middle.
half CutsceneVoidFront(float d)
{
    float e = max(_CutsceneVoidParams.y, 1e-3);
    float t = saturate((d - (_CutsceneVoidParams.x - e)) / e);
    return (half)(_CutsceneVoidParams.z * sin(t * 3.14159265));
}

// Lit colour → flood front tint → white void.
half3 ApplyCutsceneVoid(half3 colour, float3 positionWS)
{
    if (_CutsceneVoidParams.z <= 0.0) return colour;
    float d = CutsceneVoidDistance(positionWS, _CutsceneVoidParams.w);
    colour = lerp(colour, _CutsceneVoidEdgeColour.rgb, CutsceneVoidFront(d) * _CutsceneVoidEdgeColour.a);
    return lerp(colour, CutsceneVoidWhite().xxx, CutsceneVoidAmount(d));
}

#endif
