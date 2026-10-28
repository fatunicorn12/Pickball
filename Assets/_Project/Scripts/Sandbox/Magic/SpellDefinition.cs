using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// One spell as data: what it costs, how it aims, how it is delivered
    /// (<see cref="SpellDelivery"/>) and every number of that delivery. The
    /// default values are the Arcane Orb — a slow, glowing ball fired at the
    /// crosshair in the style of Expedition 33's free aim, which spends 15 of
    /// 100 mana per shot — so the original asset needs none of the newer
    /// fields set.
    ///
    /// Every tunable lives here rather than on <see cref="SpellCaster"/> or the
    /// delivery components, so a new spell of an existing kind (a faster bolt,
    /// a lobbed bomb) is a new asset, not a code change, and live tuning in
    /// Play mode reaches spells already in flight: orbs, strikes, zones and
    /// burns read their definition every frame.
    ///
    /// Fields are grouped by delivery. A group a spell's delivery does not use
    /// is ignored (the Fireball's Strike numbers do nothing), and each tooltip
    /// names the delivery that reads it.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Magic/Spell", fileName = "Spell")]
    public class SpellDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Name shown by the spell selector HUD.")]
        [SerializeField] private string displayName = "Arcane Orb";

        [Tooltip("How the spell reaches its target. Orb: a projectile (Arcane Orb, Fireball). " +
                 "Strike: lightning called onto the crosshair after a telegraph (Thunderbolt). " +
                 "Zone: an area placed on the ground (Freeze Zone). Channel: hold fire, standing " +
                 "still, to restore your own health (Heal).")]
        [SerializeField] private SpellDelivery delivery = SpellDelivery.Orb;

        [Tooltip("The spell's colour in the HUD selector (icon tint, name). Alpha unused.")]
        [SerializeField] private Color themeColour = new(0.3f, 0.88f, 1f, 1f);

        [Tooltip("Icon in the HUD selector. Setup Magic generates one if this is empty; " +
                 "replace it with painted art any time.")]
        [SerializeField] private Sprite icon;

        [Header("Art (optional models)")]
        [Tooltip("Optional model that replaces the spell's procedural centrepiece: the orb's " +
                 "core (Fireball: the fireball core), the Thunderbolt's bolt, the Freeze Zone's " +
                 "ice crystals / frost ring, the Heal's rune. Colliders on it are stripped when " +
                 "spawned. Empty = the procedural look.")]
        [SerializeField] private GameObject modelPrefab;

        [Tooltip("Optional second model, where a spell has two parts: the Thunderbolt's " +
                 "thundercloud. Ignored by the other spells.")]
        [SerializeField] private GameObject secondaryModelPrefab;

        [Tooltip("Uniform scale applied to the models above (x). Zone models are further " +
                 "scaled by the zone radius, the bolt by the strike height.")]
        [Min(0.01f)] [SerializeField] private float modelScale = 1f;

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

        [Header("Orb (projectile)")]
        [Tooltip("Orb delivery: the orb spawned per shot. Built by MagicOrbPrefabBuilder " +
                 "(Tools ▸ Pickleball ▸ Setup Magic). Missing = an Orb spell cannot fire.")]
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

        [Header("Blast (Orb burst, Strike splash)")]
        [Tooltip("Damage dealt to whatever the orb touches directly, or to the enemy a " +
                 "strike lands on. It is not hit again by the blast.")]
        [Min(0f)] [SerializeField] private float directDamage = 14f;

        [Tooltip("Burst (or strike splash) radius, in metres. Everything else damageable " +
                 "inside it takes blast damage; the burst shell grows to exactly this size.")]
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

        [Tooltip("Layers the orb collides with and the blast reaches (also what a strike's " +
                 "splash and chain, and a zone's slow, can reach, and what counts as ground). " +
                 "Triggers are always ignored.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Burn (Orb: damage over time on what the burst hits)")]
        [Tooltip("The burn spawned on each victim (gameplay + flames). Built by Setup Magic. " +
                 "Empty, or 0 duration below = no burn.")]
        [SerializeField] private BurnStatus burnPrefab;

        [Tooltip("Damage per burn tick.")]
        [Min(0f)] [SerializeField] private float burnDamagePerTick;

        [Tooltip("Seconds between burn ticks. The first tick lands this long after the hit.")]
        [Min(0.05f)] [SerializeField] private float burnTickInterval = 0.5f;

        [Tooltip("Seconds a burn lasts. Ticks = duration / interval. Re-igniting a burning " +
                 "target refreshes it (longest duration, highest damage); it never stacks.")]
        [Min(0f)] [SerializeField] private float burnDuration;

        [Tooltip("On: everything the blast damages burns too. Off: only the direct hit burns.")]
        [SerializeField] private bool burnBlastVictims = true;

        [Header("Strike (called down after a telegraph)")]
        [Tooltip("The strike spawned per cast (gameplay + telegraph and bolt visuals). Built by " +
                 "Setup Magic. Missing = a Strike spell cannot fire.")]
        [SerializeField] private LightningStrike strikePrefab;

        [Tooltip("Seconds between the cast and the bolt landing, while the telegraph shows " +
                 "where it will land. ~0.3 reads as a wind-up without feeling like lag; much " +
                 "longer lets targets walk out.")]
        [Min(0f)] [SerializeField] private float strikeDelay = 0.35f;

        [Tooltip("On: a strike cast with the crosshair on an enemy follows that enemy during " +
                 "the telegraph and hits it wherever it went. Off: it lands where it was aimed.")]
        [SerializeField] private bool strikeTracksTarget = true;

        [Tooltip("Seconds the strike staggers the enemy it hits directly, if that enemy can " +
                 "be staggered (IStaggerable). 0 = no stagger.")]
        [Min(0f)] [SerializeField] private float staggerSeconds;

        [Tooltip("How many extra enemies the bolt arcs to after the hit. 0 = no chain.")]
        [Range(0, 4)] [SerializeField] private int chainCount;

        [Tooltip("Metres from the strike point a chain can jump.")]
        [Min(0f)] [SerializeField] private float chainRadius = 5f;

        [Tooltip("Damage each chained enemy takes.")]
        [Min(0f)] [SerializeField] private float chainDamage;

        [Header("Zone (placed on the ground)")]
        [Tooltip("The zone spawned per cast (gameplay + frost visuals). Built by Setup Magic. " +
                 "Missing = a Zone spell cannot fire.")]
        [SerializeField] private FreezeZone zonePrefab;

        [Tooltip("Zone radius, in metres. The frost disc drawn is exactly this size.")]
        [Min(0.1f)] [SerializeField] private float zoneRadius = 3f;

        [Tooltip("How high above the ground the zone reaches, in metres. A target standing on " +
                 "a ledge above this is outside it.")]
        [Min(0.1f)] [SerializeField] private float zoneHeight = 2.5f;

        [Tooltip("Seconds the zone lasts.")]
        [Min(0.1f)] [SerializeField] private float zoneDuration = 4f;

        [Tooltip("Speed of a target inside the zone, as a fraction of normal (0-1). 0.3 is " +
                 "heavily slowed; 0 is a full freeze.")]
        [Range(0f, 1f)] [SerializeField] private float slowMultiplier = 0.3f;

        [Tooltip("Seconds a target stays slowed after leaving the zone (or the zone ending).")]
        [Min(0.05f)] [SerializeField] private float slowLinger = 0.35f;

        [Tooltip("Metres a Zone or Strike looks straight down from the aim point for the floor " +
                 "(a crosshair on a wall or a chest lands on the ground beneath). Nothing " +
                 "found = it lands at the aim point itself.")]
        [Min(0f)] [SerializeField] private float groundProbeDistance = 12f;

        [Header("Channel (hold still to cast)")]
        [Tooltip("Seconds fire must be held, standing still, to complete the channel.")]
        [Min(0.05f)] [SerializeField] private float channelSeconds = 2f;

        [Tooltip("When the mana is paid. OnCompletion: a cancelled channel costs nothing (the " +
                 "cost must still be affordable to start). OnStart: paid up front, lost if cancelled.")]
        [SerializeField] private ChannelManaTiming channelManaTiming = ChannelManaTiming.OnCompletion;

        [Tooltip("Health restored on completion, in points (through Health.Heal).")]
        [Min(0f)] [SerializeField] private float healAmount = 30f;

        [Tooltip("Raw stick deflection (0-1) still counted as standing still, so pad drift " +
                 "never cancels the channel. Keyboard movement is always past it.")]
        [Range(0f, 0.9f)] [SerializeField] private float channelMoveDeadzone = 0.2f;

        [Tooltip("On: a channel cannot start at full health (no mana is risked for nothing).")]
        [SerializeField] private bool refuseAtFullHealth = true;

        /// <summary>Name for the HUD.</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>How the spell is delivered.</summary>
        public SpellDelivery Delivery => delivery;

        /// <summary>HUD tint.</summary>
        public Color ThemeColour => themeColour;

        /// <summary>HUD icon; may be null.</summary>
        public Sprite Icon => icon;

        /// <summary>True for spells aimed with the crosshair (every delivery but
        /// <see cref="SpellDelivery.Channel"/>).</summary>
        public bool UsesCrosshair => delivery != SpellDelivery.Channel;

        /// <summary>Optional model replacing the procedural centrepiece; may be null.</summary>
        public GameObject ModelPrefab => modelPrefab;

        /// <summary>Optional second model (the Thunderbolt's cloud); may be null.</summary>
        public GameObject SecondaryModelPrefab => secondaryModelPrefab;

        /// <summary>Scale applied to the models.</summary>
        public float ModelScale => modelScale;

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

        /// <summary>Burn prefab; null = no burn.</summary>
        public BurnStatus BurnPrefab => burnPrefab;

        /// <summary>Damage per burn tick.</summary>
        public float BurnDamagePerTick => burnDamagePerTick;

        /// <summary>Seconds between burn ticks.</summary>
        public float BurnTickInterval => burnTickInterval;

        /// <summary>Seconds a burn lasts; 0 = no burn.</summary>
        public float BurnDuration => burnDuration;

        /// <summary>True when blast victims burn too, not only the direct hit.</summary>
        public bool BurnBlastVictims => burnBlastVictims;

        /// <summary>True when this spell's hits ignite a burn.</summary>
        public bool Burns => burnPrefab != null && burnDuration > 0f && burnDamagePerTick > 0f;

        /// <summary>Strike prefab (Strike delivery); may be null.</summary>
        public LightningStrike StrikePrefab => strikePrefab;

        /// <summary>Seconds of telegraph before the strike lands.</summary>
        public float StrikeDelay => strikeDelay;

        /// <summary>True when a strike follows the enemy it was cast on.</summary>
        public bool StrikeTracksTarget => strikeTracksTarget;

        /// <summary>Stagger seconds on the direct strike target.</summary>
        public float StaggerSeconds => staggerSeconds;

        /// <summary>Extra enemies a strike chains to.</summary>
        public int ChainCount => chainCount;

        /// <summary>Metres a chain can jump.</summary>
        public float ChainRadius => chainRadius;

        /// <summary>Damage per chained enemy.</summary>
        public float ChainDamage => chainDamage;

        /// <summary>Zone prefab (Zone delivery); may be null.</summary>
        public FreezeZone ZonePrefab => zonePrefab;

        /// <summary>Zone radius, m.</summary>
        public float ZoneRadius => zoneRadius;

        /// <summary>Zone height above its ground point, m.</summary>
        public float ZoneHeight => zoneHeight;

        /// <summary>Zone lifetime, s.</summary>
        public float ZoneDuration => zoneDuration;

        /// <summary>Speed fraction inside the zone, 0-1.</summary>
        public float SlowMultiplier => slowMultiplier;

        /// <summary>Seconds a slow lingers after leaving.</summary>
        public float SlowLinger => slowLinger;

        /// <summary>Metres to look down for the floor under an aim point.</summary>
        public float GroundProbeDistance => groundProbeDistance;

        /// <summary>Seconds to hold a channel.</summary>
        public float ChannelSeconds => channelSeconds;

        /// <summary>When a channel pays its mana.</summary>
        public ChannelManaTiming ChannelManaTiming => channelManaTiming;

        /// <summary>Health restored by a completed channel.</summary>
        public float HealAmount => healAmount;

        /// <summary>Raw stick magnitude still counted as standing still.</summary>
        public float ChannelMoveDeadzone => channelMoveDeadzone;

        /// <summary>True when a channel refuses to start at full health.</summary>
        public bool RefuseAtFullHealth => refuseAtFullHealth;
    }
}
