using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Swept contact tests for her projectiles, which move themselves without
    /// Rigidbodies (as Newton's apples and orbs do). A projectile reports where it
    /// was last frame and where it is now; testing that segment against the
    /// player's capsule cannot tunnel at any frame rate. Plain C#, EditMode-tested.
    /// </summary>
    public static class HypatiaContact
    {
        /// <summary>
        /// Squared distance between segments p1-q1 and p2-q2 (Ericson, Real-Time
        /// Collision Detection §5.1.9). Degenerate segments (points) are handled.
        /// </summary>
        public static float SegmentSegmentDistanceSq(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            const float eps = 1e-8f;

            if (a <= eps && e <= eps) return r.sqrMagnitude;
            if (a <= eps)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= eps)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }

            Vector3 c1 = p1 + d1 * s, c2 = p2 + d2 * t;
            return (c1 - c2).sqrMagnitude;
        }

        /// <summary>
        /// Did a sphere of <paramref name="radius"/> moving from
        /// <paramref name="from"/> to <paramref name="to"/> this frame touch a
        /// capsule whose axis runs <paramref name="capsuleBottom"/> to
        /// <paramref name="capsuleTop"/> (sphere centres, not tips)?
        /// </summary>
        public static bool SweptSphereHitsCapsule(Vector3 from, Vector3 to, float radius,
                                                  Vector3 capsuleBottom, Vector3 capsuleTop,
                                                  float capsuleRadius)
        {
            float reach = radius + capsuleRadius;
            return SegmentSegmentDistanceSq(from, to, capsuleBottom, capsuleTop) <= reach * reach;
        }
    }
}
