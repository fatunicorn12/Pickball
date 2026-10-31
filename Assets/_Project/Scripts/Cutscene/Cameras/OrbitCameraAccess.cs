using System;
using System.Reflection;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// Reads <see cref="SandboxOrbitCamera"/>'s resting settings and sets its private view state
    /// (yaw, pitch, smoothing velocities) so the camera resumes gameplay on exactly the pose the
    /// last cutscene frame showed.
    /// <para>
    /// Why reflection: the orbit camera's yaw and pitch are private and only change through input,
    /// lock-on or <c>RecenterBehind</c>, which smooth-damps the yaw and stops within 0.5° and nudges
    /// the pitch by one damped step. A hand-back through it can't land within the 0.1° an invisible
    /// cut needs, and SandboxOrbitCamera.cs is not ours to change. The fields are looked up once by
    /// name; <see cref="IsSupported"/> is false (and the hand-back falls back to RecenterBehind) if a
    /// rename breaks the lookup, and an EditMode test pins every name. A public
    /// <c>SnapView(yaw, pitch)</c> on SandboxOrbitCamera would replace this.
    /// </para>
    /// </summary>
    public static class OrbitCameraAccess
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly Type T = typeof(SandboxOrbitCamera);

        static readonly FieldInfo TargetField = T.GetField("target", Flags);
        static readonly FieldInfo TargetOffsetField = T.GetField("targetOffset", Flags);
        static readonly FieldInfo DistanceField = T.GetField("distance", Flags);
        static readonly FieldInfo LockPitchField = T.GetField("lockPitch", Flags);
        static readonly FieldInfo SafeBandEnabledField = T.GetField("safeBandEnabled", Flags);
        static readonly FieldInfo SafeBandMinField = T.GetField("safeBandMin", Flags);
        static readonly FieldInfo SafeBandMaxField = T.GetField("safeBandMax", Flags);
        static readonly FieldInfo YawField = T.GetField("_yaw", Flags);
        static readonly FieldInfo PitchField = T.GetField("_pitch", Flags);
        static readonly FieldInfo RecenterField = T.GetField("_recenterYaw", Flags);
        static readonly FieldInfo VelocityField = T.GetField("_velocity", Flags);
        static readonly FieldInfo YawVelocityField = T.GetField("_yawVelocity", Flags);
        static readonly FieldInfo PitchVelocityField = T.GetField("_pitchVelocity", Flags);
        static readonly FieldInfo LockBlendField = T.GetField("_lockBlend", Flags);

        /// <summary>The field names this class depends on, for the test that pins them.</summary>
        public static readonly string[] FieldNames =
        {
            "target", "targetOffset", "distance", "lockPitch", "safeBandEnabled", "safeBandMin", "safeBandMax",
            "_yaw", "_pitch", "_recenterYaw", "_velocity", "_yawVelocity", "_pitchVelocity", "_lockBlend",
        };

        /// <summary>True when every field was found.</summary>
        public static bool IsSupported =>
            TargetField != null && TargetOffsetField != null && DistanceField != null && LockPitchField != null &&
            SafeBandEnabledField != null && SafeBandMinField != null && SafeBandMaxField != null &&
            YawField != null && PitchField != null && RecenterField != null && VelocityField != null &&
            YawVelocityField != null && PitchVelocityField != null && LockBlendField != null;

        /// <summary>The camera's follow target, or null.</summary>
        public static Transform Target(SandboxOrbitCamera camera) =>
            camera != null && TargetField != null ? TargetField.GetValue(camera) as Transform : null;

        /// <summary>The camera's resting settings (its defaults when it is missing or unreadable).</summary>
        public static OrbitRest Rest(SandboxOrbitCamera camera)
        {
            var rest = OrbitRest.Defaults;
            if (camera == null || !IsSupported) return rest;
            rest.TargetOffset = (Vector3)TargetOffsetField.GetValue(camera);
            rest.Distance = (float)DistanceField.GetValue(camera);
            rest.Pitch = (float)LockPitchField.GetValue(camera);
            rest.SafeBand = (bool)SafeBandEnabledField.GetValue(camera);
            rest.SafeBandMin = (float)SafeBandMinField.GetValue(camera);
            rest.SafeBandMax = (float)SafeBandMaxField.GetValue(camera);
            return rest;
        }

        /// <summary>
        /// Puts the camera's view state at rest on <paramref name="yaw"/> and <paramref name="pitch"/>
        /// with no recentre pending and no smoothing velocity. Call while it is disabled; its next
        /// LateUpdate then reproduces the resting pose. False when unsupported.
        /// </summary>
        public static bool SnapView(SandboxOrbitCamera camera, float yaw, float pitch)
        {
            if (camera == null || !IsSupported) return false;
            YawField.SetValue(camera, yaw);
            PitchField.SetValue(camera, pitch);
            RecenterField.SetValue(camera, null);
            VelocityField.SetValue(camera, Vector3.zero);
            YawVelocityField.SetValue(camera, 0f);
            PitchVelocityField.SetValue(camera, 0f);
            LockBlendField.SetValue(camera, 0f);
            return true;
        }

        /// <summary>The camera's current yaw and pitch (for logs and tests).</summary>
        public static Vector2 View(SandboxOrbitCamera camera)
        {
            if (camera == null || !IsSupported) return Vector2.zero;
            return new Vector2((float)YawField.GetValue(camera), (float)PitchField.GetValue(camera));
        }
    }
}
