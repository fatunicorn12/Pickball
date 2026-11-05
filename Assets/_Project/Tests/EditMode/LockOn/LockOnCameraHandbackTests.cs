using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.LockOn
{
    /// <summary>
    /// The look tilt is written only to the transform, never to _yaw or _pitch, and scales with
    /// _lockBlend. Those two facts are what keep the hand-backs clean: a conversation restores the
    /// tilted pose it recorded and the tilt carries on from there, and a release fades it with no
    /// jump. The cutscene's hand-back has its own test beside OrbitCameraAccess's
    /// (Cutscene/Cameras/OrbitHandoffFromLockTests).
    ///
    /// Time.deltaTime is meaningless in EditMode, so these assert only what holds for any value.
    /// </summary>
    public class LockOnCameraHandbackTests : LockOnTestFixture
    {
        SandboxOrbitCamera _orbit;
        Transform _followed;

        [SetUp]
        public void BuildOrbit()
        {
            _followed = Host("Player", Vector3.zero).transform;
            var camGo = Host("Camera", new Vector3(0f, 2f, -4f));
            camGo.AddComponent<Camera>().fieldOfView = 60f;
            _orbit = camGo.AddComponent<SandboxOrbitCamera>();
            var so = new SerializedObject(_orbit);
            so.FindProperty("target").objectReferenceValue = _followed;
            so.FindProperty("targetOffset").vector3Value = Vector3.up * 1.35f;
            so.FindProperty("distance").floatValue = 4f;
            so.FindProperty("occlusionEnabled").boolValue = false;
            so.FindProperty("safeBandEnabled").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            SetPrivate(_orbit, "_yaw", 30f);
            SetPrivate(_orbit, "_pitch", 10f);
        }

        void Tilted(float tilt, float blend)
        {
            SetPrivate(_orbit, "_lookTilt", tilt);
            SetPrivate(_orbit, "_lockBlend", blend);
        }

        float CameraPitch()
        {
            float x = _orbit.transform.eulerAngles.x;
            return x > 180f ? x - 360f : x;
        }

        static float Smooth(float k) => k * k * (3f - 2f * k);

        [Test]
        public void WithNoLock_TheCameraIsExactlyTheOrbit()
        {
            Call(_orbit, "LateUpdate");
            Assert.That(Quaternion.Angle(_orbit.transform.rotation, Quaternion.Euler(10f, 30f, 0f)), Is.LessThan(1e-3f));
        }

        [Test]
        public void TheTilt_TurnsTheLook_NotTheOrbit()
        {
            var lockOn = _followed.gameObject.AddComponent<LockOnTargeting>();
            SetRef(_orbit, "lockOn", lockOn);
            ForceLock(lockOn, NewFloater("High", Vector3.forward, 20f, out _));
            Tilted(20f, 1f);
            Call(_orbit, "LateUpdate");

            float orbitPitch = GetPrivate<float>(_orbit, "_pitch");
            float orbitYaw = GetPrivate<float>(_orbit, "_yaw");
            Assert.That(CameraPitch(), Is.LessThan(orbitPitch - 1f), "looking up past the orbit");
            Assert.That(orbitPitch, Is.GreaterThanOrEqualTo(-6f), "while the orbit stays above the floor");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(_orbit.transform.eulerAngles.y, orbitYaw)), Is.LessThan(1e-2f));
        }

        [TestCase(1f)]
        [TestCase(0.5f)]
        [TestCase(0.1f)]
        public void AfterARelease_TheTiltFadesWithTheBlend_AndTheOrbitIsUntouched(float blend)
        {
            Tilted(30f, blend);
            Call(_orbit, "LateUpdate");
            float after = GetPrivate<float>(_orbit, "_lockBlend");
            Assert.That(after, Is.LessThanOrEqualTo(blend));
            Assert.That(CameraPitch(), Is.EqualTo(10f - 30f * Smooth(after)).Within(0.01f));
            Assert.That(GetPrivate<float>(_orbit, "_pitch"), Is.EqualTo(10f), "mouse look resumes from here");
            Assert.That(GetPrivate<float>(_orbit, "_yaw"), Is.EqualTo(30f));
        }

        [Test]
        public void OnceFaded_TheTiltIsForgotten()
        {
            Tilted(30f, 0f);
            Call(_orbit, "LateUpdate");
            Assert.That(CameraPitch(), Is.EqualTo(10f).Within(1e-3f));
            Assert.That(GetPrivate<float>(_orbit, "_lookTilt"), Is.EqualTo(0f));
        }

        [Test]
        public void AimingCancelsTheTilt_SoTheCrosshairIsTheOrbit()
        {
            Tilted(30f, 1f);
            SetPrivate(_orbit, "_aimBlend", 1f);
            Call(_orbit, "LateUpdate");
            // UpdateAim eases the blend first; any part of it left scales the tilt down the same way.
            float aim = GetPrivate<float>(_orbit, "_aimBlend");
            float blend = GetPrivate<float>(_orbit, "_lockBlend");
            Assert.That(CameraPitch(), Is.EqualTo(10f - 30f * Smooth(blend) * (1f - aim)).Within(0.01f));
        }

        [Test]
        public void AConversation_KeepsTheTilt_SoTheRestoredPoseCarriesOn()
        {
            // The dialogue camera disables the rig mid-lock and restores the pose it recorded,
            // tilt included. Waking up must not snap out of that tilt, nor carry speed into it.
            Tilted(20f, 1f);
            SetPrivate(_orbit, "_tiltVelocity", 50f);

            Call(_orbit, "OnEnable");

            Assert.That(GetPrivate<float>(_orbit, "_lookTilt"), Is.EqualTo(20f));
            Assert.That(GetPrivate<float>(_orbit, "_lockBlend"), Is.EqualTo(1f));
            Assert.That(GetPrivate<float>(_orbit, "_tiltVelocity"), Is.EqualTo(0f));
        }
    }
}
