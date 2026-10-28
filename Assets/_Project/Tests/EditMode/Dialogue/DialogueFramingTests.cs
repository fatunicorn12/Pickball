using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Pins the conversation shot: behind the player on the player → NPC line,
    /// on the configured shoulder, the NPC in front of the camera and pushed to
    /// the far side of the frame, never NaN, and pulled in (never pushed out)
    /// when something blocks it.
    /// </summary>
    public class DialogueFramingTests
    {
        const float Tolerance = 1e-4f;

        static DialogueFramingSettings Settings(float side = 0.6f, float aimSide = 0.3f) => new()
        {
            PivotHeight = 1.35f,
            PushInDistance = 1.7f,
            CameraHeight = 0.2f,
            SideOffset = side,
            AimHeightOffset = -0.1f,
            AimSideOffset = aimSide,
            FieldOfView = 42f,
        };

        static readonly Vector3 Feet = new(2f, 0f, -1f);
        static readonly Vector3 NpcFocus = new(2f, 1.5f, 2f);    // 3 m along +Z

        /// <summary>A world point in the camera's local frame: x right, y up, z forward.</summary>
        static Vector3 InCamera(DialogueShot shot, Vector3 world)
            => Quaternion.Inverse(shot.Rotation) * (world - shot.Position);

        [Test]
        public void Camera_SitsBehindThePlayer_OnTheLineToTheNpc()
        {
            var shot = DialogueFraming.Solve(Feet, Vector3.right, NpcFocus, Settings());
            Vector3 fromPivot = shot.Position - shot.Pivot;

            Assert.That(shot.Pivot, Is.EqualTo(Feet + Vector3.up * 1.35f));
            Assert.That(fromPivot.z, Is.EqualTo(-1.7f).Within(Tolerance), "behind, along the NPC line");
            Assert.That(fromPivot.y, Is.EqualTo(0.2f).Within(Tolerance));
        }

        [TestCase(0.6f)]
        [TestCase(-0.6f)]
        public void Camera_IsOverTheConfiguredShoulder(float side)
        {
            var shot = DialogueFraming.Solve(Feet, Vector3.forward, NpcFocus, Settings(side));
            // Facing +Z, world +X is the right-hand side.
            Assert.That((shot.Position - shot.Pivot).x, Is.EqualTo(side).Within(Tolerance));
        }

        [TestCase(0.6f)]
        [TestCase(-0.6f)]
        public void Npc_IsInFront_AndOffCentreAwayFromTheShoulder(float side)
        {
            var shot = DialogueFraming.Solve(Feet, Vector3.forward, NpcFocus, Settings(side, aimSide: 0.3f));
            Vector3 npc = InCamera(shot, NpcFocus);
            Vector3 player = InCamera(shot, shot.Pivot);

            // Over the right shoulder (side > 0) the player is on the left of the
            // frame and the NPC right of centre; mirrored for the left shoulder.
            Assert.That(npc.z, Is.GreaterThan(0f), "the NPC is in front of the camera");
            Assert.That(Mathf.Sign(player.x), Is.EqualTo(-Mathf.Sign(side)), "the shoulder is on the far side");
            Assert.That(Mathf.Sign(npc.x), Is.EqualTo(Mathf.Sign(side)), "the NPC is on the open side");
            Assert.That(Mathf.Abs(npc.x / npc.z), Is.LessThan(0.3f), "only slightly off centre");
        }

        [Test]
        public void Npc_WithNoAimShift_IsDeadCentre()
        {
            var shot = DialogueFraming.Solve(Feet, Vector3.forward, NpcFocus, Settings(aimSide: 0f));
            Vector3 npc = InCamera(shot, NpcFocus + Vector3.up * -0.1f);
            Assert.That(npc.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(npc.y, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void Shot_FollowsTheLineNotThePlayersFacing()
        {
            // Facing away from the NPC (locked on and turned): the shot still frames the NPC.
            var shot = DialogueFraming.Solve(Feet, Vector3.back, NpcFocus, Settings());
            Assert.That(InCamera(shot, NpcFocus).z, Is.GreaterThan(0f));
        }

        [Test]
        public void Shot_RotatesWithTheScene()
        {
            var a = DialogueFraming.Solve(Feet, Vector3.forward, NpcFocus, Settings());
            var turn = Quaternion.Euler(0f, 70f, 0f);
            var b = DialogueFraming.Solve(turn * Feet, turn * Vector3.forward, turn * NpcFocus, Settings());

            Assert.That(Vector3.Distance(b.Position, turn * a.Position), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(b.Rotation, turn * a.Rotation), Is.LessThan(0.05f));
        }

        [Test]
        public void NpcStraightAbove_FallsBackToThePlayersFacing_WithoutNaN()
        {
            var above = Feet + Vector3.up * 3f;
            var shot = DialogueFraming.Solve(Feet, Vector3.right, above, Settings());

            Assert.That(float.IsNaN(shot.Position.x) || float.IsNaN(shot.Rotation.x), Is.False);
            Assert.That((shot.Position - shot.Pivot).x, Is.EqualTo(-1.7f).Within(Tolerance), "behind along facing");

            var degenerate = DialogueFraming.Solve(Feet, Vector3.up, above, Settings());
            Assert.That(float.IsNaN(degenerate.Position.x), Is.False);
        }

        [Test]
        public void FieldOfView_ComesFromTheSettings()
        {
            Assert.That(DialogueFraming.Solve(Feet, Vector3.forward, NpcFocus, Settings()).FieldOfView,
                        Is.EqualTo(42f));
        }

        // -------------------------------------------------------------- clearance

        [Test]
        public void PullIn_ClearPath_LeavesTheCameraAlone()
        {
            var pivot = Vector3.zero;
            var desired = new Vector3(0f, 0f, -2f);
            Assert.That(DialogueFraming.PullIn(pivot, desired, 5f, 0.4f), Is.EqualTo(desired));
            Assert.That(DialogueFraming.PullIn(pivot, desired, 2f, 0.4f), Is.EqualTo(desired));
        }

        [Test]
        public void PullIn_Blocked_StopsAtTheClearDistance_OnTheSameLine()
        {
            var result = DialogueFraming.PullIn(Vector3.zero, new Vector3(0f, 0f, -2f), 1.2f, 0.4f);
            Assert.That(result.z, Is.EqualTo(-1.2f).Within(Tolerance));
            Assert.That(result.x, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void PullIn_NeverCloserThanTheMinimum()
        {
            var result = DialogueFraming.PullIn(Vector3.zero, new Vector3(0f, 0f, -2f), 0.05f, 0.4f);
            Assert.That(result.z, Is.EqualTo(-0.4f).Within(Tolerance));
        }

        [Test]
        public void PullIn_KeepsTheAim_SoTheNpcStaysFramed()
        {
            var shot = DialogueFraming.Solve(Feet, Vector3.forward, NpcFocus, Settings());
            var pulled = shot.WithPosition(DialogueFraming.PullIn(shot.Pivot, shot.Position, 0.8f, 0.4f));

            Assert.That(pulled.Aim, Is.EqualTo(shot.Aim));
            Assert.That(InCamera(pulled, NpcFocus).z, Is.GreaterThan(0f));
        }

        // ------------------------------------------------------------- smoothstep

        [Test]
        public void SmoothStep_HitsItsEnds_IsMonotonic_AndClamps()
        {
            Assert.That(DialogueFraming.SmoothStep01(0f), Is.EqualTo(0f));
            Assert.That(DialogueFraming.SmoothStep01(1f), Is.EqualTo(1f));
            Assert.That(DialogueFraming.SmoothStep01(0.5f), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(DialogueFraming.SmoothStep01(-3f), Is.EqualTo(0f));
            Assert.That(DialogueFraming.SmoothStep01(7f), Is.EqualTo(1f));

            float previous = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float v = DialogueFraming.SmoothStep01(i / 100f);
                Assert.That(v, Is.GreaterThanOrEqualTo(previous));
                previous = v;
            }
            // Flat at both ends: the blend neither snaps in nor out.
            Assert.That(DialogueFraming.SmoothStep01(0.01f), Is.LessThan(0.001f));
            Assert.That(1f - DialogueFraming.SmoothStep01(0.99f), Is.LessThan(0.001f));
        }
    }
}
