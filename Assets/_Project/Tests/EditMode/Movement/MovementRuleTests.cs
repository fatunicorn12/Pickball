using NUnit.Framework;
using Pickleball.Sandbox.Movement.Logic;
using UnityEngine;

namespace Pickleball.Tests.Movement
{
    /// <summary>
    /// The timing and gesture rules behind sprint, the skid turn, the jump chain
    /// and the spin jump. Each is a plain class exactly so these can run without
    /// a scene, a CharacterController or an input device.
    /// </summary>
    public class MovementRuleTests
    {
        const float Dt = 1f / 60f;

        // ------------------------------------------------------------ sprint

        [Test]
        public void Sprint_StartsOnlyAfterTheDelay()
        {
            var ramp = new SprintRamp();
            Run(ramp, 0.9f, true);
            Assert.That(ramp.IsSprinting(1f), Is.False);
            Run(ramp, 0.2f, true);
            Assert.That(ramp.IsSprinting(1f), Is.True);
        }

        [Test]
        public void Sprint_BlendRampsFromZeroToOne()
        {
            var ramp = new SprintRamp();
            Run(ramp, 1f + 0.15f, true);
            Assert.That(ramp.Blend(1f, 0.3f), Is.EqualTo(0.5f).Within(0.06f));
            Run(ramp, 0.5f, true);
            Assert.That(ramp.Blend(1f, 0.3f), Is.EqualTo(1f));
        }

        [Test]
        public void Sprint_SurvivesAGapShorterThanTheGrace()
        {
            // A stick flicked through neutral on the way to a reversal.
            var ramp = new SprintRamp();
            Run(ramp, 1.5f, true);
            Run(ramp, 0.1f, false);
            Assert.That(ramp.IsSprinting(1f), Is.True);
        }

        [Test]
        public void Sprint_IsLostOnceTheGapOutlastsTheGrace()
        {
            var ramp = new SprintRamp();
            Run(ramp, 1.5f, true);
            Run(ramp, 0.3f, false);
            Assert.That(ramp.IsSprinting(1f), Is.False);
            Assert.That(ramp.RunTime, Is.EqualTo(0f));
        }

        [Test]
        public void Sprint_ForceFullSkipsTheBuildUp()
        {
            var ramp = new SprintRamp();
            ramp.ForceFull(1f, 0.3f);
            Assert.That(ramp.Blend(1f, 0.3f), Is.EqualTo(1f));
        }

        static void Run(SprintRamp ramp, float seconds, bool qualifying)
        {
            for (float t = 0f; t < seconds - 1e-5f; t += Dt) ramp.Tick(qualifying, Dt, 0.15f);
        }

        // ------------------------------------------------------------- skid

        [Test]
        public void Skid_TriggersOnAHardReversalAtSpeed()
        {
            Assert.That(SkidRule.ShouldSkid(Vector3.forward, Vector3.back, 1f, 6f, 3f, 0.5f, 135f), Is.True);
        }

        [Test]
        public void Skid_IgnoresAQuarterTurn()
        {
            Assert.That(SkidRule.ShouldSkid(Vector3.forward, Vector3.right, 1f, 6f, 3f, 0.5f, 135f), Is.False);
        }

        [Test]
        public void Skid_NeedsSpeedAndAPushedStick()
        {
            Assert.That(SkidRule.ShouldSkid(Vector3.forward, Vector3.back, 1f, 2f, 3f, 0.5f, 135f), Is.False);
            Assert.That(SkidRule.ShouldSkid(Vector3.forward, Vector3.back, 0.3f, 6f, 3f, 0.5f, 135f), Is.False);
        }

        [Test]
        public void Skid_AngleIgnoresHeight()
        {
            Assert.That(SkidRule.ReversalAngle(new Vector3(0f, 5f, 1f), new Vector3(0f, -3f, -1f)),
                        Is.EqualTo(180f).Within(1e-3f));
        }

        // ------------------------------------------------------- jump chain

        [Test]
        public void Chain_StandingStartIsASingle()
        {
            Assert.That(new JumpChain().NextStep(0f, 5f, 0.2f, 3f), Is.EqualTo(1));
        }

        [Test]
        public void Chain_QuickRejumpsClimbSingleDoubleTriple()
        {
            var chain = new JumpChain();
            chain.Landed(1, 1f);
            Assert.That(chain.NextStep(1.1f, 5f, 0.2f, 3f), Is.EqualTo(2));
            chain.Landed(2, 2f);
            Assert.That(chain.NextStep(2.1f, 5f, 0.2f, 3f), Is.EqualTo(3));
            chain.Landed(3, 3f);
            Assert.That(chain.NextStep(3.1f, 5f, 0.2f, 3f), Is.EqualTo(1));
        }

        [Test]
        public void Chain_TripleNeedsSpeed()
        {
            var chain = new JumpChain();
            chain.Landed(2, 1f);
            Assert.That(chain.NextStep(1.05f, 1f, 0.2f, 3f), Is.EqualTo(1));
        }

        [Test]
        public void Chain_BreaksWhenTheWindowPassesOrOnANonChainLanding()
        {
            var chain = new JumpChain();
            chain.Landed(1, 1f);
            Assert.That(chain.NextStep(1.5f, 5f, 0.2f, 3f), Is.EqualTo(1));
            chain.Landed(0, 2f);
            Assert.That(chain.NextStep(2.05f, 5f, 0.2f, 3f), Is.EqualTo(1));
        }

        // --------------------------------------------------------- spin

        [Test]
        public void Spin_AFullCircleWithinTheWindowCounts()
        {
            var spin = new SpinGesture();
            Circle(spin, 360f, 0.4f);
            Assert.That(spin.HasSpin(300f, 0.6f), Is.True);
        }

        [Test]
        public void Spin_ASlowCircleDoesNot()
        {
            var spin = new SpinGesture();
            Circle(spin, 360f, 2f);
            Assert.That(spin.HasSpin(300f, 0.6f), Is.False);
        }

        [Test]
        public void Spin_ABackAndForthWaggleCancelsOut()
        {
            var spin = new SpinGesture();
            Circle(spin, 180f, 0.2f);
            Circle(spin, -180f, 0.2f, startDeg: 180f);
            Assert.That(Mathf.Abs(spin.Rotation(0.6f)), Is.LessThan(30f));
        }

        [Test]
        public void Spin_IgnoresTheDeadZoneAndCanBeSpent()
        {
            var spin = new SpinGesture();
            Circle(spin, 360f, 0.4f, magnitude: 0.2f);
            Assert.That(spin.HasSpin(300f, 0.6f), Is.False);
            Circle(spin, 360f, 0.4f);
            spin.Consume();
            Assert.That(spin.HasSpin(300f, 0.6f), Is.False);
        }

        [Test]
        public void Spin_IsFrameRateIndependent()
        {
            var slow = new SpinGesture();
            var fast = new SpinGesture();
            Circle(slow, 360f, 0.4f, dt: 1f / 30f);
            Circle(fast, 360f, 0.4f, dt: 1f / 144f);
            Assert.That(slow.Rotation(0.6f), Is.EqualTo(fast.Rotation(0.6f)).Within(15f));
        }

        static void Circle(SpinGesture spin, float degrees, float seconds,
                           float startDeg = 0f, float magnitude = 1f, float dt = Dt)
        {
            int frames = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i <= frames; i++)
            {
                float a = (startDeg + degrees * i / frames) * Mathf.Deg2Rad;
                spin.Tick(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * magnitude, 0.5f, dt);
            }
        }
    }
}
