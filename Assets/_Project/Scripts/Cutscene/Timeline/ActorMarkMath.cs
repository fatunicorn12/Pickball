using UnityEngine;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// The blocking maths: resolve a marker-relative pose to the world, and ease between two poses.
    /// Actors stand upright, so only the frame's yaw is used (a tilted marker never tilts an actor
    /// or its offsets). Pure functions, tested in EditMode.
    /// </summary>
    public static class ActorMarkMath
    {
        /// <summary>Yaw in degrees of a rotation's forward, flattened. 0 for straight up or down.</summary>
        public static float YawOf(Quaternion rotation)
        {
            Vector3 f = rotation * Vector3.forward;
            f.y = 0f;
            return f.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The world pose for an offset (metres, in the frame's yawed axes: x right, y up, z
        /// forward) from a frame, facing either <paramref name="facePosition"/> (when
        /// <paramref name="hasFace"/>) or the frame's yaw plus <paramref name="yawDegrees"/>.
        /// </summary>
        public static Pose Resolve(Vector3 framePosition, Quaternion frameRotation, Vector3 offset, float yawDegrees,
            bool hasFace, Vector3 facePosition)
        {
            Quaternion flat = Quaternion.Euler(0f, YawOf(frameRotation), 0f);
            Vector3 position = framePosition + flat * offset;
            Quaternion rotation = flat * Quaternion.Euler(0f, yawDegrees, 0f);
            if (hasFace)
            {
                Vector3 d = facePosition - position;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-6f) rotation = Quaternion.LookRotation(d, Vector3.up);
            }
            return new Pose(position, rotation);
        }

        /// <summary>Eased progress for a raw 0..1 progress.</summary>
        public static float Ease(MarkEase ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case MarkEase.EaseInOut: return t * t * (3f - 2f * t);
                case MarkEase.EaseIn: return t * t;
                case MarkEase.EaseOut: return 1f - (1f - t) * (1f - t);
                default: return t;
            }
        }

        /// <summary>The pose <paramref name="t"/> (0..1, clamped) of the way from a to b, eased.</summary>
        public static Pose Interpolate(Pose a, Pose b, float t, MarkEase ease)
        {
            float k = Ease(ease, t);
            return new Pose(Vector3.LerpUnclamped(a.position, b.position, k),
                Quaternion.Slerp(a.rotation, b.rotation, k));
        }
    }
}
