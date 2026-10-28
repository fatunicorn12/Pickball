namespace Pickleball.Sandbox
{
    /// <summary>
    /// Smash-style combo bookkeeping for the training stage, with no Unity
    /// dependencies so it can be unit tested. Feed it hits and the clock; it
    /// keeps the running count, the damage total, the last hit and the chain
    /// step, and forgets them all once <see cref="Timeout"/> seconds pass with
    /// no new hit (the same idea as <c>TrainingDummy.refillDelay</c>: the dummy
    /// is back at full health, so the old combo is over).
    /// </summary>
    public sealed class ComboTracker
    {
        public float Timeout { get; set; }

        public int Hits { get; private set; }
        public float TotalDamage { get; private set; }
        public float LastHit { get; private set; }
        /// <summary>1-based position in the sword chain at the latest swing; 0 when unknown.</summary>
        public int ChainStep { get; private set; }
        public string StepName { get; private set; } = string.Empty;
        public bool Active => Hits > 0;

        float _lastHitTime, _lastSwingTime;

        public ComboTracker(float timeout) { Timeout = timeout; }

        /// <summary>A hit landed on the dummy for <paramref name="damage"/> at time <paramref name="now"/>.</summary>
        public void RegisterHit(float damage, float now)
        {
            if (damage <= 0f) return;
            // A hit that arrives after the timeout but before the next Tick still starts a fresh combo.
            if (Hits > 0 && now - _lastHitTime >= Timeout) Clear();
            Hits++;
            TotalDamage += damage;
            LastHit = damage;
            _lastHitTime = now;
        }

        /// <summary>The player started a swing; <paramref name="index"/> is 0-based.</summary>
        public void RegisterSwing(int index, string name, float now)
        {
            ChainStep = index + 1;
            StepName = name ?? string.Empty;
            _lastSwingTime = now;
        }

        /// <summary>Call every frame. Returns true when anything just expired.</summary>
        public bool Tick(float now)
        {
            bool expired = false;
            if (Hits > 0 && now - _lastHitTime >= Timeout) { Clear(); expired = true; }
            if (ChainStep > 0 && now - _lastSwingTime >= Timeout)
            {
                ChainStep = 0;
                StepName = string.Empty;
                expired = true;
            }
            return expired;
        }

        public void Clear()
        {
            Hits = 0;
            TotalDamage = 0f;
            LastHit = 0f;
            ChainStep = 0;
            StepName = string.Empty;
        }
    }
}
