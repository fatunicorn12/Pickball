using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Swinging on the tether: a pendulum against a rope, owning movement
    /// outright. Ported from ChibiLocomotion unchanged; the motor latches it
    /// (tether button with an anchor in reach) and sets
    /// <see cref="MovementMotor.TetherAnchor"/> before Enter.
    ///
    /// Released by the tether button, or by Jump — which keeps the swing's
    /// velocity and adds a kick: letting go at the right moment is the whole
    /// skill of it. The rope, reticle and throw gesture are presentation and
    /// follow <see cref="IMovementState.MoveChanged"/>.
    /// </summary>
    public sealed class TetherMove : MoveState
    {
        Vector3 _swing;
        float _ropeLength;
        bool _releaseJump;

        public override MoveKind Kind => MoveKind.Tether;
        public override bool AllowsDash => false;
        public override bool AllowsTether => false;
        public override bool AllowsAnchor => false;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            var c = m.Config;
            var anchor = m.TetherAnchor;
            _releaseJump = false;
            _ropeLength = Mathf.Clamp(Vector3.Distance(m.transform.position, anchor.Position),
                                      c.TetherMinLength, c.TetherMaxLength);

            // Enter gently. Only the TANGENTIAL part of the current motion is
            // carried in — velocity along the rope means nothing to a pendulum,
            // and feeding it in is what made latching launch you into an orbit.
            // Capped too, so the swing is built up rather than arrived at.
            Vector3 entry = m.Travel * m.Speed + m.Momentum;
            entry.y = m.VerticalVelocity;
            Vector3 radial = (m.transform.position - anchor.Position).normalized;
            _swing = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(entry, radial), c.EntrySpeedCap);

            m.Speed = 0f;
            m.Momentum = Vector3.zero;
            m.Chain.Reset();
        }

        public override MoveKind Next(MovementMotor m)
        {
            ref var i = ref m.Intent;
            bool release = m.TetherAnchor == null;
            if (!release && i.TetherPressed)
            {
                i.TetherPressed = false;
                release = true;
            }
            else if (!release && i.JumpPressed)
            {
                m.ConsumeJump();
                _releaseJump = true;
                release = true;
            }
            if (!release) return MoveKind.Tether;
            return m.IsGrounded ? m.GroundKind : MoveKind.Fall;
        }

        /// <summary>
        /// Pendulum step: integrate freely, then project back onto the sphere of
        /// radius ropeLength and strip the outward velocity. Only constrains when
        /// the rope is taut, so it behaves as a rope rather than a rigid rod.
        /// </summary>
        public override void Tick(MovementMotor m, float dt)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            m.ClearField();   // the tether ignores wells (see ForceFieldChannel.Clear)

            Vector3 anchor = m.TetherAnchor.Position;
            Vector3 pos = m.transform.position;

            // Rope length is fixed at latch. Reeling in shrank the radius while
            // tangential speed stayed put, which sent angular velocity through
            // the roof — and W, the natural "forward" key, did it by accident.
            _swing += Vector3.down * (c.SwingGravity * dt);

            // The stick pushes along the tangent plane: you accelerate in
            // whichever direction you lean, camera-relative like everything else.
            Vector3 radialDir = (pos - anchor).normalized;
            if (i.HasMove)
            {
                Vector3 push = Vector3.ProjectOnPlane(i.MoveDirection, radialDir);
                if (push.sqrMagnitude > 0.0001f) _swing += push.normalized * (c.SwingPumpAccel * dt);
            }

            _swing *= 1f - Mathf.Clamp01(c.SwingDamping * dt);
            _swing = Vector3.ClampMagnitude(_swing, c.MaxSwingSpeed);

            Vector3 next = pos + _swing * dt;
            Vector3 offset = next - anchor;
            float dist = offset.magnitude;
            if (dist > _ropeLength && dist > 0.0001f)
            {
                Vector3 radial = offset / dist;
                next = anchor + radial * _ropeLength;
                float outward = Vector3.Dot(_swing, radial);
                if (outward > 0f) _swing -= radial * outward;
            }

            float intended = _swing.magnitude;
            CollisionFlags flags = m.Controller.Move(next - pos);

            // Re-derive from what actually happened, so hitting a wall kills the
            // swing instead of the solver insisting on velocity it never got.
            // Re-clamped: a collision resolve can spike this for one frame.
            if (dt > 0f)
                _swing = Vector3.ClampMagnitude((m.transform.position - pos) / dt, c.MaxSwingSpeed);

            // Sliding along a wall keeps most tangential speed, so a scrape reads
            // as no impact at all; scale the penalty by how much travel was lost.
            if (flags != CollisionFlags.None && intended > 0.5f)
            {
                float lost = Mathf.Clamp01(1f - _swing.magnitude / intended);
                if (lost > c.ImpactMinLoss) _swing *= Mathf.Lerp(1f, c.ImpactSpeedRetain, lost);
            }

            // Hang along the rope, facing the way you are going.
            Vector3 up = (anchor - m.transform.position).normalized;
            Vector3 travel = Vector3.ProjectOnPlane(_swing, up);
            if (travel.sqrMagnitude > 0.05f)
                m.transform.rotation = Quaternion.LookRotation(travel.normalized, up);

            // Reported for the animator and listeners; nothing integrates these.
            m.Speed = _swing.magnitude;
            m.VerticalVelocity = _swing.y;
        }

        public override void Exit(MovementMotor m, MoveKind to)
        {
            var c = m.Config;
            // Hand the swing's velocity back: horizontal becomes momentum,
            // vertical the jump arc. Releasing at the bottom of a swing should
            // fling you, and this is what makes that true.
            m.VerticalVelocity = _swing.y + (_releaseJump ? c.TetherReleaseJump : 0f);
            m.Momentum = new Vector3(_swing.x, 0f, _swing.z);
            m.Speed = 0f;
            _swing = Vector3.zero;
            m.TetherAnchor = null;

            // Face the way you are travelling, and level out — the rig was
            // rotated to hang along the rope.
            Vector3 flat = m.Momentum;
            if (flat.sqrMagnitude > 0.01f)
                m.SetBodyYaw(Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg);
            else
                m.SetBodyYaw(m.transform.eulerAngles.y);
            m.Travel = m.transform.forward;
            if (m.VerticalVelocity > 0f) m.MarkAirborne();
        }
    }
}
