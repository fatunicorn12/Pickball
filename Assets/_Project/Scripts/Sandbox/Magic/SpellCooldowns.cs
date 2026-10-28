using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// One cooldown per spell, so selecting another spell never resets the one
    /// you just cast, and the selector HUD can show every spell's recovery at
    /// once.
    ///
    /// Keyed by reference (the <see cref="SpellDefinition"/> asset), held in a
    /// small fixed array: no allocation per cast or per frame. With more spells
    /// than slots, the slot whose cooldown is nearest done is reused, so an
    /// overflow can only ever shorten a nearly finished cooldown.
    ///
    /// Plain C# with explicit <see cref="Tick"/>, so it is tested without a
    /// scene. A single spell behaves exactly like the caster's old single
    /// cooldown: <see cref="Start"/> sets it full, <see cref="Tick"/> counts it
    /// down to zero and never below.
    /// </summary>
    public sealed class SpellCooldowns
    {
        const int DefaultCapacity = 8;

        struct Entry
        {
            public object Key;
            public float Left;
            public float Total;
        }

        readonly Entry[] _entries;
        int _count;

        /// <summary>Creates a table with room for <paramref name="capacity"/> spells.</summary>
        public SpellCooldowns(int capacity = DefaultCapacity)
        {
            _entries = new Entry[Mathf.Max(1, capacity)];
        }

        /// <summary>Starts (or restarts) <paramref name="key"/>'s cooldown at
        /// <paramref name="seconds"/>. Zero or less clears it.</summary>
        public void Start(object key, float seconds)
        {
            if (key == null) return;
            int i = IndexOf(key);
            if (seconds <= 0f)
            {
                if (i >= 0) _entries[i].Left = 0f;
                return;
            }
            if (i < 0) i = FreeSlot();
            _entries[i] = new Entry { Key = key, Left = seconds, Total = seconds };
        }

        /// <summary>Seconds until <paramref name="key"/> can be cast again; 0 when ready.</summary>
        public float Remaining(object key)
        {
            int i = IndexOf(key);
            return i >= 0 ? _entries[i].Left : 0f;
        }

        /// <summary>1 right after the cast, falling to 0 when ready.</summary>
        public float Normalised(object key)
        {
            int i = IndexOf(key);
            if (i < 0 || _entries[i].Total <= 0f) return 0f;
            return Mathf.Clamp01(_entries[i].Left / _entries[i].Total);
        }

        /// <summary>Counts every cooldown down by <paramref name="dt"/> seconds,
        /// stopping at zero.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            for (int i = 0; i < _count; i++)
                if (_entries[i].Left > 0f) _entries[i].Left = Mathf.Max(0f, _entries[i].Left - dt);
        }

        /// <summary>Every spell ready at once (respawn, debug).</summary>
        public void Clear()
        {
            for (int i = 0; i < _count; i++) _entries[i] = default;
            _count = 0;
        }

        int IndexOf(object key)
        {
            if (key == null) return -1;
            for (int i = 0; i < _count; i++)
                if (ReferenceEquals(_entries[i].Key, key)) return i;
            return -1;
        }

        int FreeSlot()
        {
            if (_count < _entries.Length) return _count++;
            int best = 0;
            for (int i = 1; i < _entries.Length; i++)
                if (_entries[i].Left < _entries[best].Left) best = i;
            return best;
        }
    }
}
