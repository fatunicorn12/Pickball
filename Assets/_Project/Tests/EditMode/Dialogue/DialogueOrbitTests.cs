using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Looking around in a talk, as pure math: the orbit starts exactly on the
    /// shot, pitch is clamped (but never snaps the shot), yaw is free and wraps
    /// the way it was turned, damping runs on unscaled time and does nothing on
    /// a zero step, walls pull the camera in at once and let it out gently, and
    /// the camera is pushed out of the player's body.
    /// </summary>
    public class DialogueOrbitTests
    {
        const float Tolerance = 1e-3f;
        const float Smooth = 0.08f;
        const float MinPitch = -10f, MaxPitch = 60f;
        static readonly Vector3 Pivot = new(1f, 1f, 2f);
        static readonly Vector3 Shot = new(-1f, 1.5f, -1.5f);

        static DialogueOrbit Begun(Vector3? shot = null)
        {
            var orbit = new DialogueOrbit();
            orbit.Begin(Pivot, shot ?? Shot);
            return orbit;
        }

        static void Settle(DialogueOrbit orbit, float smoothTime = Smooth)
        {
            for (int i = 0; i < 400 && !orbit.IsSettled; i++)
            {
                orbit.Step(0.016f, smoothTime);
                orbit.FollowDistance(orbit.Radius, 0.4f, 0.016f, 0.2f);
            }
        }

        static float ElevationOf(Vector3 position)
        {
            Vector3 offset = position - Pivot;
            return Mathf.Asin(offset.y / offset.magnitude) * Mathf.Rad2Deg;
        }

        [Test]
        public void Begin_StartsExactlyOnTheShot_Unmoved()
        {
            var orbit = Begun();

            Assert.That(Vector3.Distance(orbit.Position, Shot), Is.LessThan(1e-4f));
            Assert.That(orbit.Radius, Is.EqualTo(Vector3.Distance(Shot, Pivot)).Within(1e-5f));
            Assert.That(orbit.Moved, Is.False);
            Assert.That(orbit.IsSettled, Is.True);
        }

        [Test]
        public void Pitch_IsClampedToTheRange()
        {
            var orbit = Begun();

            orbit.AddLook(0f, 500f, MinPitch, MaxPitch);
            Assert.That(orbit.TargetPitch, Is.EqualTo(MaxPitch));
            Settle(orbit);
            Assert.That(orbit.Pitch, Is.EqualTo(MaxPitch));
            Assert.That(ElevationOf(orbit.Position), Is.EqualTo(MaxPitch).Within(Tolerance));

            orbit.AddLook(0f, -1000f, MinPitch, MaxPitch);
            Settle(orbit);
            Assert.That(orbit.Pitch, Is.EqualTo(MinPitch));
            Assert.That(orbit.Position.y, Is.LessThan(Pivot.y), "below the NPC's head, looking up");
        }

        [Test]
        public void Pitch_RangeIncludesTheShotsOwnElevation_SoTheFirstTouchNeverSnaps()
        {
            Vector3 low = Pivot + Quaternion.Euler(-30f, 200f, 0f) * Vector3.back * 3f;   // 30° below
            var orbit = Begun(low);
            float start = orbit.Pitch;
            Assert.That(start, Is.EqualTo(-30f).Within(Tolerance));

            orbit.AddLook(5f, 0f, MinPitch, MaxPitch);
            Settle(orbit);
            Assert.That(orbit.Pitch, Is.EqualTo(start).Within(Tolerance), "a turn alone keeps the height");

            orbit.AddLook(0f, -5f, MinPitch, MaxPitch);
            Assert.That(orbit.TargetPitch, Is.EqualTo(start).Within(Tolerance), "no lower than where it began");
            orbit.AddLook(0f, 5f, MinPitch, MaxPitch);
            Assert.That(orbit.TargetPitch, Is.EqualTo(start + 5f).Within(Tolerance), "free to rise");
        }

        [Test]
        public void Yaw_PastHalfATurn_WrapsAndKeepsTurningTheWayItWasTurned()
        {
            var orbit = Begun();
            orbit.AddLook(200f, 0f, MinPitch, MaxPitch);

            float previous = orbit.Yaw;
            for (int i = 0; i < 400 && !orbit.IsSettled; i++)
            {
                orbit.Step(0.016f, Smooth);
                Assert.That(Mathf.DeltaAngle(previous, orbit.Yaw), Is.GreaterThanOrEqualTo(-1e-4f),
                            "never turns back the short way");
                Assert.That(orbit.Yaw, Is.GreaterThan(-180f).And.LessThanOrEqualTo(180f));
                previous = orbit.Yaw;
            }
            Assert.That(orbit.Yaw, Is.EqualTo(-160f).Within(Tolerance));
            Assert.That(orbit.TargetYaw, Is.EqualTo(-160f).Within(Tolerance));
        }

        [Test]
        public void Yaw_AFullCircle_ComesBackToTheShot()
        {
            var orbit = Begun();
            orbit.AddLook(360f, 0f, MinPitch, MaxPitch);
            Settle(orbit);

            Assert.That(Vector3.Distance(orbit.Position, Shot), Is.LessThan(1e-3f));
        }

        [Test]
        public void Yaw_Positive_TurnsTheViewRight()
        {
            var orbit = Begun();
            Vector3 before = Pivot - orbit.Position;
            orbit.AddLook(30f, 0f, MinPitch, MaxPitch);
            Settle(orbit);
            Vector3 after = Pivot - orbit.Position;
            before.y = after.y = 0f;

            Assert.That(Vector3.SignedAngle(before, after, Vector3.up), Is.EqualTo(30f).Within(0.05f));
            Assert.That(Vector3.Distance(orbit.Position, Pivot), Is.EqualTo(orbit.Radius).Within(Tolerance),
                        "same distance from the NPC's head");
        }

        [Test]
        public void Damping_RunsOnUnscaledTime_AndAZeroStepChangesNothing()
        {
            var orbit = Begun();
            orbit.AddLook(90f, 20f, MinPitch, MaxPitch);

            orbit.Step(0f, Smooth);         // a frame with no time at all
            Assert.That(orbit.Yaw, Is.EqualTo(0f));
            Assert.That(float.IsNaN(orbit.Pitch), Is.False);

            float pitch0 = orbit.Pitch;
            orbit.Step(0.016f, Smooth);     // what the camera passes while timeScale is 0
            Assert.That(orbit.Yaw, Is.GreaterThan(0f).And.LessThan(90f), "damped, not snapped");
            Assert.That(orbit.Pitch, Is.GreaterThan(pitch0).And.LessThan(pitch0 + 20f));

            Settle(orbit);
            Assert.That(orbit.Yaw, Is.EqualTo(90f).Within(Tolerance));
            Assert.That(orbit.IsSettled, Is.True);
        }

        [Test]
        public void Damping_IsFrameRateIndependent()
        {
            var fine = Begun();
            var coarse = Begun();
            fine.AddLook(90f, 0f, MinPitch, MaxPitch);
            coarse.AddLook(90f, 0f, MinPitch, MaxPitch);

            for (int i = 0; i < 10; i++) fine.Step(0.01f, Smooth);
            coarse.Step(0.1f, Smooth);

            Assert.That(fine.Yaw, Is.EqualTo(coarse.Yaw).Within(0.01f));
        }

        [Test]
        public void Damping_OfZero_FollowsAtOnce()
        {
            var orbit = Begun();
            orbit.AddLook(45f, 0f, MinPitch, MaxPitch);
            orbit.Step(0.016f, 0f);

            Assert.That(orbit.Yaw, Is.EqualTo(45f));
        }

        [Test]
        public void Wall_PullsTheCameraInAtOnce_ThenLetsItOutGently()
        {
            var orbit = Begun();
            float radius = orbit.Radius;

            float pulled = orbit.FollowDistance(radius * 0.5f, 0.4f, 0.016f, 0.2f);
            Assert.That(pulled, Is.EqualTo(radius * 0.5f), "in at once: never inside the wall");
            Assert.That(orbit.IsSettled, Is.True);

            float easing = orbit.FollowDistance(radius, 0.4f, 0.016f, 0.2f);
            Assert.That(easing, Is.GreaterThan(radius * 0.5f).And.LessThan(radius), "out gently");
            Assert.That(orbit.IsSettled, Is.False);

            for (int i = 0; i < 400; i++) orbit.FollowDistance(radius * 10f, 0.4f, 0.016f, 0.2f);
            Assert.That(orbit.Distance, Is.EqualTo(radius), "never past the shot's distance");
            Assert.That(orbit.IsSettled, Is.True);
        }

        [Test]
        public void Wall_NeverPullsCloserThanTheMinimum()
        {
            var orbit = Begun();

            Assert.That(orbit.FollowDistance(0.05f, 0.4f, 0.016f, 0.2f), Is.EqualTo(0.4f));
            Assert.That(orbit.FollowDistance(0f, 0f, 0.016f, 0.2f), Is.EqualTo(0f));
        }

        [Test]
        public void PushOut_PutsAPointInsideThePlayerOnHisSurface()
        {
            Vector3 feet = Vector3.zero, head = new(0f, 1.5f, 0f);
            const float r = 0.8f;

            static float Off(Vector3 point, Vector3 feet, Vector3 head, Vector3 expected)
                => Vector3.Distance(DialogueOrbit.PushOut(point, feet, head, r, Vector3.forward), expected);

            Assert.That(Off(new Vector3(0.3f, 1f, 0f), feet, head, new Vector3(0.8f, 1f, 0f)), Is.LessThan(1e-5f),
                        "inside: out to the surface, the shortest way");
            Assert.That(Off(new Vector3(1f, 1f, 0f), feet, head, new Vector3(1f, 1f, 0f)), Is.LessThan(1e-5f),
                        "outside: untouched");
            Assert.That(Off(new Vector3(0f, 2.4f, 0f), feet, head, new Vector3(0f, 2.4f, 0f)), Is.LessThan(1e-5f),
                        "well over his head: untouched");
            Assert.That(Off(new Vector3(0f, 1f, 0f), feet, head, new Vector3(0f, 1f, 0.8f)), Is.LessThan(1e-5f),
                        "on the axis: the fallback way");
        }

        [TestCase(180f, 180f)]
        [TestCase(-180f, 180f)]
        [TestCase(190f, -170f)]
        [TestCase(-190f, 170f)]
        [TestCase(540f, 180f)]
        [TestCase(30f, 30f)]
        public void WrapAngle_IsInTheHalfOpenRange(float degrees, float expected)
        {
            Assert.That(DialogueOrbit.WrapAngle(degrees), Is.EqualTo(expected).Within(1e-4f));
        }
    }
}
