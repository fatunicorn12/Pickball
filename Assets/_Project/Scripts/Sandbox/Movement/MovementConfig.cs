using Pickleball.Sandbox.Movement.Logic;
using UnityEngine;

namespace Pickleball.Sandbox.Movement
{
    /// <summary>
    /// Every gameplay tunable of the player's movement, in one asset.
    ///
    /// Tuned live in Play mode: the motor reads this asset every frame, so a
    /// change in the inspector is felt on the next step. Presentation-only values
    /// (stride matching, clip timing) live on <c>MovementAnimationConfig</c>.
    ///
    /// <b>Where the defaults come from.</b> Super Mario Sunshine's own numbers
    /// (doldecomp <c>sms</c> + the shipped <c>params.szs</c>), scaled so Mario's
    /// 160 units = our 1.7 m, then two deliberate departures, both requested:
    /// time runs ~1.46× slower than Sunshine (floatier, more hang), and heights
    /// are ~0.8× Sunshine's so the scanned human-scale world still reads.
    /// <c>Docs/Systems/Movement.md</c> has the Sunshine value next to every
    /// default.
    ///
    /// Jumps are authored as apex heights in metres and converted to launch
    /// speeds by <see cref="JumpMath"/>, so changing a gravity never silently
    /// changes how high a jump goes.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Movement Config", fileName = "MovementConfig")]
    public class MovementConfig : ScriptableObject
    {
        // ------------------------------------------------------------- ground
        [Header("Ground — run")]
        [Tooltip("Top speed at full stick before the sprint kicks in, m/s.")]
        [SerializeField, Min(0.1f)] private float runSpeed = 4.2f;
        [Tooltip("Starting from rest, he jumps straight to this speed, m/s (Sunshine " +
                 "kicks Mario to 8 units/frame). What makes the first step feel instant.")]
        [SerializeField, Min(0f)] private float runStartSpeed = 1.5f;
        [Tooltip("Acceleration up to run speed, m/s². Higher = snappier starts.")]
        [SerializeField, Min(0.1f)] private float runAcceleration = 18f;
        [Tooltip("Slow-down with the stick released below the run-stop speed, m/s².")]
        [SerializeField, Min(0.1f)] private float runDeceleration = 22f;
        [Tooltip("Stick deflection (0–1) that counts as full speed. Sunshine reaches top " +
                 "speed at ~62% tilt.")]
        [SerializeField, Range(0.1f, 1f)] private float stickFullTilt = 0.75f;
        [Tooltip("Stick response curve exponent: speed = top × (tilt / full)^this. " +
                 "Sunshine's is quadratic (2); 1 = linear.")]
        [SerializeField, Range(1f, 3f)] private float stickExponent = 1.6f;
        [Tooltip("Smoothing on turning toward the stick, s. Lower = twitchier.")]
        [SerializeField, Min(0.001f)] private float turnSmoothTime = 0.07f;
        [Tooltip("Fastest the body can turn at run speed, °/s (Sunshine: ~680 at our " +
                 "time scale, the same at every speed).")]
        [SerializeField, Min(10f)] private float runTurnRate = 900f;

        [Header("Ground — sprint (automatic)")]
        [Tooltip("Sprint top speed, m/s (Sunshine's top run, at our time scale: 7.0).")]
        [SerializeField, Min(0.1f)] private float sprintSpeed = 7f;
        [Tooltip("Seconds of continuous running before the sprint starts. No button: he " +
                 "just picks up pace.")]
        [SerializeField, Min(0f)] private float sprintDelay = 1f;
        [Tooltip("Seconds to ramp from run speed to sprint speed once the delay is up.")]
        [SerializeField, Min(0f)] private float sprintRampTime = 0.4f;
        [Tooltip("A gap in running shorter than this (s) keeps the sprint — a stick " +
                 "flicked through neutral, a skid turn, a hop.")]
        [SerializeField, Min(0f)] private float sprintGrace = 0.2f;
        [Tooltip("Stick deflection (0–1) that counts as running toward a sprint.")]
        [SerializeField, Range(0f, 1f)] private float sprintStickThreshold = 0.7f;
        [Tooltip("Acceleration from run to sprint speed while ramping, m/s².")]
        [SerializeField, Min(0.1f)] private float sprintAcceleration = 12f;
        [Tooltip("Fastest the body can turn at sprint speed, °/s. Lower makes wider " +
                 "sprint turns and leaves reversals to the skid.")]
        [SerializeField, Min(10f)] private float sprintTurnRate = 680f;
        [Tooltip("Holding the sprint button (Shift / LT) skips the build-up.")]
        [SerializeField] private bool sprintButtonSkipsDelay = true;

        [Header("Run to stop")]
        [Tooltip("Releasing the stick above this speed (m/s) plays the planted run-to-stop.")]
        [SerializeField, Min(0f)] private float stopTriggerSpeed = 3f;
        [Tooltip("Deceleration during the run-to-stop, m/s² (Sunshine: ~18 at our time " +
                 "scale; Run To Stop.fbx alone would want ~5).")]
        [SerializeField, Min(0.1f)] private float stopDeceleration = 12f;
        [Tooltip("Stick must stay neutral this long (s) before the stop commits, so a " +
                 "reversal flicked through the centre reaches the skid instead.")]
        [SerializeField, Min(0f)] private float stopInputDelay = 0.05f;

        // -------------------------------------------------------------- skid
        [Header("Skid turn (snap-back redirect)")]
        [Tooltip("Slowest speed at which a reversal skids rather than steers, m/s " +
                 "(Sunshine: 2.2 at our scale).")]
        [SerializeField, Min(0f)] private float skidMinSpeed = 2.6f;
        [Tooltip("How far (°) the stick must point from the travel direction to skid " +
                 "(Sunshine: 100; wider here so diagonals steer instead).")]
        [SerializeField, Range(90f, 180f)] private float skidMinAngle = 120f;
        [Tooltip("Stick deflection (0–1) needed to skid.")]
        [SerializeField, Range(0f, 1f)] private float skidMinStick = 0.5f;
        [Tooltip("Length of the whole redirect, s. Sunshine's brake + turn-end is ~0.4 s " +
                 "and throws your speed away; this keeps it. 0 = an instant flip.")]
        [SerializeField, Min(0f)] private float skidDuration = 0.14f;
        [Tooltip("Fraction of the skid (0–1) spent planted, braking along the old " +
                 "direction while the body swings round, before pushing off.")]
        [SerializeField, Range(0f, 1f)] private float skidPlantFraction = 0.4f;
        [Tooltip("Exit speed as a fraction of entry speed (0–1.2). 1 = every bit of " +
                 "momentum kept (Sunshine: ~0.25).")]
        [SerializeField, Range(0f, 1.2f)] private float skidSpeedKeep = 0.92f;
        [Tooltip("How fast the plant still slides on along the old direction, as a " +
                 "fraction of entry speed (0–1). 0 = stops dead.")]
        [SerializeField, Range(0f, 1f)] private float skidSlide = 0.3f;
        [Tooltip("Apex of the side somersault (Jump during a skid), m (Sunshine: 6.5).")]
        [SerializeField, Min(0f)] private float sideFlipHeight = 4.2f;
        [Tooltip("Horizontal speed of the side somersault, m/s — mostly straight up.")]
        [SerializeField, Min(0f)] private float sideFlipSpeed = 2.5f;

        // -------------------------------------------------------------- jumps
        [Header("Jump chain (single → double → triple)")]
        [Tooltip("Apex of the first jump from a standstill, m (Sunshine: 2.4).")]
        [SerializeField, Min(0f)] private float singleJumpHeight = 2f;
        [Tooltip("Apex of the second jump of a chain from a standstill, m (Sunshine: 3.6).")]
        [SerializeField, Min(0f)] private float doubleJumpHeight = 2.8f;
        [Tooltip("Apex of the triple jump (a flip), m (Sunshine: 8.6 — about 5 body " +
                 "heights, which overshoots this world).")]
        [SerializeField, Min(0f)] private float tripleJumpHeight = 4.5f;
        [Tooltip("Extra apex at full sprint speed for single and double jumps, as a " +
                 "fraction of their standing height (Sunshine: +41% single, +35% double).")]
        [SerializeField, Range(0f, 1f)] private float runningJumpBonus = 0.38f;
        [Tooltip("Horizontal speed kept on a single or double jump's takeoff (0–1). " +
                 "Sunshine: 0.8. The triple keeps all of it.")]
        [SerializeField, Range(0f, 1f)] private float jumpTakeoffSpeedKeep = 0.8f;
        [Tooltip("Jump again within this long (s) of landing to chain the next jump " +
                 "(Sunshine: 0.13 s; 0.2 at our time scale).")]
        [SerializeField, Min(0f)] private float chainWindow = 0.2f;
        [Tooltip("Ground speed needed for the triple jump, m/s (Sunshine: 78% of top " +
                 "speed — i.e. you have to be sprinting).")]
        [SerializeField, Min(0f)] private float tripleMinSpeed = 5f;
        [Tooltip("Releasing Jump while rising faster than this cuts the upward speed to " +
                 "it, m/s (Sunshine's variable height). Lower = taps give shorter hops.")]
        [SerializeField, Min(0f)] private float jumpReleaseSpeed = 5f;
        [Tooltip("A release earlier than this (s) after takeoff is honoured only then, so " +
                 "a tap still gives a readable hop.")]
        [SerializeField, Min(0f)] private float minJumpHoldTime = 0.03f;
        [Tooltip("Grace after leaving a ledge during which Jump still works, s. (Sunshine " +
                 "has none; it keeps Mario grounded over 1 m drops instead.)")]
        [SerializeField, Min(0f)] private float coyoteTime = 0.12f;
        [Tooltip("A Jump pressed this long before landing still fires on landing, s.")]
        [SerializeField, Min(0f)] private float jumpBuffer = 0.12f;

        [Header("Gravity (positive magnitudes, m/s²)")]
        [Tooltip("Gravity while rising with Jump held, m/s² (Sunshine: 38 at true scale, " +
                 "18 at our time scale). Lower = floatier climb.")]
        [SerializeField, Min(0.1f)] private float riseGravity = 18f;
        [Tooltip("Gravity near the apex with Jump held (inside the apex band), m/s². " +
                 "Lower = more hang at the top. Not in Sunshine; asked for.")]
        [SerializeField, Min(0.1f)] private float apexGravity = 10f;
        [Tooltip("Gravity falling, or rising after Jump is released, m/s². Higher = " +
                 "crisper landings.")]
        [SerializeField, Min(0.1f)] private float fallGravity = 26f;
        [Tooltip("Half-width of the apex band in vertical speed, m/s. 0 = no hang.")]
        [SerializeField, Min(0f)] private float apexBand = 2f;
        [Tooltip("Terminal fall speed, m/s (Sunshine: 16.4 at our time scale). Also " +
                 "guards a missed ground check banking a huge fall while standing.")]
        [SerializeField, Min(0.1f)] private float maxFallSpeed = 18f;
        [Tooltip("Gravity for arcs that are not jumps (walking off a ledge, dash exit, " +
                 "tether release, dive), m/s².")]
        [SerializeField, Min(0.1f)] private float plainGravity = 22f;
        [Tooltip("Downward speed held while grounded, m/s (negative). Welds the capsule " +
                 "to slopes and steps.")]
        [SerializeField] private float groundedStick = -2f;

        [Header("Air control")]
        [Tooltip("Acceleration of the planar velocity toward the stick in the air, m/s². " +
                 "Pulling back slows him rather than turning him round (Sunshine: 4.6 " +
                 "forward/back plus a sideways strafe).")]
        [SerializeField, Min(0f)] private float airAcceleration = 6f;
        [Tooltip("Slow-down in the air with the stick released, m/s².")]
        [SerializeField, Min(0f)] private float airDrag = 1f;
        [Tooltip("Smoothing on the body turning to follow air travel, s.")]
        [SerializeField, Min(0.001f)] private float airTurnSmoothTime = 0.12f;

        // --------------------------------------------------------------- dive
        [Header("Dive")]
        [Tooltip("At or above this ground speed (m/s) the crouch button dives instead of " +
                 "crouching (Sunshine: 47% of top speed). In the air it always dives.")]
        [SerializeField, Min(0f)] private float diveMinSpeed = 3.2f;
        [Tooltip("Added to the current speed by a ground dive, m/s (Sunshine: 3.3).")]
        [SerializeField, Min(0f)] private float diveSpeedBoost = 3.3f;
        [Tooltip("Fastest a dive can be, m/s (Sunshine: 10.5).")]
        [SerializeField, Min(0f)] private float diveMaxSpeed = 10f;
        [Tooltip("Horizontal speed of a dive started in the air, m/s (Sunshine sets the " +
                 "maximum outright).")]
        [SerializeField, Min(0f)] private float diveAirSpeed = 9f;
        [Tooltip("Upward launch of a dive from the ground, m/s (Sunshine: 4.4). An air dive " +
                 "keeps the vertical speed it had.")]
        [SerializeField, Min(0f)] private float diveHopSpeed = 4.4f;
        [Tooltip("Steering while diving, °/s.")]
        [SerializeField, Min(0f)] private float diveTurnRate = 90f;
        [Tooltip("Speed bled off per second in a dive, m/s².")]
        [SerializeField, Min(0f)] private float diveDrag = 0.5f;

        [Header("Belly slide")]
        [Tooltip("Speed decay sliding with the stick neutral, 1/s (exponential: speed × " +
                 "e^(−this·t)). Sunshine: 2.5 at our time scale — ~1.4 s from a full dive.")]
        [SerializeField, Min(0f)] private float bellySlideDecay = 2.5f;
        [Tooltip("Same, holding the stick forward, 1/s (Sunshine: 0.8 — a long glide).")]
        [SerializeField, Min(0f)] private float bellySlideDecayForward = 0.8f;
        [Tooltip("Same, holding the stick back, 1/s (Sunshine: 4.2 — a brake).")]
        [SerializeField, Min(0f)] private float bellySlideDecayBack = 4.2f;
        [Tooltip("Steering while sliding, °/s.")]
        [SerializeField, Min(0f)] private float bellySlideTurnRate = 120f;
        [Tooltip("Below this speed (m/s) he stands up on his own.")]
        [SerializeField, Min(0f)] private float bellySlideStopSpeed = 0.3f;
        [Tooltip("Shortest slide before he may dive again or stand, s.")]
        [SerializeField, Min(0f)] private float bellySlideMinTime = 0.15f;
        [Tooltip("Capsule height while sliding, as a fraction of standing height.")]
        [SerializeField, Range(0.2f, 1f)] private float bellySlideHeightFraction = 0.45f;
        [Tooltip("Apex of the rollout (Jump out of a slide), m (Sunshine: 1.9 at speed).")]
        [SerializeField, Min(0f)] private float rolloutHeight = 1.3f;
        [Tooltip("Horizontal speed kept by the rollout (0–1.2).")]
        [SerializeField, Range(0f, 1.2f)] private float rolloutSpeedKeep = 1f;

        // --------------------------------------------------------------- spin
        [Header("Spin jump")]
        [Tooltip("Stick rotation (°, one direction) that arms the spin jump (Sunshine: " +
                 "all four quadrants, just over 180°–270°).")]
        [SerializeField, Range(90f, 720f)] private float spinRequiredDegrees = 270f;
        [Tooltip("…within this many seconds (Sunshine: 0.33 s; 0.5 at our time scale).")]
        [SerializeField, Range(0.1f, 1f)] private float spinWindow = 0.5f;
        [Tooltip("Stick deflection (0–1) below which rotation does not count.")]
        [SerializeField, Range(0f, 1f)] private float spinStickMin = 0.5f;
        [Tooltip("Apex of the spin jump from the ground, m (Sunshine: 6.6).")]
        [SerializeField, Min(0f)] private float spinJumpHeight = 4f;
        [Tooltip("Horizontal speed kept on a spin jump's takeoff (0–1).")]
        [SerializeField, Range(0f, 1f)] private float spinHorizontalKeep = 0.6f;
        [Tooltip("Gravity on the way down from a spin, m/s² (Sunshine: 35% of normal).")]
        [SerializeField, Min(0.1f)] private float spinFallGravity = 8f;
        [Tooltip("Fall speed cap during the spin's descent, m/s — the slow corkscrew down.")]
        [SerializeField, Min(0.1f)] private float spinFallSpeed = 5f;
        [Tooltip("Spinning the stick mid-jump turns the jump into the slow spin descent " +
                 "(no extra lift), as in Sunshine.")]
        [SerializeField] private bool spinInAir = true;

        // ---------------------------------------------- existing abilities
        [Header("Feather air jump")]
        [Tooltip("Apex of the feather's mid-air jump, m above where it is used.")]
        [SerializeField, Min(0f)] private float featherJumpHeight = 2f;

        [Header("Dash (protractor) — fixed 45°")]
        [Tooltip("Speed along the dash vector, m/s. 14 m/s over 0.22 s covers 3.08 m: " +
                 "2.18 across and 2.18 up — fixed, so gaps can be designed against it.")]
        [SerializeField, Min(0f)] private float dashSpeed = 14f;
        [Tooltip("Dash length, s.")]
        [SerializeField, Min(0.01f)] private float dashDuration = 0.22f;
        [Tooltip("Time after a dash ends before the next, s.")]
        [SerializeField, Min(0f)] private float dashCooldown = 0.6f;
        [Tooltip("Fraction of the dash's upward speed kept when it ends (0–1). Low = crisp " +
                 "stop at the apex, high = floats on.")]
        [SerializeField, Range(0f, 1f)] private float dashExitVerticalKeep = 0.35f;
        [Tooltip("Fraction of the dash's horizontal speed kept when it ends (0–1).")]
        [SerializeField, Range(0f, 1f)] private float dashExitHorizontalKeep = 0.7f;
        [Tooltip("How strongly gravity wells bend a dash, as a multiple of their pull on " +
                 "walking. 0 = the old immune dash.")]
        [SerializeField, Min(0f)] private float dashFieldBend = 2.5f;

        [Header("Gravity Anchor (Newton's reward)")]
        [Tooltip("Seconds one anchor lasts.")]
        [SerializeField, Min(0f)] private float anchorDuration = 2f;
        [Tooltip("Seconds after an anchor ends before the next.")]
        [SerializeField, Min(0f)] private float anchorCooldown = 1.5f;
        [Tooltip("Upward acceleration while anchored with Jump held, m/s².")]
        [SerializeField, Min(0f)] private float anchorRiseAcceleration = 9f;
        [Tooltip("Top rise speed while anchored, m/s.")]
        [SerializeField, Min(0f)] private float anchorMaxRiseSpeed = 4.5f;
        [Tooltip("How quickly vertical speed is brought to rest by the anchor, m/s².")]
        [SerializeField, Min(0f)] private float anchorHaltRate = 25f;
        [Tooltip("Fraction of a fall's speed kept the instant the anchor catches it (0–1).")]
        [SerializeField, Range(0f, 1f)] private float anchorCatchKeep = 0.25f;

        [Header("Carried momentum")]
        [Tooltip("How fast carried momentum (tether release, knockback) bleeds off in the " +
                 "air, m/s².")]
        [SerializeField, Min(0f)] private float momentumAirDrag = 1.6f;
        [Tooltip("Same, on the ground, m/s².")]
        [SerializeField, Min(0f)] private float momentumGroundDrag = 11f;

        [Header("Force fields")]
        [Tooltip("How quickly field velocity relaxes toward the speed the field asks for, " +
                 "1/s. A steady pull settles at (acceleration / this). PROVISIONAL — the " +
                 "Newton fight's tuning pass owns the final value.")]
        [SerializeField, Min(0f)] private float fieldDamping = 2.5f;
        [Tooltip("Backstop on field velocity, m/s. Only bites when wells stack.")]
        [SerializeField, Min(0f)] private float maxFieldSpeed = 18f;

        [Header("Tether (orbital)")]
        [Tooltip("Longest rope, m.")]
        [SerializeField, Min(0.1f)] private float tetherMaxLength = 14f;
        [Tooltip("Shortest rope, m. Short ropes orbit viciously fast.")]
        [SerializeField, Min(0.1f)] private float tetherMinLength = 4f;
        [Tooltip("Gravity while swinging, m/s² (positive). Gentler than jump gravity.")]
        [SerializeField, Min(0f)] private float swingGravity = 11f;
        [Tooltip("Push along the tangent from the stick, m/s² — the only way to gain speed.")]
        [SerializeField, Min(0f)] private float swingPumpAccel = 7f;
        [Tooltip("Fraction of swing speed bled per second, so it settles.")]
        [SerializeField, Min(0f)] private float swingDamping = 0.12f;
        [Tooltip("Ceiling on swing speed, m/s.")]
        [SerializeField, Min(0.1f)] private float maxSwingSpeed = 13f;
        [Tooltip("Most of your run-up carried into the swing on latching, m/s.")]
        [SerializeField, Min(0f)] private float entrySpeedCap = 4.5f;
        [Tooltip("Speed kept after a solid collision mid-swing (0–1).")]
        [SerializeField, Range(0f, 1f)] private float impactSpeedRetain = 0.25f;
        [Tooltip("Fraction of travel that must be lost before it counts as a hit (0–1).")]
        [SerializeField, Range(0f, 1f)] private float impactMinLoss = 0.25f;
        [Tooltip("Upward kick when jumping off the rope, m/s.")]
        [SerializeField, Min(0f)] private float tetherReleaseJump = 4.2f;

        [Header("Skateboard")]
        [Tooltip("Ride speed, m/s.")]
        [SerializeField, Min(0f)] private float skateSpeed = 6.2f;
        [Tooltip("Ride acceleration, m/s².")]
        [SerializeField, Min(0f)] private float skateAcceleration = 5f;
        [Tooltip("Turn smoothing while riding, s — a board cannot pivot on the spot.")]
        [SerializeField, Min(0.001f)] private float skateTurnSmoothTime = 0.28f;

        [Header("Crouch")]
        [Tooltip("Crouch-walk speed, m/s (the Crouched Walking clip's authored speed).")]
        [SerializeField, Min(0f)] private float crouchSpeed = 0.39f;
        [Tooltip("Capsule height while crouched, as a fraction of standing height.")]
        [SerializeField, Range(0.3f, 1f)] private float crouchHeightFraction = 0.62f;

        [Header("Lock-on facing")]
        [Tooltip("Below this speed (m/s), with no stick input, a locked-on character turns " +
                 "to face the target.")]
        [SerializeField, Min(0f)] private float lockFaceSpeed = 0.6f;
        [Tooltip("Turn smoothing when an attack squares him up to the target, s.")]
        [SerializeField, Min(0.001f)] private float lockAttackTurnTime = 0.04f;

        [Header("Ground probe")]
        [Tooltip("How far below the capsule the ground check reaches, m.")]
        [SerializeField, Min(0f)] private float groundProbe = 0.12f;

        // ------------------------------------------------------------ getters
        public float RunSpeed => runSpeed;
        public float RunStartSpeed => runStartSpeed;
        public float RunAcceleration => runAcceleration;
        public float RunDeceleration => runDeceleration;
        public float TurnSmoothTime => turnSmoothTime;
        public float RunTurnRate => runTurnRate;
        public float SprintSpeed => sprintSpeed;
        public float SprintDelay => sprintDelay;
        public float SprintRampTime => sprintRampTime;
        public float SprintGrace => sprintGrace;
        public float SprintStickThreshold => sprintStickThreshold;
        public float SprintAcceleration => sprintAcceleration;
        public float SprintTurnRate => sprintTurnRate;
        public bool SprintButtonSkipsDelay => sprintButtonSkipsDelay;
        public float StopTriggerSpeed => stopTriggerSpeed;
        public float StopDeceleration => stopDeceleration;
        public float StopInputDelay => stopInputDelay;
        public float SkidMinSpeed => skidMinSpeed;
        public float SkidMinAngle => skidMinAngle;
        public float SkidMinStick => skidMinStick;
        public float SkidDuration => skidDuration;
        public float SkidPlantFraction => skidPlantFraction;
        public float SkidSpeedKeep => skidSpeedKeep;
        public float SkidSlide => skidSlide;
        public float SideFlipHeight => sideFlipHeight;
        public float SideFlipSpeed => sideFlipSpeed;
        public float TripleJumpHeight => tripleJumpHeight;
        public float JumpTakeoffSpeedKeep => jumpTakeoffSpeedKeep;
        public float ChainWindow => chainWindow;
        public float TripleMinSpeed => tripleMinSpeed;
        public float JumpReleaseSpeed => jumpReleaseSpeed;
        public float MinJumpHoldTime => minJumpHoldTime;
        public float CoyoteTime => coyoteTime;
        public float JumpBuffer => jumpBuffer;
        public float MaxFallSpeed => maxFallSpeed;
        public float GroundedStick => groundedStick;
        public float AirAcceleration => airAcceleration;
        public float AirDrag => airDrag;
        public float AirTurnSmoothTime => airTurnSmoothTime;
        public float DiveMinSpeed => diveMinSpeed;
        public float DiveSpeedBoost => diveSpeedBoost;
        public float DiveMaxSpeed => diveMaxSpeed;
        public float DiveAirSpeed => diveAirSpeed;
        public float DiveHopSpeed => diveHopSpeed;
        public float DiveTurnRate => diveTurnRate;
        public float DiveDrag => diveDrag;
        public float BellySlideDecay => bellySlideDecay;
        public float BellySlideDecayForward => bellySlideDecayForward;
        public float BellySlideDecayBack => bellySlideDecayBack;
        public float BellySlideTurnRate => bellySlideTurnRate;
        public float BellySlideStopSpeed => bellySlideStopSpeed;
        public float BellySlideMinTime => bellySlideMinTime;
        public float BellySlideHeightFraction => bellySlideHeightFraction;
        public float RolloutHeight => rolloutHeight;
        public float RolloutSpeedKeep => rolloutSpeedKeep;
        public float SpinRequiredDegrees => spinRequiredDegrees;
        public float SpinWindow => spinWindow;
        public float SpinStickMin => spinStickMin;
        public float SpinJumpHeight => spinJumpHeight;
        public float SpinHorizontalKeep => spinHorizontalKeep;
        public bool SpinInAir => spinInAir;
        public float FeatherJumpHeight => featherJumpHeight;
        public float DashSpeed => dashSpeed;
        public float DashDuration => dashDuration;
        public float DashCooldown => dashCooldown;
        public float DashExitVerticalKeep => dashExitVerticalKeep;
        public float DashExitHorizontalKeep => dashExitHorizontalKeep;
        public float DashFieldBend => dashFieldBend;
        public float AnchorDuration => anchorDuration;
        public float AnchorCooldown => anchorCooldown;
        public float AnchorRiseAcceleration => anchorRiseAcceleration;
        public float AnchorMaxRiseSpeed => anchorMaxRiseSpeed;
        public float AnchorHaltRate => anchorHaltRate;
        public float AnchorCatchKeep => anchorCatchKeep;
        public float MomentumAirDrag => momentumAirDrag;
        public float MomentumGroundDrag => momentumGroundDrag;
        public float FieldDamping => fieldDamping;
        public float MaxFieldSpeed => maxFieldSpeed;
        public float TetherMaxLength => tetherMaxLength;
        public float TetherMinLength => tetherMinLength;
        public float SwingGravity => swingGravity;
        public float SwingPumpAccel => swingPumpAccel;
        public float SwingDamping => swingDamping;
        public float MaxSwingSpeed => maxSwingSpeed;
        public float EntrySpeedCap => entrySpeedCap;
        public float ImpactSpeedRetain => impactSpeedRetain;
        public float ImpactMinLoss => impactMinLoss;
        public float TetherReleaseJump => tetherReleaseJump;
        public float SkateSpeed => skateSpeed;
        public float SkateAcceleration => skateAcceleration;
        public float SkateTurnSmoothTime => skateTurnSmoothTime;
        public float CrouchSpeed => crouchSpeed;
        public float CrouchHeightFraction => crouchHeightFraction;
        public float LockFaceSpeed => lockFaceSpeed;
        public float LockAttackTurnTime => lockAttackTurnTime;
        public float GroundProbe => groundProbe;

        /// <summary>The jump gravity profile: light rise, hanging apex, heavy fall.</summary>
        public GravityProfile JumpGravity
            => new(riseGravity, apexGravity, fallGravity, apexBand, maxFallSpeed);

        /// <summary>One gravity for arcs that are not jumps.</summary>
        public GravityProfile PlainGravity => GravityProfile.Uniform(plainGravity, maxFallSpeed);

        /// <summary>The spin jump: a normal held rise, then a light, capped descent.</summary>
        public GravityProfile SpinGravity
            => new(riseGravity, apexGravity, spinFallGravity, apexBand, spinFallSpeed);

        /// <summary>Sprint ramp blend (0–1) → top ground speed, m/s.</summary>
        public float GroundTopSpeed(float sprintBlend) => Mathf.Lerp(runSpeed, sprintSpeed, sprintBlend);

        /// <summary>Stick deflection (0–1) → drive fraction (0–1) through the response curve.</summary>
        public float StickDrive(float tilt)
            => Mathf.Pow(Mathf.Clamp01(tilt / Mathf.Max(stickFullTilt, 0.01f)), stickExponent);

        /// <summary>Turn-rate cap at a ground speed, °/s: the run rate up to run speed,
        /// narrowing toward the sprint rate as he speeds up.</summary>
        public float TurnRateAt(float speed)
            => Mathf.Lerp(runTurnRate, sprintTurnRate, Mathf.InverseLerp(runSpeed, sprintSpeed, speed));

        /// <summary>Apex height of a chain jump (1–3) at a ground speed, m.</summary>
        public float ChainJumpHeight(int step, float groundSpeed)
        {
            if (step >= 3) return tripleJumpHeight;
            float standing = step == 2 ? doubleJumpHeight : singleJumpHeight;
            float speedFraction = Mathf.Clamp01(groundSpeed / Mathf.Max(sprintSpeed, 0.01f));
            return standing * (1f + runningJumpBonus * speedFraction);
        }
    }
}
