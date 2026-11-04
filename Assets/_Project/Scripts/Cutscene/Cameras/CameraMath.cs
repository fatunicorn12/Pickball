using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// The small camera formulas the shot list is written in, as pure functions: easing, yaw,
    /// projection to the viewport, the lens-to-focal-length rule and Birdy's TCU fraction. Shared
    /// by the rigs, the framing validator and the tests so all three agree to the last digit.
    /// </summary>
    public static class CameraMath
    {
        /// <summary>Sensor height the shot list's focal lengths assume, millimetres.</summary>
        public const float SensorHeightMm = 24f;

        /// <summary>Eased progress for a raw 0..1 progress (clamped).</summary>
        public static float Ease(EaseKind ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case EaseKind.InOut: return t * t * (3f - 2f * t);
                case EaseKind.In: return t * t;
                case EaseKind.Out: return 1f - (1f - t) * (1f - t);
                default: return t;
            }
        }

        /// <summary>Eased progress of <paramref name="t"/> through [<paramref name="from"/>, <paramref name="to"/>].</summary>
        public static float Progress(float t, float from, float to, EaseKind ease)
        {
            if (to <= from + 1e-6f) return t >= to ? 1f : 0f;
            return Ease(ease, (t - from) / (to - from));
        }

        /// <summary>
        /// How far along the straight line from <paramref name="a"/> to <paramref name="b"/> the point
        /// nearest <paramref name="p"/> lies, 0..1. Exact, so a dolly driven by it moves as smoothly as
        /// what it tracks.
        /// </summary>
        public static float SegmentProgress(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            return lengthSquared > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSquared) : 0f;
        }

        /// <summary>
        /// How far a camera moving at <paramref name="speed"/> has travelled
        /// <paramref name="elapsed"/> seconds into a steady slowdown lasting
        /// <paramref name="easeSeconds"/>. Its speed starts unchanged and reaches 0 at the end.
        /// </summary>
        public static float StoppingDistance(float elapsed, float speed, float easeSeconds)
        {
            if (easeSeconds <= 0f) return 0f;
            float t = Mathf.Clamp(elapsed, 0f, easeSeconds);
            return speed * (t - t * t / (2f * easeSeconds));
        }

        /// <summary>Yaw in degrees of a direction, flattened (0 = +Z, 90 = +X); 0 for vertical.</summary>
        public static float YawOf(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude < 1e-10f ? 0f : Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        /// <summary>The horizontal unit vector for a yaw.</summary>
        public static Vector3 YawDirection(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        /// <summary>
        /// Projects a point for a pinhole camera: x and y are viewport (0..1 from the bottom-left),
        /// z is the depth along the camera's forward (negative behind it).
        /// </summary>
        public static Vector3 Viewport(Vector3 cameraPosition, Quaternion cameraRotation, float verticalFov, float aspect,
            Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(cameraRotation) * (point - cameraPosition);
            float tanV = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * aspect;
            if (local.z <= 1e-5f) return new Vector3(local.x >= 0f ? 2f : -1f, local.y >= 0f ? 2f : -1f, local.z);
            return new Vector3(0.5f + 0.5f * local.x / (local.z * tanH), 0.5f + 0.5f * local.y / (local.z * tanV), local.z);
        }

        /// <summary>The shot list's focal length for a vertical FOV: f = 12 / tan(vFOV/2), millimetres.</summary>
        public static float FocalLengthMm(float verticalFov) =>
            SensorHeightMm * 0.5f / Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);

        /// <summary>
        /// Birdy's TCU fraction: HB / (2 · d · tan(vFOV/2)), his head box's share of the frame height
        /// at depth <paramref name="depth"/> along the camera's forward. Infinite at or behind the lens.
        /// </summary>
        public static float TcuFraction(float headBox, float depth, float verticalFov)
        {
            if (depth <= 1e-5f) return float.PositiveInfinity;
            return headBox / (2f * depth * Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad));
        }

        /// <summary>The depth at which Birdy's head box fills <paramref name="fraction"/> of the frame height.</summary>
        public static float DepthForFraction(float headBox, float fraction, float verticalFov) =>
            headBox / (2f * Mathf.Max(1e-4f, fraction) * Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad));

        /// <summary>
        /// The rotation that puts <paramref name="target"/> at viewport <paramref name="screen"/> for a
        /// camera at <paramref name="position"/> with no roll (the composer's solution with damping
        /// off). Used for the top-down guard and tests; the live shots use Cinemachine's composer.
        /// </summary>
        public static Quaternion Compose(Vector3 position, Vector3 target, Vector2 screen, float verticalFov, float aspect)
        {
            Vector3 dir = target - position;
            if (dir.sqrMagnitude < 1e-10f) return Quaternion.identity;
            float tanV = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * aspect;
            // The target's direction in camera space when it sits at the screen point.
            Vector3 local = new Vector3((screen.x - 0.5f) * 2f * tanH, (screen.y - 0.5f) * 2f * tanV, 1f).normalized;
            float yawOff = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitchOff = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            // Solve pitch first (iteratively, the coupling is weak), then yaw.
            float yaw = YawOf(dir);
            float flat = new Vector2(dir.x, dir.z).magnitude;
            float elev = Mathf.Atan2(dir.y, flat) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.Euler(-(elev - pitchOff), yaw - yawOff, 0f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = Viewport(position, rot, verticalFov, aspect, target);
                float dx = (screen.x - v.x) * 2f * tanH;
                float dy = (screen.y - v.y) * 2f * tanV;
                rot = Quaternion.Euler(rot.eulerAngles.x + Mathf.Atan(dy) * Mathf.Rad2Deg,
                    rot.eulerAngles.y - Mathf.Atan(dx) * Mathf.Rad2Deg, 0f);
            }
            return rot;
        }
    }
}
