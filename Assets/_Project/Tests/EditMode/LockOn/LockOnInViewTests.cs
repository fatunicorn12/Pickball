using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.LockOn
{
    /// <summary>
    /// "Only lock on if a lockable enemy is in view": the aim point is inside the camera's viewport
    /// and the camera has a clear line to it. The press, the hint, target switching and a held
    /// lock all go through this check, on a real Camera and real colliders.
    /// </summary>
    public class LockOnInViewTests : LockOnTestFixture
    {
        LockOnTargeting _lockOn;
        Camera _camera;

        [SetUp]
        public void BuildRig()
        {
            var player = Host("Player", Vector3.zero);
            _lockOn = player.AddComponent<LockOnTargeting>();

            var camGo = Host("Camera", new Vector3(0f, 1.5f, -4f));
            camGo.transform.rotation = Quaternion.identity;
            _camera = camGo.AddComponent<Camera>();
            _camera.fieldOfView = 60f;
            _camera.aspect = 16f / 9f;
            SetRef(_lockOn, "aimSource", camGo.transform);
        }

        void Press()
        {
            Physics.SyncTransforms();
            SetPrivate(_lockOn, "_togglePressed", true);
            Call(_lockOn, "Update");
        }

        void Tick()
        {
            Physics.SyncTransforms();
            Call(_lockOn, "Update");
        }

        string ReticleMode()
        {
            var reticle = GetPrivate<LockOnReticle>(_lockOn, "_reticle");
            return reticle == null ? "None" : GetPrivate<object>(reticle, "_mode").ToString();
        }

        /// <summary>A target whose aim point projects to this viewport spot.</summary>
        Health TargetAtViewport(float x, float y, float depth = 8f)
        {
            Vector3 aim = _camera.ViewportToWorldPoint(new Vector3(x, y, depth));
            // The chest sits 0.2 m above the middle of the 2 m body, which sits at height 1 on its root.
            var h = NewFloater("Target", aim - Vector3.up * 1.2f, 1f, out _);
            Physics.SyncTransforms();
            return h;
        }

        // ------------------------------------------------------------------ the press

        [Test]
        public void Press_LocksATargetInPlainView()
        {
            var h = TargetAtViewport(0.5f, 0.5f);
            Press();
            Assert.That(_lockOn.Target, Is.SameAs(h));
        }

        [TestCase(1.01f, 0.5f)]    // the old check passed everything to 2% outside the screen
        [TestCase(-0.01f, 0.5f)]
        [TestCase(0.5f, 1.01f)]
        [TestCase(0.5f, -0.01f)]
        [TestCase(0.99f, 0.5f)]    // on screen, but inside the inset
        [TestCase(0.5f, 0.99f)]
        public void Press_DoesNotLockATargetAtOrPastTheEdge(float x, float y)
        {
            TargetAtViewport(x, y);
            Press();
            Assert.That(_lockOn.IsLocked, Is.False);
        }

        [Test]
        public void Press_DoesNotLockATargetBehindTheCamera()
        {
            NewFloater("Behind", new Vector3(0f, 0f, -10f), 1f, out _);
            Press();
            Assert.That(_lockOn.IsLocked, Is.False);
        }

        [Test]
        public void Press_JudgesTheAimPoint_NotTheFirstColliderCentre()
        {
            // A big collider first, so the old first-collider centre was well on screen,
            // and an aim point that the upper body lifts off the top of it.
            Vector3 top = _camera.ViewportToWorldPoint(new Vector3(0.5f, 1f, 8f));
            var h = NewFloater("Tall", new Vector3(top.x, 0f, top.z), 1f, out _);
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.transform.SetParent(h.transform, false);
            head.transform.localPosition = Vector3.up * (top.y * 2f + 6f);
            Press();
            Assert.That(_lockOn.IsLocked, Is.False, "the combined body's chest is above the frame");
        }

        [Test]
        public void Press_DoesNotLockThroughAWallOnTheCamerasLine_EvenWithTheChestClear()
        {
            // Off to the side: the wall crosses camera-to-target but not chest-to-target.
            var h = NewFloater("Target", new Vector3(-3f, 0f, 8f), 1f, out var body);
            Physics.SyncTransforms();
            Vector3 aim = body.bounds.center;
            Vector3 cam = _camera.transform.position;
            Vector3 onCamLine = Vector3.Lerp(cam, aim, Mathf.InverseLerp(cam.z, aim.z, 1.2f));
            var wall = Primitive(PrimitiveType.Cube, "Wall", new Vector3(onCamLine.x - 0.3f, 3f, 1.2f));
            wall.transform.localScale = new Vector3(1.2f, 6f, 0.2f);
            Physics.SyncTransforms();
            Vector3 chest = Vector3.up * 1.2f;
            Assert.That(Physics.Linecast(chest, aim, out var hit) && hit.collider.gameObject == wall, Is.False, "precondition: the chest's line is clear");

            Press();
            Assert.That(_lockOn.IsLocked, Is.False);

            Object.DestroyImmediate(wall);
            Press();
            Assert.That(_lockOn.Target, Is.SameAs(h), "and it locks once the wall is gone");
        }

        [Test]
        public void Press_SeesPastOtherEnemies_AndTriggers()
        {
            var far = NewFloater("Far", new Vector3(0f, 0f, 10f), 1f, out _);
            var blocker = NewFloater("Near", new Vector3(0f, 0f, 3f), 1f, out _);
            var zone = Primitive(PrimitiveType.Cube, "Zone", new Vector3(0f, 2f, 6f));
            zone.transform.localScale = new Vector3(8f, 8f, 0.5f);
            zone.GetComponent<Collider>().isTrigger = true;
            blocker.enabled = false;   // out of the running, but still standing in the line
            Press();
            Assert.That(_lockOn.Target, Is.SameAs(far));
        }

        [Test]
        public void Press_WithNothingInView_RecentresTheCamera()
        {
            var rig = _camera.gameObject.AddComponent<SandboxOrbitCamera>();
            SetRef(_lockOn, "cameraRig", rig);
            NewFloater("Behind", new Vector3(0f, 0f, -10f), 1f, out _);
            Press();
            Assert.That(_lockOn.IsLocked, Is.False);
            Assert.That(GetPrivate<float?>(rig, "_recenterYaw").HasValue, Is.True);
        }

        [Test]
        public void AFriendly_IsStillLockable_ButNeverHomedOnUnlocked()
        {
            var birdy = TargetAtViewport(0.5f, 0.5f, 3f);
            var so = new SerializedObject(birdy);
            so.FindProperty("grantsXp").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();

            Assert.That(_lockOn.TryGetAttackTarget(30f, out _, out _), Is.False, "no lock, and it pays no XP");
            Press();
            Assert.That(_lockOn.Target, Is.SameAs(birdy));
            Assert.That(_lockOn.TryGetAttackTarget(30f, out Vector3 point, out _), Is.True, "locked on purpose");
            Assert.That(point, Is.EqualTo(_lockOn.AimPoint));
        }

        [Test]
        public void SwingHoming_AimsAtTheSamePointALockWould()
        {
            var h = TargetAtViewport(0.5f, 0.5f, 3f);
            Assert.That(_lockOn.TryGetAttackTarget(30f, out Vector3 unlocked, out float radius), Is.True);
            Press();
            Assert.That(_lockOn.Target, Is.SameAs(h));
            Assert.That(unlocked, Is.EqualTo(_lockOn.AimPoint));
            Assert.That(radius, Is.EqualTo(_lockOn.TargetRadius));
        }

        [Test]
        public void Range_StillLimitsThePress()
        {
            SetFloat(_lockOn, "acquireRange", 5f);
            NewFloater("Far", new Vector3(0f, 0f, 10f), 1f, out _);
            Press();
            Assert.That(_lockOn.IsLocked, Is.False);
        }

        // ------------------------------------------------------------------- the hint

        [TestCase(0.5f, 0.5f)]
        [TestCase(0.9f, 0.4f)]
        [TestCase(0.99f, 0.5f)]
        [TestCase(1.01f, 0.5f)]
        [TestCase(0.5f, 0.99f)]
        public void TheHint_ShowsExactlyWhenAPressWouldLock(float x, float y)
        {
            TargetAtViewport(x, y);
            Tick();
            bool hinted = ReticleMode() == "Hint";
            Press();
            Assert.That(_lockOn.IsLocked, Is.EqualTo(hinted), $"hint said {hinted}");
        }

        [Test]
        public void TheHint_AgreesBehindAWall()
        {
            NewFloater("Target", new Vector3(0f, 0f, 8f), 1f, out _);
            var wall = Primitive(PrimitiveType.Cube, "Wall", new Vector3(0f, 2f, 3f));
            wall.transform.localScale = new Vector3(8f, 8f, 0.2f);
            Tick();
            Assert.That(ReticleMode(), Is.Not.EqualTo("Hint"));
            Press();
            Assert.That(_lockOn.IsLocked, Is.False);
        }

        [Test]
        public void TheHint_FloatsOverTheBody_ButStaysOnScreen()
        {
            var h = TargetAtViewport(0.5f, 0.5f);
            Tick();
            var reticle = GetPrivate<LockOnReticle>(_lockOn, "_reticle");
            Vector3 point = GetPrivate<Vector3>(reticle, "_hintPoint");
            Assert.That(point.y, Is.EqualTo(h.transform.position.y + 2.5f).Within(1e-3f), "half a metre above the body's top");

            // Near the top of the frame the air above the body is off screen.
            Object.DestroyImmediate(h.gameObject);
            var high = TargetAtViewport(0.5f, 0.96f, 4f);
            Tick();
            point = GetPrivate<Vector3>(reticle, "_hintPoint");
            Assert.That(LockOnView.InsideViewport(_camera.WorldToViewportPoint(point), 0f), Is.True);
            Assert.That(high != null);
        }

        // ------------------------------------------------------------------ a held lock

        void Hold(Health h)
        {
            Physics.SyncTransforms();
            ForceLock(_lockOn, h);
        }

        [Test]
        public void AHeldLock_StaysWhileInView_ClearingTheClock()
        {
            var h = TargetAtViewport(0.5f, 0.5f);
            Hold(h);
            SetPrivate(_lockOn, "_lostSightFor", 10f);
            Tick();
            Assert.That(_lockOn.Target, Is.SameAs(h));
            Assert.That(GetPrivate<float>(_lockOn, "_lostSightFor"), Is.EqualTo(0f));
        }

        [Test]
        public void AHeldLock_IsOnlyJudgedAgainstTheEdge_NotThePressInset()
        {
            var h = TargetAtViewport(0.99f, 0.5f);
            Hold(h);
            SetPrivate(_lockOn, "_lostSightFor", 10f);
            Tick();
            Assert.That(_lockOn.Target, Is.SameAs(h), "a press couldn't lock it here, but a lock can keep it");
        }

        [Test]
        public void AHeldLock_OffTheScreenPastItsGrace_LetsGo()
        {
            var h = TargetAtViewport(0.5f, 0.5f);
            Hold(h);
            h.transform.position = _camera.transform.position - Vector3.forward * 6f;
            SetPrivate(_lockOn, "_lostSightFor", 10f);
            Tick();
            Assert.That(_lockOn.IsLocked, Is.False);
        }

        [Test]
        public void AHeldLock_OffTheScreen_WaitsOutItsGrace()
        {
            var h = TargetAtViewport(0.5f, 0.5f);
            Hold(h);
            h.transform.position = _camera.transform.position - Vector3.forward * 6f;
            SetFloat(_lockOn, "lostSightGrace", 1000f);
            Tick();
            Assert.That(_lockOn.Target, Is.SameAs(h));
        }

        [Test]
        public void AHeldLock_BehindAWallPastItsGrace_LetsGo()
        {
            var h = TargetAtViewport(0.5f, 0.5f);
            Hold(h);
            var wall = Primitive(PrimitiveType.Cube, "Wall", _camera.transform.position + Vector3.forward * 3f);
            wall.transform.localScale = new Vector3(8f, 8f, 0.2f);
            SetPrivate(_lockOn, "_lostSightFor", 10f);
            Tick();
            Assert.That(_lockOn.IsLocked, Is.False);
        }

        [Test]
        public void ADeadTarget_HandsTheLockToTheNextOneInView_Only()
        {
            var h = TargetAtViewport(0.45f, 0.5f);
            var next = TargetAtViewport(0.6f, 0.5f);
            var hidden = NewFloater("Behind", new Vector3(0f, 0f, -10f), 1f, out _);
            Hold(h);
            h.Damage(1e6f, Vector3.zero);
            Tick();
            Assert.That(_lockOn.Target, Is.SameAs(next));
            Assert.That(hidden.IsDead, Is.False);

            next.Damage(1e6f, Vector3.zero);
            Tick();
            Assert.That(_lockOn.IsLocked, Is.False, "the only one left is behind the camera");
        }

        // ------------------------------------------------- the lens against the wall

        [Test]
        public void WithTheLensPushedThroughAWallBehindThePlayer_TheLockSurvives()
        {
            // Back to a wall, occlusion stops at occlusionMinDistance and leaves the lens beyond it.
            var followed = _lockOn.transform;
            var rig = _camera.gameObject.AddComponent<SandboxOrbitCamera>();
            var so = new SerializedObject(rig);
            so.FindProperty("target").objectReferenceValue = followed;
            so.FindProperty("targetOffset").vector3Value = Vector3.up * 1.35f;
            so.FindProperty("distance").floatValue = 4f;
            so.FindProperty("occlusionMinDistance").floatValue = 2f;
            so.FindProperty("safeBandEnabled").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            SetPrivate(rig, "_yaw", 0f);
            SetPrivate(rig, "_pitch", 0f);
            SetRef(_lockOn, "cameraRig", rig);

            var wall = Primitive(PrimitiveType.Cube, "BackWall", new Vector3(0f, 2f, -1f));
            wall.transform.localScale = new Vector3(8f, 8f, 0.2f);
            var h = NewFloater("Target", new Vector3(0f, 0f, 8f), 1f, out _);
            Physics.SyncTransforms();
            Call(rig, "LateUpdate");

            Assert.That(rig.SightOrigin, Is.EqualTo(followed.position + Vector3.up * 1.35f), "the follow point, not the lens");
            Press();
            Assert.That(_lockOn.Target, Is.SameAs(h));
        }

        [Test]
        public void WithAClearView_TheSightLineStartsAtTheLens()
        {
            var rig = _camera.gameObject.AddComponent<SandboxOrbitCamera>();
            Assert.That(rig.SightOrigin, Is.EqualTo(_camera.transform.position));
        }
    }
}
