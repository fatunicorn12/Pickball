using System;
using UnityEngine;

namespace Pickleball.Sandbox.Juggle
{
    /// <summary>
    /// How much punishment one enemy takes before it breaks out of a combo, in
    /// damage. Serialized inline on the enemy, like <see cref="JuggleSettings"/>,
    /// so it can be tuned live. Measured in damage rather than hits so that a
    /// heavy weapon's fewer, bigger swings fill it no faster than a light one's.
    /// </summary>
    [Serializable]
    public struct RevengeSettings
    {
        [Tooltip("Damage taken in one stretch before it breaks out. 0 = never, so it can be " +
                 "stunlocked. A compass with every ability learned deals about 130 per " +
                 "ground-and-air loop.")]
        [Min(0f)] [SerializeField] private float limit;

        [Tooltip("Damage the gauge sheds per second once hits stop. Higher forgives a pause sooner.")]
        [Min(0f)] [SerializeField] private float decayPerSecond;

        [Tooltip("Seconds after a hit before the gauge starts to shed. Bridges the gap between " +
                 "swings, so a steady combo keeps filling it.")]
        [Min(0f)] [SerializeField] private float decayDelay;

        [Tooltip("Seconds after breaking out during which hits still hurt but no longer stun, " +
                 "lift or push it. This is the window an enemy's counterattack plays in.")]
        [Min(0f)] [SerializeField] private float armorSeconds;

        public float Limit => limit;
        public float DecayPerSecond => decayPerSecond;
        public float DecayDelay => decayDelay;
        public float ArmorSeconds => armorSeconds;

        public RevengeSettings(float limit, float decayPerSecond = 30f, float decayDelay = 1f,
                               float armorSeconds = 2f)
        {
            this.limit = Mathf.Max(0f, limit);
            this.decayPerSecond = Mathf.Max(0f, decayPerSecond);
            this.decayDelay = Mathf.Max(0f, decayDelay);
            this.armorSeconds = Mathf.Max(0f, armorSeconds);
        }

        /// <summary>Starting numbers for a weight class: a lightweight stays in a combo
        /// for longest, and a heavyweight shrugs one off soonest.</summary>
        public static RevengeSettings For(JuggleWeight weight) => weight switch
        {
            JuggleWeight.Light => new RevengeSettings(220f),
            JuggleWeight.Heavy => new RevengeSettings(100f),
            _ => new RevengeSettings(150f),
        };
    }
}
