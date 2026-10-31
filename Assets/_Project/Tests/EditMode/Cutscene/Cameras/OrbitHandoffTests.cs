using System.Reflection;
using NUnit.Framework;
using Pickleball.Cutscene.Cameras;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Cameras
{
    /// <summary>
    /// Pose G is the gameplay orbit camera's own resting pose: after the hand-back snaps its view
    /// state, its next LateUpdate leaves the camera exactly where the cutscene's last frame was.
    /// </summary>
    public class OrbitHandoffTests
    {
        GameObject _cam, _target;

        [TearDown]
        public void TearDown()
        {
            if (_cam != null) Object.DestroyImmediate(_cam);
            if (_target != null) Object.DestroyImmediate(_target);
        }

        [Test]
        public void EveryOrbitCameraFieldTheHandbackNeedsExists()
        {
            foreach (string name in OrbitCameraAccess.FieldNames)
                Assert.IsNotNull(typeof(SandboxOrbitCamera).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                    $"SandboxOrbitCamera.{name} was renamed; update OrbitCameraAccess.");
            Assert.IsTrue(OrbitCameraAccess.IsSupported);
        }

        [Test]
        public void RestReadsTheSerializedSettings()
        {
            var orbit = Make(new Vector3(0f, 2.03f, 0f), 4f, 16f);
            var rest = OrbitCameraAccess.Rest(orbit);
            Assert.AreEqual(2.03f, rest.TargetOffset.y, 1e-5f);
            Assert.AreEqual(4f, rest.Distance, 1e-5f);
            Assert.AreEqual(16f, rest.Pitch, 1e-5f);
            Assert.AreSame(_target.transform, OrbitCameraAccess.Target(orbit));
        }

        [Test]
        public void SnappedOrbitCameraHoldsPoseGOnItsNextFrame()
        {
            var orbit = Make(new Vector3(0f, 2.03f, 0f), 4f, 16f);
            _target.transform.SetPositionAndRotation(new Vector3(-4f, 1011f, -53f), Quaternion.Euler(0f, 21.95f, 0f));
            var cam = _cam.GetComponent<Camera>();
            var rest = OrbitCameraAccess.Rest(orbit);
            float yaw = OrbitRest.YawBehind(_target.transform.forward);
            Pose g = rest.Solve(_target.transform.position, yaw, cam.fieldOfView);
            Assert.IsTrue(OrbitCameraAccess.SnapView(orbit, yaw, rest.Pitch));
            _cam.transform.SetPositionAndRotation(g.position, g.rotation);

            typeof(SandboxOrbitCamera).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(orbit, null);

            Assert.That(Vector3.Distance(g.position, _cam.transform.position), Is.LessThan(0.01f), "within 1 cm");
            Assert.That(Quaternion.Angle(g.rotation, _cam.transform.rotation), Is.LessThan(0.1f), "within 0.1°");
            var view = OrbitCameraAccess.View(orbit);
            Assert.AreEqual(yaw, view.x, 1e-4f);
            Assert.AreEqual(16f, view.y, 1e-4f);
        }

        [Test]
        public void PoseGSitsOnTheSafeBandEdge()
        {
            var rest = OrbitRest.Defaults;
            rest.TargetOffset = new Vector3(0f, 2.03f, 0f);
            Pose g = rest.Solve(Vector3.zero, 0f, 60f);
            Vector3 feet = CameraMath.Viewport(g.position, g.rotation, 60f, 16f / 9f, Vector3.zero);
            Assert.GreaterOrEqual(feet.y, rest.SafeBandMin - 1e-4f);
            Assert.AreEqual(16f, g.rotation.eulerAngles.x, 1e-3f);
        }

        SandboxOrbitCamera Make(Vector3 offset, float distance, float lockPitch)
        {
            _target = new GameObject("OrbitTarget (test)");
            _cam = new GameObject("OrbitCamera (test)", typeof(Camera));
            _cam.GetComponent<Camera>().fieldOfView = 60f;
            var orbit = _cam.AddComponent<SandboxOrbitCamera>();
            var so = new SerializedObject(orbit);
            so.FindProperty("target").objectReferenceValue = _target.transform;
            so.FindProperty("targetOffset").vector3Value = offset;
            so.FindProperty("distance").floatValue = distance;
            so.FindProperty("lockPitch").floatValue = lockPitch;
            so.FindProperty("occlusionEnabled").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            return orbit;
        }
    }
}
