using Pickleball.Sandbox.Movement.Logic;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// The feather's mid-air jump (the cloud-in-a-bottle beat): once per airtime,
    /// gated on <see cref="AbilityKind.DoubleJump"/>. A fixed, committed height
    /// on the floaty profile — the front-flip clip is time-scaled to
    /// <see cref="MovementMotor.JumpAirTime"/>, which assumes the full arc.
    ///
    /// Not to be confused with the chain's <see cref="JumpKind.Double"/>, which
    /// is a ground jump.
    /// </summary>
    public sealed class AirJumpMove : MoveState
    {
        public override MoveKind Kind => MoveKind.AirJump;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            var c = m.Config;
            m.AirJumpsUsed++;
            var profile = c.JumpGravity;
            float launch = JumpMath.LaunchSpeed(c.FeatherJumpHeight, profile);
            m.VerticalVelocity = launch;
            m.JumpAirTime = JumpMath.AirTime(launch, profile);
            m.ConsumeJump();
            m.Chain.Reset();
        }

        public override MoveKind Next(MovementMotor m)
        {
            if (m.IsGrounded)
            {
                m.Chain.Landed(0, UnityEngine.Time.time);
                return m.GroundKind;
            }
            if (m.Intent.CrouchPressed) return MoveKind.Dive;
            return MoveKind.AirJump;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            float dy = m.AirVertical(m.Config.JumpGravity, true, dt);
            m.SteerAir(dt);
            m.MoveStandard(dt, dy);
        }
    }
}
