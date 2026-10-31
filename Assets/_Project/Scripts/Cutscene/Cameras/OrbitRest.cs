using System;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// The gameplay orbit camera's settings that decide where it comes to rest behind the player,
    /// read from <see cref="SandboxOrbitCamera"/> (never hard-coded), and the pose they give: the
    /// shot list's pose G. The maths is the orbit camera's own <c>LateUpdate</c> at rest: focus =
    /// target + targetOffset, rotation = Euler(pitch, yaw, 0), position = focus − forward · distance,
    /// then its safe-band correction (which keeps the feet above <c>safeBandMin</c> of the frame and
    /// at rest moves the camera exactly onto the band's edge). The pitch is <c>lockPitch</c>: the
    /// pitch <c>RecenterBehind</c> swings to, which is what the hand-back calls.
    /// </summary>
    [Serializable]
    public struct OrbitRest
    {
        /// <summary>Focus offset above the target, metres (SandboxOrbitCamera.targetOffset).</summary>
        public Vector3 TargetOffset;

        /// <summary>Distance from the focus, metres (SandboxOrbitCamera.distance).</summary>
        public float Distance;

        /// <summary>Resting pitch after RecenterBehind, degrees down (SandboxOrbitCamera.lockPitch).</summary>
        public float Pitch;

        /// <summary>Whether the safe band is on (SandboxOrbitCamera.safeBandEnabled).</summary>
        public bool SafeBand;

        /// <summary>Lowest viewport height the feet may sit at (SandboxOrbitCamera.safeBandMin).</summary>
        public float SafeBandMin;

        /// <summary>Highest viewport height the focus may sit at (SandboxOrbitCamera.safeBandMax).</summary>
        public float SafeBandMax;

        /// <summary>The orbit camera's defaults (its field initialisers), for tests and a missing camera.</summary>
        public static OrbitRest Defaults => new OrbitRest
        {
            TargetOffset = new Vector3(0f, 1.35f, 0f),
            Distance = 4f,
            Pitch = 16f,
            SafeBand = true,
            SafeBandMin = 0.15f,
            SafeBandMax = 0.75f,
        };

        /// <summary>The yaw RecenterBehind derives from a forward vector (Atan2(x, z)), degrees.</summary>
        public static float YawBehind(Vector3 forward) => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

        /// <summary>
        /// The resting pose for the target at <paramref name="targetPosition"/>, yaw <paramref name="yaw"/>,
        /// at <paramref name="distanceScale"/> × the distance, with vertical FOV <paramref name="fov"/>.
        /// </summary>
        public Pose Solve(Vector3 targetPosition, float yaw, float fov, float distanceScale = 1f)
        {
            Vector3 focus = targetPosition + TargetOffset;
            Quaternion rot = Quaternion.Euler(Pitch, yaw, 0f);
            Vector3 position = focus - rot * Vector3.forward * (Distance * distanceScale);
            if (SafeBand) position += rot * Vector3.up * BandShift(position, rot, targetPosition, focus, fov);
            return new Pose(position, rot);
        }

        /// <summary>SandboxOrbitCamera.SafeBandCorrection's shift along the camera's up, metres.</summary>
        public float BandShift(Vector3 position, Quaternion rot, Vector3 feet, Vector3 head, float fov)
        {
            float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            Quaternion inv = Quaternion.Inverse(rot);
            Vector3 f = inv * (feet - position);
            Vector3 h = inv * (head - position);
            float feetY = f.z > 1e-3f ? 0.5f + 0.5f * f.y / (f.z * tanHalf) : 0.5f;
            float headY = h.z > 1e-3f ? 0.5f + 0.5f * h.y / (h.z * tanHalf) : 0.5f;
            float shift = CameraOcclusion.SafeBandShift(feetY, f.z, tanHalf, SafeBandMin, 1f);
            if (shift == 0f) shift = CameraOcclusion.SafeBandShift(headY, h.z, tanHalf, 0f, SafeBandMax);
            return shift;
        }
    }
}
