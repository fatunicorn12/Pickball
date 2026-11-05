using System.Reflection;
using NUnit.Framework;
using Pickleball.Cutscene.Cameras;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Cameras
{
    /// <summary>
    /// A cutscene can start with a lock held and the view tilted up at a high target. The hand-back
    /// snaps only the fields <see cref="OrbitCameraAccess"/> knows about, so this pins that the tilt
    /// and its pull-back don't survive the snap: they scale with _lockBlend, which it clears.
    /// </summary>
    public class OrbitHandoffFromLockTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        GameObject _cam, _target;

        [TearDown]
        public void TearDown()
        {
            if (_cam != null) Object.DestroyImmediate(_cam);
            if (_target != null) Object.DestroyImmediate(_target);
        }

        [Test]
        public void SnappingFromATiltedLock_StillHoldsPoseG()
        {
            _target = new GameObject("OrbitTarget (test)");
            _cam = new GameObject("OrbitCamera (test)", typeof(Camera));
            _cam.GetComponent<Camera>().fieldOfView = 60f;
            var orbit = _cam.AddComponent<SandboxOrbitCamera>();
            var so = new SerializedObject(orbit);
            so.FindProperty("target").objectReferenceValue = _target.transform;
            so.FindProperty("targetOffset").vector3Value = new Vector3(0f, 2.03f, 0f);
            so.FindProperty("distance").floatValue = 4f;
            so.FindProperty("lockPitch").floatValue = 16f;
            so.FindProperty("occlusionEnabled").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            // As the rig was left by a lock on something overhead.
            Set(orbit, "_lookTilt", 25f);
            Set(orbit, "_lockRise", 60f);
            Set(orbit, "_lockBlend", 1f);

            _target.transform.SetPositionAndRotation(new Vector3(-4f, 1011f, -53f), Quaternion.Euler(0f, 21.95f, 0f));
            var rest = OrbitCameraAccess.Rest(orbit);
            float yaw = OrbitRest.YawBehind(_target.transform.forward);
            Pose g = rest.Solve(_target.transform.position, yaw, _cam.GetComponent<Camera>().fieldOfView);
            Assert.IsTrue(OrbitCameraAccess.SnapView(orbit, yaw, rest.Pitch));
            _cam.transform.SetPositionAndRotation(g.position, g.rotation);

            typeof(SandboxOrbitCamera).GetMethod("LateUpdate", Private).Invoke(orbit, null);

            Assert.That(Vector3.Distance(g.position, _cam.transform.position), Is.LessThan(0.01f), "within 1 cm");
            Assert.That(Quaternion.Angle(g.rotation, _cam.transform.rotation), Is.LessThan(0.1f), "within 0.1°");
        }

        static void Set(SandboxOrbitCamera orbit, string field, float value)
        {
            var info = typeof(SandboxOrbitCamera).GetField(field, Private);
            Assert.IsNotNull(info, field);
            info.SetValue(orbit, value);
        }
    }
}
