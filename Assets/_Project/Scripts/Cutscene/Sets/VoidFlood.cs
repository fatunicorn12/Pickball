using UnityEngine;

namespace Pickleball.Cutscene.Sets
{
    /// <summary>
    /// The radial colour flood that dissolves the white void outward from MC_Start, shared by
    /// the C# that drives it and the shaders that draw it (Shaders/Cutscene/CutsceneVoid.hlsl).
    ///
    /// <see cref="CutsceneLook"/> publishes the flood as global shader values, so every set's
    /// painterly material, the void dome and the void disc reveal in step without referencing
    /// each other. A pixel at flood distance d (horizontal distance from the centre, plus a
    /// rise weight times its height above it) is white while d is beyond the flood radius and
    /// revealed once the radius passes it, with a gold front in between. When the globals are
    /// unset (no CutsceneLook in the scene) the strength is 0 and nothing is whitened.
    /// </summary>
    public static class VoidFlood
    {
        /// <summary>Global float4: the flood centre (xyz), world space.</summary>
        public static readonly int CentreId = Shader.PropertyToID("_CutsceneVoidCentre");

        /// <summary>Global float4: x = flood radius (m), y = front width (m), z = strength
        /// (1 while the void is up, 0 once resolved), w = rise weight for set geometry.</summary>
        public static readonly int ParamsId = Shader.PropertyToID("_CutsceneVoidParams");

        /// <summary>Global colour of the flood's front.</summary>
        public static readonly int EdgeColourId = Shader.PropertyToID("_CutsceneVoidEdgeColour");

        /// <summary>
        /// Global shader float: the HDR brightness of the void's white (<see cref="ResolveConfig.VoidWhite"/>).
        /// The gameplay tonemapper maps 1.0 to light grey, so the void writes white above the
        /// tonemapper's white level instead. Unset (0) reads as 1.
        /// </summary>
        public static readonly int WhiteId = Shader.PropertyToID("_CutsceneVoidWhite");

        /// <summary>Global shader float: how far the void dome has faded, 0..1 (see <see cref="SkyFade"/>).</summary>
        public static readonly int SkyId = Shader.PropertyToID("_CutsceneVoidSky");

        /// <summary>
        /// The dome's cross-fade to the real sky over the whole flood (S1_SH07 4.0–6.0 s): a cubic
        /// ease-out, so the blue starts with the colour and is mostly in by the middle of the flood. A
        /// smoothstep left the sky white until about 1.2 s into the flood in the Play recording
        /// (review round 3).
        /// </summary>
        public static float SkyFade(float blend)
        {
            float u = 1f - Mathf.Clamp01(blend);
            return 1f - u * u * u;
        }

        /// <summary>
        /// The flood radius at a look blend. -front at blend 0 (everything white, the centre
        /// included) rising to <paramref name="maxRadius"/> at blend 1, along blend^exponent so the
        /// flood starts slowly at MC's feet and accelerates outward.
        /// </summary>
        public static float Radius(float blend, float maxRadius, float exponent, float front)
        {
            float start = -Mathf.Max(front, 0f);
            if (blend <= 0f) return start;
            if (blend >= 1f) return maxRadius;
            return Mathf.Lerp(start, maxRadius, Mathf.Pow(blend, Mathf.Max(exponent, 0.01f)));
        }

        /// <summary>
        /// How white a point at flood distance <paramref name="distance"/> is (1 = void, 0 =
        /// revealed). The C# mirror of CutsceneVoidAmount in CutsceneVoid.hlsl, for tests and
        /// tooling.
        /// </summary>
        public static float Whiteness(float distance, float radius, float front, float strength)
        {
            float e = Mathf.Max(front, 0.001f);
            float t = Mathf.Clamp01((distance - (radius - e)) / e);
            return strength * t * t * (3f - 2f * t);
        }

        /// <summary>A point's flood distance from the centre: horizontal distance plus
        /// <paramref name="rise"/> times its height above the centre.</summary>
        public static float Distance(Vector3 point, Vector3 centre, float rise)
        {
            float dx = point.x - centre.x, dz = point.z - centre.z;
            return Mathf.Sqrt(dx * dx + dz * dz) + rise * Mathf.Max(0f, point.y - centre.y);
        }
    }
}
