using UnityEngine;

namespace Pickleball.Rendering
{
    /// <summary>
    /// Every tunable for the screen outline, in one asset, so the whole game shares one look and it
    /// can be changed live in Play mode. The pass reads these values each frame.
    /// </summary>
    [CreateAssetMenu(fileName = "OutlineSettings", menuName = "Pickleball/Rendering/Outline Settings")]
    public sealed class OutlineSettings : ScriptableObject
    {
        [Header("Shader")]
        [Tooltip("The outline shader. Referenced here, not found by name, so that builds include it.")]
        [SerializeField] private Shader _shader;

        [Header("Line")]
        [Tooltip("Line colour. Alpha below 1 lets the surface show through the line.")]
        [SerializeField] private Color _color = Color.black;

        [Tooltip("Whole line width in pixels on a 1080p screen; scales with screen height. Higher looks more " +
                 "like thick cartoon inking, but above about 8 small details such as hair start to clog.")]
        [Range(1f, 16f)]
        [SerializeField] private float _widthAt1080p = 6f;

        [Header("What counts as an edge")]
        [Tooltip("Metres. A step in a surface smaller than this draws no line. Lower finds more small " +
                 "details such as folds and steps; too low and curved surfaces get stray lines.")]
        [Min(0.001f)]
        [SerializeField] private float _depthThreshold = 0.25f;

        [Tooltip("Metres added to the depth threshold per metre from the camera, because a pixel covers " +
                 "more of the world further away. Raise it if distant objects fill with lines.")]
        [Min(0f)]
        [SerializeField] private float _depthThresholdPerMetre = 0.02f;

        [Tooltip("0 to 2: how sharply a surface must turn to draw a line (1 - cos of the angle: 0.3 is " +
                 "45°, 1 is 90°). Lower inks soft creases; raise it to clear lines from faceted terrain.")]
        [Range(0.01f, 2f)]
        [SerializeField] private float _normalThreshold = 0.9f;

        [Header("Distance")]
        [Tooltip("Metres from the camera where lines start to fade. Lines don't get thinner with distance, " +
                 "so far-off detail would otherwise turn to black mush.")]
        [Min(0f)]
        [SerializeField] private float _fadeStart = 45f;

        [Tooltip("Metres from the camera where lines have gone completely.")]
        [Min(0f)]
        [SerializeField] private float _fadeEnd = 110f;

        /// <summary>The outline shader asset; null means the outline cannot draw.</summary>
        public Shader Shader => _shader;

        /// <summary>Line colour, with alpha.</summary>
        public Color Color => _color;

        /// <summary>Whole line width, in pixels on a 1080p screen.</summary>
        public float WidthAt1080p => _widthAt1080p;

        /// <summary>The smallest surface step that draws a line, in metres.</summary>
        public float DepthThreshold => _depthThreshold;

        /// <summary>Extra depth threshold per metre from the camera.</summary>
        public float DepthThresholdPerMetre => _depthThresholdPerMetre;

        /// <summary>How sharply a surface must turn to draw a line, as 1 - cos(angle).</summary>
        public float NormalThreshold => _normalThreshold;

        /// <summary>Where lines start to fade, in metres from the camera.</summary>
        public float FadeStart => _fadeStart;

        /// <summary>Where lines have gone completely, in metres from the camera.</summary>
        public float FadeEnd => _fadeEnd;
    }
}
