using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using Pickleball.Sandbox.Movement;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// How one frame of movement input maps onto the channel's rules
    /// (<see cref="ChannelSignals.AddIntent"/>): the stick past its deadzone and
    /// every movement button cancel as "moved", jump as "airborne", dash and
    /// tether as "busy" — on the press, before the motor reacts.
    /// </summary>
    public class ChannelSignalsTests
    {
        const float Deadzone = 0.2f;

        static ChannelSignals From(MovementIntent intent, float deadzone = Deadzone)
        {
            var signals = new ChannelSignals { Held = true };
            signals.AddIntent(intent, deadzone);
            return signals;
        }

        [Test]
        public void NoInput_NothingSet()
        {
            var s = From(new MovementIntent());
            Assert.That(s.Moving || s.Airborne || s.Busy, Is.False);
            Assert.That(SpellChannel.Interruption(s), Is.EqualTo(ChannelStopReason.None));
        }

        [Test]
        public void StickInsideDeadzone_IsStandingStill()
        {
            Assert.That(From(new MovementIntent { RawStick = new Vector2(0.12f, 0.12f) }).Moving, Is.False);
        }

        [Test]
        public void StickPastDeadzone_IsMoving()
        {
            Assert.That(From(new MovementIntent { RawStick = new Vector2(0.25f, 0f) }).Moving, Is.True);
        }

        [Test]
        public void KeyboardStep_IsMoving()
        {
            Assert.That(From(new MovementIntent { RawStick = Vector2.up }).Moving, Is.True);
        }

        [Test]
        public void NegativeDeadzone_TreatedAsZero()
        {
            Assert.That(From(new MovementIntent { RawStick = new Vector2(0.01f, 0f) }, -1f).Moving, Is.True);
        }

        [Test]
        public void MovementButtons_AreMoving()
        {
            Assert.That(From(new MovementIntent { CrouchPressed = true }).Moving, Is.True, "crouch / dive");
            Assert.That(From(new MovementIntent { TrickPressed = true }).Moving, Is.True, "flip");
            Assert.That(From(new MovementIntent { MountPressed = true }).Moving, Is.True, "skateboard");
            Assert.That(From(new MovementIntent { AnchorPressed = true }).Moving, Is.True, "gravity anchor");
        }

        [Test]
        public void Jump_IsAirborne()
        {
            var s = From(new MovementIntent { JumpPressed = true });
            Assert.That(s.Airborne, Is.True);
            Assert.That(SpellChannel.Interruption(s), Is.EqualTo(ChannelStopReason.Airborne));
        }

        [Test]
        public void DashAndTether_AreBusy()
        {
            Assert.That(From(new MovementIntent { DashPressed = true }).Busy, Is.True);
            Assert.That(From(new MovementIntent { TetherPressed = true }).Busy, Is.True);
        }

        [Test]
        public void HoldingJumpWithoutAPress_DoesNotCancel()
        {
            // JumpHeld alone is a jump already in progress (which would be airborne
            // anyway) or a button resting down: only the press is new movement.
            Assert.That(From(new MovementIntent { JumpHeld = true }).Airborne, Is.False);
        }

        [Test]
        public void SprintHeldAlone_DoesNotCancel()
        {
            // Sprint without a stick deflection goes nowhere.
            Assert.That(From(new MovementIntent { SprintHeld = true }).Moving, Is.False);
        }
    }
}
