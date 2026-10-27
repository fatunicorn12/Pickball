using Pickleball.Sandbox.Movement.Logic;
using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Standing, walking and running — and, as <see cref="SprintMove"/>, sprinting.
    ///
    /// Sunshine's run, ported: a kick straight to a starting speed so the first
    /// step is instant, a curved stick response, a turn rate that narrows as he
    /// speeds up, and an automatic sprint once he has run for about a second
    /// (<see cref="SprintRamp"/>). A hard reversal at speed is not steered at
    /// all — it hands over to <see cref="SkidTurnMove"/>.
    ///
    /// Ground and Sprint run the same code; they are separate kinds so listeners
    /// (animation, sword abilities) can tell a sprint from a run.
    /// </summary>
    public class GroundMove : MoveState
    {
        public override MoveKind Kind => MoveKind.Ground;
        public override bool AllowsMount => true;

        public override MoveKind Next(MovementMotor m)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            if (!m.IsGrounded) return MoveKind.Fall;
            if (m.JumpBuffered) return m.BeginGroundJump();
            if (i.CrouchPressed && m.Speed >= c.DiveMinSpeed) return MoveKind.Dive;
            if (i.CrouchHeld && m.Speed < c.DiveMinSpeed) return MoveKind.Crouch;
            if (!m.IsMovementLocked && SkidRule.ShouldSkid(m.Travel, i.MoveDirection, i.MoveMagnitude,
                    m.Speed, c.SkidMinSpeed, c.SkidMinStick, c.SkidMinAngle))
                return MoveKind.SkidTurn;
            // Releasing the stick at speed hands the slow-down to the planted
            // run-to-stop. A locked character is not a candidate: he is not
            // choosing to stop, a well core is holding him (gating rather than
            // re-arming here is what stopped the old trigger cannon that fired
            // Stop twenty times a third of a second inside a core).
            if (!m.IsMovementLocked && !i.HasMove && m.NeutralTime >= c.StopInputDelay
                && m.Speed > c.StopTriggerSpeed)
                return MoveKind.RunStop;
            return m.GroundKind;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            bool locked = m.IsMovementLocked;

            bool qualifying = !locked && i.MoveMagnitude >= c.SprintStickThreshold;
            m.Sprint.Tick(qualifying, dt, c.SprintGrace);
            if (qualifying && i.SprintHeld && c.SprintButtonSkipsDelay)
                m.Sprint.ForceFull(c.SprintDelay, c.SprintRampTime);

            float top = c.GroundTopSpeed(m.SprintBlend);
            float target = locked ? 0f : c.StickDrive(i.MoveMagnitude) * top;
            float speed = m.Speed;
            // Sunshine's start kick: from rest he is at walking pace on frame one.
            if (target > 0.01f && speed < c.RunStartSpeed) speed = Mathf.Min(c.RunStartSpeed, target);
            float accel = target < speed ? c.RunDeceleration
                        : speed >= c.RunSpeed - 0.01f ? c.SprintAcceleration
                        : c.RunAcceleration;
            m.Speed = Mathf.MoveTowards(speed, target, accel * dt);

            m.SteerHeading(i.MoveDirection, i.MoveMagnitude, target > 0.01f,
                           c.TurnSmoothTime, c.TurnRateAt(m.Speed));

            if (i.TrickPressed)
            {
                i.TrickPressed = false;
                m.RaiseTrick();
            }
            m.MoveStandard(dt, m.GroundVertical(dt));
        }
    }
}
