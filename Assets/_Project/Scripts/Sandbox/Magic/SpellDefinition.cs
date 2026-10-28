using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// One aimed spell as data: what it costs, how it aims, how its orb flies
    /// and how hard it bursts. The default values are the arcane orb — a slow,
    /// glowing ball fired at the crosshair in the style of Expedition 33's free
    /// aim, which spends 15 of 100 mana per shot.
    ///
    /// Every tunable lives here rather than on <see cref="SpellCaster"/> or
    /// <see cref="MagicOrb"/>, so a second spell (a faster bolt, a lobbed bomb)
    /// is a new asset, not a code change, and live tuning in Play mode reaches
    /// orbs already in flight: the orb reads its definition every frame.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Magic/Spell", fileName = "Spell")]
    public class SpellDefinition : ScriptableObject
    {
        [Header("Cost")]
        [Tooltip("Mana spent per shot, in points (the bar holds 100 by default). " +
                 "Higher means fewer shots before the regen delay kicks in.")]
        [Min(0)] [SerializeField] private int manaCost = 15;

        [Tooltip("Seconds after a shot before the next one can fire. Presses inside " +
                 "it are ignored silently (not a 'no mana' failure). Lower feels like " +
                 "a machine gun; higher makes each orb a decision.")]
        [Min(0f)] [SerializeField] private float cooldown = 0.45f;

        [Tooltip("Off: one orb per press. On: holding fire repeats a shot every " +
                 "cooldown while mana lasts.")]
        [SerializeField] private bool autoRepeat;

        [Header("Aim")]
        [Tooltip("How far the crosshair ray reaches, in metres. A miss aims at the " +
                 "point this far along the camera's view.")]
        [Min(1f)] [SerializeField] private float maxRange = 60f;

        [Tooltip("Aim assist, in metres: when the thin crosshair ray is not on an " +
                 "enemy, a sphere this thick is cast for one. 0 turns assist off; much " +
                 "above 0.5 starts snapping to targets you were not aiming at.")]
        [Min(0f)] [SerializeField] private float aimAssistRadius = 0.35f;

        [Tooltip("Stick magnitude cap while aiming (0-1). 0.45 is a deliberate walk " +
                 "(and no sprint); 1 lets him run at full speed while aiming.")]
        [Range(0f, 1f)] [SerializeField] private float aimMoveScale = 0.45f;

        [Tooltip("Metres. An aim point closer to the hand than this (hugging a wall) " +
                 "fires straight along the camera's view instead of at the point, so " +
                 "point-blank shots do not swing about.")]
        [Min(0f)] [SerializeField] private float minAimDistance = 1.5f;

        [Header("Orb")]
        [Tooltip("The orb spawned per shot. Built by MagicOrbPrefabBuilder " +
                 "(Tools ▸ Pickleball ▸ Setup Magic). Missing = the spell cannot fire.")]
        [SerializeField] private MagicOrb orbPrefab;

        [Tooltip("Flight speed, in m/s. ~9 is slow enough to watch it travel and " +
                 "for a moving target to dodge; much faster stops reading as an orb.")]
        [Min(0.1f)] [SerializeField] private float orbSpeed = 9f;

        [Tooltip("Collision radius of the orb, in metres. Bigger is more forgiving " +
                 "and bursts earlier on walls and floors.")]
        [Min(0.01f)] [SerializeField] private float orbRadius = 0.28f;

        [Tooltip("Seconds of flight before an orb that hit nothing fizzles out. " +
                 "Range in metres is this times the speed (4 s x 9 m/s = 36 m).")]
        [Min(0.1f)] [SerializeField] private float orbLifetime = 4f;

        [Tooltip("Homing toward the enemy that was under the crosshair at launch, in " +
                 "degrees per second. 0 flies straight; ~25 corrects a near miss; " +
                 "much higher turns it into a guided missile.")]
        [Min(0f)] [SerializeField] private float homingTurnRate = 25f;

        [Header("Blast")]
        [Tooltip("Damage dealt to whatever the orb touches directly. It is not hit " +
                 "again by the blast.")]
        [Min(0f)] [SerializeField] private float directDamage = 14f;

        [Tooltip("Burst radius, in metres. Everything else damageable inside it " +
                 "takes blast damage; the burst shell grows to exactly this size.")]
        [Min(0f)] [SerializeField] private float blastRadius = 2.2f;

        [Tooltip("Blast damage at the burst centre.")]
        [Min(0f)] [SerializeField] private float blastDamageMax = 10f;

        [Tooltip("Blast damage at the edge of the radius. Above 0 so being clipped " +
                 "by the edge still counts as a hit.")]
        [Min(0f)] [SerializeField] private float blastDamageMin = 4f;

        [Tooltip("Horizontal shove, in m/s, along the orb's travel for the direct " +
                 "hit and away from the centre for the blast (scaled by blast falloff). " +
                 "How a target reacts is its own business: the boss ignores it.")]
        [Min(0f)] [SerializeField] private float knockback = 4f;

        [Tooltip("Layers the orb collides with and the blast reaches. Triggers are " +
                 "always ignored.")]
        [SerializeField] private LayerMask hitMask = ~0;

        /// <summary>Mana points one shot costs.</summary>
        public int ManaCost => manaCost;

        /// <summary>Seconds between shots.</summary>
        public float Cooldown => cooldown;

        /// <summary>True when holding fire repeats the shot every cooldown.</summary>
        public bool AutoRepeat => autoRepeat;

        /// <summary>Metres the crosshair ray reaches.</summary>
        public float MaxRange => maxRange;

        /// <summary>Metres of aim-assist sphere cast; 0 = off.</summary>
        public float AimAssistRadius => aimAssistRadius;

        /// <summary>Stick magnitude cap (0-1) while aiming. Applied by whoever
        /// listens to <see cref="SpellCaster.AimStarted"/>, not by the caster.</summary>
        public float AimMoveScale => aimMoveScale;

        /// <summary>Metres below which an aim point is too close to fire at.</summary>
        public float MinAimDistance => minAimDistance;

        /// <summary>The orb prefab spawned per shot; may be null (cannot fire).</summary>
        public MagicOrb OrbPrefab => orbPrefab;

        /// <summary>Orb flight speed, m/s.</summary>
        public float OrbSpeed => orbSpeed;

        /// <summary>Orb collision radius, m.</summary>
        public float OrbRadius => orbRadius;

        /// <summary>Seconds before an orb that hit nothing fizzles.</summary>
        public float OrbLifetime => orbLifetime;

        /// <summary>Homing turn rate, degrees per second; 0 = straight.</summary>
        public float HomingTurnRate => homingTurnRate;

        /// <summary>Damage to the thing the orb touched.</summary>
        public float DirectDamage => directDamage;

        /// <summary>Blast radius, m.</summary>
        public float BlastRadius => blastRadius;

        /// <summary>Blast damage at the centre.</summary>
        public float BlastDamageMax => blastDamageMax;

        /// <summary>Blast damage at the edge of the radius.</summary>
        public float BlastDamageMin => blastDamageMin;

        /// <summary>Knockback speed, m/s.</summary>
        public float Knockback => knockback;

        /// <summary>Layers the orb and its blast can hit.</summary>
        public LayerMask HitMask => hitMask;
    }
}
