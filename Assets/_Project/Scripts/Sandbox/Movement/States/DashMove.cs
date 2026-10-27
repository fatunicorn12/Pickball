using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// The protractor's dash: a fixed velocity at exactly 45° — (horizontal
    /// facing, 1) normalised, independent of the camera — for a fixed time, then
    /// a hand-back of part of that velocity so it flows into the fall instead of
    /// stopping dead. Ported from ChibiLocomotion unchanged.
    ///
    /// Overrides everything while it runs: no gravity, no input steering. That
    /// is what keeps the arc exactly 45° and exactly as long as advertised, and
    /// so designable. Gravity wells bend it rather than being ignored (Newton's
    /// wells exist to disrupt dodges): the frame's pull is applied to the dash
    /// velocity itself, never banked as field velocity, so nothing is left to
    /// release when the dash hands movement back.
    /// </summary>
    public sealed class DashMove : MoveState
    {
        Vector3 _velocity;
        float _timeLeft;

        public override MoveKind Kind => MoveKind.Dash;
        public override bool AllowsDash => false;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            var c = m.Config;
            if (!m.IsGrounded) m.AirDashUsed = true;   // one per airtime

            Vector3 flat = m.transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            Vector3 dir = (flat + Vector3.up).normalized;   // exactly 45°
            _velocity = dir * c.DashSpeed;
            _timeLeft = c.DashDuration;
            m.DashReadyAt = Time.time + c.DashDuration + c.DashCooldown;
            m.DashDirection = dir;
            m.VerticalVelocity = _velocity.y;
            m.MarkAirborne();
            m.Chain.Reset();
        }

        public override MoveKind Next(MovementMotor m)
        {
            if (_timeLeft > 0f) return MoveKind.Dash;
            return m.IsGrounded ? m.GroundKind : MoveKind.Fall;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            m.ClearField();
            _velocity += m.FrameFieldAcceleration * (m.Config.DashFieldBend * dt);
            _timeLeft -= dt;
            m.Controller.Move(_velocity * dt);
            m.VerticalVelocity = _velocity.y;
        }

        public override void Exit(MovementMotor m, MoveKind to)
        {
            var c = m.Config;
            m.VerticalVelocity = _velocity.y * c.DashExitVerticalKeep;
            var flat = new Vector3(_velocity.x, 0f, _velocity.z);
            m.Speed = Mathf.Min(flat.magnitude * c.DashExitHorizontalKeep, c.SprintSpeed);
            if (flat.sqrMagnitude > 1e-4f) m.Travel = flat;
            _velocity = Vector3.zero;
            _timeLeft = 0f;
        }
    }
}
