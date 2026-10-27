using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Sunshine's dive: crouch at running speed, or at any time in the air.
    ///
    /// From the ground it is a lunge — the current speed plus a boost (capped),
    /// with a small hop. From the air it sets a fixed fast dive speed and keeps
    /// whatever vertical speed he had, so diving at a jump's apex goes flat and
    /// long. Steering is only a nudge. Landing turns it into a
    /// <see cref="BellySlideMove"/>; there is no recovering in the air.
    /// </summary>
    public sealed class DiveMove : MoveState
    {
        public override MoveKind Kind => MoveKind.Dive;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            i.CrouchPressed = false;

            // Along travel, or toward the stick if it is pushed broadly forward.
            Vector3 dir = m.Travel;
            if (i.HasMove && Vector3.Angle(i.MoveDirection, dir) < 90f) dir = i.MoveDirection;
            m.Travel = dir;
            m.SetBodyYaw(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);

            // IsGrounded is still the frame's ground test here: true for a dive
            // off the ground (or out of a slide), false when he slid off a ledge
            // mid-slide or dove in the air.
            if (m.IsGrounded)
            {
                m.Speed = Mathf.Min(m.Speed + c.DiveSpeedBoost, c.DiveMaxSpeed);
                m.VerticalVelocity = c.DiveHopSpeed;
                m.MarkAirborne();
            }
            else if (from != MoveKind.BellySlide)
            {
                m.Speed = Mathf.Max(m.Speed, c.DiveAirSpeed);
            }
            m.SetAirTopSpeed();
            m.Chain.Reset();
        }

        public override MoveKind Next(MovementMotor m)
            => m.IsGrounded ? MoveKind.BellySlide : MoveKind.Dive;

        public override void Tick(MovementMotor m, float dt)
        {
            var c = m.Config;
            m.Speed = Mathf.MoveTowards(m.Speed, 0f, c.DiveDrag * dt);
            m.SteerAtRate(c.DiveTurnRate, dt);
            m.MoveStandard(dt, m.AirVertical(c.PlainGravity, false, dt));
        }
    }
}
