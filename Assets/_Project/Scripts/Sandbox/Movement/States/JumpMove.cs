using Pickleball.Sandbox.Movement.Logic;
using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Every jump off the ground: the single → double → triple chain, the side
    /// somersault out of a skid, and the rollout out of a belly slide. Which one
    /// is set by the move that hands over (<see cref="MovementMotor.PendingJump"/>)
    /// and exposed as <see cref="IMovementState.CurrentJump"/>.
    ///
    /// Heights come from the config in metres and are turned into launch speeds
    /// by <see cref="JumpMath"/> over the jump gravity profile, so the apex is
    /// the authored height at any frame rate. The whole arc, descent included,
    /// stays in this move; landing records the chain step so a quick re-jump
    /// climbs the chain.
    ///
    /// Variable height is Sunshine's: let go of Jump while still rising fast and
    /// the upward speed is cut to <see cref="MovementConfig.JumpReleaseSpeed"/>.
    /// As in Sunshine, the triple and the side somersault are committed — they
    /// always reach full height.
    /// </summary>
    public sealed class JumpMove : MoveState
    {
        JumpKind _jump;
        int _step;
        bool _variable, _released;
        float _elapsed;

        public override MoveKind Kind => MoveKind.Jump;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            var c = m.Config;
            _jump = m.PendingJump == JumpKind.None ? JumpKind.Single : m.PendingJump;
            m.PendingJump = JumpKind.Single;
            _elapsed = 0f;
            _released = false;

            // Airborne first, so the air speed cap is the speed he took off at
            // BEFORE the takeoff trim below: holding forward wins the trimmed
            // speed back in the air (as in Sunshine), which is what keeps a
            // sprinting single → double fast enough to earn the triple.
            float speed = m.Speed;
            m.MarkAirborne();
            float height;
            switch (_jump)
            {
                case JumpKind.Double:
                case JumpKind.Single:
                    _step = (int)_jump;
                    height = c.ChainJumpHeight(_step, speed);
                    m.Speed = speed * c.JumpTakeoffSpeedKeep;
                    _variable = true;
                    break;
                case JumpKind.Triple:
                    _step = 3;
                    height = c.TripleJumpHeight;   // and keeps all its speed
                    _variable = false;
                    break;
                case JumpKind.SideFlip:
                    _step = 0;
                    height = c.SideFlipHeight;
                    m.Speed = c.SideFlipSpeed;
                    _variable = false;
                    break;
                default:   // Rollout
                    _step = 0;
                    height = c.RolloutHeight;
                    m.Speed = speed * c.RolloutSpeedKeep;
                    _variable = true;
                    break;
            }

            var profile = c.JumpGravity;
            float launch = JumpMath.LaunchSpeed(height, profile);
            m.VerticalVelocity = launch;
            m.JumpAirTime = JumpMath.AirTime(launch, profile);
            m.CurrentJump = _jump;
            m.ConsumeJump();
        }

        public override MoveKind Next(MovementMotor m)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            if (m.IsGrounded)
            {
                m.Chain.Landed(_step, Time.time);
                return m.GroundKind;
            }
            if (i.JumpPressed && m.CanFeatherJump) return MoveKind.AirJump;
            if (i.CrouchPressed) return MoveKind.Dive;
            if (c.SpinInAir && m.Spin.HasSpin(c.SpinRequiredDegrees, c.SpinWindow)) return m.BeginAirSpin();
            return MoveKind.Jump;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            _elapsed += dt;

            bool held = true;
            if (_variable)
            {
                if (!_released && !i.JumpHeld && _elapsed >= c.MinJumpHoldTime)
                {
                    _released = true;
                    if (m.VerticalVelocity > c.JumpReleaseSpeed) m.VerticalVelocity = c.JumpReleaseSpeed;
                }
                // Before the minimum hold a tap still rises on the held profile.
                held = !_released && (i.JumpHeld || _elapsed < c.MinJumpHoldTime);
            }

            float dy = m.AirVertical(c.JumpGravity, held, dt);
            m.SteerAir(dt);
            m.MoveStandard(dt, dy);
        }
    }
}
