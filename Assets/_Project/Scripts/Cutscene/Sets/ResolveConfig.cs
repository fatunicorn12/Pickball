using UnityEngine;

namespace Pickleball.Cutscene.Sets
{
    /// <summary>
    /// Tuning for the void-to-shrine resolve (S1_SH07): when each kind of construction line draws,
    /// how faint the lines stay afterwards, and how the colour flood spreads. One asset drives
    /// both <see cref="ConstructionLineResolve"/> (lines, from its <c>progress</c>) and
    /// <see cref="CutsceneLook"/> (flood, from its <c>blend</c>).
    ///
    /// Timing is in progress units. With the shot list's timing, progress runs 0→1 from 1.0 s to
    /// 6.0 s of S1_SH07: the lines draw from 1.0 to 4.0 s (drawEnd 0.6) and fade to faint while the
    /// colour floods from 4.0 to 6.0 s (fadeStart 0.6).
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Resolve Config", fileName = "ResolveConfig")]
    public sealed class ResolveConfig : ScriptableObject
    {
        [Header("Drawing (progress units)")]
        [Tooltip("Progress at which every line has finished drawing, 0..1. Lower draws faster and " +
                 "leaves a longer hold on the finished drawing.")]
        [Range(0.05f, 1f)] [SerializeField] private float drawEnd = 0.6f;

        [Tooltip("Compass arcs' window inside the drawing phase (x start, y end; 0..1 of the drawing " +
                 "phase). Arcs lead, so start them at 0.")]
        [SerializeField] private Vector2 arcWindow = new(0f, 0.45f);

        [Tooltip("Column shafts' window inside the drawing phase (0..1). Overlapping the arcs makes the " +
                 "drawing feel like one gesture rather than a checklist.")]
        [SerializeField] private Vector2 columnWindow = new(0.2f, 0.8f);

        [Tooltip("Tick marks' window inside the drawing phase (0..1). The ticks pace round the ring one " +
                 "after another inside it.")]
        [SerializeField] private Vector2 tickWindow = new(0.3f, 0.85f);

        [Tooltip("The olive hill's profile curve window inside the drawing phase (0..1). It finishes " +
                 "last and leads the eye north, toward the path.")]
        [SerializeField] private Vector2 hillWindow = new(0.5f, 1f);

        [Tooltip("Share of its group's window each stroke takes, 0..1. 1 draws all strokes of a group " +
                 "together; lower makes them follow one another, like one pen.")]
        [Range(0.05f, 1f)] [SerializeField] private float strokeShare = 0.35f;

        [Header("Fade (progress units)")]
        [Tooltip("Progress at which the lines start dimming to their permanent faint level, 0..1.")]
        [Range(0f, 1f)] [SerializeField] private float fadeStart = 0.6f;

        [Tooltip("Line brightness left in gameplay, 0..1: the faint permanent construction lines. " +
                 "0 erases them; 0.5 keeps them bold.")]
        [Range(0f, 1f)] [SerializeField] private float faintAlpha = 0.22f;

        [Tooltip("Line colour (gold).")]
        [SerializeField] private Color lineColour = new(1f, 0.78f, 0.3f, 1f);

        [Header("Colour flood")]
        [Tooltip("Flood radius at the end of the resolve, metres of flood distance. Must reach past the " +
                 "void dome's highest flood distance (dome radius × its rise weight), or the dome pops " +
                 "off at the end instead of dissolving.")]
        [Min(1f)] [SerializeField] private float floodMaxRadius = 80f;

        [Tooltip("Shape of the flood's growth: radius ∝ blend^exponent. Above 1 the flood creeps from " +
                 "MC's feet and then races outward; 1 is a steady spread.")]
        [Range(0.2f, 4f)] [SerializeField] private float floodExponent = 1.5f;

        [Tooltip("Width of the flood's soft gold front, metres. Wider is a softer, slower-looking wipe.")]
        [Min(0.05f)] [SerializeField] private float floodFront = 3f;

        [Tooltip("How much height above MC_Start delays the flood, metres of flood distance per metre " +
                 "of height. 0.5 makes columns colour from the base up.")]
        [Min(0f)] [SerializeField] private float floodRise = 0.5f;

        [Tooltip("Colour of the flood's front (alpha = how strongly it tints).")]
        [SerializeField] private Color floodFrontColour = new(1f, 0.85f, 0.45f, 0.8f);

        [Header("Void white")]
        [Tooltip("HDR brightness of the void's white (the dome, the disc and every set surface beyond " +
                 "the flood), × linear white. The gameplay tonemapper (Neutral, from the pipeline's " +
                 "volume profile) maps 1.0 to about 0.8 grey and reaches pure white near 5.3, after the " +
                 "World Look vignette darkens the corners by up to ~22%. 1 = the old light-grey void; " +
                 "6.5 = pure white edge to edge. Bloom is held off by the Void's volume while the void is up.")]
        [Range(1f, 16f)] [SerializeField] private float voidWhite = 6.5f;

        /// <summary>Progress at which drawing completes.</summary>
        public float DrawEnd => drawEnd;
        /// <summary>Progress at which the lines start fading to faint.</summary>
        public float FadeStart => fadeStart;
        /// <summary>The permanent line brightness.</summary>
        public float FaintAlpha => faintAlpha;
        /// <summary>Share of its group's window one stroke takes.</summary>
        public float StrokeShare => strokeShare;
        /// <summary>Line colour.</summary>
        public Color LineColour => lineColour;
        /// <summary>Flood radius at blend 1, metres.</summary>
        public float FloodMaxRadius => floodMaxRadius;
        /// <summary>Flood growth exponent.</summary>
        public float FloodExponent => floodExponent;
        /// <summary>Flood front width, metres.</summary>
        public float FloodFront => floodFront;
        /// <summary>Flood rise weight for set geometry.</summary>
        public float FloodRise => floodRise;
        /// <summary>Flood front colour.</summary>
        public Color FloodFrontColour => floodFrontColour;

        /// <summary>HDR multiplier for the void's white (see the tooltip).</summary>
        public float VoidWhite => voidWhite;

        /// <summary>A group's window inside the drawing phase. No allocation.</summary>
        public Vector2 GroupWindow(StrokeGroup group)
        {
            switch (group)
            {
                case StrokeGroup.Arc: return arcWindow;
                case StrokeGroup.Column: return columnWindow;
                case StrokeGroup.Tick: return tickWindow;
                default: return hillWindow;
            }
        }
    }
}
