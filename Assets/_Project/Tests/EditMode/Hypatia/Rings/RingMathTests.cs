using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Rings;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Rings
{
    /// <summary>
    /// The rules a player learns from Celestial Rings: where each gap will be,
    /// what counts as standing in it, what height clears a ring and that a ring
    /// can neither skip over you nor hit you twice.
    /// </summary>
    public class RingMathTests
    {
        const float Eps = 1e-3f;

        // ─── Angles ─────────────────────────────────────────────────────────

        [TestCase(-90f, 270f)]
        [TestCase(720f, 0f)]
        [TestCase(405f, 45f)]
        [TestCase(359.5f, 359.5f)]
        [TestCase(-720f, 0f)]
        public void Normalise_WrapsIntoZeroTo360(float input, float expected)
        {
            Assert.That(RingMath.NormaliseDegrees(input), Is.EqualTo(expected).Within(Eps));
        }

        [TestCase(50f, 45f)]
        [TestCase(22.5f, 45f)]   // halves round up, not to even
        [TestCase(30f, 45f)]
        [TestCase(20f, 0f)]
        [TestCase(-50f, -45f)]
        [TestCase(180f, 180f)]
        public void Snap_RoundsToNearest45(float input, float expected)
        {
            Assert.That(RingMath.Snap(input, 45f), Is.EqualTo(expected).Within(Eps));
        }

        [Test]
        public void Snap_NonPositiveGrid_LeavesTheValue()
        {
            Assert.That(RingMath.Snap(37f, 0f), Is.EqualTo(37f));
        }

        // ─── Gap spiral ─────────────────────────────────────────────────────

        [Test]
        public void FirstGap_IsSnappedToTheGrid()
        {
            Assert.That(RingMath.GapCentreDegrees(0, 50f, 1, 45f, 45f), Is.EqualTo(45f).Within(Eps));
        }

        [Test]
        public void Spiral_StepsOneSnappedStepPerRing()
        {
            for (int i = 0; i < 8; i++)
                Assert.That(RingMath.GapCentreDegrees(i, 90f, 1, 45f, 45f),
                            Is.EqualTo(RingMath.NormaliseDegrees(90f + 45f * i)).Within(Eps),
                            $"ring {i}");
        }

        [Test]
        public void Spiral_StepIsSnapped()
        {
            // A 30° step on a 45° grid becomes 45°, so gaps stay dash-aligned.
            Assert.That(RingMath.GapCentreDegrees(1, 90f, 1, 30f, 45f), Is.EqualTo(135f).Within(Eps));
            // A step that snaps to 0 keeps the gap in place.
            Assert.That(RingMath.GapCentreDegrees(5, 90f, 1, 20f, 45f), Is.EqualTo(90f).Within(Eps));
        }

        [Test]
        public void Spiral_NegativeSign_TurnsTheOtherWay()
        {
            Assert.That(RingMath.GapCentreDegrees(1, 90f, -1, 45f, 45f), Is.EqualTo(45f).Within(Eps));
            Assert.That(RingMath.GapCentreDegrees(3, 0f, -1, 45f, 45f), Is.EqualTo(225f).Within(Eps));
        }

        [Test]
        public void Spiral_WrapsAround360()
        {
            Assert.That(RingMath.GapCentreDegrees(7, 0f, 1, 45f, 45f), Is.EqualTo(315f).Within(Eps));
            Assert.That(RingMath.GapCentreDegrees(8, 0f, 1, 45f, 45f), Is.EqualTo(0f).Within(Eps));
            Assert.That(RingMath.GapCentreDegrees(0, 350f, 1, 45f, 45f), Is.EqualTo(0f).Within(Eps));
            Assert.That(RingMath.GapCentreDegrees(1000, 0f, 1, 45f, 45f),
                        Is.EqualTo(0f).Within(Eps)); // 1000 × 45 = 125 turns
        }

        // ─── Tall cadence ───────────────────────────────────────────────────

        [Test]
        public void TallEveryZero_NeverTall()
        {
            for (int i = 0; i < 12; i++) Assert.IsFalse(RingMath.IsTall(i, 0), $"ring {i}");
        }

        [Test]
        public void TallEveryOne_AlwaysTall()
        {
            for (int i = 0; i < 12; i++) Assert.IsTrue(RingMath.IsTall(i, 1), $"ring {i}");
        }

        [Test]
        public void TallEveryThree_IsTheThirdSixthNinth()
        {
            for (int i = 0; i < 12; i++)
                Assert.That(RingMath.IsTall(i, 3), Is.EqualTo(i == 2 || i == 5 || i == 8 || i == 11),
                            $"ring {i}");
        }

        // ─── Gap test ───────────────────────────────────────────────────────

        [TestCase(0f, 0f, 30f, true)]
        [TestCase(10f, 0f, 30f, true)]
        [TestCase(350f, 0f, 30f, true)]   // across 0/360
        [TestCase(345f, 0f, 30f, true)]   // edge counts as inside
        [TestCase(16f, 0f, 30f, false)]
        [TestCase(180f, 0f, 30f, false)]
        [TestCase(5f, 350f, 40f, true)]   // gap straddling 0
        [TestCase(335f, 5f, 40f, false)]
        public void IsInGap_HandlesWraparound(float az, float centre, float width, bool expected)
        {
            Assert.That(RingMath.IsInGap(az, centre, width), Is.EqualTo(expected));
        }

        [Test]
        public void CapsuleMustFitTheGap_NotJustItsCentre()
        {
            // 30° gap, 0.35 m capsule 2 m out covers ±10.1°: centred fits, 10° off does not.
            Assert.IsTrue(RingMath.CapsuleFitsGap(0f, 2f, 0.35f, 0f, 30f));
            Assert.IsTrue(RingMath.IsInGap(10f, 0f, 30f));
            Assert.IsFalse(RingMath.CapsuleFitsGap(10f, 2f, 0.35f, 0f, 30f));
        }

        [Test]
        public void CapsuleWidening_ShrinksWithDistance_AndWraps()
        {
            // 12 m out the same capsule covers only ±1.7°.
            Assert.IsTrue(RingMath.CapsuleFitsGap(10f, 12f, 0.35f, 0f, 30f));
            Assert.IsTrue(RingMath.CapsuleFitsGap(350f, 12f, 0.35f, 0f, 30f));
            Assert.IsFalse(RingMath.CapsuleFitsGap(14f, 12f, 0.35f, 0f, 30f));
        }

        [Test]
        public void CapsuleCoveringTheCentre_NeverFits()
        {
            Assert.IsFalse(RingMath.CapsuleFitsGap(0f, 0.2f, 0.35f, 0f, 170f));
        }

        // ─── Swept contact ──────────────────────────────────────────────────

        [Test]
        public void RingPassingAStandingPlayer_Touches()
        {
            Assert.IsTrue(RingMath.BandSweepsPlayer(2.9f, 3.0f, 0.5f, 3f, 3f, 0.35f));
        }

        [Test]
        public void RingStillFarAway_DoesNotTouch()
        {
            Assert.IsFalse(RingMath.BandSweepsPlayer(1.5f, 1.6f, 0.5f, 5f, 5f, 0.35f));
        }

        [Test]
        public void FastRing_StepLargerThanThickness_DoesNotTunnel()
        {
            // 4 m in one frame with a 0.5 m band: the player at 4 m is between both ends.
            Assert.IsTrue(RingMath.BandSweepsPlayer(2f, 6f, 0.5f, 4f, 4f, 0.35f));
            Assert.IsTrue(RingMath.BandSweepsPlayer(1f, 10f, 0.1f, 4f, 4f, 0.1f));
        }

        [Test]
        public void PlayerRunningAtTheRing_IsHitEvenIfBothJumpPastEachOther()
        {
            Assert.IsTrue(RingMath.BandSweepsPlayer(3f, 3.2f, 0.1f, 3.6f, 2.6f, 0.05f));
        }

        [Test]
        public void PlayerFleeingJustAheadOfTheRing_IsNotHit()
        {
            // Swept intervals [2.75, 3.75] and [3.55, 4.75] overlap, but the
            // separation stays 0.9 m (> 0.25 + 0.35) all frame: no contact.
            Assert.IsFalse(RingMath.BandSweepsPlayer(3f, 3.5f, 0.5f, 3.9f, 4.4f, 0.35f));
        }

        [Test]
        public void PlayerRunningOutwardAheadAtRingSpeed_IsNeverHit()
        {
            const float speed = 4f, dt = 1f / 60f;
            float ring = 1.5f, player = 3f;
            for (int f = 0; f < 120; f++)
            {
                float rPrev = ring, pPrev = player;
                ring += speed * dt;
                player += speed * dt;
                Assert.IsFalse(RingMath.BandSweepsPlayer(rPrev, ring, 0.5f, pPrev, player, 0.35f),
                               $"frame {f}");
            }
        }

        [Test]
        public void LowRing_PlayerFeetAboveItsTop_IsNotHit()
        {
            Assert.IsFalse(Hit(tall: false, feet: 1.0f, az: 180f));
            Assert.IsFalse(Hit(tall: false, feet: 0.8f, az: 180f)); // exactly at the top clears
        }

        [Test]
        public void LowRing_PlayerOnTheFloor_IsHit()
        {
            Assert.IsTrue(Hit(tall: false, feet: 0f, az: 180f));
            Assert.IsTrue(Hit(tall: false, feet: 0.69f, az: 180f)); // a tap jump does not clear it
        }

        [Test]
        public void TallRing_HitsAtAnyHeight()
        {
            Assert.IsTrue(Hit(tall: true, feet: 20f, az: 180f));
        }

        [Test]
        public void PlayerInTheGap_IsNotHit_EvenByATallRing()
        {
            Assert.IsFalse(Hit(tall: true, feet: 0f, az: 0f));
            Assert.IsFalse(Hit(tall: true, feet: 20f, az: 0f));
        }

        static bool Hit(bool tall, float feet, float az)
        {
            // Gap centred on 0°, 30° wide; player 6 m out, ring sweeping 5.9 → 6.0 m.
            return RingMath.RingHitsPlayer(5.9f, 6f, 0.5f, 0f, 30f, tall, 0.8f,
                                           6f, 6f, az, feet, 0.35f);
        }

        // ─── Azimuth convention ─────────────────────────────────────────────

        [Test]
        public void Azimuth_ZeroIsForward_GrowingTowardPlusX()
        {
            Assert.That(RingMath.AzimuthDegrees(new Vector2(0f, 1f)), Is.EqualTo(0f).Within(Eps));
            Assert.That(RingMath.AzimuthDegrees(new Vector2(1f, 0f)), Is.EqualTo(90f).Within(Eps));
            Assert.That(RingMath.AzimuthDegrees(new Vector2(0f, -1f)), Is.EqualTo(180f).Within(Eps));
            Assert.That(RingMath.AzimuthDegrees(new Vector2(-1f, 0f)), Is.EqualTo(270f).Within(Eps));
        }

        [Test]
        public void DirectionFromAzimuth_InvertsAzimuth()
        {
            Vector3 east = RingMath.DirectionFromAzimuth(90f);
            Assert.That((east - Vector3.right).magnitude, Is.LessThan(Eps));
            for (float a = 0f; a < 360f; a += 22.5f)
            {
                Vector3 d = RingMath.DirectionFromAzimuth(a);
                Assert.That(RingMath.AzimuthDegrees(new Vector2(d.x, d.z)), Is.EqualTo(a).Within(Eps));
            }
        }
    }
}
