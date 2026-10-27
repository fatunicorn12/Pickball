using NUnit.Framework;
using Pickleball.Sandbox.Weapon;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down the contract WeaponClearance relies on: turning the blade by
    /// <see cref="ClearanceSolver.RequiredAngle"/> about
    /// <see cref="ClearanceSolver.AwayAxis"/> takes it exactly clear of the
    /// sphere — not short (the blade would still clip) and not past it (the arm
    /// would be wrenched further than needed, and the correction would pop
    /// instead of fading as the animation clears by itself).
    /// </summary>
    public class ClearanceSolverTests
    {
        const float Tolerance = 1e-3f;

        static readonly Vector3 Centre = new(0f, 1f, 0f);
        const float Radius = 0.3f;

        /// <summary>Closest distance from the blade segment to a point.</summary>
        static float SegmentDistance(Vector3 grip, Vector3 dir, float length, Vector3 point)
        {
            float t = Mathf.Clamp(Vector3.Dot(point - grip, dir), 0f, length);
            return (grip + dir * t - point).magnitude;
        }

        /// <summary>Apply the solver's correction, the way WeaponClearance does.</summary>
        static Vector3 Corrected(Vector3 grip, Vector3 dir, float length, out float angle)
        {
            angle = ClearanceSolver.RequiredAngle(grip, dir, length, Centre, Radius);
            Vector3 axis = ClearanceSolver.AwayAxis(grip, dir, Centre, Vector3.right);
            return Quaternion.AngleAxis(angle, axis) * dir;
        }

        [Test]
        public void BladePointingAway_NeedsNoTurn()
        {
            Vector3 grip = new(0f, 0.4f, 0f);
            Assert.AreEqual(0f, ClearanceSolver.RequiredAngle(grip, Vector3.down, 0.85f, Centre, Radius));
        }

        [Test]
        public void LongBladeThroughCentre_TurnsToTangent()
        {
            Vector3 grip = new(0f, 0.4f, 0f);    // 0.6 m below the centre
            Vector3 dir = Vector3.up;            // dead at it
            float expected = Mathf.Asin(Radius / 0.6f) * Mathf.Rad2Deg;   // 30°

            Vector3 fixedDir = Corrected(grip, dir, 0.85f, out float angle);

            Assert.AreEqual(expected, angle, Tolerance);
            Assert.AreEqual(Radius, SegmentDistance(grip, fixedDir, 0.85f, Centre), Tolerance);
        }

        [Test]
        public void ShortBlade_OnlyMovesTheTipOut()
        {
            // 0.35 m blade can't reach the tangent point (0.52 m away), so it
            // only has to bring its tip to the surface — less than asin(R/D).
            Vector3 grip = new(0f, 0.4f, 0f);
            Vector3 dir = Vector3.up;
            const float length = 0.35f;

            Vector3 fixedDir = Corrected(grip, dir, length, out float angle);

            Assert.Less(angle, Mathf.Asin(Radius / 0.6f) * Mathf.Rad2Deg);
            Assert.AreEqual(Radius, (grip + fixedDir * length - Centre).magnitude, Tolerance);
        }

        [Test]
        public void GrazingBlade_TurnsOnlyTheShortfall()
        {
            // Already 20° off the centre line; tangent is at 30°, so 10° more.
            Vector3 grip = new(0f, 0.4f, 0f);
            Vector3 dir = Quaternion.AngleAxis(20f, Vector3.forward) * Vector3.up;

            Vector3 fixedDir = Corrected(grip, dir, 0.85f, out float angle);

            Assert.AreEqual(10f, angle, Tolerance);
            Assert.AreEqual(30f, Vector3.Angle(fixedDir, Centre - grip), Tolerance);
        }

        [Test]
        public void AwayAxis_IncreasesTheAngleFromTheCentre()
        {
            Vector3 grip = new(0.1f, 0.4f, 0.05f);
            Vector3 dir = new Vector3(0.1f, 1f, -0.2f).normalized;
            float before = Vector3.Angle(dir, Centre - grip);

            Vector3 axis = ClearanceSolver.AwayAxis(grip, dir, Centre, Vector3.right);
            Vector3 turned = Quaternion.AngleAxis(5f, axis) * dir;

            Assert.AreEqual(before + 5f, Vector3.Angle(turned, Centre - grip), Tolerance);
        }

        [Test]
        public void AwayAxis_FallsBackWhenAimedAtCentre()
        {
            Vector3 grip = new(0f, 0.4f, 0f);
            Vector3 axis = ClearanceSolver.AwayAxis(grip, Vector3.up, Centre, Vector3.right);

            Assert.AreEqual(1f, axis.magnitude, Tolerance);
            Assert.AreEqual(0f, Vector3.Dot(axis, Vector3.up), Tolerance);
        }

        [Test]
        public void GripInsideSphere_StillPushesFiniteAndAway()
        {
            // The hand itself is in the head (front-flip tuck). No turn clears
            // it, but the push must stay finite so it fades rather than snaps.
            Vector3 grip = new(0f, 0.85f, 0f);
            float angle = ClearanceSolver.RequiredAngle(grip, Vector3.up, 0.85f, Centre, Radius);

            Assert.IsFalse(float.IsNaN(angle));
            Assert.Greater(angle, 0f);
            Assert.LessOrEqual(angle, 90f);
        }

        [Test]
        public void Correction_FadesToZeroAtTheTangent()
        {
            Vector3 grip = new(0f, 0.4f, 0f);
            Vector3 justInside = Quaternion.AngleAxis(29.9f, Vector3.forward) * Vector3.up;
            Vector3 justOutside = Quaternion.AngleAxis(30.1f, Vector3.forward) * Vector3.up;

            Assert.AreEqual(0.1f, ClearanceSolver.RequiredAngle(grip, justInside, 0.85f, Centre, Radius), 0.01f);
            Assert.AreEqual(0f, ClearanceSolver.RequiredAngle(grip, justOutside, 0.85f, Centre, Radius));
        }
    }
}
