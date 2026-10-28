# QA assessment

**Snapshot:** `feat/newton-boss-fight` @ `a930952`, 2026-10-26. Six read-only research passes
(one per area) plus spot checks. Nothing was run in Play mode. Findings marked **✔** were
confirmed by reading the code again or by a read-only Editor query; the rest are static analysis
with the stated confidence.

How to run and write tests: [README.md](README.md). Design context:
[../Architecture/](../Architecture/README.md).

**Keep this file current.** When a test from this plan lands, or a listed bug is fixed, delete
its line. Don't add "done" markers.

---

## 1. Summary

- **178 EditMode tests, all logic-level, all passing.** They cover:
  - pure movement maths
  - combo resolution and leveling
  - weapon clearance maths
  - `Health`, gravity-well, beam and inverse-square maths
  - worldline history and mesh, plus the Spacetime switch

  The test style is good: frame-rate sweeps, explicit time seams and a clean fixture.
- **What has no tests at all:**
  - The whole **pickleball game core**.
  - Every **MonoBehaviour that orchestrates** (`MeleeCombat`, `MovementMotor` and its 14 states,
    `NewtonBoss`, `LockOnTargeting`, `TetherSystem`, the cameras).
  - **Every shipped asset** (combat steps, leveling curve, animator controllers, settings).
  - **All 19 editor setup scripts.**
- **No PlayMode assembly and no CI.** Nothing guards physics, timing, animation, scene wiring or
  input.
- **Highest-value next step:** the P0 list in §3. It is mostly cheap EditMode tests on shipped
  assets and pure logic. It needs one small enabling change, `InternalsVisibleTo`, plus
  time-injection seams (§5). Several P0 tests are expected to **fail today**. Each such failure is
  a real bug listed in §2.

### Coverage by area

| Area | Tests today | Main gaps |
|---|---|---|
| Pickleball core (ball, court, AI, scoring, input) | **0** | Everything. The game also can't be played from the repo (§2 PB1–PB2). |
| Movement | 41, all on `Logic/*` | 14 states, motor, config-derived maths, enums, input and animator mapping |
| Sword combat and progression | 41 | `MeleeCombat` (hit sweep, buffering, track choice), XP source, unlocks, all shipped assets |
| Weapon clearance | 8 (`ClearanceSolver`) | The `WeaponClearance` component over real swings |
| Newton boss | 66 (`Health`, wells, beam, volley maths) | `NewtonBoss` phases and timers, apples, barks, HUD, regain drain |
| Spacetime | 22 | Meter, cooldown, single owner, teleport split, mesh counts, intangible beams |
| Lock-on, camera, tether, cloth, dash, skateboard | **0** | Scoring and selection, orbit maths, cloth stability, tether highlight |
| Editor setup and project config | **0** | Idempotency, missing scripts, build scenes, settings invariants |

---

## 2. Bugs and risks found

IDs are referenced from the test plan below. Confidence: **H**igh, **M**edium, **L**ow.
**✔** means verified.

### Pickleball core

| ID | Finding | Where | Conf. |
|---|---|---|---|
| PB1 | **The five event-channel assets don't load**: `m_Script` is missing because four channel classes share `EventChannels.cs`. Any scene wired to them gets null. | `ScriptableObjects/Events/*.asset`, `Events/EventChannels.cs` | ✔ (Editor query) |
| PB2 | **`GameScene.unity` is an unwired stub** (no camera, AI, GameStateManager, HitZone or UI; references null) and is still build index 0. Press Play does nothing useful. | `Scenes/GameScene.unity`, `EditorBuildSettings.asset` | ✔ |
| PB3 | The AI ignores incoming balls while walking home after a swing (`_reacting` stays true), so fast returns and early serves are free points. | `AIController` reset walk | H |
| PB4 | The AI aim error is re-rolled every loop iteration and averages out, so difficulty barely affects accuracy and the AI jitters. | `AIController.ComputeTarget` | H |
| PB5 | Rally rules: the first bounce isn't checked to be on the opponent's side, and a net-cord touch on the net's top face counts as a Kitchen floor bounce. | `GameStateManager.DetermineLoser`, `BallController.OnCollisionEnter` | M |
| PB6 | `ResetTo` teleports through `transform.position` on an interpolated, continuous body (`autoSyncTransforms` is off). | `BallController.ResetTo` | L–M |
| PB7 | Shots with \|aim\| above ~0.76 land wide with the current tuning; the AI's ±2 m/s aim ignores its own x position. | `PlayerController`, `AIController`, `BallConfig` | M (by hand) |
| PB8 | Player can serve before `serveDelay`; tooltips for `serveDelay`, `reactionDelay` and `positionTolerance` don't match their use. | `GameConfig`, `AIConfig` | L |

### Sword combat and progression

| ID | Finding | Where | Conf. |
|---|---|---|---|
| CB1 | **The first blade-live frame tests nothing, and a frame that crosses the whole active window tests nothing.** The comment claims the opposite. | `MeleeCombat.Update` / `SweepForHits` | ✔ |
| CB2 | Mashing chains at `comboOpen`, which is before `activeEnd` on every non-final step, roughly halving hit windows. *+ C* keeps about 64 ms. Jumping and pressing during Area Under the Curve loses the ring. | step assets, `MeleeCombat` | H (mechanism) |
| CB3 | A one-frame grounded flicker can start an air step and drop the ground chain. The grace period only delays *leaving* the ground. | `MeleeCombat` track choice, `ComboProgress.RecordStep` | M–H |
| CB4 | Disabling `MeleeCombat` during hitstop leaves the animator frozen (`animator.speed` 0). | `MeleeCombat.OnDisable`, `SwordAnimatorDriver` | H (rare) |
| CB5 | XP is paid for hits on already-dead targets, including someone else's killing blow (+250). Refilling dummies can be farmed for about 400 XP per KO (levels 2–5 in about 1.3 KOs). | `CombatXpSource` | M |
| CB6 | Movement unlocks live in a readonly `HashSet` and are lost on a script reload. `Revoke` raises no event. | `PlayerAbilities` | M |
| CB7 | `ExperienceCurve.TotalXpFor` overflows `int` at config values the Inspector allows (growth 3, max level 99, base ≥ 92). | `ExperienceCurve`, `LevelingConfig` | L |
| CB8 | The `AbilityPickup.consumeOnPickup` tooltip says the opposite of what the field does. | `AbilityPickup` | H (trivial) |

### Newton boss

| ID | Finding | Where | Conf. |
|---|---|---|---|
| NB1 | **A script reload in Play mode breaks the fight.** `Health` and `_player` are cached only in `Awake`/`Start`, so the boss NREs and attacks silently stop. | `NewtonBoss`, four attack components | H (not reproduced) |
| NB2 | **Player death does nothing.** Nothing subscribes to the player's `Health.Died`. | — | ✔ |
| NB3 | **`Health.Damage(NaN)` kills instantly**, `Heal(NaN)` fully heals, and `CreditDamageDealt(NaN)` cashes in all grey. | `Health` guards | ✔ |
| NB4 | Apples spawned close in time and space cancel each other almost immediately, undercutting the barrage. | `Apple`, `AppleBarrage` | M |
| NB5 | The barrage ground height is sampled once, under the boss (who stands about 1.7 m up), so markers and splats near the player may float. | `AppleBarrage` | M |
| NB6 | At point blank, 2–3 orbs of one volley ring hit (60–90 damage against 100 HP, twice in phase 2). There are no i-frames. | `InverseSquareVolley`, `Health` | M |
| NB7 | No retry path (phase, barrage flag and animator Dead flag are never reset). Markers for cancelled apples keep counting down after death. | `NewtonBoss`, `AppleBarrage` | M |

### Movement

| ID | Finding | Where | Conf. |
|---|---|---|---|
| MV1 | **The dash overshoots by up to one frame:** about 3.27 m against the "fixed" 3.08 m. | `DashMove.Tick` | ✔ |
| MV2 | The minimum tap-jump height depends on frame rate (about 0.62–0.75 m), contradicting `JumpMath`'s promise. | `JumpMove` variable-height cut | M–H |
| MV3 | `SuspendGravity` time outlives landing and freezes a later fall. | `MovementMotor` hover timer | M |
| MV4 | Anchor, then mount, then hold Jump gives a flying skateboard: the anchor keeps running in states that refuse it. | `MovementMotor`, `SkateMove` | M |
| MV5 | The `CollisionFlags.Below` fallback bypasses the slope limit, so the player can stand and chain-jump on steep walls. | `MovementMotor` ground check | M–L |
| MV6 | No ground snapping: sprinting down slopes over about 16° flickers between Ground and Fall. | `groundedStick` hold-down | M |
| MV7 | Per-frame allocations: `SphereCastAll` for the ground check, `OverlapCapsule` in `CanStandTo`. | `MovementMotor` | H |
| MV8 | A reload mid-move rebuilds states without `Enter`, so a tether snaps to its anchor and a dash ends at zero velocity. | `MovementMotor.EnsureRuntime` | M (Editor only) |
| MV9 | Landing at rest after a sprint reports `GroundKind` Sprint for up to 0.2 s, because the sprint ramp is frozen in the air. | `SprintRamp` usage | M |
| MV10 | A rollout press is eaten: the jump buffer (0.12 s) is shorter than `bellySlideMinTime` (0.15 s). Also, a ground dash plus an air dash fit in one airtime. | `BellySlideMove`, `DashMove` | M |

### Spacetime, lock-on, camera, tether, cloth

| ID | Finding | Where | Conf. |
|---|---|---|---|
| ST1 | **`SpacetimeView.Charge01` is negative (−0.167)** while the feature is off and before the first tick; the docs tell HUDs to read it. | `SpacetimeView` | ✔ |
| ST2 | **The tether's targeted highlight never shows:** `SetState` runs twice per frame on the best anchor, so the blend settles around 0.13, varying with frame rate. | `TetherSystem.Update`, `TetherAnchor.SetState` | ✔ |
| ST3 | A reload in Play mode kills `SpringCloth` (goes rigid), `SwingTwist` (twist stops), the `TetherSystem` reticle and `DashEffects` ghosts. | Awake-only state | M–H |
| ST4 | Disabling `LockOnTargeting` while locked leaves the reticle drawn at a stale point. | `LockOnTargeting.OnDisable` | M |
| ST5 | Lock-on calls `FindObjectsByType<Health>` every frame and `RaycastAll` per candidate. | `LockOnTargeting` | H |
| ST6 | The Spacetime toggle polls `WasPressedThisFrame`, which drops presses in an unfocused Editor or with simulated input (lock-on latches on `performed` instead). | `SpacetimeView` | M |
| ST7 | Ended worldlines go stale across off→on cycles and scene changes. The previous scene's ghosts can be drawn. | `SpacetimeSettings.Ended`, `WorldlineRecorder` | L–M |
| ST8 | Beams are on the Default layer, so in 4D they block lock-on line of sight and tether reach, and can trap Newton. Intangible beams cost a query each per frame and can truncate at 64 hits. | `SpacetimeView` | M |
| ST9 | Latent bugs: a camera authored with negative pitch snaps to 65° on first input, `FaceCamera` can lag a frame, and `ropeSegments = 1` writes NaN rope points. | `SandboxOrbitCamera`, `FaceCamera`, `TetherSystem` | M/L |

### Editor setup and project

| ID | Finding | Where | Conf. |
|---|---|---|---|
| ED1 | **`AutoRunOnce` (`[InitializeOnLoadMethod]`) runs the full, destructive Setup Chibi Sandbox** after a domain reload whenever the machine-wide EditorPrefs token differs. That includes batch and CI runs and any worktree. | `ChibiSandboxSetup` | ✔ |
| ED2 | **Setup Chibi Sandbox ignores Cancel on the save prompt** and replaces the open scene anyway. | `ChibiSandboxSetup.BuildScene` | ✔ |
| ED3 | **Build Training Stage changes the scene's GUID on every run** (delete then copy). | `TrainingStageSetup` | ✔ |
| ED4 | Re-running Setup Chibi Sandbox wipes everything the later menus added (Newton, fight, lock-on, World Look, Clean World, Spacetime). | `ChibiSandboxSetup` | H |
| ED5 | The Organize duplicate-model sweep deletes matching models anywhere in the tree; its comment says root level only. | `ChibiSandboxSetup.Organize` | H |
| ED6 | `Chibi.prefab` lacks `SwordAbilities`, `WeaponClearance`, `LockOnTargeting` and `Health`. They exist only as scene overrides, so SpacetimeLab's chibi has none of them. | `Prefabs/Chibi.prefab` | H |
| ED7 | Setup Weapon Clearance and Setup Newton (Boss) edit the scene without marking it dirty. Setup Cloth dirties every open scene. | those scripts | M |
| ED8 | `DashGhost.mat` has drifted from the blending the code intends; get-or-create never re-applies it. | `SandboxVfxBuilder` | H |
| ED9 | Batch entry points exit 0 on failure. Setup World Look writes a non-deterministic GPU capture to a tracked PNG. | `SwordAbilitySetup.Batch`, `WorldLookSetup` | M |
| ED10 | Build settings list GameScene and the template SampleScene; the shipped Windows build used ChibiSandbox only, and that scene list isn't recorded anywhere tracked. | `EditorBuildSettings.asset` | H |

---

## 3. Test plan, P0: write these first

These are cheap EditMode tests that protect against silent breakage. "Today" means writable now;
"seam" means it needs the enabling change named in §5 first. ⚠ marks a test expected to fail
until the bug is fixed.

### Shipped-asset validation (`[Category("Assets")]`, read-only `AssetDatabase`)

- **`EventChannelAssets_LoadWithTheirType`.** All five
  `ScriptableObjects/Events/*.asset` load as their channel type. ⚠ PB1.
- **`BuildScenes_ExistAndHaveNoNullRequiredRefs`.** Every enabled build scene exists and opens
  additively with no null required references. ⚠ PB2, ED10. This forces a decision on whether
  GameScene is still live.
- **`Prefabs_AndScenes_NoMissingScripts`.**
  `GameObjectUtility.GetMonoBehavioursWithMissingScriptCount` is 0 across all scenes and prefabs.
  Passes today; this locks it in.
- **`ConfigAssets_WithinRangeAndMin`.** Every serialized field with `[Range]`/`[Min]` on every
  asset under `ScriptableObjects/` is inside its bounds and not NaN.
- **Combat:**
  - `ShippedSteps_AllReachable`: every `ComboStepDefinition` is in the moveset or is an ability's
    step (9 of 9 today).
  - `ShippedAbilities_AreWellFormed`: step not null, role not Regular, condition min ≤ max, each
    unlocked exactly once.
  - `ShippedUnlockTable_IsReachable`: 2 ≤ level ≤ `MaxLevel`, no duplicates.
  - `ShippedLeveling_MatchesDocumentedPace`: `TotalXpFor(2..5)` = {50, 150, 300, 500}, strictly
    increasing to `MaxLevel`.
  - `ShippedSteps_TimingIsCoherent`: clip set; `activeStart < activeEnd ≤ comboClose ≤ 1`;
    `comboOpen ≤ comboClose`; burst `triggerAt ≤ comboClose`; `animatorState` unique.
  - `ShippedSteps_EffectPrefabsArePooled`: each non-null hit or burst effect has `PooledEffect`.
  - `ShippedSteps_MashedChainStillSweeps`: simulate `SwingTimeline` at 1/30 s over 10 phase
    offsets, chaining at the first frame past `comboOpen`; at least 2 swept intervals per step.
    ⚠ CB2 (*+ C* is expected to fail).
- **Animator contracts:**
  - `AttackLayer_HasStateForEveryComboStep`: each step's state exists on the Attack layer with a
    motion.
  - `NewtonController_HasEveryParameterTheBossHashes`: Death is the first AnyState transition;
    casts are gated by `!Channeling`.
  - `MovementController_ConditionsMatchMoveKind`: the animator's int conditions agree with the
    `MoveKind` and `JumpKind` values.
- **Spacetime:** `ShippedSpacetimeSettings_IsOff` (`[Category("Ship")]`). This fails while a local
  test toggle is on, and that is the point.
- **`ProjectSettings_Invariants`:** `bounceThreshold == 2`, `fixedDeltaTime == 0.02`, Input
  System-only handler.

### Pickleball core

- **`Court_*` (today).**
  - `IsInBounds`: (3.069,0,0) is in, (3.08,0,0) is out; (0,0,6.72) is in, (0,0,6.73) is out.
  - `SideOf`: z = −0.01 → Player; z = 0 → AI.
  - `ResolveZone`: (0,0,±2.13) → Kitchen; (−1,0,−4) → CourtLeft; (0,0,−2.14) → CourtRight;
    (3.2,0,1) → OutOfBounds, which beats Kitchen.
- **`Referee_*` (seam: extract `RallyReferee`).**
  - `HasWon`: (11,9) yes, (11,10) no, (12,10) yes, (14,13) no.
  - Side-out: the server wins → +1; the receiver wins → the serve changes, no point.
  - Deuce run: from 10–10, the server's two rallies give 11–10 (not won), then 12–10 (won).
  - Loser attribution: net → the hitter; out with no bounce → the hitter; out after a bounce →
    the first-bounce side.
  - Dead events are ignored in `PointScored` and `GameOver`.
  - Pin the PB5 behaviour as a characterisation test until it is fixed.
- **`BallFlight_*` (seam: extract `BallFlightModel`, shared by `FixedUpdate` and `PredictLanding`).**
  - `Accel` at `v.y` = 5, 0.5, −0.5, −5 → −5.3955, −3.4335, −9.4335, −11.3955 (default config).
  - `Bounce((1,−6,5))` → (0.85, 4.2, 4.25); the minimum pop applies on slow impacts.
  - `PredictLanding` from rest at y = 1 → (0,0,0).

### Combat

- **`ComboCondition_*` (today).** The default is always true. `requireLockOn` needs a target.
  Max 3: distance 3 passes, 3.01 fails. The [3, 7] band works. Constructor clamps.
- **`PlayerLevel_PastCap_*` (today).** `AddXp(10000)` at max level 5 → level 5, 4 `LeveledUp`
  events, `Progress01` = 1. Further XP raises nothing. `startingLevel` 99 clamps to 5.

### Newton boss

- **`Phase_EntersTwo_AtThreshold_Inclusive` (today; call `Awake` by reflection).** 50/100 HP at
  `phaseTwoAt` 0.5 → phase 2; 51 → phase 1. Also `Phase_NeverReverts` and `DeadBoss_DoesNotPhase`.
- **`Health_*` (today).**
  - `Damage_NaN_IsIgnored`, plus the same for `Heal` and `Credit`. ⚠ NB3.
  - Negative or dead `Heal`/`Credit` is a no-op.
  - `Heal` clamps at max.
  - `KillingBlow_HurtBeforeDied`: event order.
- **`RangeMultiplier_SameLawAsVolley` (today).** Equals `DamageAt(r, 1, …)`, finite at r ≤ 0,
  non-increasing in r.
- **`Apple_ClosestApproach_*` (today, via reflection).** Head-on → t = 0.5; parallel → 0;
  t clamped to [0, 1].
- **Seam: `Advance(dt, now)` on `Health` and `FluxionBeam`.**
  - `Grey_HoldsForGrace_ThenDrainsAtDecay`: 40 grey stays 40 at 2.4 s and is 26 at 3.5 s.
  - `AnyHit_IncludingPermanent_RestartsGrace`
  - `BeamLifecycle`: charging → firing at 1.1 s → idle after 2.4 s.
  - `BeamBill_ExcludesFieldVelocity`: guards the `d489f33` regression.
  - `SpeedSmoothing_FrameRateIndependent`

### Movement

- **Enum and state tests (today).**
  - `MoveKind_ValuesArePinned` and `JumpKind_ValuesArePinned`: the animator conditions use these
    ints.
  - `EveryMoveKindHasOneState`: reflect over the `MoveState` subclasses.
  - `InterruptMatrix`: `AllowsDash`/`Tether`/`Mount`/`Anchor` per kind.
  - `IsExclusive_OnlyDashTetherSkate`: this feeds combat's `IsBusy`.
- **`MovementConfig_DerivedMaths` (today; `CreateInstance` + `SerializedObject`).**
  `ChainJumpHeight`, `StickDrive`, `TurnRateAt`, `GroundTopSpeed`, `SpinGravity`, `PlainGravity`
  at their documented points; the default single-jump apex is 2.0 m ± 1e-4. Take the exact
  expected values from the config defaults when writing these.
- **Logic edge cases (today).**
  - `GravityProfile.GravityAt` and its constructor clamps.
  - `JumpMath`: `FallTime` capped branch, the ≤ 0 guards, `Step` released while rising, zero
    apex band.
  - `JumpChain`: inclusive window, `Reset`.
  - `SprintRamp`: grace boundary, `Reset`.
  - `SkidRule`: a zero travel vector never skids.
  - `SpinGesture`: dead-zone frames, hitch clears the ring, clockwise rotation.
  - `ForceFieldChannel`: undamped branch, decay after the field stops.

### Spacetime

All of these are writable today.
- **`SpacetimeSettings`:**
  - `AddEnded` drops the oldest past 256.
  - `SetIn4D` raises only on change.
  - `TryClaim`: the second view is refused; release frees the claim.
- **`SpacetimeView` meter:**
  - `Charge01_InRange_WhenOffAndBeforeFirstTick`. ⚠ ST1.
  - `Meter_DrainsAndAutoExits`.
  - `Meter_BlocksEntryBelowMinCharge`.
  - `FlipCooldown`.
  - `MaxMeterStep_ClampsLongFrame`.
  - `Tick_PrunesEndedLines`.
  - `TwoViews_OnlyOwnerFlips`.
- **`Recorder_Teleport_RetiresHistoryToEnded`:** fewer than 2 samples drops the line instead.
- **`WorldlineMesh`:**
  - `Mesh_Counts`: 8(k+1) vertices and 24k+12 indices per run; a hairpin gives two runs.
  - `Mesh_ColourAndUv_RunPastToPresent`
  - `Mesh_SlopedPath_TopStaysLevel`
  - `Mesh_NaNInput`
  - `Mesh_ReusedStaticBuffers`
- **`History_EdgeCases`:** empty → `OldestTime` NaN; span clamps; cutoff inclusive. With
  `maxPoints = 1` it returns *all* samples; pin that or fix it.

### Cloth

All of these are writable today; `Simulate` and `Rebind` are public.
- `SpringCloth_PreservesSegmentLengths`
- `SpringCloth_SwingWithinMaxAngle`
- `SpringCloth_SettlesToRest`
- `SpringCloth_ZeroDt_HoldsPose`
- `SpringCloth_HugeDt_NoNaN`
- `SpringCloth_Teleport_SnapsToRest`

---

## 4. Test plan, P1

### EditMode tests behind a refactor

| Area | Refactor | Tests it unlocks |
|---|---|---|
| Pickleball | Extract `ShotMath` (player and AI) and `InputReader.Classify(start, end, duration, screen, tuning)` | Shot velocities at aim/power extremes. In-bounds sweep from the serve anchor ⚠ PB7. Tap vs swipe at the 80 px boundary; no NaN at duration 0. |
| Pickleball | none | `HitZone.Contains`: serve anchor inside; rotated zone characterisation; disabled → false (call `Physics.SyncTransforms` first). `AIConfig` difficulty endpoints and monotonicity. Event channel raise/unsubscribe semantics. `ScoreView` text format (extract `Format`). |
| Combat | Extract `AttackBuffer` and `BladeSweep` from `MeleeCombat`, driven by injected `dt`/`now` | `PressBeforeComboOpen_ChainsAtComboOpen`, `PressAfterChainSpent_IsDropped`, `IdlePress_Expires`, `BladeOnFrame_TestsCurrentBlade` ⚠ CB1, `WholeWindowFrame_TestsAtLeastOnce` ⚠ CB1, `FlickerFramePress_StaysOnGroundTrack` ⚠ CB3 |
| Combat | Extract `XpLedger` from `CombatXpSource` | Fractional carry; kill bonus once; dead and already credited → 0; refilled target re-arms. `DeadButUncredited_PaysNothing` ⚠ CB5 |
| Combat | Static `WeaponClearance.Solve(...)`, or sample clips in EditMode | Blade clears the head by ≥ −2 cm on every frame of every attack clip; total turn ≤ `maxCorrection` |
| Boss | `NextWellDelay`, injected RNG and ground probe, `PredictLanding` | Well cadence and retry; placement ≥ 5 m from the player over 1,000 seeds; marker prediction within 0.1 m of the integrated apple |
| Boss | Fixture helpers `SetReference`/`SetBool`/`SetStrings` | `Barks_*` (rate limit, no repeat, empty → silent), `DamageNumbers_Classify`, `HealthBar_FillsTrackHealth`; `TerminalSpeed_ExceedsFreeFall` and `WellDriftAt5m_IsFelt` against the shipped prefab and config |
| Movement | `InternalsVisibleTo` plus an internal `IMoveContext` passed to `MoveState.Next` | Ground priority order (Fall > Jump > Dive > Crouch > Skid > RunStop); locked never skids; stop delay; side flip vs spin jump; skid exit speeds; landing records the chain step; coyote beats feather; belly slide rules; `LandAtRest_AfterSprint_IsGround` ⚠ MV9 |
| Movement | Pure helpers for the variable jump, intent mapping and animator mapping | `TapApex_FrameRateIndependent` ⚠ MV2; camera-relative intent; trigger choice and flip speed |
| Traversal | One state per anchor per frame in `TetherSystem`; `SetState(state, dt)` | `TetherAnchor_HeldTarget_ReachesFullTargetBlend` ⚠ ST2 |
| Traversal | Extract `LockOnScoring`, `LockOnState`, `OrbitMath`, `SwingTwist.SampleTrack` | Angle-first scoring, cone and range rejection, side flicks, handover on death, break range, sight grace; pitch normalisation ⚠ ST9; zoom and pitch clamps |
| Spacetime | none (EditMode physics) | Intangible beam through a `CharacterController` starts disabled and becomes solid when clear; owner-ignore collisions; `onlyIn4D`/`onlyIn3D` dressing; `RecordApples` binds a recorder |
| Editor | New `Pickleball.Tests.EditorTools` assembly, `[Category("EditorSetup")]`, worktree only | Each Setup menu twice → `git status` clean; Setup Chibi Sandbox smoke (components on Chibi, 4 pickups, 4 anchors, 0 missing scripts); Spacetime Add/Remove idempotent and a no-op while off |

### PlayMode first wave

Needs the PlayMode assembly ([README.md](README.md#adding-the-playmode-assembly)). Ranked by value:

1. **Scene boot smoke.** ChibiSandbox, TrainingStage and SpacetimeLab each run 120 frames with no
   errors. Any `LogError` fails the test. Decide whether GameScene belongs in this list.
2. **Jump and dash.** The single, double and triple apex equals config ± 0.03 m at 30, 60 and
   144 fps. Dash distance is 3.08 m ± 0.05 at each rate. ⚠ MV1.
3. **Ground combo.** A mashed Slash 1–4 against a `Health` dummy plays in order with exactly one
   hit per swing (4 `Hurt` events) at 1/30, 1/20 and 1/15 s per frame. ⚠ CB1/CB2 at low rates.
4. **Newton phases and death.**
   - At 54% HP the boss enters phase 2 the same frame; the barrage starts 1.5 s later.
   - Killing him mid-beam and mid-barrage empties `GravityWell.Active`, cancels the apples and
     drops exactly one Gravity Anchor.
5. **Gravity well vs the motor.** At 5 m the drift is about 1.44 m/s ± 15%. In the core, movement
   locks. On collapse the pull stops that frame.
6. **Spacetime bridge.** A recorded lob becomes a beam the player can stand on. Flipping back
   drops him. `SetEnabled(false)` in 4D removes every beam object, the input action and every
   recorder history.
7. **Pickleball physics.**
   - A slow drop from 0.3 m still bounces: the `bounceThreshold` regression.
   - Serves at power {0, .5, 1} × aim {0, ±.5} first bounce on the AI side, in bounds.
   - `PredictLanding` is within 0.3 m of the actual contact.
   - Game over at `pointsToWin` 1.
   - A test for PB3, AI reacting during its walk home.
8. **Lock-on.** Tab locks the target nearest the centre of view. Killing it hands the lock over
   within a frame. Disabling the component hides the reticle. ⚠ ST4.
9. **Tether.** Rope length holds to ± 0.05 m through a 3 s swing.
10. **Weapon clearance over real blended swings.** The blade-to-head gap is ≥ −2 cm on every
    frame.

### Where to start

If you're picking this up: start with P0, asset validation first (no code changes, catches PB1
and the combat data contracts). Then Health, Court and the Spacetime meter (writable today). Then
add `InternalsVisibleTo` and the `Advance` seams (§5) and continue down P0. Raise the PlayMode
assembly once P0 is green.

---

## 5. Enabling changes, ranked by how many tests they unlock

1. **`[assembly: InternalsVisibleTo("Pickleball.Tests.EditMode")]`** in a new
   `Scripts/AssemblyInfo.cs`. This exposes the motor's internal state API and lets other classes
   offer `internal` seams without widening their public API.
2. **Time injection** (`Advance(float dt, float now)` called from `Update`) on `Health`,
   `FluxionBeam`, `GravityWell`, `NewtonBoss` and `TetherSystem`, copying `SpacetimeView.Tick`.
3. **One file per ScriptableObject event-channel class.** This also fixes PB1. After the split,
   re-run Create Assets and re-wire.
4. **Pickleball extractions:** `RallyReferee`, `BallFlightModel`, `ShotMath`,
   `InputReader.Classify`.
5. **Movement `IMoveContext`** for `MoveState.Next`. Also move `Next`'s side effects (chain
   landing, sprint reset, pending jump) into `Enter` or `Tick`, so "decisions only" is true.
6. **Combat extractions:** `AttackBuffer`, `BladeSweep`, `XpLedger`.
7. **Fixture consolidation:** one shared DontSave base, plus `SetReference`/`SetBool`/`SetStrings`
   helpers.

## 6. Process and infrastructure

**P0**
- Make `AutoRunOnce` return when `Application.isBatchMode`, or delete it (ED1).
- Honour the save-prompt result in Setup Chibi Sandbox (ED2).
- Overwrite rather than delete-and-copy in Build Training Stage (ED3).
- Make the batch entry points `EditorApplication.Exit(1)` when errors were logged (ED9).

**P1**
- Add the PlayMode and EditorTools test assemblies.
- Agree the categories in [README.md](README.md#categories-proposed-none-are-in-use-yet).
- Add one orchestrator menu that runs the setup order in
  [EditorSetup.md](../Architecture/EditorSetup.md#run-order).
- Record the shipped build's scene list in a tracked file.

**P2**
- CI: a batch EditMode run on a clean worktree, gating on the results XML.
- Move input bindings into static tables so a "no two actions share a binding" test is possible.
- Pin UniTask to a tag.

## 7. Documentation drift found

These were reported to the system-doc owners through the agent-manager session. They are listed
here so they aren't lost if a doc isn't updated.

- **`SwordAbilities.md`:**
  - The long-frame sweep claim is wrong (CB1).
  - The grace period covers only leaving the ground (CB3).
  - The pad clash and vertical levers are described as unmerged, though they are merged.
  - The test count (115) is stale.
  - AirAttack's `comboOpen` differs from `WeaponSetup`.
  - Knockback advice doesn't match the shipped steps.
- **`WeaponClearance.md`:**
  - The `MeleeCombat` sweep runs in `Update`, not `LateUpdate`.
  - "Untick `rightHanded`" is reverted by setup.
  - The new-rig workflow doesn't match the setup's fixed path.
- **`Movement.md`:**
  - The West-button clash and "bindings unchanged" are stale.
  - The `IsAttacking` fallback is described wrongly.
  - The vertical levers aren't reachable by combat.
  - Frame step 2 is incomplete.
  - The spin, dive and triple-jump claims are overstated.
  - The tap height is frame-rate dependent (MV2).
  - `Next()` has side effects.
- **`Spacetime.md`:**
  - The sign of the time shift is wrong (the code shifts by −`timeAxis × age`).
  - "Nothing happens while off" is overstated.
  - The `Charge01` advice is unsafe (ST1).
  - The single-owner rule, the 256 cap, stale ended lines and the Default-layer interactions are
    missing.
  - "Remove undoes Add" overstates it.
- **`CodingStandards.md`:**
  - `ChibiLocomotion` is now 174 lines; the largest file is `ChibiSandboxSetup.cs` at 2,072.
  - Editor scripts don't follow "namespaces mirror folders".
  - There is no PlayMode or editor-tool test guidance (now in [README.md](README.md)).
  - Setup idempotency is broken by ED3, ED4 and ED9.
- **Missing system docs:** Newton boss (interim notes in
  [../Architecture/NewtonBoss.md](../Architecture/NewtonBoss.md)), pickleball core (interim notes
  in [../Architecture/PickleballCore.md](../Architecture/PickleballCore.md)), lock-on, camera,
  tether, cloth and the training stage.
