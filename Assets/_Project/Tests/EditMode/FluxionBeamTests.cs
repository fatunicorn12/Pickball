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
            Assert.That(beam.CurrentSpeed, Is.EqualTo(0f));
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
    /// The damage curve — deadzone, linear rate, ceiling — through the pure
    /// <see cref="FluxionBeam.DamageRate"/>. The beam prices SPEED (the fluxion
    /// of position), so a steady sprint is billed every second it is on you and
    /// standing still is free.
    /// </summary>
    public class FluxionBeamDamageCurveTests
    {
        const float Tolerance = 1e-3f;

        const float Deadzone = 0.35f;
        const float PerSpeed = 3f;
        const float Ceiling = 30f;

        static float RateFor(float speed) => FluxionBeam.DamageRate(speed, Deadzone, PerSpeed, Ceiling);

        [Test]
        public void StandingPerfectlyStill_CostsNothing()
        {
            Assert.That(RateFor(0f), Is.EqualTo(0f));
        }

        [Test]
        public void SpeedInsideTheDeadzone_IsFree()
        {
            Assert.That(RateFor(0.1f), Is.EqualTo(0f));
            Assert.That(RateFor(Deadzone), Is.EqualTo(0f), "The deadzone boundary itself is free.");
        }

        [Test]
        public void SteadySprinting_IsBilled()
        {
            // The bug this curve replaced: pricing acceleration made a constant-
            // speed sprint free. A 4.84 m/s sprint must cost something every second.
            Assert.That(RateFor(4.84f), Is.EqualTo((4.84f - Deadzone) * PerSpeed).Within(Tolerance));
        }

        [Test]
        public void RunningCostsMoreThanWalking()
        {
            Assert.That(RateFor(4.84f), Is.GreaterThan(RateFor(2.8f)));
            Assert.That(RateFor(2.8f), Is.GreaterThan(RateFor(0.6f)));
        }

        [Test]
        public void TheRateIsCappedHoweverFastYouMove()
        {
            Assert.That(RateFor(14f), Is.EqualTo(Ceiling).Within(Tolerance), "A dash hits the cap.");
            Assert.That(RateFor(1000f), Is.EqualTo(Ceiling).Within(Tolerance));
        }

        [Test]
        public void TheRateNeverDecreasesAsYouMoveFaster()
        {
            float previous = -1f;
            for (float speed = 0f; speed <= 30f; speed += 0.1f)
            {
                float rate = RateFor(speed);
                Assert.That(rate, Is.GreaterThanOrEqualTo(previous - Tolerance), $"Dipped at {speed} m/s.");
                Assert.That(rate, Is.LessThanOrEqualTo(Ceiling + Tolerance));
                previous = rate;
            }
        }
    }

    /// <summary>The idle/telegraph gate: no bill until the beam is lethal.</summary>
    public class FluxionBeamGateTests : BossTestFixture
    {
        [Test]
        public void CurrentDamagePerSecond_IsZeroUntilTheBeamIsActuallyFiring()
        {
            FluxionBeam beam = NewComponent<FluxionBeam>();
            FieldInfo measured = typeof(FluxionBeam)
                .GetField("_speed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(measured, Is.Not.Null, "FluxionBeam._speed not found.");
            measured.SetValue(beam, 10f);   // sprinting flat out and then some

            Assert.That(beam.CurrentDamagePerSecond, Is.EqualTo(0f), "Idle beams do not bill you.");

            beam.Fire();

            Assert.That(beam.IsFiring, Is.False);
            Assert.That(beam.CurrentDamagePerSecond, Is.EqualTo(0f),
                        "Neither does the telegraph — that is what makes it a warning.");
        }
    }
}
