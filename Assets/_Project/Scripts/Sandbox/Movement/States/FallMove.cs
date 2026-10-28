namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Airborne without a jump of his own: walked off a ledge, came out of a dash,
    /// let go of the tether, rolled off on the board's dismount. Plain gravity
    /// (one value, so those arcs stay designable), the same air control as a
    /// jump, and the coyote window — a jump pressed just after leaving a ledge is
    /// still a ground jump, chain and all.
    /// </summary>
    public sealed class FallMove : MoveState
    {
        public override MoveKind Kind => MoveKind.Fall;

        public override void Enter(MovementMotor m, MoveKind from) => m.SetAirTopSpeed();

        public override MoveKind Next(MovementMotor m)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            if (m.IsGrounded)
            {
                m.Chain.Landed(0, UnityEngine.Time.time);
                return m.LandingMove();
            }
            if (m.JumpBuffered && m.InCoyoteTime) return m.BeginGroundJump();
            if (i.JumpPressed && m.CanFeatherJump) return MoveKind.AirJump;
            if (i.CrouchPressed) return MoveKind.Dive;
            if (c.SpinInAir && m.Spin.HasSpin(c.SpinRequiredDegrees, c.SpinWindow)) return m.BeginAirSpin();
            return MoveKind.Fall;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            float dy = m.AirVertical(m.Config.PlainGravity, false, dt);
            m.SteerAir(dt);
            m.MoveStandard(dt, dy);
        }
    }
}
