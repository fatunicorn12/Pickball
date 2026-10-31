using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Subtitles: one <see cref="SubtitleClip"/> per card, bound to the scene's
    /// <see cref="SubtitleRelay"/>. The pipeline never touches UI; the subtitle view listens to the
    /// relay.
    /// </summary>
    [TrackColor(0.95f, 0.85f, 0.35f)]
    [TrackClipType(typeof(SubtitleClip))]
    [TrackBindingType(typeof(SubtitleRelay))]
    public sealed class SubtitleTrack : TrackAsset
    {
        /// <inheritdoc />
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<SubtitleMixerBehaviour>.Create(graph, inputCount);
    }
}
