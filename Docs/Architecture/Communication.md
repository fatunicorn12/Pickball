# How systems talk to each other

Snapshot as of `a930952` (feat/newton-boss-fight). `Docs/CodingStandards.md` §1 sets the rule:
ScriptableObject channels between systems, C# events inside one, and no singletons or runtime
`Find`. This page records what the code **actually** does, so a new change can follow the pattern
its neighbours use and know which deviations are deliberate.

## The six mechanisms in use

| Mechanism | Where it is used | Notes |
|---|---|---|
| **ScriptableObject event channels** (`Scripts/Events/EventChannels.cs`) | Pickleball core only: ball, player, AI, game state, score UI | Nothing in `Sandbox/` uses them. **The five shipped channel assets don't load** (see below). |
| **Plain C# events on the owning component** | Every Sandbox system | This is the de facto inter-system mechanism in the Sandbox. Listeners hold a serialized reference to the raiser and subscribe in `OnEnable`, unsubscribing in `OnDisable`. |
| **Small read-only interfaces at a boundary** | Combat → movement (`ICombatMovement`, `ICombatImpulses`), facing (`IFacingSource`), anything hittable (`IDamageable`) | See [CombatMovementBoundary.md](CombatMovementBoundary.md). |
| **Polling a read-only property** | `SwordAnimatorDriver` reads `MeleeCombat.IsAttacking` / `IsInHitstop`; `HealthBarView` reads `Health` every frame | `Health.Changed` exists "so UI never has to poll" but has no subscribers. |
| **Static registries** | `GravityWell.Active`, `Apple.Airborne`, `TetherAnchor.All` | Deliberate deviations from §1. Each lets a consumer find short-lived spawned objects without the spawner knowing about it. |
| **Direct calls and runtime lookups** | Pickleball core calls `BallController` directly; boss components `FindFirstObjectByType<ChibiLocomotion>()` once in `Start`; several components fall back to `Camera.main` in `Awake` | Not wired by setup scripts. Not hot-reload safe in the boss (the cached `_player` is lost on a script reload). |

## Events that cross system boundaries (Sandbox)

| Raiser | Event | Listeners |
|---|---|---|
| `Health` (Boss/) | `Hurt(amount, at)`, `Died`, `Knocked`, `Changed` | `Hurt`: `NewtonBoss` (its own), `TrainingDummy`, `DamageNumbers`, `HealthBarView`. `Died`: only `NewtonBoss`, on its own Health. `Knocked`: `TrainingDummy`. `Changed`: no subscriber. **Nothing listens to the player's `Died`**, so player death has no effect yet. |
| `NewtonBarks` | `Spoke(line, seconds)` | `BarkView` |
| `MeleeCombat` | `SwingStarted`, `ChainEnded`, `BladeLiveChanged`, `HitLanded`, `BurstReleased` | `SwordEffects` (all five), `SwordAnimatorDriver` (`SwingStarted`), `CombatXpSource` (`HitLanded`) |
| `SwordInput` | `AttackPressed` | `MeleeCombat.QueueAttack` |
| `PlayerLevel` | `XpGained`, `LeveledUp` | `LevelUnlocks` (`LeveledUp`), `ProgressionToast` (both) |
| `SwordAbilities` / `PlayerAbilities` | `Unlocked` | `ProgressionToast`. `PlayerAbilities.Revoke` raises nothing. |
| `MovementMotor` | `MoveChanged(from, to)`, `TrickPerformed` | `MovementAnimatorDriver` (both), `MovementEffects` (`MoveChanged`). The motor sets `CurrentJump`, `DashDirection` and `TetherAnchor` **before** raising `MoveChanged`, so listeners can read them. |
| `SpacetimeSettings` (asset) | `Changed(in4D)` | `WorldlineRecorder` (for `hideWhileIn4D`) |

In the pickleball core, `InputReader.SwingPerformed` → `PlayerController` is a C# event too.
`BallController.StateChanged` has no subscribers.

Event order is part of the contract where noted. `Health` raises `Hurt` before `Died` on a
killing blow, and the regain credit and damage numbers rely on that.

## A ScriptableObject as a runtime channel

`SpacetimeSettings` is a settings asset that also carries runtime state in `[NonSerialized]`
fields: live recorders, ended histories (capped at 256, oldest dropped), the `In4D` flag and its
`Changed` event, and a single-owner claim (`TryClaim` / `Owner`). It is the one place the Sandbox
uses an asset as the meeting point between components. Its `OnEnable` clears that runtime state
on domain reload. The single-owner rule exists because two `SpacetimeView`s flipping the one
`In4D` flag would orphan each other's beams.

## Pickleball event channels: current state

- The table in `CLAUDE.md` is accurate about **who raises and who listens**. It is not accurate
  that "systems never reference each other directly". `GameStateManager` calls
  `BallController.ResetTo` and `AIController.Serve`; `PlayerController` and `AIController` call
  `BallController.Hit`, `.State` and `.PredictLanding`.
- `VoidEventChannelSO` is defined but unused.
- **The channel assets under `ScriptableObjects/Events/` don't load** (verified in the Editor:
  `AssetDatabase.LoadMainAssetAtPath` returns null for all five). Each has `m_Script: {fileID: 0}`.
  The four concrete channel classes live together in `EventChannels.cs`. Unity can only bind a
  ScriptableObject asset to a class whose file name matches the class name. The fix is one file
  per channel class, then re-running **Tools ▸ Pickleball ▸ Create Assets**.
  `PickleballSceneBuilder.GetOrCreate` currently re-creates an unloadable asset each run.

## Guidance for new code

- **Inside the Sandbox, follow the Sandbox pattern:** C# events on the raiser, and a serialized
  reference wired by a setup menu. A ScriptableObject channel is still the rule in §1 for
  traffic between systems that must not know each other. If you add one, put each channel class
  in its own file.
- **Presentation subscribes; gameplay never calls presentation.** Known exceptions:
  - `FluxionBeam`, `Apple` and `NewtonBoss` drive their own visuals.
  - `TrainingStage` mixes input, simulation and HUD in one component.
- **Prefer an injected reference to a static registry or `Find`.** If you need a registry for
  spawned objects, follow `GravityWell.Active`: remove the entry the moment the object stops
  counting, not at its end-of-frame destroy.
