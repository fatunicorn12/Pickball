using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Blocking for a prop with no <see cref="CutsceneActor"/> slot (the propeller): the same clips
    /// and mixer as <see cref="ActorMarkTrack"/>, bound to a plain Transform. A clip framed on an
    /// actor anchor (MC's Hand_R, Birdy's PropellerSocket) makes the prop follow that anchor.
    /// </summary>
    [TrackColor(0.55f, 0.75f, 0.35f)]
    [TrackClipType(typeof(ActorMarkClip))]
    [TrackBindingType(typeof(Transform))]
    public sealed class PropMarkTrack : TrackAsset
    {
        /// <inheritdoc />
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<ActorMarkMixerBehaviour>.Create(graph, inputCount);
    }
}
