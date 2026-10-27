using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Riding the skateboard: fast, lazy-turning, no jump, no lock-on facing.
    /// Ported from ChibiLocomotion unchanged. Mounting is the motor's interrupt
    /// (grounded only, so he cannot conjure a board mid-jump); dismounting is
    /// the same button, anywhere. The board's visual and the ride lift are
    /// presentation and follow <see cref="IMovementState.MoveChanged"/>.
    /// </summary>
    public sealed class SkateMove : MoveState
    {
        public override MoveKind Kind => MoveKind.Skate;
        public override bool AllowsDash => false;
        public override bool AllowsTether => false;
        public override bool AllowsAnchor => false;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            m.Sprint.Reset();
            m.Chain.Reset();
        }

        public override MoveKind Next(MovementMotor m)
        {
            ref var i = ref m.Intent;
            if (!i.MountPressed) return MoveKind.Skate;
            i.MountPressed = false;
            return m.IsGrounded ? m.GroundKind : MoveKind.Fall;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            float target = m.IsMovementLocked ? 0f : i.MoveMagnitude * c.SkateSpeed;
            m.Speed = Mathf.MoveTowards(m.Speed, target, c.SkateAcceleration * dt);
            m.SteerHeading(i.MoveDirection, i.MoveMagnitude, target > 0.01f,
                           c.SkateTurnSmoothTime, float.PositiveInfinity, allowFacing: false);
            float dy = m.IsGrounded ? m.GroundVertical(dt) : m.AirVertical(c.PlainGravity, false, dt);
            m.MoveStandard(dt, dy);
        }
    }
}
