using System;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Decides which of her clips plays when attacks overlap, and when to start
    /// each one so its release pose lands on the gameplay release. Plain C# so it
    /// is tested without a scene; <see cref="HypatiaAnimationView"/> is its thin
    /// Animator shell.
    ///
    /// <para><b>The rules.</b> One clip at a time, by fixed priority: Death, Topple,
    /// Phase Two, Burning, Pulse, Conic, Rings. Burning outranks the attacks that come
    /// more often: a band ignites only every 15 s, and below them it lost most of its
    /// casts. A clip is never restarted. A request
    /// that would overlap a clip of equal or higher priority is refused, not queued,
    /// because a late attack pose would point at nothing: rings come often enough
    /// that a skipped one is not missed. A higher-priority clip replaces a lower one
    /// when it starts, not when it is requested, so a ring can finish during a
    /// conic's long needle lead.</para>
    ///
    /// <para><b>Two exceptions to "refused".</b> Topple and Death always interrupt.
    /// Phase Two is a one-off that a topple often causes (a topple doubles damage, so
    /// she is likely to cross the threshold while down): one refused by a topple plays
    /// when she recovers.</para>
    ///
    /// <para><b>Clock.</b> <see cref="Tick"/> takes seconds on HER clock, the same
    /// clock her animator runs on (the brain sets <c>animator.speed</c>), so a frozen
    /// Hypatia holds both the countdown and the pose.</para>
    /// </summary>
    public sealed class HypatiaAnimationDirector
    {
        /// <summary>What she can be shown doing. Conics share one priority.</summary>
        public enum Action
        {
            None,
            Rings,
            Burning,
            ConicEllipse,
            ConicParabola,
            ConicHyperbola,
            Pulse,
            PhaseTwo,
            Topple,
            Death,
        }

        /// <summary>A clip's measured facts: its length and where its release pose
        /// falls (s, at speed 1), and how far its speed may be stretched to meet a
        /// lead. A release of 0 means no gameplay moment to hit: it just plays.</summary>
        public readonly struct ClipTiming
        {
            public readonly float Length, Release, MinSpeed, MaxSpeed;

            public ClipTiming(float length, float release, float minSpeed = 1f, float maxSpeed = 1f)
            {
                Length = length;
                Release = release;
                MinSpeed = minSpeed;
                MaxSpeed = maxSpeed;
            }
        }

        /// <summary>How to play a clip: wait <see cref="Delay"/>, start
        /// <see cref="Offset"/> seconds in, at <see cref="Speed"/>. Delay and duration
        /// are on her clock.</summary>
        public readonly struct ClipPlan
        {
            public readonly float Delay, Offset, Speed, Duration, ReleaseAt;

            public ClipPlan(float delay, float offset, float speed, float duration, float releaseAt)
            {
                Delay = delay;
                Offset = offset;
                Speed = speed;
                Duration = duration;
                ReleaseAt = releaseAt;
            }
        }

        /// <summary>A clip should start now. Replaces whatever was playing, which gets
        /// no <see cref="Finished"/>.</summary>
        public event Action<Action, ClipPlan> Started;

        /// <summary>A clip ran out or was cancelled, and nothing replaced it: return to rest.</summary>
        public event Action<Action> Finished;

        Action _active = Action.None, _pending = Action.None;
        float _activeRemaining, _pendingDelay;
        ClipPlan _pendingPlan, _deferredPhaseTwo;
        bool _hasDeferredPhaseTwo;

        /// <summary>The clip playing now, or None.</summary>
        public Action Active => _active;

        /// <summary>The clip accepted and waiting for its start time, or None.</summary>
        public Action Pending => _pending;

        /// <summary>Higher wins. Conics share a rank, so one can never replace another.</summary>
        public static int Priority(Action action) => action switch
        {
            Action.Rings => 1,
            Action.ConicEllipse or Action.ConicParabola or Action.ConicHyperbola => 2,
            Action.Pulse => 3,
            Action.Burning => 4,
            Action.PhaseTwo => 5,
            Action.Topple => 6,
            Action.Death => 7,
            _ => 0,
        };

        /// <summary>
        /// Fit a clip to a lead, so its release pose lands <paramref name="lead"/>
        /// seconds from now. Prefers changing the speed, within the clip's limits. If
        /// the lead is still too long it waits first (a clip must not creep); if too
        /// short it skips into the clip, past the anticipation.
        /// </summary>
        public static ClipPlan Plan(in ClipTiming clip, float lead)
        {
            float length = Math.Max(0f, clip.Length);
            float release = Math.Min(Math.Max(0f, clip.Release), length);
            if (release <= 0f) return new ClipPlan(0f, 0f, 1f, length, 0f);

            float minSpeed = Math.Max(0.01f, clip.MinSpeed);
            float maxSpeed = Math.Max(minSpeed, clip.MaxSpeed);
            if (!(lead > 0f))
            {
                // Already due: start on the release pose.
                return new ClipPlan(0f, release, maxSpeed, (length - release) / maxSpeed, 0f);
            }

            float speed = Math.Min(maxSpeed, Math.Max(minSpeed, release / lead));
            float travel = release / speed;
            float delay = Math.Max(0f, lead - travel);
            float offset = Math.Max(0f, release - lead * speed);
            return new ClipPlan(delay, offset, speed, (length - offset) / speed, lead);
        }

        /// <summary>
        /// Seconds from a conic's telegraph to the moment it actually leaves her hand.
        /// The brain's lead only reaches the volley's start. The lob launches when its
        /// flight is short enough to still honour the marker lead, and the hyperbola
        /// waits out its asymptote telegraph on top. Keep in step with
        /// <c>ConicCaster.AimLob</c> and <c>BeginHyperbola</c>.
        /// </summary>
        public static float ConicReleaseLead(ConicKind kind, float needleLead, float asymptoteLead,
                                             float lobFlightTime, float lobMarkerLead) => kind switch
        {
            ConicKind.Parabola => needleLead + Math.Max(0f, lobMarkerLead - lobFlightTime),
            ConicKind.Hyperbola => needleLead + Math.Max(0f, asymptoteLead),
            _ => needleLead,
        };

        /// <summary>
        /// Ask for a clip. True when it was accepted: it has started, or will at
        /// <see cref="ClipPlan.Delay"/>. False when it was skipped. Use
        /// <see cref="BeginTopple"/> and <see cref="BeginDeath"/> for those two.
        /// </summary>
        public bool Request(Action action, in ClipPlan plan)
        {
            if (action is Action.None or Action.Topple or Action.Death) return false;
            if (_active == Action.Death) return false;
            if (_active == Action.Topple)
            {
                if (action == Action.PhaseTwo)
                {
                    _deferredPhaseTwo = plan;
                    _hasDeferredPhaseTwo = true;
                }
                return false;
            }

            if (action == _active || action == _pending) return false;

            int rank = Priority(action);
            if (Priority(_active) >= rank && _activeRemaining > plan.Delay) return false;

            if (_pending != Action.None && Priority(_pending) >= rank)
            {
                // Under a clip already promised, this one fits only if it starts now and
                // reaches its own release before that clip takes over.
                if (plan.Delay > 0f || plan.ReleaseAt >= _pendingDelay) return false;
            }

            if (plan.Delay > 0f)
            {
                _pending = action;
                _pendingDelay = plan.Delay;
                _pendingPlan = plan;
                return true;
            }

            Start(action, plan);
            return true;
        }

        /// <summary>The attack this clip was showing was called off (a pulse dropped
        /// mid-charge). A pending one is forgotten; a playing one stops.</summary>
        public void Cancel(Action action)
        {
            if (action == Action.None) return;
            if (_pending == action) _pending = Action.None;
            if (_active != action) return;
            _active = Action.None;
            _activeRemaining = 0f;
            Finished?.Invoke(action);
        }

        /// <summary>She toppled: everything stops until <see cref="EndTopple"/>.</summary>
        public void BeginTopple()
        {
            if (_active is Action.Death or Action.Topple) return;
            _pending = Action.None;
            _active = Action.Topple;
            _activeRemaining = 0f;
            Started?.Invoke(Action.Topple, default);
        }

        /// <summary>She is back up. A Phase Two the topple held back plays now.</summary>
        public void EndTopple()
        {
            if (_active != Action.Topple) return;
            _active = Action.None;
            Finished?.Invoke(Action.Topple);
            if (!_hasDeferredPhaseTwo) return;
            _hasDeferredPhaseTwo = false;
            Start(Action.PhaseTwo, _deferredPhaseTwo);
        }

        /// <summary>She died. Terminal: nothing plays after this, and the last pose holds.</summary>
        public void BeginDeath(in ClipPlan plan)
        {
            if (_active == Action.Death) return;
            _pending = Action.None;
            _hasDeferredPhaseTwo = false;
            _active = Action.Death;
            _activeRemaining = 0f;
            Started?.Invoke(Action.Death, plan);
        }

        /// <summary>Advance by seconds on her clock. Negative, zero and NaN do nothing,
        /// so a frozen Hypatia holds.</summary>
        public void Tick(float bossDelta)
        {
            if (!(bossDelta > 0f)) return;

            if (_active is not (Action.None or Action.Topple or Action.Death))
            {
                _activeRemaining -= bossDelta;
                if (_activeRemaining <= 0f)
                {
                    Action ended = _active;
                    _active = Action.None;
                    _activeRemaining = 0f;
                    // A clip due this same tick takes over without a return to rest.
                    if (!PendingIsDue(bossDelta)) Finished?.Invoke(ended);
                }
            }

            if (_pending == Action.None) return;
            _pendingDelay -= bossDelta;
            if (_pendingDelay > 0f) return;

            Action due = _pending;
            ClipPlan plan = _pendingPlan;
            _pending = Action.None;
            // Something more important took over while it waited: its moment is lost.
            if (Priority(_active) >= Priority(due)) return;
            Start(due, plan);
        }

        bool PendingIsDue(float bossDelta) => _pending != Action.None && _pendingDelay - bossDelta <= 0f;

        void Start(Action action, in ClipPlan plan)
        {
            _active = action;
            _activeRemaining = plan.Duration;
            Started?.Invoke(action, plan);
        }
    }
}
