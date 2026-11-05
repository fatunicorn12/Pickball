# Runtime conventions: timing, ordering, reload safety, test seams

Snapshot as of `a930952`, updated for the QA fixes through `f876b63`. These cross-cutting choices aren't owned by any one system doc.
`CodingStandards.md` states the rules. This page records how the code meets them and where it
doesn't yet.

## Simulation timing

- **The Sandbox simulates in `Update` with a variable `dt`.** This covers movement, combat, the
  boss, Spacetime and lock-on. Nothing in the Sandbox uses `FixedUpdate`.
- **Only the pickleball ball uses `FixedUpdate`,** for its manual gravity. `fixedDeltaTime` is 0.02.
- **Frame-rate independence is a tested property.** The existing EditMode tests run maths at
  30, 60 and 144 fps via `[TestCase]` (for example in `JumpMathTests` and
  `ForceFieldChannelTests`). Smoothing uses `1 - Mathf.Exp(-rate * dt)`.
- **Fixed-length actions consume exactly their time left.** `Logic/FixedDuration.Consume` steps a
  timed move by `min(dt, timeLeft)`, so the dash covers exactly speed × duration at any frame rate
  (`f876b63`). Use it for any new timed move. The sword sweep likewise tests every frame whose
  interval touched the active window (`BladeSweep`, `6991c60`).
- **Known frame-rate dependencies.** Each one has a test proposed in
  `Docs/Testing/QA-Assessment.md`.
  - **Tap jump:** the minimum tap-jump height varies with frame rate (about 0.62–0.75 m).
  - **Tether anchor glow:** `TetherAnchor.SetState` is called twice a frame on the targeted
    anchor, so its blend settles at a frame-rate-dependent value.

## Execution order contract

| Order | Component | Why |
|---|---|---|
| −100 | `SwingTwist` | Applies the baked twist in `LateUpdate` before anything reads the arm pose |
| −50 | `WeaponClearance` | Corrects the arm after twist, before trails and cloth read the blade |
| default | gameplay, `SandboxOrbitCamera`, cloth, trails | |
| +100 | `MovementAnimatorDriver` | Reads the motor after it has simulated this frame |
| 1000 | `LockOnReticle` | Draws after the camera has moved, so the ring doesn't lag |

`FaceCamera` copies the camera rotation in `LateUpdate` at the default order, the same as the
camera, so billboards can lag a frame. Give it a late order like the reticle's if that shows.

## Hot-reload safety (CodingStandards §4)

Several agents save `.cs` files while the shared Editor is open, so a domain reload can happen in
Play mode. After a reload, non-serialized fields are reset, and `Awake`/`Start` do **not** run
again; only `OnEnable` does.

**The pattern that works:** rebuild or re-fetch runtime objects in `OnEnable`, or lazily on first
use.
- The combat presentation components do this (commit `8a90edd`).
- `LockOnTargeting` self-heals its reticle lazily.
- `SpacetimeSettings.OnEnable` resets its runtime channel.
- `SwordAbilities` and `PlayerLevel` initialise lazily.
- `MovementMotor` resumes an interrupted skid, jump, dash or tether as Fall or Ground, properly
  entered, and `MovementEffects` resyncs the rope and board on enable (`f876b63`).

**Known violators.** Each one breaks after a reload in Play mode:

| Component | What is lost |
|---|---|
| `NewtonBoss` | `Health` (set in `Awake`) → NREs; the cached `_player` (also in `GravityWell`, `FluxionBeam`, `AppleBarrage`, `InverseSquareVolley`) → attacks stop |
| `InverseSquareVolley` | live orbs (a readonly list of a non-serializable struct) |
| `SpringCloth` | `_chains` built in `Awake` → the cloth goes rigid |
| `SwingTwist` | its lookup dictionary → the twist stops |
| `TetherSystem` | `_reticle` built in `Awake` |
| `DashEffects` | ghosts that are mid-fade are orphaned |
| `PlayerAbilities` | the movement unlocks (a readonly `HashSet`) |
| `ProgressionToast` | its queue isn't serializable but its counters are → blank text |

## Test seams the code already offers

Before you write a test, check which of these seams the class has. When you add logic, give it
one.

- **Pure static or plain C# classes** with no `MonoBehaviour`:
  - Movement: `JumpMath`, `GravityProfile`, `SprintRamp`, `SkidRule`, `JumpChain`, `SpinGesture`,
    `ForceFieldChannel`, `FixedDuration`
  - Combat: `ComboResolver`, `ComboProgress`, `SwingTimeline`, `AreaBurst`, `BladeSweep`,
    `GroundContact`, `ExperienceCurve`, `LevelUnlockRules`, `XpLedger`
  - Lock-on: `LockOnSteering`, `LockOnView`, `LockOnAim`
  - `ClearanceSolver`, `WorldlineHistory`, `WorldlineMesh`, `InverseSquareVolley.DamageAt`,
    `NewtonBoss.RangeMultiplier`

  Prefer this shape for anything new (CodingStandards §6).
- **Explicit time injection on a component:** `SpacetimeView.Tick(pressed, now, dt)` and
  `WorldlineRecorder.Sample(point, now)` are public so tests can drive time. This is the model
  for testing time-based components in EditMode, where `Time.deltaTime` is meaningless.
- **Lazy initialisation:** `SwordAbilities` and `PlayerLevel` initialise on first use, so EditMode
  tests work without `OnEnable`, which `AddComponent` doesn't call in edit mode.
- **Public simulation steps:** `SpringCloth.Simulate(dt)` and `Rebind()` let tests drive the
  cloth solver frame by frame (see `Tests/EditMode/Cloth/SpringClothTests`).
- **Reflection helpers** in `Tests/EditMode/BossTestFixture.cs`:
  - `SetTunable` writes a private serialized float through `SerializedObject` and fails loudly
    if the field was renamed.
  - `Tick` invokes a private `Update`. It uses the Editor's `Time.deltaTime`, so it can't test
    timing.
- **`internal` members.** `MovementMotor` exposes its state API as `internal`, but the runtime
  assembly has no `InternalsVisibleTo`, so tests can't reach it yet.

## Allocation and per-frame cost (CodingStandards §3)

The NonAlloc pattern with preallocated buffers is used in `MeleeCombat`, `ChibiFootIK` and
`MovementMotor` (ground check and stand-up test, since `f876b63`).

Known per-frame allocations or scene searches:
- **`LockOnTargeting`:** `FindObjectsByType<Health>` every frame while unlocked with the hint on.
  Line of sight and the aim measure now use buffers and don't allocate, but measuring walks each
  candidate's colliders every call.
- **`DashEffects`:** a new GameObject, Mesh and Material per ghost, with no pool.

## Async

- **UniTask is used only in the pickleball core** (`AIController`, `GameStateManager`). The Sandbox
  has no async code.
- Cancellation tokens come from `GetCancellationTokenOnDestroy()`, so a *disabled* component keeps
  running its async sequence.
- No coroutines anywhere (the CLAUDE.md rule holds). In PlayMode tests, use
  `UniTask.ToCoroutine(async () => …)` inside `[UnityTest]` to stay within it.

## Unity null semantics

The `?.` and `??` operators bypass Unity's overloaded `== null` on destroyed or "fake null"
objects. Several places use them on `UnityEngine.Object`:
- `BallController`, `GameStateManager`, `PlayerController` and `AIController`, where wiring errors
  then fail silently.
- The setup scripts' `GetComponent<T>() ?? AddComponent<T>()`, still in `ChibiSandboxSetup` and
  `NewtonBossSetup`. `WeaponSetup` switched to `TryGetComponent` and `SwingTwistSetup` to an
  explicit `== null` check.

Use an explicit `!= null` check on Unity objects.
