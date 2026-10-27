using Pickleball.Sandbox.Movement.Logic;
using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// The planted run-to-stop after releasing the stick at speed: a firmer
    /// deceleration than an ordinary slow-down, long enough for the Run To Stop
    /// clip's plant to read.
    ///
    /// Fresh input cancels it at once (responsiveness beats playing the plant
    /// out), and a reversal from inside it still skids — a stick flicked back
    /// through neutral often lands here first.
    /// </summary>
    public sealed class RunStopMove : MoveState
    {
        public override MoveKind Kind => MoveKind.RunStop;
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
            if ((i.HasMove && !m.IsMovementLocked) || m.Speed < 0.05f) return m.GroundKind;
            return MoveKind.RunStop;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            m.Speed = Mathf.MoveTowards(m.Speed, 0f, c.StopDeceleration * dt);
            m.Sprint.Tick(false, dt, c.SprintGrace);
            // No steering of his own, but a lock-on still squares him up once slow.
            m.SteerHeading(i.MoveDirection, 0f, false, c.TurnSmoothTime, c.RunTurnRate);
            if (i.TrickPressed)
            {
                i.TrickPressed = false;
                m.RaiseTrick();
            }
            m.MoveStandard(dt, m.GroundVertical(dt));
        }
    }
}
