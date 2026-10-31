using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// One blocking segment: the actor goes from <see cref="From"/> to <see cref="To"/> over the
    /// clip (a hold when they are equal). The builder lays these end to end, so the pose at any time
    /// is a pure function of the clip under the playhead; scrubbing and skipping need no history.
    /// </summary>
    [Serializable]
    public sealed class ActorMarkClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private MarkPoseRef from;
        [SerializeField] private MarkPoseRef to;

        [Tooltip("How the actor travels between the two poses.")]
        [SerializeField] private MarkEase ease = MarkEase.EaseInOut;

        /// <summary>Start pose.</summary>
        public MarkPoseRef From => from;

        /// <summary>End pose.</summary>
        public MarkPoseRef To => to;

        /// <summary>Easing.</summary>
        public MarkEase Ease => ease;

        /// <inheritdoc />
        public ClipCaps clipCaps => ClipCaps.None;

        /// <summary>Builder entry point.</summary>
        public void Configure(MarkPoseRef fromPose, MarkPoseRef toPose, MarkEase markEase)
        {
            from = fromPose;
            to = toPose;
            ease = markEase;
        }

        /// <inheritdoc />
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ActorMarkBehaviour>.Create(graph);
            var b = playable.GetBehaviour();
            var resolver = graph.GetResolver();
            b.FromFrame = from.frame.Resolve(resolver);
            b.FromFace = from.face.Resolve(resolver);
            b.FromOffset = from.offset;
            b.FromYaw = from.yaw;
            b.FromPitch = from.pitch;
            b.ToFrame = to.frame.Resolve(resolver);
            b.ToFace = to.face.Resolve(resolver);
            b.ToOffset = to.offset;
            b.ToYaw = to.yaw;
            b.ToPitch = to.pitch;
            b.Ease = ease;
            return playable;
        }
    }
}
