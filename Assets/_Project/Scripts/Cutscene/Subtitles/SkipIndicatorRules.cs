namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// When the hold-to-skip ring and its label show. Kept apart from
    /// <see cref="SkipIndicatorView"/> so the rules are tested without a canvas:
    /// the ring only appears once the player starts holding (an idle cutscene shows no UI), and
    /// the "hold to skip" hint only while the hold is in progress, not once it has completed.
    /// </summary>
    public static class SkipIndicatorRules
    {
        /// <summary>1 when the ring should be visible, else 0.</summary>
        /// <param name="playing">Whether a cutscene is playing.</param>
        /// <param name="progress">Hold-to-skip progress, 0..1.</param>
        public static float RingTarget(bool playing, float progress) =>
            playing && progress > 0f ? 1f : 0f;

        /// <summary>1 when the "hold to skip" label should be visible, else 0.</summary>
        /// <param name="playing">Whether a cutscene is playing.</param>
        /// <param name="progress">Hold-to-skip progress, 0..1.</param>
        public static float LabelTarget(bool playing, float progress) =>
            playing && progress > 0f && progress < 1f ? 1f : 0f;

        /// <summary>The ring's fill amount: progress clamped to 0..1, and 0 when nothing plays.</summary>
        /// <param name="playing">Whether a cutscene is playing.</param>
        /// <param name="progress">Hold-to-skip progress as reported.</param>
        public static float Fill(bool playing, float progress)
        {
            if (!playing || float.IsNaN(progress)) return 0f;
            return progress < 0f ? 0f : progress > 1f ? 1f : progress;
        }
    }
}
