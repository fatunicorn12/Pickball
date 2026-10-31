using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Held states of an actor or prop that are not poses: Birdy's rotor on or off his helmet and
    /// its spin, the loose propeller shown or hidden and its spin. Bound to the actor's or prop's
    /// GameObject. A custom track rather than an Animation track because Birdy's model Animator
    /// already carries his action clips, and a second Animation track on it would fight them.
    /// </summary>
    [TrackColor(0.85f, 0.65f, 0.25f)]
    [TrackClipType(typeof(ActorStateClip))]
    [TrackBindingType(typeof(GameObject))]
    public sealed class ActorStateTrack : TrackAsset
    {
        /// <inheritdoc />
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<ActorStateMixerBehaviour>.Create(graph, inputCount);
    }
}
