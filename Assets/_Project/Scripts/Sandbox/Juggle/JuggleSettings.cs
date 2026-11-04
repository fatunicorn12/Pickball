using System;
using UnityEngine;

namespace Pickleball.Sandbox.Juggle
{
    /// <summary>
    /// One enemy's juggle tuning: how high a launcher pops it, how much each
    /// air hit lifts it again, how fast that lift wears off, and how long a hit
    /// stuns it. Serialized inline on the enemy (like <see cref="Combat.HitSettings"/>
    /// on a step) so each one can be tuned live in Play mode.
    ///
    /// Every air hit applies Castle Crashers' rule
    /// <c>vy = max(vy, relaunchSpeed * weightScale * decay^n)</c>, n being the
    /// air hits taken since the last landing. A lift under
    /// <see cref="MinRelaunch"/> is dropped to nothing, so a decaying enemy
    /// stops being lifted at a definite hit; from then on each hit only stalls
    /// its fall (<c>max(vy, 0)</c>) and it steps down to the ground. A relaunch is also capped so the arc peaks no higher
    /// than <see cref="MaxHeight"/>: any lift strong enough to hold a body up
    /// would otherwise also make it climb a little more with every hit.
    /// </summary>
    [Serializable]
    public struct JuggleSettings
    {
        [Tooltip("Weight class these numbers came from. A label plus the preset the " +
                 "owner's 'Apply Weight Preset' menu restores; the juggle reads the numbers below.")]
        [SerializeField] private JuggleWeight weight;

        [Tooltip("Upward speed a launcher gives this enemy from the ground, in m/s, " +
                 "before weightScale. 3.7 m/s is a 0.7 m pop at 9.81 m/s².")]
        [Min(0f)] [SerializeField] private float launchSpeed;

        [Tooltip("Upward speed the FIRST air hit gives, in m/s, before weightScale and decay. " +
                 "Air hits need about gravity × hit interval ÷ 2 (≈1.7 m/s at 0.35 s) to hold it up.")]
        [Min(0f)] [SerializeField] private float relaunchSpeed;

        [Tooltip("Multiplies both launch and relaunch speed. Under 1 is heavier: lower pops.")]
        [Min(0f)] [SerializeField] private float weightScale;

        [Tooltip("Relaunch kept per air hit already taken (the decay^n). 1 = never tires, " +
                 "juggles forever; lower drops it sooner. 0.9 drops after ~15-18 hits.")]
        [Range(0f, 1f)] [SerializeField] private float decay;

        [Tooltip("Relaunches slower than this, in m/s, lift nothing: the hit only stalls the " +
                 "enemy's fall. Sets the hit where a decaying juggle gives out.")]
        [Min(0f)] [SerializeField] private float minRelaunch;

        [Tooltip("Highest an air hit can send it, in m above where it stands: a relaunch " +
                 "is cut so its arc peaks here. Keeps a lightweight loop at reachable height.")]
        [Min(0.1f)] [SerializeField] private float maxHeight;

        [Tooltip("Seconds a hit stuns, refreshed by every hit. A stunned enemy can't act; " +
                 "an airborne one can't act until it lands either.")]
        [Min(0f)] [SerializeField] private float hitstun;

        [Tooltip("Downward pull while juggled, in m/s². Lower is floatier and easier to " +
                 "keep up; 9.81 matches the project's physics.")]
        [Min(0.01f)] [SerializeField] private float gravity;

        [Tooltip("On: a defeat in the air waits until the enemy lands (Castle Crashers). " +
                 "Off: it dies where it is hit.")]
        [SerializeField] private bool defeatOnLanding;

        [Tooltip("Hitstun multiplier for a Heavy blow (a slam). 2 staggers twice as long as a flinch.")]
        [Min(1f)] [SerializeField] private float heavyStunScale;

        [Tooltip("Upward speed a Knock Away throws it with, in m/s, before weightScale. " +
                 "Higher is a longer, more dramatic arc; 3 m/s is about a 0.5 m hop.")]
        [Min(0f)] [SerializeField] private float knockAwayLift;

        [Tooltip("Share of the blow's knockback speed it flies away with. 1 is the blow's own " +
                 "speed; lower for a target that should not leave blade reach.")]
        [Min(0f)] [SerializeField] private float knockAwayCarry;

        [Tooltip("Seconds it stays down after a Knock Away lands. It ignores hits and can't act " +
                 "meanwhile, which is the breather a finisher buys.")]
        [Min(0f)] [SerializeField] private float recoverSeconds;

        public JuggleWeight Weight => weight;
        public float LaunchSpeed => launchSpeed;
        public float RelaunchSpeed => relaunchSpeed;
        public float WeightScale => weightScale;
        public float Decay => decay;
        public float MinRelaunch => minRelaunch;
        public float MaxHeight => maxHeight;
        public float Hitstun => hitstun;
        public float Gravity => gravity;
        public bool DefeatOnLanding => defeatOnLanding;
        public float HeavyStunScale => heavyStunScale;
        public float KnockAwayLift => knockAwayLift;
        public float KnockAwayCarry => knockAwayCarry;
        public float RecoverSeconds => recoverSeconds;

        public JuggleSettings(JuggleWeight weight, float launchSpeed, float relaunchSpeed,
                              float weightScale, float decay, float minRelaunch, float maxHeight,
                              float hitstun, float gravity = 9.81f, bool defeatOnLanding = true,
                              float heavyStunScale = 2f, float knockAwayLift = 3f,
                              float knockAwayCarry = 0.6f, float recoverSeconds = 0.8f)
        {
            this.heavyStunScale = Mathf.Max(1f, heavyStunScale);
            this.knockAwayLift = Mathf.Max(0f, knockAwayLift);
            this.knockAwayCarry = Mathf.Max(0f, knockAwayCarry);
            this.recoverSeconds = Mathf.Max(0f, recoverSeconds);
            this.weight = weight;
            this.launchSpeed = Mathf.Max(0f, launchSpeed);
            this.relaunchSpeed = Mathf.Max(0f, relaunchSpeed);
            this.weightScale = Mathf.Max(0f, weightScale);
            this.decay = Mathf.Clamp01(decay);
            this.minRelaunch = Mathf.Max(0f, minRelaunch);
            this.maxHeight = Mathf.Max(0.1f, maxHeight);
            this.hitstun = Mathf.Max(0f, hitstun);
            this.gravity = Mathf.Max(0.01f, gravity);
            this.defeatOnLanding = defeatOnLanding;
        }

        /// <summary>
        /// The starting numbers for a weight class, measured against an air hit
        /// every 0.3 s (<c>JuggleStateTests</c> pins them): Light never drops,
        /// Medium stops lifting at its 11th air hit and lands on its 17th,
        /// Heavy lifts once and lands on its 3rd.
        /// </summary>
        public static JuggleSettings For(JuggleWeight weight) => weight switch
        {
            JuggleWeight.Light => new JuggleSettings(weight, 3.7f, 4f, 1.1f, 1f, 1.5f, 3f, 0.6f),
            JuggleWeight.Heavy => new JuggleSettings(weight, 3.7f, 4f, 0.6f, 0.5f, 1.5f, 3f, 0.35f),
            _ => new JuggleSettings(JuggleWeight.Medium, 3.7f, 4f, 1f, 0.9f, 1.5f, 3f, 0.5f),
        };

        /// <summary>
        /// Upward speed the air hit numbered <paramref name="airHits"/> (0 = the
        /// first since landing) may set: <c>relaunch × weightScale × decay^n</c>,
        /// or 0 once that falls under <see cref="MinRelaunch"/>.
        /// </summary>
        public float RelaunchFor(int airHits)
        {
            float speed = relaunchSpeed * weightScale * Mathf.Pow(decay, Mathf.Max(0, airHits));
            return speed >= minRelaunch && speed > 0f ? speed : 0f;
        }

        /// <summary>Largest upward speed at <paramref name="height"/> whose arc still
        /// peaks at or under <see cref="MaxHeight"/>.</summary>
        public float CeilingSpeed(float height)
            => Mathf.Sqrt(2f * gravity * Mathf.Max(0f, maxHeight - height));

        /// <summary>Upward speed a launcher from the ground sets.</summary>
        public float GroundLaunch => launchSpeed * weightScale;
    }
}
