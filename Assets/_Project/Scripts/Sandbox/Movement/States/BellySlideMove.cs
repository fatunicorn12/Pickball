using UnityEngine;

namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Sliding on the belly after a dive lands. Sunshine's friction, ported as
    /// exponential decay: neutral stick slides for about a second and a half,
    /// holding forward glides much longer, holding back brakes.
    ///
    /// Recovery, as in Sunshine: Jump is a <see cref="JumpKind.Rollout"/> back
    /// onto the feet keeping the slide's speed; crouch dives again; slowing to a
    /// stop stands him up (or leaves him crouched under a low ceiling). Sliding
    /// off a ledge carries on as a dive. The sprint ramp is frozen throughout,
    /// so a rollout out of a sprint-dive lands sprinting.
    /// </summary>
    public sealed class BellySlideMove : MoveState
    {
        public override MoveKind Kind => MoveKind.BellySlide;
        public override bool AllowsDash => false;

        bool _rolloutQueued;

        public override void Enter(MovementMotor m, MoveKind from)
        {
            m.Chain.Reset();
            // A press buffered just before touchdown counts too.
            _rolloutQueued = m.JumpBuffered;
        }

        public override MoveKind Next(MovementMotor m)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            if (!m.IsGrounded) return MoveKind.Dive;
            // Remembered for the whole slide, not just the 0.12 s jump buffer: that
            // is shorter than bellySlideMinTime, so a Jump pressed on touchdown
            // used to expire before the slide would accept it (QA MV10).
            if (i.JumpPressed) _rolloutQueued = true;
            if (m.TimeInMove >= c.BellySlideMinTime)
            {
                if (_rolloutQueued && m.CanStandTo(1f))
                {
                    _rolloutQueued = false;
                    m.PendingJump = JumpKind.Rollout;
                    return MoveKind.Jump;
                }
                if (i.CrouchPressed) return MoveKind.Dive;
                if (m.Speed <= c.BellySlideStopSpeed)
                {
                    // Slid to a halt: the sprint banked before the dive is spent.
                    // (A rollout keeps it; standing up from rest does not.)
                    m.Sprint.Reset();
                    return m.CanStandTo(1f) ? MoveKind.Ground : MoveKind.Crouch;
                }
            }
            return MoveKind.BellySlide;
        }

        public override void Tick(MovementMotor m, float dt)
        {
            var c = m.Config;
            ref var i = ref m.Intent;
            m.RequestCapsule(c.BellySlideHeightFraction);

            float decay = c.BellySlideDecay;
            float along = 0f;
            if (i.HasMove)
            {
                along = Vector3.Dot(i.MoveDirection, m.Travel) * i.MoveMagnitude;
                decay = along >= 0f
                    ? Mathf.Lerp(c.BellySlideDecay, c.BellySlideDecayForward, along)
                    : Mathf.Lerp(c.BellySlideDecay, c.BellySlideDecayBack, -along);
            }
            m.Speed *= Mathf.Exp(-decay * dt);

            // Steer only with a stick that is not pulling back: back is the brake,
            // and must never spin him round on his belly.
            if (along > -0.3f) m.SteerAtRate(c.BellySlideTurnRate, dt);
            m.MoveStandard(dt, m.GroundVertical(dt));
        }
    }
}
