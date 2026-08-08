using UnityEngine;
using NUnit.Framework;
using Pickleball.Sandbox;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down the shape of the gravity field.
    ///
    /// The arena is only navigable because the falloff is steep and predictable:
    /// the player learns to read a well as a gradient with a shallow line through
    /// it, not as a circle to give a wide berth. Flatten the curve and the wells
    /// stop teaching anything; sharpen it and the corridors close. So the tests
    /// assert the curve's <i>shape</i> — inverse square, hard cut-off, flat core —
    /// rather than any particular strength, which is a tuning knob.
    ///
    /// The clamped core gets the most attention here. It is the one place where
    /// the honest physics would produce infinity, and a single frame of infinite
    /// acceleration means a player fired out of the level at whatever speed the
    /// timestep allowed. The clamp is a deliberate design decision and these tests
    /// exist to stop anyone "fixing" it back into a singularity.
    /// </summary>
    public class GravityWellTests : BossTestFixture
    {
        const float Tolerance = 1e-3f;

        const float Strength = 100f;
        const float Influence = 20f;
        const float Core = 2f;

        /// <summary>The live-well registry is process-wide static state. EditMode
        /// never runs the OnEnable/OnDisable that maintain it, so the suite owns
        /// leaving it empty rather than letting one test's wells leak into the
        /// next one's field readings.</summary>
        [SetUp]
        public void ClearLiveWellRegistryBefore() => GravityWell.Active.Clear();

        [TearDown]
        public void ClearLiveWellRegistryAfter() => GravityWell.Active.Clear();

        /// <summary>
        /// A well whose field is live and fully established.
        ///
        /// Two things need arranging that Play mode would do for us. The spawn ramp
        /// is set to zero so <see cref="GravityWell.Intensity"/> latches straight to
        /// 1 instead of easing in over real seconds we cannot advance, and the
        /// lifetime is set to zero — documented as "stays until something destroys
        /// it" — so a tick can never collapse the object mid-test. Only then is one
        /// frame of Update driven by hand to establish the intensity.
        /// </summary>
        GravityWell NewWell(float strength = Strength, float influence = Influence,
                            float core = Core)
        {
            GravityWell well = NewComponent<GravityWell>();
            well.transform.position = Vector3.zero;

            // SerializedObject first, Configure second. A SerializedObject snapshots
            // every field when it is constructed and writes the whole snapshot back
            // on apply, so doing it the other way round would silently revert
            // whatever Configure had just set.
            SetTunable(well, "spawnRamp", 0f);
            well.Configure(strength, influence, core, life: 0f);

            Tick(well);

            Assert.That(well.Intensity, Is.EqualTo(1f).Within(Tolerance),
                        "The harness failed to establish the field; every force " +
                        "reading below would be scaled by the wrong intensity.");
            return well;
        }

        /// <summary>Closed form of the law the well claims to obey, for comparison.</summary>
        static float InverseSquare(float strength, float distance)
            => strength / (distance * distance);

        // -------------------------------------------------------- the falloff

        [Test]
        public void AccelerationAt_ObeysInverseSquare_HalvingDistanceQuadruplesThePull()
        {
            GravityWell well = NewWell();

            float far = well.AccelerationAt(8f);
            float near = well.AccelerationAt(4f);

            Assert.That(near / far, Is.EqualTo(4f).Within(Tolerance),
                        "Halving the distance must quadruple the pull, or the field " +
                        "stops reading as a gradient.");
        }

        [Test]
        public void AccelerationAt_QuadruplesAgainOverTheNextHalving()
        {
            // Checked over a second octave so a curve that merely happens to hit
            // one ratio cannot pass.
            GravityWell well = NewWell();

            Assert.That(well.AccelerationAt(8f) / well.AccelerationAt(16f),
                        Is.EqualTo(4f).Within(Tolerance));
            Assert.That(well.AccelerationAt(4f) / well.AccelerationAt(16f),
                        Is.EqualTo(16f).Within(Tolerance));
        }

        [Test]
        public void AccelerationAt_MatchesTheClosedFormBetweenCoreAndInfluence()
        {
            GravityWell well = NewWell();

            foreach (float distance in new[] { 2.5f, 5f, 10f, 19f })
                Assert.That(well.AccelerationAt(distance),
                            Is.EqualTo(InverseSquare(Strength, distance)).Within(Tolerance),
                            $"Field departed from G·m/r² at r = {distance}.");
        }

        [Test]
        public void AccelerationAt_StrengthIsThePullAtOneMetre_ScaledByTheSquareLaw()
        {
            // The tooltip defines strength as the pull one metre out. Sampling at
            // one metre would be inside the core clamp, so verify the definition
            // where the law actually applies.
            GravityWell well = NewWell(strength: 55f, influence: 30f, core: 0.5f);

            Assert.That(well.AccelerationAt(1f), Is.EqualTo(55f).Within(Tolerance));
        }

        [Test]
        public void AccelerationAt_GrowsMonotonicallyAsYouCloseIn()
        {
            GravityWell well = NewWell();
            float previous = 0f;

            for (float distance = Influence; distance >= Core; distance -= 0.25f)
            {
                float pull = well.AccelerationAt(distance);
                Assert.That(pull, Is.GreaterThanOrEqualTo(previous - Tolerance),
                            $"The field weakened while closing in, at r = {distance}.");
                previous = pull;
            }
        }

        // ------------------------------------------------------- the cut-off

        [Test]
        public void AccelerationAt_BeyondInfluenceRadius_IsExactlyZero()
        {
            // A hard cut-off, not a soft tail: it is what bounds the per-frame cost
            // and what makes the arena readable at a glance.
            GravityWell well = NewWell();

            Assert.That(well.AccelerationAt(Influence + 0.001f), Is.EqualTo(0f));
            Assert.That(well.AccelerationAt(50f), Is.EqualTo(0f));
            Assert.That(well.AccelerationAt(float.MaxValue), Is.EqualTo(0f));
        }

        [Test]
        public void AccelerationAt_OnTheInfluenceRim_StillPulls()
        {
            // Documents which side of the boundary is inclusive, so a later
            // refactor cannot quietly move it.
            GravityWell well = NewWell();

            Assert.That(well.AccelerationAt(Influence),
                        Is.EqualTo(InverseSquare(Strength, Influence)).Within(Tolerance));
            Assert.That(well.AccelerationAt(Influence), Is.GreaterThan(0f));
        }

        // ---------------------------------------------------- the clamped core

        [Test]
        public void AccelerationAt_InsideTheCore_IsHeldFlatAtTheRimValue()
        {
            GravityWell well = NewWell();
            float atRim = InverseSquare(Strength, Core);

            foreach (float distance in new[] { Core, 1.5f, 1f, 0.25f, 0.001f, 0f })
                Assert.That(well.AccelerationAt(distance),
                            Is.EqualTo(atRim).Within(Tolerance),
                            $"The core clamp let the pull move at r = {distance}. " +
                            "Inside the core the well holds you; it does not squeeze.");
        }

        [Test]
        public void AccelerationAt_AtTheCentre_IsFiniteAndANumber()
        {
            // The singularity guard, stated plainly. Literal 1/r² at r = 0 is
            // infinity, and one frame of that flings the player out of the level.
            GravityWell well = NewWell();

            float atCentre = well.AccelerationAt(0f);

            Assert.That(float.IsNaN(atCentre), Is.False, "Pull at the centre was NaN.");
            Assert.That(float.IsInfinity(atCentre), Is.False,
                        "Pull at the centre was infinite — the core clamp is gone.");
            Assert.That(atCentre, Is.EqualTo(InverseSquare(Strength, Core)).Within(Tolerance));
        }

        [Test]
        public void AccelerationAt_HasAFiniteCeilingHoweverStrongTheWell()
        {
            // The ceiling is strength / coreRadius², and it must hold for a well
            // configured far outside the shipped tuning.
            GravityWell well = NewWell(strength: 5000f, influence: 40f, core: 3f);
            float ceiling = InverseSquare(5000f, 3f);

            for (float distance = 0f; distance <= 40f; distance += 0.1f)
            {
                float pull = well.AccelerationAt(distance);
                Assert.That(float.IsNaN(pull) || float.IsInfinity(pull), Is.False,
                            $"Non-finite pull at r = {distance}.");
                Assert.That(pull, Is.LessThanOrEqualTo(ceiling + Tolerance),
                            $"Pull exceeded its ceiling at r = {distance}.");
            }
        }

        [Test]
        public void AccelerationAt_IsContinuousAcrossTheCoreRim()
        {
            // No step at the boundary: crossing into the core should change what
            // the well does to you, not jolt how hard it pulls.
            GravityWell well = NewWell();

            float justOutside = well.AccelerationAt(Core + 0.001f);
            float justInside = well.AccelerationAt(Core - 0.001f);

            Assert.That(justInside, Is.EqualTo(justOutside).Within(0.1f));
        }

        [Test]
        public void AccelerationAt_WithANegativeDistance_StaysClamped()
        {
            // Nothing should hand it a negative radius, but a signed distance
            // slipping in must not invert or blow up the field.
            GravityWell well = NewWell();

            Assert.That(well.AccelerationAt(-5f),
                        Is.EqualTo(InverseSquare(Strength, Core)).Within(Tolerance));
        }

        // ------------------------------------------------------ the vector form

        [Test]
        public void AccelerationAt_Vector_PullsTowardTheCentreWithTheScalarMagnitude()
        {
            GravityWell well = NewWell();
            var sample = new Vector3(6f, 0f, 0f);   // 6 m out along +X.

            Vector3 pull = well.AccelerationAt(sample);

            Assert.That(pull.magnitude, Is.EqualTo(well.AccelerationAt(6f)).Within(Tolerance),
                        "The vector and scalar forms must be the same equation.");
            Assert.That(pull.normalized.x, Is.EqualTo(-1f).Within(Tolerance),
                        "The field must pull toward the centre, not away from it.");
        }

        [Test]
        public void AccelerationAt_Vector_BeyondInfluence_IsZero()
        {
            GravityWell well = NewWell();

            Assert.That(well.AccelerationAt(new Vector3(0f, 0f, Influence + 1f)),
                        Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void AccelerationAt_Vector_AtTheExactCentre_IsZeroRatherThanUndirected()
        {
            // At r = 0 there is no direction to pull in. Returning zero is the only
            // answer that is not a guess, and it keeps the NaN out of a normalise.
            GravityWell well = NewWell();

            Vector3 pull = well.AccelerationAt(well.Centre);

            Assert.That(pull, Is.EqualTo(Vector3.zero));
            Assert.That(float.IsNaN(pull.x), Is.False);
        }

        // --------------------------------------------------- the combined field

        [Test]
        public void FieldAt_SumsEveryLiveWell()
        {
            // Two equal wells straddling a point cancel there — the corridors
            // between wells exist because the field superposes.
            GravityWell left = NewWell();
            GravityWell right = NewWell();
            left.transform.position = new Vector3(-5f, 0f, 0f);
            right.transform.position = new Vector3(5f, 0f, 0f);

            // The registry is normally maintained by OnEnable/OnDisable, which the
            // editor does not run for us, so state it explicitly for this test.
            GravityWell.Active.Clear();
            GravityWell.Active.Add(left);
            GravityWell.Active.Add(right);

            Vector3 middle = GravityWell.FieldAt(Vector3.zero);

            Assert.That(middle.magnitude, Is.EqualTo(0f).Within(Tolerance),
                        "Balanced wells should leave a still point between them.");
            Assert.That(GravityWell.FieldStrengthAt(new Vector3(-4f, 0f, 0f)),
                        Is.GreaterThan(0f),
                        "Off centre, the nearer well should win.");
        }

        [Test]
        public void FieldAt_WithNoLiveWells_IsZero()
        {
            Assert.That(GravityWell.FieldAt(Vector3.zero), Is.EqualTo(Vector3.zero));
            Assert.That(GravityWell.FieldStrengthAt(Vector3.zero), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------- geometry

        [Test]
        public void Configure_ReportsTheRadiiItWasGiven()
        {
            GravityWell well = NewWell(strength: 30f, influence: 12f, core: 1.25f);

            Assert.That(well.InfluenceRadius, Is.EqualTo(12f).Within(Tolerance));
            Assert.That(well.CoreRadius, Is.EqualTo(1.25f).Within(Tolerance));
            Assert.That(well.Centre, Is.EqualTo(well.transform.position));
        }
    }
}
