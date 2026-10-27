using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// The snap-back redirect: a hard stick reversal at speed plants, swings the
    /// body round, and pushes off the new way at almost the speed he came in at.
    ///
    /// Sunshine's skid brakes for ~0.07 s, snaps Mario round at walking pace and
    /// makes him earn his speed back over ~0.4 s. This keeps the read — plant,
    /// turn, push — but compresses it to <see cref="MovementConfig.SkidDuration"/>
    /// and hands back <see cref="MovementConfig.SkidSpeedKeep"/> of the entry
    /// speed. The sprint ramp is left untouched for the whole move, so a sprint
    /// comes out the other side a sprint.
    ///
    /// Two phases: PLANT (sliding on along the old direction, decaying to zero,
    /// while the body swings toward the side the stick came from) then PUSH
    /// (speed ramping up along the new direction). Jump during either is
    /// Sunshine's side somersault; crouch dives out the new way.
    /// </summary>
    public sealed class SkidTurnMove : MoveState
    {
        Vector3 _from, _to;
        float _entrySpeed, _elapsed, _fromYaw, _turn;

        public override MoveKind Kind => MoveKind.SkidTurn;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            ref var i = ref m.Intent;
            _from = m.Travel;
            _to = i.HasMove ? i.MoveDirection : -_from;
            _entrySpeed = m.Speed;
            _elapsed = 0f;
            _fromYaw = m.transform.eulerAngles.y;
            // Signed, so the body turns toward the side the stick was swung to
            // rather than always the same way round.
            _turn = Vector3.SignedAngle(m.transform.forward, _to, Vector3.up);
            m.Chain.Reset();
        }

        public override MoveKind Next(MovementMotor m)
        {
            var c = m.Config;
            if (!m.IsGrounded) return MoveKind.Fall;
            if (m.JumpBuffered)
            {
                // A spin gesture in progress wins: rotating the stick passes
                // through "reversed" on its way round.
                if (m.Spin.HasSpin(c.SpinRequiredDegrees, c.SpinWindow)) return m.BeginGroundJump();
                m.PendingJump = JumpKind.SideFlip;
                return MoveKind.Jump;
            }
            if (m.Intent.CrouchPressed) return MoveKind.Dive;
            if (_elapsed >= c.SkidDuration) return m.GroundKind;
            return MoveKind.SkidTurn;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            _elapsed += dt;

            // Follow the stick while it stays broadly the new way.
            if (i.HasMove && Vector3.Angle(i.MoveDirection, _to) < 75f) _to = i.MoveDirection;

            float u = c.SkidDuration > 0f ? Mathf.Clamp01(_elapsed / c.SkidDuration) : 1f;
            float plant = c.SkidPlantFraction;
            if (u < plant)
            {
                float k = u / plant;
                m.Travel = _from;
                m.Speed = _entrySpeed * c.SkidSlide * (1f - k);
                float eased = k * k * (3f - 2f * k);
                m.SetBodyYaw(_fromYaw + _turn * eased);
            }
            else
            {
                float k = plant < 1f ? (u - plant) / (1f - plant) : 1f;
                m.Travel = _to;
                m.Speed = _entrySpeed * c.SkidSpeedKeep * k;
                m.SetBodyYaw(Yaw(_to));
            }
            m.MoveStandard(dt, m.GroundVertical(dt));
        }

        public override void Exit(MovementMotor m, MoveKind to)
        {
            // However it ends, he faces and travels the new way. Falling keeps
            // what the skid had; everything else leaves with the kept momentum,
            // so a dive or a jump out of the plant is not a dead stop.
            m.Travel = _to;
            m.SetBodyYaw(Yaw(_to));
            if (to != MoveKind.Fall) m.Speed = _entrySpeed * m.Config.SkidSpeedKeep;
        }

        static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    }
}
