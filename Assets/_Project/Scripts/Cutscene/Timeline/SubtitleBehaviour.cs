using UnityEngine.Playables;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Runtime half of a <see cref="SubtitleClip"/>: the card, built once when the graph is created
    /// so the mixer only passes a cached struct to the relay each frame.
    /// </summary>
    public sealed class SubtitleBehaviour : PlayableBehaviour
    {
        /// <summary>The card this clip shows.</summary>
        public SubtitleCue Cue;
    }
}
