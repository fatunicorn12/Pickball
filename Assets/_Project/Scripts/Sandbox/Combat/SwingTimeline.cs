namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// The clock of one swing: where it is, and which of its windows it has
    /// just crossed.
    ///
    /// The combo is driven by this timer rather than read back off the
    /// Animator. Animator state info is unreliable the frame a transition
    /// happens, and every window here — blade live, chain open, burst release —
    /// needs frame-accurate timing. The step's duration is derived from the
    /// very clip its Animator state plays (see
    /// <see cref="ComboStepDefinition.Duration"/>), so the two stay in step.
    ///
    /// Pure C#: frozen simply by not advancing it, which is how hitstop works.
    /// </summary>
    public sealed class SwingTimeline
    {
        float _duration = 1f;
        SwingWindows _windows;
        float _burstAt = -1f;
        bool _bladeLive, _burstFired;

        /// <summary>Seconds since the swing started (hitstop excluded).</summary>
        public float Elapsed { get; private set; }

        /// <summary>Progress through the swing, 0-1.</summary>
        public float Normalized => _duration > 0f ? Elapsed / _duration : 1f;

        /// <summary>True from <see cref="Start"/> until the swing finishes or is stopped.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>True while the blade should be swept for hits.</summary>
        public bool IsBladeLive => IsRunning && _bladeLive;

        /// <summary>True once a buffered press may chain into the next step.</summary>
        public bool IsChainWindowOpen => IsRunning && Normalized >= _windows.ComboOpen;

        /// <summary>Begin a swing of <paramref name="duration"/> seconds.
        /// <paramref name="burstAt"/> is the fraction at which to release the
        /// area burst, or negative for none.</summary>
        public void Start(float duration, SwingWindows windows, float burstAt = -1f)
        {
            _duration = duration > 0.01f ? duration : 0.01f;
            _windows = windows;
            _burstAt = burstAt;
            _bladeLive = false;
            _burstFired = false;
            Elapsed = 0f;
            IsRunning = true;
        }

        /// <summary>Convenience overload reading everything off a step asset.</summary>
        public void Start(ComboStepDefinition step)
            => Start(step.Duration, step.Windows, step.Burst.Enabled ? step.Burst.TriggerAt : -1f);

        /// <summary>
        /// Move the clock on by <paramref name="dt"/> seconds and report every
        /// window crossed. Crossing both edges of a narrow active window in one
        /// tick reports BladeOn and BladeOff together, so the caller still gets
        /// one sweep in.
        /// </summary>
        public SwingSignal Advance(float dt)
        {
            if (!IsRunning) return SwingSignal.None;
            var signal = SwingSignal.None;
            float before = Normalized;
            Elapsed += dt;
            float t = Normalized;

            bool shouldBeLive = t >= _windows.ActiveStart && t <= _windows.ActiveEnd;
            bool crossedWholeWindow = before < _windows.ActiveStart && t > _windows.ActiveEnd;
            if (crossedWholeWindow)
            {
                signal |= SwingSignal.BladeOn | SwingSignal.BladeOff;
                _bladeLive = false;
            }
            else if (shouldBeLive != _bladeLive)
            {
                signal |= shouldBeLive ? SwingSignal.BladeOn : SwingSignal.BladeOff;
                _bladeLive = shouldBeLive;
            }

            if (!_burstFired && _burstAt >= 0f && t >= _burstAt)
            {
                _burstFired = true;
                signal |= SwingSignal.BurstDue;
            }

            if (t >= _windows.ComboClose || t >= 1f)
            {
                if (_bladeLive) signal |= SwingSignal.BladeOff;
                _bladeLive = false;
                IsRunning = false;
                signal |= SwingSignal.Finished;
            }
            return signal;
        }

        /// <summary>Abandon the swing (chained out of, or interrupted). Reports
        /// BladeOff if the blade was live so listeners can tidy up.</summary>
        public SwingSignal Stop()
        {
            var signal = _bladeLive ? SwingSignal.BladeOff : SwingSignal.None;
            _bladeLive = false;
            IsRunning = false;
            return signal;
        }
    }
}
