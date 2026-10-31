using UnityEngine;

namespace Pickleball.Cutscene.Sets
{
    /// <summary>
    /// A named scene look (the white void, golden hour) as an asset, so the lighting of each state
    /// is tuned in one place and <see cref="CutsceneLook"/> blends between two of them. The
    /// GoldenHour profile must equal the gameplay lighting (AcademyGreyboxSetup.ApplyLighting and
    /// the scene's sun), because the end of the resolve hands straight over to gameplay.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Look Profile", fileName = "LookProfile")]
    public sealed class LookProfile : ScriptableObject
    {
        [Header("Fog")]
        [Tooltip("Fog mode. The void uses Exponential (a soft white falloff with no horizon); " +
                 "gameplay uses Linear (clear near, haze far).")]
        [SerializeField] private FogMode fogMode = FogMode.Linear;

        [Tooltip("Fog colour. White in the void; warm haze at golden hour.")]
        [SerializeField] private Color fogColor = Color.white;

        [Tooltip("Exponential fog density, per metre. Higher swallows the world closer to the camera; " +
                 "0.06 leaves a figure 3 m away clear and everything past 40 m white.")]
        [Min(0f)] [SerializeField] private float fogDensity = 0.01f;

        [Tooltip("Linear fog start, metres from the camera. Nothing nearer is fogged.")]
        [Min(0f)] [SerializeField] private float fogStart = 300f;

        [Tooltip("Linear fog end, metres from the camera. Everything farther is fully fog-coloured.")]
        [Min(0f)] [SerializeField] private float fogEnd = 2600f;

        [Tooltip("Exponential density that looks like this profile's fog, per metre. Used only while " +
                 "blending from an exponential profile into a linear one (RenderSettings can't mix modes); " +
                 "lower means the haze is already thin just before the end of the resolve.")]
        [Min(0f)] [SerializeField] private float fogBlendDensity = 0.00035f;

        [Header("Ambient (trilight)")]
        [Tooltip("Ambient light from above. In the void, white: it is MC's soft top fill.")]
        [SerializeField] private Color ambientSky = Color.white;

        [Tooltip("Ambient light from the horizon.")]
        [SerializeField] private Color ambientEquator = Color.white;

        [Tooltip("Ambient light from below. Slightly darker than the sky reads as light from above.")]
        [SerializeField] private Color ambientGround = Color.white;

        [Header("Sun")]
        [Tooltip("Sun intensity (unitless, as on the Light). 0 turns the sun off: no key, no shadows.")]
        [Min(0f)] [SerializeField] private float sunIntensity = 1.35f;

        [Tooltip("Sun colour.")]
        [SerializeField] private Color sunColor = new(1f, 0.88f, 0.72f);

        [Tooltip("Sun shadow strength, 0..1. 0 is shadowless.")]
        [Range(0f, 1f)] [SerializeField] private float sunShadowStrength = 1f;

        /// <summary>This profile as a value, for blending. Builds a struct; no allocation.</summary>
        public LookState State => new(fogMode, fogColor, fogDensity, fogStart, fogEnd, fogBlendDensity,
                                      ambientSky, ambientEquator, ambientGround,
                                      sunIntensity, sunColor, sunShadowStrength);

        /// <summary>Setup-menu entry point: overwrite every field from a state.</summary>
        public void Configure(in LookState state)
        {
            fogMode = state.FogMode;
            fogColor = state.FogColor;
            fogDensity = state.FogDensity;
            fogStart = state.FogStart;
            fogEnd = state.FogEnd;
            fogBlendDensity = state.FogBlendDensity;
            ambientSky = state.AmbientSky;
            ambientEquator = state.AmbientEquator;
            ambientGround = state.AmbientGround;
            sunIntensity = state.SunIntensity;
            sunColor = state.SunColor;
            sunShadowStrength = state.SunShadowStrength;
        }
    }
}
