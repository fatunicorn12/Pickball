using Pickleball.Sandbox.Juggle;
using UnityEngine;

namespace Pickleball.Sandbox.Enemies
{
    /// <summary>
    /// One enemy type's numbers. The defaults are the basic enemy: 300 HP, 10 damage
    /// every 0.5 s, running at the player once within range. A new enemy type starts
    /// by making another of these; most differences need no code.
    ///
    /// Read live, except <see cref="MaxHealth"/>, which applies when the enemy spawns.
    /// </summary>
    [CreateAssetMenu(fileName = "EnemyConfig", menuName = "Pickleball/Enemies/Enemy Config")]
    public class EnemyConfig : ScriptableObject
    {
        [Header("Vitals")]
        [Tooltip("Hit points. Applied when it spawns, replacing whatever its Health says.")]
        [Min(1f)] [SerializeField] private float maxHealth = 300f;

        [Tooltip("Seconds the body stays after defeat before it despawns. Long enough for " +
                 "the death to read; shorter clears a crowd sooner.")]
        [Min(0f)] [SerializeField] private float corpseSeconds = 2f;

        [Header("Awareness")]
        [Tooltip("Metres. It notices the player inside this, and starts running at them.")]
        [Min(0f)] [SerializeField] private float aggroRange = 14f;

        [Tooltip("Metres. Once it has noticed, it gives up only beyond this. Keep it above " +
                 "Aggro Range: the gap is what stops it dithering at the edge.")]
        [Min(0f)] [SerializeField] private float loseRange = 20f;

        [Header("Chase")]
        [Tooltip("Running speed, m/s. The player runs at 5 and sprints at 7, so 4.5 can be " +
                 "outrun at any time but closes in the moment you stop.")]
        [Min(0f)] [SerializeField] private float runSpeed = 4.5f;

        [Tooltip("How fast it turns to face the player, as a smoothing rate per second. " +
                 "Higher snaps round; lower swings wide.")]
        [Min(0f)] [SerializeField] private float turnRate = 10f;

        [Tooltip("Metres from the player's centre it stops running at. Keep it under Attack " +
                 "Reach, or it stands just too far away to hit.")]
        [Min(0f)] [SerializeField] private float stopDistance = 1.3f;

        [Header("Attack")]
        [Tooltip("Damage per strike. The player's grey (recoverable) pool applies to it.")]
        [Min(0f)] [SerializeField] private float attackDamage = 10f;

        [Tooltip("Seconds between strikes. The player's hit grace is 0.5 s too, so several " +
                 "enemies together cannot out-hit one: a strike inside the grace is refused. " +
                 "Below that grace, even a single enemy loses strikes to it.")]
        [Min(0.01f)] [SerializeField] private float attackInterval = 0.5f;

        [Tooltip("Seconds from getting in reach to the first strike. 0 hurts on contact; " +
                 "more gives a moment to step away.")]
        [Min(0f)] [SerializeField] private float firstHitDelay = 0.25f;

        [Tooltip("Horizontal metres from the player's centre at which a strike can land.")]
        [Min(0f)] [SerializeField] private float attackReach = 1.7f;

        [Tooltip("Metres of height difference at which a strike can land. A jump clears " +
                 "this, so jumping over one dodges it.")]
        [Min(0f)] [SerializeField] private float attackHeight = 1.4f;

        [Header("Falling")]
        [Tooltip("Gravity while walking off a ledge, m/s². (Juggles use their own, below.)")]
        [Min(0f)] [SerializeField] private float gravity = 20f;

        [Tooltip("Fastest it falls, m/s.")]
        [Min(0f)] [SerializeField] private float maxFallSpeed = 18f;

        [Header("Getting hit")]
        [Tooltip("Weight class and juggle tuning: how launchers lift it and how long a hit " +
                 "stuns it. See Docs/Systems/Juggle.md.")]
        [SerializeField] private JuggleSettings juggle = JuggleSettings.For(JuggleWeight.Medium);

        [Tooltip("How much damage in one stretch before it breaks out of a combo and fights " +
                 "back through hits. See Docs/Systems/Juggle.md.")]
        [SerializeField] private RevengeSettings revenge = RevengeSettings.For(JuggleWeight.Medium);

        /// <summary>Hit points, applied on spawn.</summary>
        public float MaxHealth => maxHealth;
        /// <summary>Seconds the body stays after defeat.</summary>
        public float CorpseSeconds => corpseSeconds;
        /// <summary>Metres at which it notices the player.</summary>
        public float AggroRange => aggroRange;
        /// <summary>Metres beyond which it gives up.</summary>
        public float LoseRange => loseRange;
        /// <summary>Running speed, m/s.</summary>
        public float RunSpeed => runSpeed;
        /// <summary>Turn smoothing rate per second.</summary>
        public float TurnRate => turnRate;
        /// <summary>Metres from the player it stops at.</summary>
        public float StopDistance => stopDistance;
        /// <summary>Damage per strike.</summary>
        public float AttackDamage => attackDamage;
        /// <summary>Seconds between strikes.</summary>
        public float AttackInterval => attackInterval;
        /// <summary>Seconds from entering reach to the first strike.</summary>
        public float FirstHitDelay => firstHitDelay;
        /// <summary>Horizontal strike reach, m.</summary>
        public float AttackReach => attackReach;
        /// <summary>Height difference a strike can cross, m.</summary>
        public float AttackHeight => attackHeight;
        /// <summary>Falling gravity, m/s².</summary>
        public float Gravity => gravity;
        /// <summary>Terminal fall speed, m/s.</summary>
        public float MaxFallSpeed => maxFallSpeed;
        /// <summary>Juggle tuning.</summary>
        public JuggleSettings Juggle => juggle;
        /// <summary>Combo break-out tuning.</summary>
        public RevengeSettings Revenge => revenge;
    }
}
