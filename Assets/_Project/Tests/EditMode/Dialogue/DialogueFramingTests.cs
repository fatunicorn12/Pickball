using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Pins the conversation shot: behind the player on the player → NPC line,
    /// on the configured shoulder, wide enough that every sightline to the NPC
    /// (head and feet, across its width) clears the player's body capsule at any
    /// distance and height, the NPC's head at the configured screen point with
    /// its feet above the box, never NaN, pulled in (never pushed out) when
    /// something blocks it, and a blend path that swings around the subjects
    /// instead of through them.
    /// </summary>
    public class DialogueFramingTests
    {
        const float Tolerance = 1e-4f;
        const float Aspect = 16f / 9f;
        const float Clearance = 0.7f;     // controller radius + cloak margin
        const float HalfWidth = 0.3f;
        const float HeadTop = 0.45f;       // hair above the Head bone
        const float HeadScreen = 0.62f;

        static DialogueFramingSettings Settings(float side = -0.8f) => new()
        {
            PushInDistance = 1.7f,
            CameraHeight = 0.1f,
            SideOffset = side,
            PlayerClearance = Clearance,
            NpcHalfWidth = HalfWidth,
            MaxSideOffset = 2f,
            NpcHeadScreenHeight = HeadScreen,
            NpcFeetScreenHeight = 0.31f,
            NpcScreenSide = 0.15f,
            MinFieldOfView = 40f,
            MaxFieldOfView = 55f,
        };

        static readonly Vector3 Feet = new(2f, 0f, -1f);
        static readonly Vector3 Head = Feet + Vector3.up * 1.35f;

        /// <summary>A Birdy-sized NPC <paramref name="distance"/> m ahead (+Z) of the
        /// player's axis, focus 0.85 m above its feet, its ground
        /// <paramref name="drop"/> m below the player's.</summary>
        static (Vector3 focus, Vector3 feet) NpcAhead(float distance, float focusHeight = 0.85f, float drop = 0f)
        {
            var feet = Feet + new Vector3(0f, -drop, distance);
            return (feet + Vector3.up * focusHeight, feet);
        }

        static DialogueShot Solve(float distance, float side = -0.8f, float focusHeight = 0.85f, float drop = 0f)
        {
            var (focus, feet) = NpcAhead(distance, focusHeight, drop);
            return DialogueFraming.Solve(Head, Feet, Vector3.forward, focus, feet, Aspect, Settings(side));
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

        /// <summary>Shortest distance between segments ab and cd, by fine sampling of
        /// ab against the exact point-to-segment distance to cd.</summary>
        static float SegmentDistance(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float best = float.MaxValue;
            for (int i = 0; i <= 400; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 400f);
                Vector3 cd = d - c;
                float t = Mathf.Clamp01(Vector3.Dot(p - c, cd) / cd.sqrMagnitude);
                best = Mathf.Min(best, Vector3.Distance(p, c + cd * t));
            }
            return best;
        }

        // -------------------------------------------------------------- position

        [Test]
        public void Camera_SitsBehindThePlayer_OnTheLineToTheNpc_AboveTheHead()
        {
            var shot = Solve(2.4f);
            Vector3 fromAxis = shot.Position - Feet;

            Assert.That(shot.Pivot, Is.EqualTo(Head), "the pivot is the player's head");
            Assert.That(fromAxis.z, Is.EqualTo(-1.7f).Within(Tolerance), "behind, along the NPC line");
            Assert.That(shot.Position.y, Is.EqualTo(Head.y + 0.1f).Within(Tolerance));
        }

        [TestCase(0.8f)]
        [TestCase(-0.8f)]
        public void Camera_IsOverTheConfiguredShoulder_WhenNothingNeedsClearing(float side)
        {
            var settings = Settings(side);
            settings.PlayerClearance = 0f;
            settings.NpcHalfWidth = 0f;
            var (focus, feet) = NpcAhead(2.4f);
            var shot = DialogueFraming.Solve(Head, Feet, Vector3.forward, focus, feet, Aspect, settings);
            Assert.That((shot.Position - Feet).x, Is.EqualTo(side).Within(Tolerance));
        }

        [Test]
        public void Camera_SwingsWiderWhenTheNpcIsCloser()
        {
            float near = Mathf.Abs((Solve(1.5f).Position - Feet).x);
            float far = Mathf.Abs((Solve(2.4f).Position - Feet).x);
            Assert.That(near, Is.GreaterThan(far));
            Assert.That(far, Is.EqualTo(DialogueFraming.RequiredSideOffset(2.4f, 1.7f, Clearance, HalfWidth))
                                .Within(1e-3f));
        }

        [Test]
        public void Camera_TooCloseToClearFromBehind_ComesForwardToASideShot()
        {
            // At 1.0 m, clearing 0.7 m round the player from 1.7 m back needs ~4 m of
            // swing; the cap is 2 m, so the camera comes forward instead.
            var shot = Solve(1.0f);
            Vector3 fromAxis = shot.Position - Feet;
            Assert.That(Mathf.Abs(fromAxis.x), Is.LessThanOrEqualTo(2f + Tolerance));
            Assert.That(fromAxis.z, Is.GreaterThan(-1.7f), "pushed in less than configured");
            Assert.That(DialogueFraming.RequiredSideOffset(1f, -fromAxis.z, Clearance, HalfWidth),
                        Is.LessThanOrEqualTo(2f + 1e-3f));
        }

        [TestCase(1.0f, 0.85f, 0f)]
        [TestCase(1.5f, 0.85f, 0f)]
        [TestCase(2.4f, 0.85f, 0f)]
        [TestCase(1.0f, 1.35f, 0f)]
        [TestCase(1.5f, 1.35f, 0f)]
        [TestCase(2.4f, 1.35f, 0f)]
        [TestCase(1.0f, 0.85f, 0.8f)]
        [TestCase(1.5f, 0.85f, 0.8f)]
        [TestCase(2.4f, 0.85f, 0.8f)]
        public void Sightlines_ToTheNpcsHeadAndFeet_AcrossItsWidth_ClearThePlayersBody(
            float distance, float focusHeight, float npcDrop)
        {
            // QA: the cloak hid most of Birdy. Every sightline from the camera to the
            // NPC (head and feet, either edge) must clear the player's capsule: his
            // axis from the feet to the top of the hair, radius = controller + cloak.
            var shot = Solve(distance, focusHeight: focusHeight, drop: npcDrop);
            var (focus, feet) = NpcAhead(distance, focusHeight, npcDrop);
            Vector3 axisBottom = Feet, axisTop = Head + Vector3.up * HeadTop;

            foreach (float edge in new[] { -HalfWidth, 0f, HalfWidth })
            {
                Vector3 across = Vector3.right * edge;
                foreach (var target in new[] { focus + across, feet + across })
                    Assert.That(SegmentDistance(shot.Position, target, axisBottom, axisTop),
                                Is.GreaterThanOrEqualTo(Clearance - 2e-3f),
                                $"edge {edge:+0.0;-0.0;0} to {(target.y > feet.y ? "head" : "feet")}");
            }
        }

        [Test]
        public void RequiredSideOffset_WithNoWidth_IsTheHeadOnlyFormula()
        {
            float d = 1.5f, p = 1.7f, r = 0.45f;
            Assert.That(DialogueFraming.RequiredSideOffset(d, p, r, 0f),
                        Is.EqualTo(r * (d + p) / Mathf.Sqrt(d * d - r * r)).Within(1e-4f));
            Assert.That(DialogueFraming.RequiredSideOffset(0.3f, p, r, 0.3f), Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void Side_IsCappedAtTheMaximum_AndNeverNaN_WhenTheNpcIsInsideTheClearance()
        {
            var shot = Solve(0.5f);
            Assert.That(Mathf.Abs((shot.Position - Feet).x), Is.EqualTo(2f).Within(Tolerance));
            Assert.That(float.IsNaN(shot.Rotation.x) || float.IsNaN(shot.FieldOfView), Is.False);
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

            Assert.That(npc.y, Is.EqualTo(HeadScreen).Within(1e-3f), "head on the upper third");
            Assert.That(npc.x, Is.EqualTo(0.5f + Mathf.Sign(side) * 0.15f).Within(1e-3f), "toward the open side");
            Assert.That(Mathf.Sign(player.x - 0.5f), Is.EqualTo(-Mathf.Sign(side)), "the shoulder fills the far side");
            Assert.That(InCamera(shot, focus).z, Is.GreaterThan(0f));
        }

        [TestCase(1.5f)]
        [TestCase(2.4f)]
        public void PlayerHead_IsInFrame_AsTheFramingShoulder(float distance)
        {
            var shot = Solve(distance);
            Vector2 head = Viewport(shot, Head);
            Assert.That(InCamera(shot, Head).z, Is.GreaterThan(0f));
            Assert.That(head.x, Is.InRange(0.5f, 1.05f), "on the right of a left-shoulder shot");
            Assert.That(head.y, Is.InRange(0.3f, 1.05f));
        }

        [TestCase(1.0f)]
        [TestCase(1.5f)]
        [TestCase(2.4f)]
        public void Npc_FeetLandAboveTheDialogueBox(float distance)
        {
            var shot = Solve(distance);
            var (_, feet) = NpcAhead(distance);
            Assert.That(Viewport(shot, feet).y, Is.GreaterThan(0.29f), "above the box (bottom 0.27)");
        }

        [Test]
        public void FieldOfView_IsClamped_ForVeryShortAndVeryTallNpcs()
        {
            Assert.That(Solve(1.5f, focusHeight: 0.1f).FieldOfView, Is.EqualTo(40f).Within(Tolerance));
            Assert.That(Solve(1.5f, focusHeight: 3f).FieldOfView, Is.EqualTo(55f).Within(Tolerance));
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
            var a = DialogueFraming.Solve(Head, Feet, Vector3.forward, focus, feet, Aspect, Settings());
            var turn = Quaternion.Euler(0f, 70f, 0f);
            var b = DialogueFraming.Solve(turn * Head, turn * Feet, turn * Vector3.forward, turn * focus, turn * feet,
                                          Aspect, Settings());

            Assert.That(Vector3.Distance(b.Position, turn * a.Position), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(b.Rotation, turn * a.Rotation), Is.LessThan(0.05f));
            Assert.That(b.FieldOfView, Is.EqualTo(a.FieldOfView).Within(1e-3f));
        }

        [Test]
        public void NpcOnThePlayersAxis_FallsBackToThePlayersFacing_WithoutNaN()
        {
            var above = Feet + Vector3.up * 3f;
            var shot = DialogueFraming.Solve(Head, Feet, Vector3.right, above, Feet, Aspect, Settings());

            Assert.That(float.IsNaN(shot.Position.x) || float.IsNaN(shot.Rotation.x), Is.False);
            Assert.That((shot.Position - Feet).x, Is.LessThanOrEqualTo(Tolerance), "behind (or level) along facing");

            var degenerate = DialogueFraming.Solve(Head, Feet, Vector3.up, above, Feet, Aspect, Settings());
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
            Assert.That(npc.y, Is.EqualTo(HeadScreen).Within(1e-3f));
            Assert.That(npc.x, Is.EqualTo(0.35f).Within(1e-3f));
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
            var shot = DialogueFraming.Solve(player + Vector3.up * 1.35f, player, Vector3.forward, npcFocus, npcFeet,
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
            var (orbit, lerp) = NearestToBodies(new Vector3(1.5f, 2f, 4.5f), side: -0.8f);
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
            var shot = DialogueFraming.Solve(Vector3.up * 1.35f, Vector3.zero, Vector3.forward, npcFocus, npcFeet,
                                             Aspect, Settings());
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
