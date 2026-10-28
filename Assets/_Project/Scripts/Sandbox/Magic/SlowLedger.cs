using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The slows on one target, by source: each source (a zone) holds one entry
    /// with a multiplier and an expiry time, and the target runs at the
    /// strongest active one. This is the stacking rule <see cref="ISlowable"/>
    /// promises, kept out of <see cref="StatusReceiver"/> so it is tested
    /// without a scene.
    ///
    /// <b>Strongest wins, nothing multiplies.</b> Two overlapping 30% zones
    /// leave the target at 30%, not 9%: stacking slows multiplicatively turns a
    /// slow into a freeze, which is a different spell.
    ///
    /// <b>Time is injected</b> (<c>now</c>, in seconds on any clock that only
    /// moves forward), so expiry is exact at any frame rate. Expired entries are
    /// dropped lazily when read. A fixed array, no allocation: with more
    /// sources than slots, the entry that expires soonest is replaced.
    /// </summary>
    public sealed class SlowLedger
    {
        const int DefaultCapacity = 8;

        struct Entry
        {
            public object Source;
            public float Multiplier;
            public float ExpiresAt;
        }

        readonly Entry[] _entries;
        int _count;

        /// <summary>Creates a ledger with room for <paramref name="capacity"/>
        /// simultaneous sources.</summary>
        public SlowLedger(int capacity = DefaultCapacity)
        {
            _entries = new Entry[Mathf.Max(1, capacity)];
        }

        /// <summary>Number of entries not yet pruned (some may have expired).</summary>
        public int Count => _count;

        /// <summary>
        /// Sets <paramref name="source"/>'s slow to <paramref name="speedMultiplier"/>
        /// (clamped to 0-1) until <paramref name="now"/> + <paramref name="seconds"/>,
        /// replacing whatever that source held. Zero or negative seconds removes
        /// the source's entry. A null source is ignored.
        /// </summary>
        public void Apply(object source, float speedMultiplier, float seconds, float now)
        {
            if (source == null) return;
            int i = IndexOf(source);
            if (seconds <= 0f)
            {
                if (i >= 0) RemoveAt(i);
                return;
            }
            if (i < 0) i = FreeSlot();
            _entries[i] = new Entry
            {
                Source = source,
                Multiplier = Mathf.Clamp01(speedMultiplier),
                ExpiresAt = now + seconds,
            };
        }

        /// <summary>The speed multiplier at <paramref name="now"/>: the lowest
        /// unexpired entry's, or 1 when nothing slows the target. An entry counts
        /// up to, but not at, its expiry time.</summary>
        public float MultiplierAt(float now)
        {
            Prune(now);
            float result = 1f;
            for (int i = 0; i < _count; i++)
                if (_entries[i].Multiplier < result) result = _entries[i].Multiplier;
            return result;
        }

        /// <summary>True while any slow is active at <paramref name="now"/>.</summary>
        public bool IsSlowedAt(float now)
        {
            Prune(now);
            return _count > 0;
        }

        /// <summary>Seconds until the last active slow runs out; 0 when none.</summary>
        public float RemainingAt(float now)
        {
            Prune(now);
            float latest = now;
            for (int i = 0; i < _count; i++)
                if (_entries[i].ExpiresAt > latest) latest = _entries[i].ExpiresAt;
            return latest - now;
        }

        /// <summary>Drops every slow (a cleanse, a respawn).</summary>
        public void Clear()
        {
            for (int i = 0; i < _count; i++) _entries[i] = default;
            _count = 0;
        }

        void Prune(float now)
        {
            for (int i = _count - 1; i >= 0; i--)
                if (_entries[i].ExpiresAt <= now) RemoveAt(i);
        }

        void RemoveAt(int i)
        {
            _count--;
            _entries[i] = _entries[_count];
            _entries[_count] = default;
        }

        int IndexOf(object source)
        {
            for (int i = 0; i < _count; i++)
                if (ReferenceEquals(_entries[i].Source, source)) return i;
            return -1;
        }

        int FreeSlot()
        {
            if (_count < _entries.Length) return _count++;
            int soonest = 0;
            for (int i = 1; i < _entries.Length; i++)
                if (_entries[i].ExpiresAt < _entries[soonest].ExpiresAt) soonest = i;
            return soonest;
        }
    }
}
