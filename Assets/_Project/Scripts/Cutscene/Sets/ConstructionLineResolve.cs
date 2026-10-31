using UnityEngine;

namespace Pickleball.Cutscene.Sets
{
    /// <summary>
    /// The gold construction lines that draw the Outer Shrine out of the white void (S1_SH07):
    /// compass arcs sweep the lawn circle, ruled lines rise into the column shafts, tick marks
    /// pace the ring, and one ruled curve draws the olive hill's profile. Then they fade to the
    /// faint permanent construction lines the Academy keeps everywhere (TheAcademy.md §2).
    ///
    /// <b>One float drives it:</b> <see cref="Progress"/>, 0..1 (Timeline animates the serialized
    /// <c>progress</c> field). <see cref="ResolveSchedule"/> turns it into each stroke's drawn
    /// fraction and the overall brightness, using the windows on <see cref="ResolveConfig"/>.
    ///
    /// <b>How it draws:</b> every stroke lives in one static line mesh built by the set menu (a
    /// camera-facing ribbon per segment; Shaders/Cutscene/ConstructionLine.shader). Each vertex
    /// knows its stroke index and how far along the stroke it is, so the shader hides whatever
    /// lies past that stroke's drawn fraction. Per frame this component only writes the
    /// preallocated fraction array and the brightness into a MaterialPropertyBlock: no mesh
    /// edits and no allocation. The array and block are rebuilt in OnEnable, so a script reload
    /// is safe.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ConstructionLineResolve : MonoBehaviour
    {
        /// <summary>Maximum strokes the line shader indexes (its _StrokeVisible array length).</summary>
        public const int MaxStrokes = 160;

        private static readonly int VisibleId = Shader.PropertyToID("_StrokeVisible");
        private static readonly int AlphaId = Shader.PropertyToID("_LineAlpha");
        private static readonly int ColourId = Shader.PropertyToID("_LineColor");

        [Header("Drive")]
        [Tooltip("0 = nothing drawn, drawEnd = every line drawn, 1 = lines faded to their permanent " +
                 "faint level. Animate this from Timeline.")]
        [Range(0f, 1f)] [SerializeField] private float progress = 1f;

        [Header("Wiring (set by the set menu)")]
        [Tooltip("Timing, brightness and colour.")]
        [SerializeField] private ResolveConfig config;

        [Tooltip("The renderer of the baked line mesh.")]
        [SerializeField] private Renderer lines;

        [Tooltip("Each stroke's group, by stroke index (the index baked into the line mesh).")]
        [SerializeField] private StrokeGroup[] strokeGroups = new StrokeGroup[0];

        [Tooltip("Each stroke's order inside its group, 0 = first .. 1 = last.")]
        [SerializeField] private float[] strokeOrders = new float[0];

        private float[] _visible;
        private MaterialPropertyBlock _block;

        /// <summary>The resolve progress, 0..1.</summary>
        public float Progress
        {
            get => progress;
            set => progress = Mathf.Clamp01(value);
        }

        /// <summary>Number of strokes in the baked mesh.</summary>
        public int StrokeCount => strokeGroups != null ? strokeGroups.Length : 0;

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(ResolveConfig resolveConfig, Renderer lineRenderer, StrokeGroup[] groups, float[] orders)
        {
            config = resolveConfig;
            lines = lineRenderer;
            strokeGroups = groups ?? new StrokeGroup[0];
            strokeOrders = orders ?? new float[0];
        }

        /// <summary>
        /// Stroke <paramref name="index"/>'s drawn fraction at <paramref name="atProgress"/>, the
        /// same value the shader receives. For tests and tooling.
        /// </summary>
        public float StrokeVisible(int index, float atProgress)
        {
            if (config == null || index < 0 || index >= StrokeCount) return 0f;
            float draw = ResolveSchedule.DrawPhase(atProgress, config.DrawEnd);
            float order = strokeOrders != null && index < strokeOrders.Length ? strokeOrders[index] : 0f;
            Vector2 window = ResolveSchedule.StrokeWindow(config.GroupWindow(strokeGroups[index]), order, config.StrokeShare);
            return ResolveSchedule.Visible(draw, window);
        }

        private void OnEnable()
        {
            _visible = new float[MaxStrokes];
            _block = new MaterialPropertyBlock();
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void OnDidApplyAnimationProperties()
        {
            Apply();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        /// <summary>Pushes the current progress to the line material. No allocation.</summary>
        public void Apply()
        {
            if (config == null || lines == null || _visible == null || _block == null) return;

            int count = Mathf.Min(StrokeCount, MaxStrokes);
            for (int i = 0; i < count; i++) _visible[i] = StrokeVisible(i, progress);
            for (int i = count; i < MaxStrokes; i++) _visible[i] = 0f;

            float alpha = progress <= 0f ? 0f : ResolveSchedule.Alpha(progress, config.FadeStart, config.FaintAlpha);
            _block.SetFloatArray(VisibleId, _visible);
            _block.SetFloat(AlphaId, alpha);
            _block.SetColor(ColourId, config.LineColour);
            lines.SetPropertyBlock(_block);
        }
    }
}
