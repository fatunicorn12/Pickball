using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// The automated framing rules from the shot list, as pure functions the validator applies to
    /// every sample and the tests pin:
    /// (a) a subject anchor inside the central safe frame;
    /// (b) Birdy's TCU fraction outside the allowed windows;
    /// (c) the lens inside, or within the near clip of, a character's bounds;
    /// (d) a camera pose against a target pose (the MATCH into gameplay).
    /// </summary>
    public static class FramingChecks
    {
        /// <summary>
        /// (a) True when a viewport point (x, y from <see cref="CameraMath.Viewport"/>, z depth) is in
        /// front of the lens and inside the central <paramref name="safe"/> share of the frame, shrunk
        /// on every side by <paramref name="margin"/> (viewport units, for noise).
        /// </summary>
        public static bool InSafeFrame(Vector3 viewport, float safe, float margin = 0f)
        {
            if (viewport.z <= 0f) return false;
            float lo = 0.5f * (1f - safe) + margin;
            float hi = 1f - lo;
            return viewport.x >= lo && viewport.x <= hi && viewport.y >= lo && viewport.y <= hi;
        }

        /// <summary>True when a viewport point is in front of the lens and inside the frame.</summary>
        public static bool InFrame(Vector3 viewport) =>
            viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;

        /// <summary>
        /// (b) True when a Birdy framing breaks the TCU rule: his head box fills at least
        /// <paramref name="threshold"/> of the frame (frac ≥ 0.30) with his eyes in frame, outside an
        /// allowed window. An INSERT that keeps his eyes out of frame is not a close-up.
        /// </summary>
        public static bool TcuViolation(float frac, bool eyesInFrame, bool allowed, float threshold)
        {
            return frac >= threshold && eyesInFrame && !allowed;
        }

        /// <summary>(c) True when the lens is inside <paramref name="bounds"/> or within <paramref name="nearClip"/> of it.</summary>
        public static bool LensTooClose(Bounds bounds, Vector3 lens, float nearClip)
        {
            return bounds.SqrDistance(lens) <= nearClip * nearClip;
        }

        /// <summary>(d) Position (metres), rotation (degrees) and FOV (degrees) differences between two poses.</summary>
        public static Vector3 PoseDelta(Pose a, float fovA, Pose b, float fovB)
        {
            return new Vector3(Vector3.Distance(a.position, b.position), Quaternion.Angle(a.rotation, b.rotation),
                Mathf.Abs(fovA - fovB));
        }

        /// <summary>(d) True when a pose delta is within an invisible cut: 1 cm, 0.1°, the same FOV.</summary>
        public static bool IsInvisibleCut(Vector3 delta, float metres = 0.01f, float degrees = 0.1f, float fov = 0.01f)
        {
            return delta.x <= metres && delta.y <= degrees && delta.z <= fov;
        }

        /// <summary>
        /// How many steps of a camera's per-sample movement stutter: longer than
        /// <paramref name="minStep"/> metres and <paramref name="ratio"/> times the median of the
        /// steps within <paramref name="halfWindow"/> samples each side. A smooth move changes step
        /// gradually and never trips this, however fast it goes.
        /// </summary>
        public static int CountStutters(IReadOnlyList<float> steps, float minStep, float ratio, int halfWindow)
        {
            int count = 0;
            var window = new List<float>(halfWindow * 2 + 1);
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] <= minStep) continue;
                window.Clear();
                int last = Mathf.Min(steps.Count - 1, i + halfWindow);
                for (int k = Mathf.Max(0, i - halfWindow); k <= last; k++) window.Add(steps[k]);
                window.Sort();
                if (steps[i] > window[window.Count / 2] * ratio) count++;
            }
            return count;
        }

        /// <summary>
        /// The viewport distance an orientation wobble of <paramref name="degrees"/> can move a point
        /// near the frame edge, for a vertical FOV (conservative: uses the edge's tangent slope).
        /// </summary>
        public static float NoiseMargin(float degrees, float verticalFov)
        {
            if (degrees <= 0f) return 0f;
            float half = verticalFov * 0.5f * Mathf.Deg2Rad;
            float tanHalf = Mathf.Tan(half);
            // d(viewport)/d(angle) at the edge = sec²(half) / (2 tan(half)).
            float slope = 1f / (Mathf.Cos(half) * Mathf.Cos(half)) / (2f * tanHalf);
            return slope * degrees * Mathf.Deg2Rad;
        }

        /// <summary>
        /// Degrees a direction lies outside a frame (negative when inside): the larger of the
        /// horizontal and vertical angular overshoots past the frame edges. The black-sun rule wants
        /// at least 5° outside in scene 5.
        /// </summary>
        public static float DegreesOutsideFrame(Vector3 cameraPosition, Quaternion cameraRotation, float verticalFov,
            float aspect, Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(cameraRotation) * (point - cameraPosition);
            if (local.z <= 0f) return 90f;
            float halfV = verticalFov * 0.5f;
            float halfH = Mathf.Atan(Mathf.Tan(halfV * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
            float h = Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg) - halfH;
            float v = Mathf.Abs(Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg) - halfV;
            return Mathf.Max(h, v);
        }
    }
}
