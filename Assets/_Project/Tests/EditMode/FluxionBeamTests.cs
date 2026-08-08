using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;

namespace Pickleball.Tests
{
    /// <summary>
    /// What the beam exposes to the rest of the game: a three-state life cycle
    /// (idle → telegraph → lethal) and two live readouts for the HUD.
    ///
    /// The state machine is worth pinning even though it is small, because the
    /// telegraph is the entire fairness argument for an attack that deals
    /// permanent damage. If firing could ever begin without a charge window, or a
    /// second trigger could restart a beam already committed, the player's one
    /// window to stop moving would stop being reliable.
    ///
    /// These tests use nothing but public API. The interesting arithmetic — the
    /// damage curve itself — is private and is covered separately, under protest,
    /// in <see cref="FluxionBeamDamageCurveTests"/>.
    /// </summary>
    public class FluxionBeamTests : BossTestFixture
    {
        [Test]
        public void NewBeam_IsInert()
        {
            FluxionBeam beam = NewComponent<FluxionBeam>();

            Assert.That(beam.IsActive, Is.False);
            Assert.That(beam.IsFiring, Is.False);
            Assert.That(beam.CurrentAcceleration, Is.EqualTo(0f));
            Assert.That(beam.CurrentDamagePerSecond, Is.EqualTo(0f));
        }

        [Test]
        public void Fire_OpensTheTelegraphWindowRatherThanFiringImmediately()
        {
            // The charge window is the "stop now" tell. Firing on the same frame
            // as the trigger would make the beam unavoidable permanent chip.
            FluxionBeam beam = NewComponent<FluxionBeam>();

            beam.Fire();

            Assert.That(beam.IsActive, Is.True, "The beam should be live...");
            Assert.That(beam.IsFiring, Is.False, "...but not yet lethal.");
        }

        [Test]
        public void Fire_WhileAlreadyActive_DoesNotReopenTheTelegraph()
        {
            // A boss brain that spams Fire must not be able to hold the beam in a
            // permanent charge state, nor restart the window mid-attack.
            FluxionBeam beam = NewComponent<FluxionBeam>();

            beam.Fire();
            beam.Fire();
            beam.Fire();

            Assert.That(beam.IsActive, Is.True);
            Assert.That(beam.IsFiring, Is.False);
        }

        [Test]
        public void Stop_ReturnsTheBeamToIdle()
        {
            FluxionBeam beam = NewComponent<FluxionBeam>();
            beam.Fire();

            beam.Stop();

            Assert.That(beam.IsActive, Is.False);
            Assert.That(beam.IsFiring, Is.False);
            Assert.That(beam.CurrentDamagePerSecond, Is.EqualTo(0f));
        }

        [Test]
        public void Stop_OnAnIdleBeam_IsHarmless()
        {
            // Phase transitions cancel the beam unconditionally, so Stop has to be
            // safe to call on a beam that was never fired.
            FluxionBeam beam = NewComponent<FluxionBeam>();

            beam.Stop();
            beam.Stop();

            Assert.That(beam.IsActive, Is.False);
        }

        [Test]
        public void Fire_AfterStop_CanOpenAFreshTelegraph()
        {
            FluxionBeam beam = NewComponent<FluxionBeam>();
            beam.Fire();
            beam.Stop();

            beam.Fire();

            Assert.That(beam.IsActive, Is.True);
            Assert.That(beam.IsFiring, Is.False);
        }
    }

    /// <summary>
    /// The damage curve — deadzone, linear rate, ceiling — reached by reflection.
    ///
    /// <b>Read this before copying the pattern.</b> <c>DamagePerSecond()</c> and the
    /// measured <c>_acceleration</c> it reads are private, and the only supported
    /// route to them is a live <see cref="ChibiLocomotion"/> being driven by a real
    /// CharacterController over real frames — a PlayMode integration test, not a
    /// unit test. Two options were rejected: widening the production API purely so
    /// a test could reach it, which lets the tests dictate the shape of the game
    /// code; and leaving the curve untested, which abandons the single most
    /// balance-sensitive number in the fight (this is the damage that cannot be
    /// won back).
    ///
    /// So this fixture reaches in, and pays for it: it is coupled to two private
    /// names and will break on a rename. Both lookups assert with an explanatory
    /// message so that break is a sentence, not a NullReferenceException.
    ///
    /// <b>The proper fix is a refactor, not more reflection.</b> Lifting the curve
    /// into a pure static — <c>static float DamageRate(float acceleration, float
    /// deadzone, float perAcceleration, float ceiling)</c> — would make it directly
    /// testable, and would suit a formula that has no business knowing it lives on
    /// a MonoBehaviour. Deliberately not done here: this task was not scoped to
    /// change production code.
    /// </summary>
    public class FluxionBeamDamageCurveTests : BossTestFixture
    {
        const float Tolerance = 1e-3f;

        const float Deadzone = 2.5f;
        const float PerAcceleration = 0.55f;
        const float Ceiling = 42f;

        FluxionBeam NewBeam()
        {
            FluxionBeam beam = NewComponent<FluxionBeam>();
            SetTunable(beam, "deadzone", Deadzone);
            SetTunable(beam, "damagePerAcceleration", PerAcceleration);
            SetTunable(beam, "maxDamagePerSecond", Ceiling);
            return beam;
        }

        /// <summary>Force a measured |dv/dt| onto the beam and read the rate it
        /// prices that at. See the fixture summary for why this is reflection.</summary>
        static float RateFor(FluxionBeam beam, float acceleration)
        {
            FieldInfo measured = typeof(FluxionBeam)
                .GetField("_acceleration", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(measured, Is.Not.Null,
                        "FluxionBeam._acceleration not found — see this fixture's summary; " +
                        "the damage curve has no public entry point.");
            measured.SetValue(beam, acceleration);

            MethodInfo curve = typeof(FluxionBeam)
                .GetMethod("DamagePerSecond", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(curve, Is.Not.Null,
                        "FluxionBeam.DamagePerSecond() not found — see this fixture's summary.");
            return (float)curve.Invoke(beam, null);
        }

        [Test]
        public void StandingPerfectlyStill_CostsNothing()
        {
            // The beam's whole proposition. If this is ever non-zero, the one
            // correct answer to the attack stops working.
            FluxionBeam beam = NewBeam();

            Assert.That(RateFor(beam, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void AccelerationInsideTheDeadzone_IsFree()
        {
            // The deadzone exists to absorb controller jitter, step offsets and
            // landing settle, so "still" is achievable on a real thumbstick.
            FluxionBeam beam = NewBeam();

            Assert.That(RateFor(beam, 1f), Is.EqualTo(0f));
            Assert.That(RateFor(beam, Deadzone - 0.01f), Is.EqualTo(0f));
            Assert.That(RateFor(beam, Deadzone), Is.EqualTo(0f),
                        "The deadzone boundary itself is free.");
        }

        [Test]
        public void AboveTheDeadzone_TheRateScalesWithTheExcessOnly()
        {
            // Prices the excess, not the raw derivative — otherwise crossing the
            // deadzone would be a cliff rather than an edge.
            FluxionBeam beam = NewBeam();

            Assert.That(RateFor(beam, Deadzone + 10f),
                        Is.EqualTo(10f * PerAcceleration).Within(Tolerance));
            Assert.That(RateFor(beam, Deadzone + 20f),
                        Is.EqualTo(20f * PerAcceleration).Within(Tolerance));
        }

        [Test]
        public void TheRateEntersFromZeroRatherThanSteppingUp()
        {
            FluxionBeam beam = NewBeam();

            float justOver = RateFor(beam, Deadzone + 0.001f);

            Assert.That(justOver, Is.GreaterThan(0f));
            Assert.That(justOver, Is.LessThan(0.01f),
                        "Crossing the deadzone should cost a trickle, not a chunk.");
        }

        [Test]
        public void TheRateIsCappedHoweverViolentlyYouMove()
        {
            // Without a ceiling a dash into a gravity well could delete the bar in
            // a frame, and permanent damage makes that unrecoverable by design.
            FluxionBeam beam = NewBeam();

            Assert.That(RateFor(beam, 1000f), Is.EqualTo(Ceiling).Within(Tolerance));
            Assert.That(RateFor(beam, 100000f), Is.EqualTo(Ceiling).Within(Tolerance));
        }

        [Test]
        public void TheRateNeverDecreasesAsMovementGetsMoreViolent()
        {
            FluxionBeam beam = NewBeam();
            float previous = -1f;

            for (float acceleration = 0f; acceleration <= 200f; acceleration += 0.5f)
            {
                float rate = RateFor(beam, acceleration);
                Assert.That(rate, Is.GreaterThanOrEqualTo(previous - Tolerance),
                            $"The curve dipped at |dv/dt| = {acceleration}.");
                Assert.That(rate, Is.LessThanOrEqualTo(Ceiling + Tolerance));
                previous = rate;
            }
        }

        [Test]
        public void CurrentDamagePerSecond_IsZeroUntilTheBeamIsActuallyFiring()
        {
            // The gate the telegraph depends on: even a player sprinting flat out
            // during the charge window is charged nothing.
            FluxionBeam beam = NewBeam();
            RateFor(beam, 500f);    // Moving violently enough to be well over the cap.

            Assert.That(beam.CurrentAcceleration, Is.EqualTo(500f).Within(Tolerance));
            Assert.That(beam.CurrentDamagePerSecond, Is.EqualTo(0f),
                        "Idle beams do not bill you.");

            beam.Fire();

            Assert.That(beam.IsFiring, Is.False);
            Assert.That(beam.CurrentDamagePerSecond, Is.EqualTo(0f),
                        "Neither does the telegraph — that is what makes it a warning.");
        }
    }
}
