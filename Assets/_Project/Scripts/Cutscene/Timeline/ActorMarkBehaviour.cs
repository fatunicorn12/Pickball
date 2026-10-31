using UnityEngine;
using UnityEngine.Playables;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Runtime half of an <see cref="ActorMarkClip"/>: the resolved frame and face transforms,
    /// cached when the graph is built so the mixer does no lookups per frame.
    /// </summary>
    public sealed class ActorMarkBehaviour : PlayableBehaviour
    {
        /// <summary>Start frame.</summary>
        public Transform FromFrame;
        /// <summary>Start offset, metres.</summary>
        public Vector3 FromOffset;
        /// <summary>Start face target or null.</summary>
        public Transform FromFace;
        /// <summary>Start extra yaw, degrees.</summary>
        public float FromYaw;

        /// <summary>Tilt of the start pose, degrees.</summary>
        public float FromPitch;

        /// <summary>End frame.</summary>
        public Transform ToFrame;
        /// <summary>End offset, metres.</summary>
        public Vector3 ToOffset;
        /// <summary>End face target or null.</summary>
        public Transform ToFace;
        /// <summary>End extra yaw, degrees.</summary>
        public float ToYaw;

        /// <summary>Tilt of the end pose, degrees.</summary>
        public float ToPitch;

        /// <summary>Easing between them.</summary>
        public MarkEase Ease;

        /// <summary>
        /// The pose at <paramref name="t"/> (0..1). False when a frame is missing, so the mixer
        /// leaves the actor where it is rather than snapping it to the origin.
        /// </summary>
        public bool TryEvaluate(float t, out Pose pose)
        {
            if (FromFrame == null || ToFrame == null)
            {
                pose = default;
                return false;
            }
            Pose a = ActorMarkMath.Resolve(FromFrame.position, FromFrame.rotation, FromOffset, FromYaw,
                FromFace != null, FromFace != null ? FromFace.position : default);
            Pose b = ActorMarkMath.Resolve(ToFrame.position, ToFrame.rotation, ToOffset, ToYaw,
                ToFace != null, ToFace != null ? ToFace.position : default);
            if (FromPitch != 0f) a.rotation *= Quaternion.Euler(FromPitch, 0f, 0f);
            if (ToPitch != 0f) b.rotation *= Quaternion.Euler(ToPitch, 0f, 0f);
            pose = ActorMarkMath.Interpolate(a, b, t, Ease);
            return true;
        }
    }
}
