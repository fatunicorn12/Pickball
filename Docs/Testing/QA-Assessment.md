# QA assessment

**Snapshot:** `feat/newton-boss-fight` @ `a930952`, 2026-10-26. Six read-only research passes
(one per area) plus spot checks. Nothing was run in Play mode. Findings marked **✔** were
confirmed by reading the code again or by a read-only Editor query; the rest are static analysis
with the stated confidence.

**Updated 2026-10-27** for the fixes through `f876b63` and the first batch of P0 tests
(`Tests/EditMode/{Validation,PickleballCore,Boss,Cloth}/`).

How to run and write tests: [README.md](README.md). Design context:
[../Architecture/](../Architecture/README.md).

**Keep this file current.** When a test from this plan lands, or a listed bug is fixed, delete
its line. Don't add "done" markers.

---

## 1. Summary

- **275 EditMode tests: 267 pass, 8 skipped as known bugs (PB1, NB3), 0 fail.** This was 178 at
  `a930952`. The other agents' QA fixes added 32, and the first P0 batch added 65. They cover:
  - pure movement maths
  - combo resolution, leveling, the blade sweep, ground contact and the XP ledger
  - weapon clearance maths
  - `Health`, gravity-well, beam and inverse-square maths, plus Newton's phase switch
  - worldline history and mesh, plus the Spacetime switch and meter readout
  - court line calls and the spring-cloth solver
  - **shipped assets:** script links, value ranges, the combat data graph, and the animator
    contracts

  The test style is good: frame-rate sweeps, explicit time seams and a clean fixture.
- **What still has no tests:**
  - The pickleball **rules, ball physics, AI and input**. Only the court is covered.
  - Every **MonoBehaviour that orchestrates** (`MeleeCombat`, `MovementMotor` and its 14 states,
    `NewtonBoss` beyond its phase switch, `LockOnTargeting`, `TetherSystem`, the cameras).
  - **All 19 editor setup scripts.**
- **No PlayMode assembly and no CI.** Nothing guards physics, timing, animation, scene wiring or
  input.
- **Highest-value next step:** the rest of the P0 list in §3. Most of it is cheap; the remainder
  needs `InternalsVisibleTo` and time-injection seams (§5).

### Coverage by area

| Area | Tests today | Main gaps |
|---|---|---|
| Pickleball core (ball, court, AI, scoring, input) | Court only (`PickleballCore/`) | Rules, ball flight, AI, input. The game also can't be played from the repo (§2 PB1–PB2). |
| Movement | `Logic/*` plus dash reach | 14 states, motor, config-derived maths, input and animator mapping |
| Sword combat and progression | Resolver, timeline, `BladeSweep`, `GroundContact`, `XpLedger`, progression | `MeleeCombat` orchestration (buffering), `PlayerLevel` past the cap, `ComboCondition` |
| Weapon clearance | `ClearanceSolver` | The `WeaponClearance` component over real swings |
| Newton boss | `Health` (incl. guards), wells, beam and volley maths, phase switch | Attack timers, apples, barks, HUD, regain drain |
| Spacetime | History, mesh, switch, `Charge01`, latched input | Meter drain and cooldown, single owner, teleport split, mesh counts, intangible beams |
| Lock-on, camera, tether, dash, skateboard | **0** | Scoring and selection, orbit maths, tether highlight |
| Cloth | `SpringCloth` solver (`Cloth/`) | Leg-capsule collisions, reload safety (ST3) |
| Shipped assets | Script links, ranges, combat graph, animator contracts (`Validation/`) | Build-scene wiring (needs scene opens), release guards |
| Editor setup scripts | **0** | Idempotency and smoke tests (needs an EditorTools assembly) |

---

## 2. Bugs and risks found

IDs are referenced from the test plan below. Confidence: **H**igh, **M**edium, **L**ow.
**✔** means verified.

### Pickleball core

| ID | Finding | Where | Conf. |
|---|---|---|---|
| PB1 | **The five event-channel assets don't load**: `m_Script` is missing because four channel classes share `EventChannels.cs`. Any scene wired to them gets null. Test: `ShippedAssetTests.EventChannelAsset_LoadsAsItsChannelType` (ignored until fixed). | `ScriptableObjects/Events/*.asset`, `Events/EventChannels.cs` | ✔ (Editor query) |
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
| CB6 | Movement unlocks live in a readonly `HashSet` and are lost on a script reload. `Revoke` raises no event. | `PlayerAbilities` | M |
| CB7 | `ExperienceCurve.TotalXpFor` overflows `int` at config values the Inspector allows (growth 3, max level 99, base ≥ 92). `ShippedLevelingCurve_RisesStrictlyToTheCap` guards the shipped config only. | `ExperienceCurve`, `LevelingConfig` | L |

### Newton boss

| ID | Finding | Where | Conf. |
|---|---|---|---|
| NB1 | **A script reload in Play mode breaks the fight.** `Health` and `_player` are cached only in `Awake`/`Start`, so the boss NREs and attacks silently stop. | `NewtonBoss`, four attack components | H (not reproduced) |
| NB2 | **Player death does nothing.** Nothing subscribes to the player's `Health.Died`. | — | ✔ |
| NB4 | Apples spawned close in time and space cancel each other almost immediately, undercutting the barrage. | `Apple`, `AppleBarrage` | M |
| NB5 | The barrage ground height is sampled once, under the boss (who stands about 1.7 m up), so markers and splats near the player may float. | `AppleBarrage` | M |
| NB6 | At point blank, 2–3 orbs of one volley ring hit (60–90 damage against 100 HP, twice in phase 2). There are no i-frames. | `InverseSquareVolley`, `Health` | M |
| NB7 | No retry path (phase, barrage flag and animator Dead flag are never reset). Markers for cancelled apples keep counting down after death. | `NewtonBoss`, `AppleBarrage` | M |

### Movement

| ID | Finding | Where | Conf. |
|---|---|---|---|
| MV2 | The minimum tap-jump height depends on frame rate (about 0.62–0.75 m), contradicting `JumpMath`'s promise. | `JumpMove` variable-height cut | M–H |

### Spacetime, lock-on, camera, tether, cloth

| ID | Finding | Where | Conf. |
|---|---|---|---|
| ST2 | **The tether's targeted highlight never shows:** `SetState` runs twice per frame on the best anchor, so the blend settles around 0.13, varying with frame rate. | `TetherSystem.Update`, `TetherAnchor.SetState` | ✔ |
| ST3 | A reload in Play mode kills `SpringCloth` (goes rigid), the `TetherSystem` reticle and `DashEffects` ghosts. `SwingTwist` is fixed: it now rebuilds its lookup and bones in `OnEnable`. | Awake-only state | M–H |
| ST4 | Disabling `LockOnTargeting` while locked leaves the reticle drawn at a stale point. | `LockOnTargeting.OnDisable` | M |
| ST5 | Lock-on calls `FindObjectsByType<Health>` every frame and `RaycastAll` per candidate. | `LockOnTargeting` | H |
| ST7 | Ended worldlines go stale across off→on cycles and scene changes. The previous scene's ghosts can be drawn. Documented in `Spacetime.md`, not fixed. | `SpacetimeSettings.Ended`, `WorldlineRecorder` | L–M |
| ST8 | Beams are on the Default layer, so in 4D they block lock-on line of sight and tether reach, and can trap Newton. Intangible beams cost a query each per frame and can truncate at 64 hits. Documented in `Spacetime.md`, not fixed. | `SpacetimeView` | M |
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
| ED7 | Setup Newton (Boss) edits the scene without marking it dirty. Setup Cloth dirties every open scene. | `NewtonBossSetup`, `ClothSetup` | M |
| ED8 | `DashGhost.mat` has drifted from the blending the code intends; get-or-create never re-applies it. | `SandboxVfxBuilder` | H |
| ED9 | Batch entry points exit 0 on failure. Setup World Look writes a non-deterministic GPU capture to a tracked PNG. | `SwordAbilitySetup.Batch`, `WorldLookSetup` | M |
| ED10 | Build settings list GameScene and the template SampleScene; the shipped Windows build used ChibiSandbox only, and that scene list isn't recorded anywhere tracked. | `EditorBuildSettings.asset` | H |

---

## 3. Test plan, P0: write these first

These are cheap EditMode tests that protect against silent breakage. "Today" means writable now;
"seam" means it needs the enabling change named in §5 first. ⚠ marks a test expected to fail
until the bug is fixed.

### Shipped-asset validation (`[Category("Assets")]`, read-only `AssetDatabase`)

The read-only asset checks now live in `Tests/EditMode/Validation/`. They cover script links,
`[Range]`/`[Min]` bounds, the combat data graph and timing, effect pooling, and the Attack-layer,
movement and Newton animator contracts. These remain:

- **`BuildScenes_ExistAndHaveNoNullRequiredRefs`.** Every enabled build scene opens additively
  with no null required references. ⚠ PB2, ED10. It has to open scenes, so run it in a worktree,
  never in the shared Editor. It also forces a decision on whether GameScene is still live.
- **Spacetime:** `ShippedSpacetimeSettings_IsOff` (`[Category("Ship")]`, `[Explicit]`). It fails
  while a local test toggle is on, and that is the point.

The asset tests deliberately check structure, not tuning. An exact leveling pace and fixed
project-settings values were considered and left out, because retuning shouldn't need a test
change.

### Pickleball core

Court line calls are covered by `Tests/EditMode/PickleballCore/CourtTests`.

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

Phase two's threshold and one-way rule are covered by `Boss/NewtonPhaseTests`, and `Health`'s
input guards and event order by `Boss/HealthGuardTests`.

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

The `MoveKind` and `JumpKind` values the animator conditions use are pinned by
`Validation/AnimatorContractTests`.

- **State tests (today).**
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

---

## 4. Test plan, P1

### EditMode tests behind a refactor

| Area | Refactor | Tests it unlocks |
|---|---|---|
| Pickleball | Extract `ShotMath` (player and AI) and `InputReader.Classify(start, end, duration, screen, tuning)` | Shot velocities at aim/power extremes. In-bounds sweep from the serve anchor ⚠ PB7. Tap vs swipe at the 80 px boundary; no NaN at duration 0. |
| Pickleball | none | `HitZone.Contains`: serve anchor inside; rotated zone characterisation; disabled → false (call `Physics.SyncTransforms` first). `AIConfig` difficulty endpoints and monotonicity. Event channel raise/unsubscribe semantics. `ScoreView` text format (extract `Format`). |
| Combat | Extract `AttackBuffer` from `MeleeCombat`, driven by injected `dt`/`now`. `BladeSweep`, `GroundContact` and `XpLedger` are already extracted and tested. Under `XpLedger` a refilled target deliberately pays no new XP, which supersedes the old "refill re-arms the bonus" idea. | `PressBeforeComboOpen_ChainsAtComboOpen`, `PressAfterChainSpent_IsDropped`, `IdlePress_Expires` |
| Combat | Static `WeaponClearance.Solve(...)`, or sample clips in EditMode | Blade clears the head by ≥ −2 cm on every frame of every attack clip; total turn ≤ `maxCorrection` |
| Boss | `NextWellDelay`, injected RNG and ground probe, `PredictLanding` | Well cadence and retry; placement ≥ 5 m from the player over 1,000 seeds; marker prediction within 0.1 m of the integrated apple |
| Boss | Fixture helpers `SetReference`/`SetBool`/`SetStrings` | `Barks_*` (rate limit, no repeat, empty → silent), `DamageNumbers_Classify`, `HealthBar_FillsTrackHealth`; `TerminalSpeed_ExceedsFreeFall` and `WellDriftAt5m_IsFelt` against the shipped prefab and config |
| Movement | `InternalsVisibleTo` plus an internal `IMoveContext` passed to `MoveState.Next` | Ground priority order (Fall > Jump > Dive > Crouch > Skid > RunStop); locked never skids; stop delay; side flip vs spin jump; skid exit speeds; landing records the chain step; coyote beats feather; belly slide rules; `LandAtRest_AfterSprint_IsGround` |
| Movement | Pure helpers for the variable jump, intent mapping and animator mapping | `TapApex_FrameRateIndependent` ⚠ MV2; camera-relative intent; trigger choice and flip speed |
| Traversal | One state per anchor per frame in `TetherSystem`; `SetState(state, dt)` | `TetherAnchor_HeldTarget_ReachesFullTargetBlend` ⚠ ST2 |
| Traversal | Extract `LockOnScoring`, `LockOnState`, `OrbitMath`. `SwingTwist`'s maths is already in `SwingTwistMath`, covered by `SwingTwistMathTests` | Angle-first scoring, cone and range rejection, side flicks, handover on death, break range, sight grace; pitch normalisation ⚠ ST9; zoom and pitch clamps |
| Spacetime | none (EditMode physics) | Intangible beam through a `CharacterController` starts disabled and becomes solid when clear; owner-ignore collisions; `onlyIn4D`/`onlyIn3D` dressing; `RecordApples` binds a recorder |
| Editor | New `Pickleball.Tests.EditorTools` assembly, `[Category("EditorSetup")]`, worktree only | Each Setup menu twice → `git status` clean; Setup Chibi Sandbox smoke (components on Chibi, 4 pickups, 4 anchors, 0 missing scripts); Spacetime Add/Remove idempotent and a no-op while off |

### PlayMode first wave

Needs the PlayMode assembly ([README.md](README.md#adding-the-playmode-assembly)). Ranked by value:

1. **Scene boot smoke.** ChibiSandbox, TrainingStage and SpacetimeLab each run 120 frames with no
   errors. Any `LogError` fails the test. Decide whether GameScene belongs in this list.
2. **Jump and dash.** The single, double and triple apex equals config ± 0.03 m at 30, 60 and
   144 fps. Dash distance is 3.08 m ± 0.05 at each rate.
3. **Ground combo.** A mashed Slash 1–4 against a `Health` dummy plays in order with exactly one
   hit per swing (4 `Hurt` events) at 1/30, 1/20 and 1/15 s per frame. This is the in-scene
   check of the CB1/CB2 fixes.
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

If you're picking this up:
1. Finish the remaining P0 tests that are writable today: the Spacetime meter, `ComboCondition`,
   `PlayerLevel`, and the movement logic edges.
2. Add `InternalsVisibleTo` and the `Advance` seams (§5) and continue down P0.
3. Raise the PlayMode assembly.

Tests for known bugs are committed with `[Ignore("Known bug <ID> …")]`, so the suite stays green
while the bug stays visible as a skipped test. Whoever fixes the bug removes the `Ignore` in the
same commit.

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
6. **Combat extraction:** `AttackBuffer`.
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
- Agree the remaining categories in [README.md](README.md#categories).
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

- **`CodingStandards.md`:**
  - `ChibiLocomotion` is now 174 lines; the largest file is `ChibiSandboxSetup.cs` at 2,072.
  - Editor scripts don't follow "namespaces mirror folders".
  - There is no PlayMode or editor-tool test guidance (now in [README.md](README.md)).
  - Setup idempotency is broken by ED3, ED4 and ED9.
- **Missing system docs:** Newton boss (interim notes in
  [../Architecture/NewtonBoss.md](../Architecture/NewtonBoss.md)), pickleball core (interim notes
  in [../Architecture/PickleballCore.md](../Architecture/PickleballCore.md)), lock-on, camera,
  tether, cloth and the training stage.
