using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Pins the conversation shot: behind the player on the player → NPC line,
    /// on the configured shoulder, wide enough that the sightline to the NPC's
    /// head clears the player's head at any distance, the NPC's head at the
    /// configured screen point with its feet above the box, never NaN, pulled
    /// in (never pushed out) when something blocks it, and a blend path that
    /// swings around the subjects instead of through them.
    /// </summary>
    public class DialogueFramingTests
    {
        const float Tolerance = 1e-4f;
        const float Aspect = 16f / 9f;

        static DialogueFramingSettings Settings(float side = -0.8f) => new()
        {
            PushInDistance = 1.7f,
            CameraHeight = 0.3f,
            SideOffset = side,
            PlayerClearance = 0.45f,
            MaxSideOffset = 1.6f,
            NpcHeadScreenHeight = 0.66f,
            NpcFeetScreenHeight = 0.31f,
            NpcScreenSide = 0.1f,
            MinFieldOfView = 30f,
            MaxFieldOfView = 50f,
        };

        static readonly Vector3 Feet = new(2f, 0f, -1f);
        static readonly Vector3 Head = Feet + Vector3.up * 1.35f;

        /// <summary>A Birdy-sized NPC <paramref name="distance"/> m ahead (+Z) of the
        /// player's head, focus 0.85 m above its feet.</summary>
        static (Vector3 focus, Vector3 feet) NpcAhead(float distance, float focusHeight = 0.85f)
        {
            var feet = Feet + new Vector3(0f, 0f, distance);
            return (feet + Vector3.up * focusHeight, feet);
        }

        static DialogueShot Solve(float distance, float side = -0.8f, float focusHeight = 0.85f)
        {
            var (focus, feet) = NpcAhead(distance, focusHeight);
            return DialogueFraming.Solve(Head, Vector3.forward, focus, feet, Aspect, Settings(side));
        }

        /// <summary>A world point in the camera's local frame: x right, y up, z forward.</summary>
        static Vector3 InCamera(DialogueShot shot, Vector3 world)
            => Quaternion.Inverse(shot.Rotation) * (world - shot.Position);

        /// <summary>Viewport point (0–1, bottom left) of <paramref name="world"/>.</summary>
        static Vector2 Viewport(DialogueShot shot, Vector3 world)
        {
            Vector3 c = InCamera(shot, world);
            float tanHalf = Mathf.Tan(shot.FieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Vector2(0.5f + 0.5f * c.x / (c.z * tanHalf * Aspect), 0.5f + 0.5f * c.y / (c.z * tanHalf));
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        // -------------------------------------------------------------- position

        [Test]
        public void Camera_SitsBehindThePlayer_OnTheLineToTheNpc()
        {
            var shot = Solve(3f);
            Vector3 fromPivot = shot.Position - shot.Pivot;

            Assert.That(shot.Pivot, Is.EqualTo(Head), "the pivot is the player's head");
            Assert.That(fromPivot.z, Is.EqualTo(-1.7f).Within(Tolerance), "behind, along the NPC line");
            Assert.That(fromPivot.y, Is.EqualTo(0.3f).Within(Tolerance));
        }

        [TestCase(0.8f)]
        [TestCase(-0.8f)]
        public void Camera_IsOverTheConfiguredShoulder_WhenTheNpcIsFarEnough(float side)
        {
            // At 3 m the clearance needs only ~0.71 m, so the configured 0.8 holds.
            var shot = Solve(3f, side);
            Assert.That((shot.Position - shot.Pivot).x, Is.EqualTo(side).Within(Tolerance));
        }

        [Test]
        public void Camera_SwingsWiderWhenTheNpcIsClose()
        {
            float near = Mathf.Abs((Solve(1f).Position - Feet).x);
            float far = Mathf.Abs((Solve(3f).Position - Feet).x);
            Assert.That(near, Is.GreaterThan(far));
            Assert.That(near, Is.EqualTo(DialogueFraming.RequiredSideOffset(1f, 1.7f, 0.45f)).Within(1e-3f));
        }

        [TestCase(1.0f, 1.35f)]
        [TestCase(1.5f, 1.35f)]
        [TestCase(2.4f, 1.35f)]
        [TestCase(1.0f, 0.85f)]
        [TestCase(1.5f, 0.85f)]
        [TestCase(2.4f, 0.85f)]
        public void Sightline_ToTheNpcsHead_ClearsThePlayersHead(float distance, float focusHeight)
        {
            // QA: the chibi's hair hid Birdy. The sightline must pass 0.45 m from
            // the player's head, whatever the distance and the NPC's height.
            var shot = Solve(distance, focusHeight: focusHeight);
            var (focus, _) = NpcAhead(distance, focusHeight);

            Assert.That(DistanceToSegment(shot.Pivot, shot.Position, focus), Is.GreaterThanOrEqualTo(0.45f - 1e-3f));
        }

        [Test]
        public void Side_IsCappedAtTheMaximum_AndNeverNaN_WhenTheNpcIsInsideTheClearance()
        {
            var shot = Solve(0.3f);
            Assert.That(Mathf.Abs((shot.Position - shot.Pivot).x), Is.EqualTo(1.6f).Within(Tolerance));
            Assert.That(float.IsNaN(shot.Rotation.x) || float.IsNaN(shot.FieldOfView), Is.False);
            Assert.That(DialogueFraming.RequiredSideOffset(0.3f, 1.7f, 0.45f), Is.EqualTo(float.PositiveInfinity));
        }

        // ----------------------------------------------------------- composition

        [TestCase(-0.8f)]
        [TestCase(0.8f)]
        public void Npc_HeadOnTheUpperThird_OnTheOpenSide_PlayerOnTheOther(float side)
        {
            var shot = Solve(1.5f, side);
            var (focus, _) = NpcAhead(1.5f);
            Vector2 npc = Viewport(shot, focus);
            Vector2 player = Viewport(shot, shot.Pivot);

            Assert.That(npc.y, Is.EqualTo(0.66f).Within(1e-3f), "head on the upper third");
            Assert.That(npc.x, Is.EqualTo(0.5f + Mathf.Sign(side) * 0.1f).Within(1e-3f), "toward the open side");
            Assert.That(Mathf.Sign(player.x - 0.5f), Is.EqualTo(-Mathf.Sign(side)), "the shoulder fills the far side");
            Assert.That(InCamera(shot, focus).z, Is.GreaterThan(0f));
        }

        [TestCase(1.0f)]
        [TestCase(1.5f)]
        [TestCase(2.4f)]
        public void Npc_FeetLandAboveTheDialogueBox(float distance)
        {
            var shot = Solve(distance);
            var (_, feet) = NpcAhead(distance);
            Assert.That(Viewport(shot, feet).y, Is.InRange(0.27f, 0.36f), "just above the box (bottom 0.27)");
        }

        [Test]
        public void FieldOfView_IsClamped_ForVeryShortAndVeryTallNpcs()
        {
            Assert.That(Solve(1.5f, focusHeight: 0.1f).FieldOfView, Is.EqualTo(30f).Within(Tolerance));
            Assert.That(Solve(1.5f, focusHeight: 3f).FieldOfView, Is.EqualTo(50f).Within(Tolerance));
        }

        [TestCase(0.5f, 0.5f, 0f)]
        [TestCase(0.4f, 0.66f, -1.2f)]
        [TestCase(0.7f, 0.2f, 2f)]
        public void LookPlacing_PutsTheTargetExactlyThere_WithoutRoll(float x, float y, float targetRise)
        {
            var position = new Vector3(1f, 2f, -3f);
            var target = new Vector3(0.3f, 2f + targetRise, 1.5f);
            var rotation = DialogueFraming.LookPlacing(position, target, x, y, 40f, Aspect);
            var shot = new DialogueShot(position, position, rotation, 40f);

            Vector2 placed = Viewport(shot, target);
            Assert.That(placed.x, Is.EqualTo(x).Within(1e-3f));
            Assert.That(placed.y, Is.EqualTo(y).Within(1e-3f));
            Assert.That((rotation * Vector3.right).y, Is.EqualTo(0f).Within(1e-4f), "no roll");
        }

        [Test]
        public void Shot_RotatesWithTheScene()
        {
            var (focus, feet) = NpcAhead(2f);
            var a = DialogueFraming.Solve(Head, Vector3.forward, focus, feet, Aspect, Settings());
            var turn = Quaternion.Euler(0f, 70f, 0f);
            var b = DialogueFraming.Solve(turn * Head, turn * Vector3.forward, turn * focus, turn * feet,
                                          Aspect, Settings());

            Assert.That(Vector3.Distance(b.Position, turn * a.Position), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(b.Rotation, turn * a.Rotation), Is.LessThan(0.05f));
            Assert.That(b.FieldOfView, Is.EqualTo(a.FieldOfView).Within(1e-3f));
        }

        [Test]
        public void NpcStraightAbove_FallsBackToThePlayersFacing_WithoutNaN()
        {
            var above = Feet + Vector3.up * 3f;
            var shot = DialogueFraming.Solve(Head, Vector3.right, above, Feet, Aspect, Settings());

            Assert.That(float.IsNaN(shot.Position.x) || float.IsNaN(shot.Rotation.x), Is.False);
            Assert.That((shot.Position - shot.Pivot).x, Is.EqualTo(-1.7f).Within(Tolerance), "behind along facing");

            var degenerate = DialogueFraming.Solve(Head, Vector3.up, above, Feet, Aspect, Settings());
            Assert.That(float.IsNaN(degenerate.Position.x) || float.IsNaN(degenerate.Rotation.w), Is.False);
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
        public void Compose_AfterAPullIn_KeepsTheNpcWhereTheFramingPutIt()
        {
            var shot = Solve(1.5f);
            var (focus, feet) = NpcAhead(1.5f);
            Vector3 pulled = DialogueFraming.PullIn(shot.Pivot, shot.Position, 0.8f, 0.4f);
            var recomposed = DialogueFraming.Compose(shot.Pivot, pulled, focus, feet, Aspect, Settings());

            Vector2 npc = Viewport(recomposed, focus);
            Assert.That(npc.y, Is.EqualTo(0.66f).Within(1e-3f));
            Assert.That(npc.x, Is.EqualTo(0.4f).Within(1e-3f));
        }

        // ------------------------------------------------------------ blend path

        static Pose LookingAt(Vector3 position, Vector3 target)
            => new(position, Quaternion.LookRotation(target - position, Vector3.up));

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        [Test]
        public void OrbitBlend_EndsAreExact()
        {
            var from = LookingAt(new Vector3(1f, 2f, -4f), new Vector3(0f, 1.3f, 0f));
            var to = LookingAt(new Vector3(-0.8f, 1.6f, -1.7f), new Vector3(0f, 1.4f, 2f));
            var center = new Vector3(0f, 1.4f, 1f);

            var start = DialogueFraming.OrbitBlend(center, from, to, 0f);
            var end = DialogueFraming.OrbitBlend(center, from, to, 1f);

            Assert.That(start.position, Is.EqualTo(from.position));
            Assert.That(start.rotation, Is.EqualTo(from.rotation));
            Assert.That(end.position, Is.EqualTo(to.position));
            Assert.That(end.rotation, Is.EqualTo(to.rotation));
        }

        /// <summary>The nearest a blend from <paramref name="start"/> to the shot comes
        /// to the player or the NPC, flat, and the same for a straight lerp.</summary>
        static (float orbit, float lerp) NearestToBodies(Vector3 start, float side)
        {
            Vector3 player = Vector3.zero, npcFeet = new(0f, 0f, 2f), npcFocus = npcFeet + Vector3.up * 0.85f;
            var shot = DialogueFraming.Solve(player + Vector3.up * 1.35f, Vector3.forward, npcFocus, npcFeet,
                                             Aspect, Settings(side));
            var from = LookingAt(start, new Vector3(0f, 1.35f, 0f));
            var to = new Pose(shot.Position, shot.Rotation);
            var center = (shot.Pivot + npcFocus) * 0.5f;

            float nearestLerp = float.MaxValue, nearestOrbit = float.MaxValue;
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                Vector3 lerp = Vector3.Lerp(from.position, to.position, t);
                Vector3 orbit = DialogueFraming.OrbitBlend(center, from, to, t).position;
                nearestLerp = Mathf.Min(nearestLerp, FlatDistance(lerp, player), FlatDistance(lerp, npcFeet));
                nearestOrbit = Mathf.Min(nearestOrbit, FlatDistance(orbit, player), FlatDistance(orbit, npcFeet));
            }
            return (nearestOrbit, nearestLerp);
        }

        [Test]
        public void OrbitBlend_CameraInFrontOfThePlayer_SwingsAroundInsteadOfThroughThem()
        {
            // The orbit camera beyond the NPC, looking back at the player: the shot
            // is behind the player, so a straight lerp cuts through both of them.
            var (orbit, lerp) = NearestToBodies(new Vector3(0f, 2f, 4.5f), side: -0.8f);
            Assert.That(lerp, Is.LessThan(0.5f), "precondition: a straight lerp hits a body");
            Assert.That(orbit, Is.GreaterThan(1f), "the swing keeps a body's width clear");
        }

        [TestCase(-0.8f, 1.5f)]
        [TestCase(-0.8f, -1.5f)]
        [TestCase(0.8f, 1.5f)]
        [TestCase(0.8f, -1.5f)]
        public void OrbitBlend_FromBehindEitherShoulder_NeverPassesThroughThePlayer(float side, float startX)
        {
            var (orbit, _) = NearestToBodies(new Vector3(startX, 2.3f, -3.5f), side);
            Assert.That(orbit, Is.GreaterThan(1f));
        }

        [Test]
        public void OrbitBlend_MidSwing_KeepsTheSubjectsInView()
        {
            Vector3 npcFeet = new(0f, 0f, 2f), npcFocus = npcFeet + Vector3.up * 0.85f;
            var shot = DialogueFraming.Solve(Vector3.up * 1.35f, Vector3.forward, npcFocus, npcFeet, Aspect,
                                             Settings());
            var from = LookingAt(new Vector3(0.1f, 2f, 4.5f), new Vector3(0f, 1.35f, 0f));
            var center = (shot.Pivot + npcFocus) * 0.5f;

            var mid = DialogueFraming.OrbitBlend(center, from, new Pose(shot.Position, shot.Rotation), 0.5f);

            Assert.That(Vector3.Angle(mid.forward, center - mid.position), Is.LessThan(60f));
            Assert.That(float.IsNaN(mid.position.x) || float.IsNaN(mid.rotation.w), Is.False);
        }

        [Test]
        public void OrbitBlend_EndOnTheCentreAxis_FallsBackToAStraightLerp()
        {
            var center = new Vector3(0f, 1f, 0f);
            var from = LookingAt(new Vector3(0f, 5f, 0.01f), center);
            var to = LookingAt(new Vector3(0f, 1.5f, -2f), center);

            var mid = DialogueFraming.OrbitBlend(center, from, to, 0.5f);

            Assert.That(Vector3.Distance(mid.position, Vector3.Lerp(from.position, to.position, 0.5f)),
                        Is.LessThan(1e-4f));
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
