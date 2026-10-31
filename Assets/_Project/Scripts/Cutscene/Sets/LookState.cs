using UnityEngine;

namespace Pickleball.Cutscene.Sets
{
    /// <summary>
    /// One complete scene look: fog, trilight ambient and the sun. A plain value so the blend
    /// between two looks is a pure function (<see cref="LookBlend"/>) that tests can check
    /// without a scene, and so <see cref="CutsceneLook"/> can build one every frame without
    /// allocating.
    /// </summary>
    public readonly struct LookState
    {
        /// <summary>RenderSettings.fogMode.</summary>
        public readonly FogMode FogMode;
        /// <summary>RenderSettings.fogColor.</summary>
        public readonly Color FogColor;
        /// <summary>RenderSettings.fogDensity (exponential modes).</summary>
        public readonly float FogDensity;
        /// <summary>RenderSettings.fogStartDistance (linear mode), metres.</summary>
        public readonly float FogStart;
        /// <summary>RenderSettings.fogEndDistance (linear mode), metres.</summary>
        public readonly float FogEnd;
        /// <summary>
        /// The exponential density that looks closest to this look's fog. Only used while
        /// blending from an exponential look into a linear one, which RenderSettings cannot mix.
        /// </summary>
        public readonly float FogBlendDensity;
        /// <summary>Trilight ambient, sky (top) colour.</summary>
        public readonly Color AmbientSky;
        /// <summary>Trilight ambient, equator colour.</summary>
        public readonly Color AmbientEquator;
        /// <summary>Trilight ambient, ground (bottom) colour.</summary>
        public readonly Color AmbientGround;
        /// <summary>Sun (directional light) intensity.</summary>
        public readonly float SunIntensity;
        /// <summary>Sun colour.</summary>
        public readonly Color SunColor;
        /// <summary>Sun shadow strength, 0..1.</summary>
        public readonly float SunShadowStrength;

        /// <summary>Builds a look from every field.</summary>
        public LookState(FogMode fogMode, Color fogColor, float fogDensity, float fogStart, float fogEnd,
                         float fogBlendDensity, Color ambientSky, Color ambientEquator, Color ambientGround,
                         float sunIntensity, Color sunColor, float sunShadowStrength)
        {
            FogMode = fogMode;
            FogColor = fogColor;
            FogDensity = fogDensity;
            FogStart = fogStart;
            FogEnd = fogEnd;
            FogBlendDensity = fogBlendDensity;
            AmbientSky = ambientSky;
            AmbientEquator = ambientEquator;
            AmbientGround = ambientGround;
            SunIntensity = sunIntensity;
            SunColor = sunColor;
            SunShadowStrength = sunShadowStrength;
        }
    }
}
