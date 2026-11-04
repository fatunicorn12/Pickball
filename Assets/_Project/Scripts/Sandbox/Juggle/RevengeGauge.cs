using UnityEngine;

namespace Pickleball.Sandbox.Juggle
{
    /// <summary>
    /// Kingdom Hearts II's revenge value: an enemy keeps a tally of the damage it
    /// has taken in one stretch, and past a limit it breaks out of the combo.
    /// Without this, chains and finisher rings stunlock anything that reacts to
    /// being hit.
    ///
    /// Separate from <see cref="JuggleState"/> because it answers a different
    /// question: the juggle says what a hit does to the body, and this says whether
    /// the body reacts at all. The owner asks here first. Plain C#, dt-driven.
    /// </summary>
    public sealed class RevengeGauge
    {
        /// <summary>Tuning, re-read every hit and tick so live inspector edits apply.</summary>
        public RevengeSettings Settings { get; set; }

        /// <summary>Damage tallied so far.</summary>
        public float Value { get; private set; }

        /// <summary>Seconds of armor left after breaking out.</summary>
        public float ArmorLeft { get; private set; }

        /// <summary>True while hits still hurt but no longer stun, lift or push it.</summary>
        public bool IsArmored => ArmorLeft > 0f;

        float _decayDelayLeft;

        public RevengeGauge(RevengeSettings settings) => Settings = settings;

        /// <summary>Tally a hit's damage. True on the one hit that breaks it out, which
        /// empties the gauge and starts the armor. While armored nothing is tallied,
        /// so the gauge starts afresh when the armor ends.</summary>
        public bool Add(float damage)
        {
            if (IsArmored || Settings.Limit <= 0f) return false;
            Value += Mathf.Max(0f, damage);
            _decayDelayLeft = Settings.DecayDelay;
            if (Value < Settings.Limit) return false;

            Value = 0f;
            ArmorLeft = Settings.ArmorSeconds;
            return true;
        }

        /// <summary>Advance by <paramref name="dt"/>: run the armor down, and shed the
        /// tally once the delay after the last hit has passed. Any delay left over
        /// inside this tick still sheds, so the rate doesn't depend on frame rate.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            ArmorLeft = Mathf.Max(0f, ArmorLeft - dt);

            float shedFor = dt - _decayDelayLeft;
            _decayDelayLeft = Mathf.Max(0f, _decayDelayLeft - dt);
            if (shedFor > 0f) Value = Mathf.Max(0f, Value - Settings.DecayPerSecond * shedFor);
        }

        /// <summary>Empty and unarmored: a respawn.</summary>
        public void Reset()
        {
            Value = 0f;
            ArmorLeft = 0f;
            _decayDelayLeft = 0f;
        }
    }
}
