using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down the maths SwingTwist uses to put a swing's hip turn back on the
    /// spine. It covers where a baked track is sampled, how a crossfade blends
    /// two tracks, and that the full-weight correction lands the hips exactly
    /// where the clip has them.
    ///
    /// The hemisphere cases are here because "the bake stored a sample as −q
    /// and the slerp went the long way round" was a suspect for the third swing
    /// contorting. These tests show the maths cannot do that. The measured
    /// tracks had no sign flips anyway (see Docs/Systems/SwordAbilities.md §5).
    /// </summary>
    public class SwingTwistMathTests
    {
        const float Tolerance = 1e-3f;

        static Quaternion Yaw(float degrees) => Quaternion.AngleAxis(degrees, Vector3.up);

        static Quaternion Negate(Quaternion q) => new(-q.x, -q.y, -q.z, -q.w);

        static void AssertSameRotation(Quaternion expected, Quaternion actual)
            => Assert.Less(Quaternion.Angle(expected, actual), Tolerance,
                           $"expected {expected.eulerAngles}, got {actual.eulerAngles}");

        // ------------------------------------------------------------ SampleTrack

        [Test]
        public void SampleTrack_ClampsToTheEnds()
        {
            var track = new[] { Yaw(0f), Yaw(30f), Yaw(60f) };
            AssertSameRotation(Yaw(0f), SwingTwistMath.SampleTrack(track, -0.5f));
            AssertSameRotation(Yaw(60f), SwingTwistMath.SampleTrack(track, 1f));
            // A one-shot past its end holds the last frame; it does not wrap.
            AssertSameRotation(Yaw(60f), SwingTwistMath.SampleTrack(track, 1.7f));
        }

        [Test]
        public void SampleTrack_InterpolatesBetweenNeighbours()
        {
            var track = new[] { Yaw(0f), Yaw(30f), Yaw(60f) };
            AssertSameRotation(Yaw(15f), SwingTwistMath.SampleTrack(track, 0.25f));
            AssertSameRotation(Yaw(45f), SwingTwistMath.SampleTrack(track, 0.75f));
        }

        [Test]
        public void SampleTrack_SampleStoredInTheOtherHemisphere_SameRotation()
        {
            var plain = new[] { Yaw(10f), Yaw(40f) };
            var flipped = new[] { Yaw(10f), Negate(Yaw(40f)) };
            for (float t = 0f; t <= 1f; t += 0.125f)
                AssertSameRotation(SwingTwistMath.SampleTrack(plain, t),
                                   SwingTwistMath.SampleTrack(flipped, t));
            // And it goes the short way: halfway between 10° and 40° is 25°.
            AssertSameRotation(Yaw(25f), SwingTwistMath.SampleTrack(flipped, 0.5f));
        }

        [Test]
        public void SampleTrack_EmptyOrSingle_IsSafe()
        {
            AssertSameRotation(Quaternion.identity, SwingTwistMath.SampleTrack(null, 0.5f));
            AssertSameRotation(Quaternion.identity, SwingTwistMath.SampleTrack(new Quaternion[0], 0.5f));
            AssertSameRotation(Yaw(20f), SwingTwistMath.SampleTrack(new[] { Yaw(20f) }, 0.5f));
        }

        // ------------------------------------------------------------ BlendStates

        [Test]
        public void BlendStates_StartsOnCurrentAndEndsOnNext()
        {
            var a = Yaw(-30f);
            var b = Quaternion.Euler(10f, 50f, 0f);
            AssertSameRotation(a, SwingTwistMath.BlendStates(a, b, 0f));
            AssertSameRotation(b, SwingTwistMath.BlendStates(a, b, 1f));
        }

        [Test]
        public void BlendStates_NextInTheOtherHemisphere_StillTakesTheShortWay()
        {
            var mid = SwingTwistMath.BlendStates(Yaw(-30f), Negate(Yaw(50f)), 0.5f);
            AssertSameRotation(Yaw(10f), mid);
        }

        // ------------------------------------------------------------- Correction

        [Test]
        public void Correction_FullWeight_PutsTheHipsWhereTheClipHasThem()
        {
            var animatorRotation = Quaternion.Euler(0f, 135f, 0f);
            var clipHips = Quaternion.Euler(-14f, 78f, -8f);      // a side-on, tilted pelvis
            var currentHips = Quaternion.Euler(5f, 200f, 3f);     // whatever locomotion left
            var turn = SwingTwistMath.Correction(animatorRotation, clipHips, currentHips, 1f);
            AssertSameRotation(animatorRotation * clipHips, turn * currentHips);
        }

        [Test]
        public void Correction_ZeroWeight_LeavesThePoseAlone()
        {
            var turn = SwingTwistMath.Correction(Quaternion.identity, Yaw(70f), Yaw(-20f), 0f);
            AssertSameRotation(Quaternion.identity, turn);
        }

        [Test]
        public void Correction_PartialWeight_IsThatFractionOfTheTurn()
        {
            var turn = SwingTwistMath.Correction(Quaternion.identity, Yaw(60f), Quaternion.identity, 0.25f);
            AssertSameRotation(Yaw(15f), turn);
        }

        [Test]
        public void Correction_ClipHipsInTheOtherHemisphere_SameTurnAtEveryWeight()
        {
            var animatorRotation = Quaternion.Euler(0f, 40f, 0f);
            var currentHips = Quaternion.Euler(0f, 95f, 0f);
            var clipHips = Quaternion.Euler(12f, -30f, 4f);
            for (float w = 0f; w <= 1f; w += 0.25f)
                AssertSameRotation(
                    SwingTwistMath.Correction(animatorRotation, clipHips, currentHips, w),
                    SwingTwistMath.Correction(animatorRotation, Negate(clipHips), currentHips, w));
        }
    }
}
