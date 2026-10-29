using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Every number in the Hypatia fight, in one asset. The brain, the attacks,
    /// the burn and the presentation all read it; none of them keeps a tunable of
    /// its own, so the fight is tuned in one Inspector and Setup Hypatia Fight
    /// never resets it (it creates the asset once and then only assigns it).
    ///
    /// All values are starting points from the design, not play-tested.
    /// Distances are metres from the arena centre on the plate unless a tooltip
    /// says otherwise; heights are metres above the plate.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Hypatia Config", fileName = "HypatiaConfig")]
    public class HypatiaConfig : ScriptableObject
    {
        // ─── Fight ───────────────────────────────────────────────────────────
        [Header("Fight")]
        [Tooltip("Hypatia's health, HP. Setup Hypatia Fight copies it onto her Health. " +
                 "Higher makes the fight longer at the same damage.")]
        [SerializeField, Min(1f)] private float maxHealth = 700f;

        [Tooltip("Health fraction (0-1) at or below which Alexandria's Burning starts. " +
                 "One-way: healing above it never ends phase 2.")]
        [SerializeField, Range(0f, 1f)] private float phaseTwoAt = 0.5f;

        [Tooltip("Seconds between unprompted barks. Lower = chattier.")]
        [SerializeField, Min(1f)] private float unpromptedBarkInterval = 24f;

        // ─── Body ────────────────────────────────────────────────────────────
        [Header("Body")]
        [Tooltip("Height of her feet above the plate while hovering, m. Above about " +
                 "2.5 m only jumping attacks reach her outside the recalibration window.")]
        [SerializeField, Min(0f)] private float hoverHeight = 2.6f;

        [Tooltip("Height of her feet while recalibrating, m. Low enough that ground " +
                 "combos land.")]
        [SerializeField, Min(0f)] private float recalibrationHoverHeight = 0.4f;

        [Tooltip("How quickly she moves toward her target hover height, 1/s " +
                 "(exponential). Higher = she drops into the window faster.")]
        [SerializeField, Min(0.1f)] private float hoverSettleRate = 4f;

        [Tooltip("Idle bob amplitude, m.")]
        [SerializeField, Min(0f)] private float hoverBobAmplitude = 0.15f;

        [Tooltip("Idle bob period, s.")]
        [SerializeField, Min(0.1f)] private float hoverBobPeriod = 3f;

        [Tooltip("How fast she turns to face the player, °/s.")]
        [SerializeField, Min(0f)] private float turnDegreesPerSecond = 120f;

        // ─── Arena ───────────────────────────────────────────────────────────
        [Header("Arena (the Rete)")]
        [Tooltip("Outer radius of the walkable plate, m. Rings die here.")]
        [SerializeField, Min(1f)] private float arenaRadius = 13f;

        [Tooltip("Outer radius of each concentric floor band, m, innermost first. " +
                 "Band 0 is the hub and never burns; the last entry should equal " +
                 "Arena Radius. Four bands by design.")]
        [SerializeField] private float[] bandOuterRadii = { 6f, 8.33f, 10.67f, 13f };

        // ─── Celestial rings ────────────────────────────────────────────────
        [Header("Celestial Rings")]
        [Tooltip("Seconds into the fight before the first ring is forecast.")]
        [SerializeField, Min(0f)] private float firstRingDelay = 2f;

        [Tooltip("Seconds between rings in phase 1. Lower = denser spiral.")]
        [SerializeField, Min(0.2f)] private float ringIntervalPhaseOne = 3.2f;

        [Tooltip("Seconds between rings in phase 2.")]
        [SerializeField, Min(0.2f)] private float ringIntervalPhaseTwo = 2.6f;

        [Tooltip("Outward speed of a ring, m/s. At 4 m/s a ring takes ~3 s to " +
                 "cross the plate.")]
        [SerializeField, Min(0.1f)] private float ringSpeed = 4f;

        [Tooltip("Radius a ring is born at, m (just outside her).")]
        [SerializeField, Min(0f)] private float ringStartRadius = 1.5f;

        [Tooltip("Radial thickness of a ring's hit band, m.")]
        [SerializeField, Min(0.05f)] private float ringThickness = 0.5f;

        [Tooltip("Top of a low ring, m. A held single jump (2 m) clears it; a " +
                 "0.69 m tap jump does not.")]
        [SerializeField, Min(0.1f)] private float lowRingHeight = 0.8f;

        [Tooltip("Drawn height of a tall ring, m. Visual only: a tall ring hits at " +
                 "ANY height, because chained jumps plus feather and dash reach ~9 m " +
                 "and the Gravity Anchor higher still. Only the gap gets through.")]
        [SerializeField, Min(0.1f)] private float tallRingVisualHeight = 7f;

        [Tooltip("Every Nth ring is tall (1 = all tall, 0 = never).")]
        [SerializeField, Min(0)] private int tallRingEvery = 3;

        [Tooltip("Gap centres snap to multiples of this, °. 45 matches the Angle Dash.")]
        [SerializeField, Range(1f, 180f)] private float gapSnapDegrees = 45f;

        [Tooltip("Angular width of a ring's gap, °. 30° is ~6.8 m at the rim and " +
                 "~1 m at 2 m out.")]
        [SerializeField, Range(1f, 180f)] private float gapWidthDegrees = 30f;

        [Tooltip("Each ring's gap is rotated this far from the previous one, °, " +
                 "so the safe route is a spiral. Snapped to Gap Snap Degrees.")]
        [SerializeField] private float gapStepDegrees = 45f;

        [Tooltip("Seconds the floor shows a ring's gap before that ring launches.")]
        [SerializeField, Min(0f)] private float gapForecastLead = 1f;

        [Tooltip("Damage of a low ring, HP.")]
        [SerializeField, Min(0f)] private float lowRingDamage = 12f;

        [Tooltip("Damage of a tall ring, HP.")]
        [SerializeField, Min(0f)] private float tallRingDamage = 15f;

        [Tooltip("Damage of a burning ring (phase 2), HP. Burning rings act tall.")]
        [SerializeField, Min(0f)] private float fireRingDamage = 18f;

        [Tooltip("Most rings alive at once. A forecast past the cap is skipped.")]
        [SerializeField, Min(1)] private int maxLiveRings = 6;

        [Tooltip("Keep launching rings during the recalibration window, so the " +
                 "player jumps them mid-combo. Off pauses the ring timer.")]
        [SerializeField] private bool ringsDuringRecalibration = true;

        // ─── Conic sections ─────────────────────────────────────────────────
        [Header("Conic Sections")]
        [Tooltip("Seconds into the fight before the first conic telegraph.")]
        [SerializeField, Min(0f)] private float firstConicDelay = 4f;

        [Tooltip("Seconds from the end of one recalibration window to the next " +
                 "conic telegraph, phase 1.")]
        [SerializeField, Min(0.5f)] private float conicIntervalPhaseOne = 6f;

        [Tooltip("Same, phase 2.")]
        [SerializeField, Min(0.5f)] private float conicIntervalPhaseTwo = 4.5f;

        [Tooltip("Seconds the astrolabe needle shows e before the conic fires.")]
        [SerializeField, Min(0f)] private float needleLead = 1.2f;

        [Tooltip("Relative chance of a parabola. 0 removes it.")]
        [SerializeField, Min(0f)] private float parabolaWeight = 1f;

        [Tooltip("Relative chance of an ellipse volley. 0 removes it.")]
        [SerializeField, Min(0f)] private float ellipseWeight = 1f;

        [Tooltip("Relative chance of a hyperbola. 0 removes it.")]
        [SerializeField, Min(0f)] private float hyperbolaWeight = 1f;

        [Tooltip("Never pick the same conic twice in a row (when another has weight).")]
        [SerializeField] private bool noConicRepeat = true;

        [Header("Conic: Parabola (lob)")]
        [Tooltip("Lob flight time, s. The landing marker shows for the whole flight, " +
                 "so keep it at or above Lob Marker Lead.")]
        [SerializeField, Min(0.2f)] private float lobFlightTime = 1.4f;

        [Tooltip("Minimum seconds the landing marker shows before impact.")]
        [SerializeField, Min(0f)] private float lobMarkerLead = 1f;

        [Tooltip("Gravity on the lob, m/s². Higher = a taller, snappier arc for the " +
                 "same flight time.")]
        [SerializeField, Min(0.1f)] private float lobGravity = 14f;

        [Tooltip("Fraction (0-1) of the predicted lead used. 1 aims exactly where the " +
                 "player will be at constant velocity; lower is more forgiving.")]
        [SerializeField, Range(0f, 1f)] private float lobLeadFraction = 1f;

        [Tooltip("Cap on how far ahead of the player the lob aims, m.")]
        [SerializeField, Min(0f)] private float lobMaxLead = 6f;

        [Tooltip("Lobs per parabola volley, phase 1.")]
        [SerializeField, Min(1)] private int lobCountPhaseOne = 1;

        [Tooltip("Lobs per parabola volley, phase 2.")]
        [SerializeField, Min(1)] private int lobCountPhaseTwo = 2;

        [Tooltip("Seconds between lobs of one volley.")]
        [SerializeField, Min(0f)] private float lobStagger = 0.4f;

        [Tooltip("Blast radius on landing, m.")]
        [SerializeField, Min(0.1f)] private float lobBlastRadius = 1.6f;

        [Tooltip("Damage of a lob blast, HP.")]
        [SerializeField, Min(0f)] private float lobDamage = 16f;

        [Header("Conic: Ellipse (orbiters)")]
        [Tooltip("Fewest orbiters per ellipse volley.")]
        [SerializeField, Min(1)] private int orbiterCountMin = 3;

        [Tooltip("Most orbiters per ellipse volley.")]
        [SerializeField, Min(1)] private int orbiterCountMax = 5;

        [Tooltip("Most orbiters alive at once; the oldest retire first.")]
        [SerializeField, Min(1)] private int maxLiveOrbiters = 5;

        [Tooltip("Lowest orbit eccentricity (0 = circle).")]
        [SerializeField, Range(0f, 0.95f)] private float ellipseEccentricityMin = 0.35f;

        [Tooltip("Highest orbit eccentricity. Higher = a sharper whip at periapsis.")]
        [SerializeField, Range(0f, 0.95f)] private float ellipseEccentricityMax = 0.75f;

        [Tooltip("Closest an orbit may pass to her, m.")]
        [SerializeField, Min(0.5f)] private float orbitPeriapsisMin = 2.5f;

        [Tooltip("Furthest an orbit may reach from her, m. Keep below Arena Radius.")]
        [SerializeField, Min(1f)] private float orbitApoapsisMax = 11.5f;

        [Tooltip("Shortest orbital period, s.")]
        [SerializeField, Min(0.5f)] private float orbitPeriodMin = 6f;

        [Tooltip("Longest orbital period, s.")]
        [SerializeField, Min(0.5f)] private float orbitPeriodMax = 9f;

        [Tooltip("Seconds an orbiter lives.")]
        [SerializeField, Min(0.5f)] private float orbiterLifetime = 12f;

        [Tooltip("Seconds between orbiters of one volley appearing.")]
        [SerializeField, Min(0f)] private float orbiterSpawnStagger = 0.25f;

        [Tooltip("Height of the orbit plane above the plate, m.")]
        [SerializeField, Min(0f)] private float orbitHeight = 1f;

        [Tooltip("Orbiter hit radius, m.")]
        [SerializeField, Min(0.05f)] private float orbiterRadius = 0.45f;

        [Tooltip("Damage of an orbiter contact, HP.")]
        [SerializeField, Min(0f)] private float orbiterDamage = 10f;

        [Tooltip("Seconds one orbiter waits before it can hit the player again.")]
        [SerializeField, Min(0f)] private float orbiterRehitDelay = 1f;

        [Header("Conic: Hyperbola (flyby shot)")]
        [Tooltip("Lowest hyperbola eccentricity (>1). Near 1 = a hairpin turn.")]
        [SerializeField, Min(1.01f)] private float hyperbolaEccentricityMin = 1.3f;

        [Tooltip("Highest hyperbola eccentricity. Higher = a straighter flyby.")]
        [SerializeField, Min(1.01f)] private float hyperbolaEccentricityMax = 2f;

        [Tooltip("Closest pass to her, m.")]
        [SerializeField, Min(0.5f)] private float hyperbolaPeriapsis = 2.5f;

        [Tooltip("Seconds the asymptotes show on the floor before the shot fires.")]
        [SerializeField, Min(0f)] private float asymptoteLead = 0.8f;

        [Tooltip("Shot speed along the path, m/s.")]
        [SerializeField, Min(0.1f)] private float hyperbolaShotSpeed = 16f;

        [Tooltip("Distance from her where the shot enters and leaves, m.")]
        [SerializeField, Min(1f)] private float hyperbolaReach = 16f;

        [Tooltip("Aim the shot's outgoing path through the player's position at fire " +
                 "time (periapsis on them if they stand inside it). Off = a random bearing.")]
        [SerializeField] private bool hyperbolaExitAtPlayer = true;

        [Tooltip("Shot height above the plate, m.")]
        [SerializeField, Min(0f)] private float hyperbolaHeight = 1f;

        [Tooltip("Shot hit radius, m.")]
        [SerializeField, Min(0.05f)] private float hyperbolaShotRadius = 0.5f;

        [Tooltip("Damage of the shot, HP.")]
        [SerializeField, Min(0f)] private float hyperbolaDamage = 18f;

        // ─── Recalibration ──────────────────────────────────────────────────
        [Header("Recalibration window")]
        [Tooltip("Seconds she is vulnerable after each conic volley finishes.")]
        [SerializeField, Min(0f)] private float recalibrationDuration = 2.5f;

        [Tooltip("Damage multiplier on her during the window. 2 = double damage.")]
        [SerializeField, Min(0f)] private float recalibrationDamageMultiplier = 2f;

        [Tooltip("Damage multiplier on her outside the window. 1 = listed damage.")]
        [SerializeField, Min(0f)] private float normalDamageMultiplier = 1f;

        // ─── Alexandria's Burning ───────────────────────────────────────────
        [Header("Alexandria's Burning (phase 2)")]
        [Tooltip("Seconds into phase 2 before the first band starts its warning.")]
        [SerializeField, Min(0f)] private float firstBurnDelay = 3f;

        [Tooltip("Seconds between band ignitions.")]
        [SerializeField, Min(1f)] private float burnInterval = 15f;

        [Tooltip("Seconds a band smoulders (warning) before it ignites.")]
        [SerializeField, Min(0f)] private float burnWarningLead = 2f;

        [Tooltip("Never burn inside this radius, m. Bands whose inner edge is below " +
                 "it stay safe.")]
        [SerializeField, Min(0f)] private float minSafeRadius = 6f;

        [Tooltip("Fire damage while standing in a burned band, HP/s. Recoverable " +
                 "(grey), so attacking her wins it back.")]
        [SerializeField, Min(0f)] private float fireDamagePerSecond = 8f;

        [Tooltip("Fire reaches this high, m. Above it (mid-jump) the player is not " +
                 "burning.")]
        [SerializeField, Min(0f)] private float fireHeight = 1.2f;

        [Header("Burning scrolls (optional)")]
        [Tooltip("Burning scrolls drift in burned bands. Off by default.")]
        [SerializeField] private bool scrollsEnabled;

        [Tooltip("Seconds between scroll spawns while a band is burned.")]
        [SerializeField, Min(0.5f)] private float scrollInterval = 4f;

        [Tooltip("Most scrolls alive at once.")]
        [SerializeField, Min(1)] private int maxLiveScrolls = 4;

        [Tooltip("Scroll drift speed, m/s.")]
        [SerializeField, Min(0f)] private float scrollDriftSpeed = 0.8f;

        [Tooltip("Seconds a scroll lasts before it burns away.")]
        [SerializeField, Min(0.5f)] private float scrollLifetime = 14f;

        [Tooltip("Scroll height above the plate, m.")]
        [SerializeField, Min(0f)] private float scrollHeight = 0.6f;

        [Tooltip("Pickup radius, m.")]
        [SerializeField, Min(0.05f)] private float scrollPickupRadius = 0.8f;

        [Tooltip("Health a scroll costs to collect, HP (not recoverable).")]
        [SerializeField, Min(0f)] private float scrollHealthCost = 6f;

        // ─── Accessors ──────────────────────────────────────────────────────
        public float MaxHealth => maxHealth;
        public float PhaseTwoAt => phaseTwoAt;
        public float UnpromptedBarkInterval => unpromptedBarkInterval;

        public float HoverHeight => hoverHeight;
        public float RecalibrationHoverHeight => recalibrationHoverHeight;
        public float HoverSettleRate => hoverSettleRate;
        public float HoverBobAmplitude => hoverBobAmplitude;
        public float HoverBobPeriod => hoverBobPeriod;
        public float TurnDegreesPerSecond => turnDegreesPerSecond;

        public float ArenaRadius => arenaRadius;
        /// <summary>Outer radius of each band, innermost first. Do not modify.</summary>
        public float[] BandOuterRadii => bandOuterRadii;

        public float FirstRingDelay => firstRingDelay;
        public float RingInterval(bool phaseTwo) => phaseTwo ? ringIntervalPhaseTwo : ringIntervalPhaseOne;
        public float RingSpeed => ringSpeed;
        public float RingStartRadius => ringStartRadius;
        public float RingThickness => ringThickness;
        public float LowRingHeight => lowRingHeight;
        public float TallRingVisualHeight => tallRingVisualHeight;
        public int TallRingEvery => tallRingEvery;
        public float GapSnapDegrees => gapSnapDegrees;
        public float GapWidthDegrees => gapWidthDegrees;
        public float GapStepDegrees => gapStepDegrees;
        public float GapForecastLead => gapForecastLead;
        public float LowRingDamage => lowRingDamage;
        public float TallRingDamage => tallRingDamage;
        public float FireRingDamage => fireRingDamage;
        public int MaxLiveRings => maxLiveRings;
        public bool RingsDuringRecalibration => ringsDuringRecalibration;

        public float FirstConicDelay => firstConicDelay;
        public float ConicInterval(bool phaseTwo) => phaseTwo ? conicIntervalPhaseTwo : conicIntervalPhaseOne;
        public float NeedleLead => needleLead;
        public float ParabolaWeight => parabolaWeight;
        public float EllipseWeight => ellipseWeight;
        public float HyperbolaWeight => hyperbolaWeight;
        public bool NoConicRepeat => noConicRepeat;

        public float LobFlightTime => lobFlightTime;
        public float LobMarkerLead => lobMarkerLead;
        public float LobGravity => lobGravity;
        public float LobLeadFraction => lobLeadFraction;
        public float LobMaxLead => lobMaxLead;
        public int LobCount(bool phaseTwo) => phaseTwo ? lobCountPhaseTwo : lobCountPhaseOne;
        public float LobStagger => lobStagger;
        public float LobBlastRadius => lobBlastRadius;
        public float LobDamage => lobDamage;

        public int OrbiterCountMin => orbiterCountMin;
        public int OrbiterCountMax => Mathf.Max(orbiterCountMin, orbiterCountMax);
        public int MaxLiveOrbiters => maxLiveOrbiters;
        public float EllipseEccentricityMin => ellipseEccentricityMin;
        public float EllipseEccentricityMax => Mathf.Max(ellipseEccentricityMin, ellipseEccentricityMax);
        public float OrbitPeriapsisMin => orbitPeriapsisMin;
        public float OrbitApoapsisMax => orbitApoapsisMax;
        public float OrbitPeriodMin => orbitPeriodMin;
        public float OrbitPeriodMax => Mathf.Max(orbitPeriodMin, orbitPeriodMax);
        public float OrbiterLifetime => orbiterLifetime;
        public float OrbiterSpawnStagger => orbiterSpawnStagger;
        public float OrbitHeight => orbitHeight;
        public float OrbiterRadius => orbiterRadius;
        public float OrbiterDamage => orbiterDamage;
        public float OrbiterRehitDelay => orbiterRehitDelay;

        public float HyperbolaEccentricityMin => hyperbolaEccentricityMin;
        public float HyperbolaEccentricityMax => Mathf.Max(hyperbolaEccentricityMin, hyperbolaEccentricityMax);
        public float HyperbolaPeriapsis => hyperbolaPeriapsis;
        public float AsymptoteLead => asymptoteLead;
        public float HyperbolaShotSpeed => hyperbolaShotSpeed;
        public float HyperbolaReach => hyperbolaReach;
        public bool HyperbolaExitAtPlayer => hyperbolaExitAtPlayer;
        public float HyperbolaHeight => hyperbolaHeight;
        public float HyperbolaShotRadius => hyperbolaShotRadius;
        public float HyperbolaDamage => hyperbolaDamage;

        public float RecalibrationDuration => recalibrationDuration;
        public float RecalibrationDamageMultiplier => recalibrationDamageMultiplier;
        public float NormalDamageMultiplier => normalDamageMultiplier;

        public float FirstBurnDelay => firstBurnDelay;
        public float BurnInterval => burnInterval;
        public float BurnWarningLead => burnWarningLead;
        public float MinSafeRadius => minSafeRadius;
        public float FireDamagePerSecond => fireDamagePerSecond;
        public float FireHeight => fireHeight;

        public bool ScrollsEnabled => scrollsEnabled;
        public float ScrollInterval => scrollInterval;
        public int MaxLiveScrolls => maxLiveScrolls;
        public float ScrollDriftSpeed => scrollDriftSpeed;
        public float ScrollLifetime => scrollLifetime;
        public float ScrollHeight => scrollHeight;
        public float ScrollPickupRadius => scrollPickupRadius;
        public float ScrollHealthCost => scrollHealthCost;
    }
}
