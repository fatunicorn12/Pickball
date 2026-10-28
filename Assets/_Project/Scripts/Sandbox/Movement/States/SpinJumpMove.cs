using Pickleball.Sandbox.Movement.Logic;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Sunshine's spin jump: rotate the stick, then Jump, for a tall corkscrew
    /// that comes down slowly under a light, capped gravity
    /// (<see cref="MovementConfig.SpinGravity"/>).
    ///
    /// Also entered mid-air, as in Sunshine, by spinning the stick during a jump
    /// or fall: no extra lift, just the slow descent from wherever he is.
    /// <see cref="MovementMotor.PendingSpinLift"/> says which. Committed height —
    /// no variable-jump cut — and the corkscrew itself is presentation (the
    /// animator driver spins the model), not a rotation of the body.
    /// </summary>
    public sealed class SpinJumpMove : MoveState
    {
        public override MoveKind Kind => MoveKind.SpinJump;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            var c = m.Config;
            m.Spin.Consume();
            if (m.PendingSpinLift)
            {
                var profile = c.SpinGravity;
                float launch = JumpMath.LaunchSpeed(c.SpinJumpHeight, profile);
                m.VerticalVelocity = launch;
                m.JumpAirTime = JumpMath.AirTime(launch, profile);
                m.Speed *= c.SpinHorizontalKeep;
                m.ConsumeJump();
                m.MarkAirborne();
            }
            m.PendingSpinLift = false;
            m.Chain.Reset();
        }

        public override MoveKind Next(MovementMotor m)
        {
            ref var i = ref m.Intent;
            if (m.IsGrounded)
            {
                m.Chain.Landed(0, UnityEngine.Time.time);
                return m.LandingMove();
            }
            if (i.CrouchPressed) return MoveKind.Dive;
            if (i.JumpPressed && m.CanFeatherJump) return MoveKind.AirJump;
            return MoveKind.SpinJump;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            float dy = m.AirVertical(m.Config.SpinGravity, true, dt);
            m.SteerAir(dt);
            m.MoveStandard(dt, dy);
        }
    }
}
