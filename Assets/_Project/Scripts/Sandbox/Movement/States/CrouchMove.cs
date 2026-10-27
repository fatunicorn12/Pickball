using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Crouched: a shorter capsule and a slow crouch-walk. Held, not toggled.
    ///
    /// Pressing crouch at running speed dives instead (see <see cref="DiveMove"/>),
    /// so this is only reached from a walk or a standstill. Standing up is
    /// refused under a ceiling — the motor will not grow the capsule into one.
    /// No jump, flip trick or dash from a crouch, as before.
    /// </summary>
    public sealed class CrouchMove : MoveState
    {
        public override MoveKind Kind => MoveKind.Crouch;
        public override bool AllowsDash => false;
        public override bool AllowsMount => true;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            m.Sprint.Reset();
            m.Chain.Reset();
        }

        public override MoveKind Next(MovementMotor m)
        {
            if (!m.IsGrounded && m.CanStandTo(1f)) return MoveKind.Fall;
            if (!m.Intent.CrouchHeld && m.CanStandTo(1f)) return m.GroundKind;
            return MoveKind.Crouch;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            m.RequestCapsule(c.CrouchHeightFraction);

            float target = m.IsMovementLocked ? 0f : c.StickDrive(i.MoveMagnitude) * c.CrouchSpeed;
            float accel = target < m.Speed ? c.RunDeceleration : c.RunAcceleration;
            m.Speed = Mathf.MoveTowards(m.Speed, target, accel * dt);
            m.SteerHeading(i.MoveDirection, i.MoveMagnitude, target > 0.01f, c.TurnSmoothTime, c.RunTurnRate);

            float dy = m.IsGrounded ? m.GroundVertical(dt) : m.AirVertical(c.PlainGravity, false, dt);
            m.MoveStandard(dt, dy);
        }
    }
}
