# Player Movement (Sunshine-style)

Code: `Assets/_Project/Scripts/Sandbox/Movement/` (namespace `Pickleball.Sandbox.Movement`)
Tests: `Assets/_Project/Tests/EditMode/Movement/`
Assets: `Assets/_Project/ScriptableObjects/Movement/`
Setup: **Tools ▸ Pickleball ▸ Setup Movement** (`Scripts/Editor/MovementSetup.cs`)

## 1. Purpose and feel

The chibi student moves like Mario in *Super Mario Sunshine* (GameCube, 2002), slowed down a
little and scaled to a 1.7 m character:

- **Run, then sprint.** Pushing the stick gives instant walking pace, then a run (4.2 m/s). After
  about a second of continuous running he ramps into a **sprint** (7 m/s). There is no button for
  it; Shift / LT skips the build-up.
- **Snap-back redirect.** Reversing the stick hard at speed is a **skid turn**: he plants, swings
  round and pushes off the other way in 0.14 s, keeping 92% of his speed and his sprint.
  Sunshine's own skid throws your speed away. This one keeps it (asked for as the top priority).
  Jumping during the skid gives Sunshine's **side somersault**, a tall jump out the new way.
- **Floaty, high jumps.**
  - Chain: a single (2 m standing), then a double (2.8 m) if he re-jumps within 0.2 s of landing,
    then a **triple** flip (4.5 m) if he is still at sprint speed.
  - Height control: letting go of Jump early cuts the jump short. Holding it rises on light
    gravity and hangs at the apex.
  - Falls come down on heavier gravity, capped at 18 m/s.
- **Dive.** Crouch (Ctrl / B) at running speed, or at any time in the air, dives forward. Landing
  turns into a **belly slide**: holding forward glides, holding back brakes. Jump rolls out onto
  his feet with the slide's speed; crouch dives again.
- **Spin jump.** Rotate the stick (about three-quarters of a circle in half a second), then Jump,
  for a tall corkscrew that comes down slowly. Spinning the stick mid-jump turns any jump into the
  slow spin descent.
- **The existing abilities are unchanged:**
  - feather mid-air jump
  - protractor 45° dash
  - orbital tether
  - skateboard
  - Newton's Gravity Anchor
  - crouch
  - lock-on facing
  - `PlayerAbilities` gating

### Where the numbers come from

They come from the doldecomp `sms` decompilation plus the shipped `params.szs` values. A
subagent's research note has the per-value sources and confidence:
`side-session-notes/…/sunshine-movement-research.md`. Mario's 160 game units are mapped to our
1.7 m. Two deliberate departures, both asked for:

1. Time runs about 1.46× slower than Sunshine. Floatier: a standing jump is about 1.05 s of
   airtime against Sunshine's 0.7 s.
2. Heights are about 0.8× Sunshine's, so the scanned, human-scale academy still reads. Sunshine's
   triple jump is 5 body heights.

| Move | Sunshine (at 1.7 m scale) | Default here |
|---|---|---|
| Top run | 10.2 m/s, no sprint (≈7.0 at our time scale) | run 4.2, sprint 7.0 after 1 s |
| Start | instant kick to 2.55 m/s | kick to 1.5 m/s |
| Stick response | quadratic, full at 62% tilt | exponent 1.6, full at 75% |
| Turn rate | 989°/s (678 at our time scale), any speed | 900°/s at run → 680°/s at sprint |
| Skid trigger | stick > 100° off facing above 3.2 m/s | > 120° above 2.6 m/s |
| Skid | brake ~0.07 s, exit at walking pace | 0.14 s total, exits at 92% of entry speed |
| Single jump | 2.40 m standing, 3.39 m at top speed | 2.0 m → 2.76 m at sprint |
| Double jump | 3.6 m standing, 4.86 m at top speed | 2.8 m → 3.86 m at sprint |
| Triple jump | 8.56 m, needs 78% of top speed | 4.5 m, needs 5 m/s |
| Chain window | 0.133 s | 0.2 s |
| Variable height | release: vy decays to ≤ 6.4 m/s | release: vy capped to 5 m/s |
| Gravity | 38.25 m/s², one value | rise 18 / apex 10 / fall 26 m/s² |
| Terminal fall | 23.9 m/s (16.4 at our time scale) | 18 m/s |
| Side somersault | 6.5 m | 4.2 m |
| Dive (ground) | speed + 4.8, cap 15.3 m/s, vy 6.4 | speed + 3.3, cap 10, vy 4.4 |
| Belly slide | ×0.97/tick neutral, 0.99 forward, 0.95 back | e^(−2.5t) / e^(−0.8t) / e^(−4.2t) |
| Spin input | 4 quadrants within 0.33 s | 270° within 0.5 s |
| Spin jump | 6.6 m, descent at 35% gravity | 4 m, descent 8 m/s² capped at 5 m/s |
| Coyote / buffer | none (stays grounded over 1 m drops) | 0.12 s / 0.12 s |

## 2. Architecture

```
PlayerMovementInput ──Read()──▶ MovementMotor ──MoveChanged / TrickPerformed──▶ MovementAnimatorDriver ─▶ Animator, model, foot IK
  (input only)        intent     (simulation)                                 └─▶ MovementEffects ─▶ puff, dash VFX, rope, board
                                    ▲    │
                ChibiLocomotion ────┘    └── IMovementState (read-only) ──▶ combat, HUD, audio…
       (facade + lock-on / combat facing via IFacingSource)
```

| Component | File | One job |
|---|---|---|
| `PlayerMovementInput` | `PlayerMovementInput.cs` | Builds the InputActions (moved from ChibiLocomotion, bindings unchanged) and returns a camera-relative `MovementIntent` per frame. |
| `MovementMotor` | `MovementMotor.cs` | Owns the CharacterController and the state machine, and integrates the body: speed along a heading, vertical velocity, momentum, force fields. Raises `MoveChanged`. Never touches Animator, VFX or UI. |
| `MoveState` + 14 states | `States/*.cs` | One class per `MoveKind`. `Next()` decides, `Tick()` integrates. |
| `MovementConfig` | `MovementConfig.cs` | Every gameplay tunable, read live every frame. |
| `MovementAnimatorDriver` | `Presentation/MovementAnimatorDriver.cs` | Listens to `MoveChanged` and sets the Animator: triggers, continuous params, and the int `Move` / `JumpKind`. Owns the model offset, ride lift and placeholder poses. |
| `MovementEffects` | `Presentation/MovementEffects.cs` | Listens to `MoveChanged`: the feather puff, `DashEffects.Play`, `TetherSystem.Attach/Detach`, `SkateboardMount.SetRidden`. |
| `MovementAnimationConfig` | `Presentation/MovementAnimationConfig.cs` | Presentation tunables (strides, clip timing, poses). |
| `ChibiLocomotion` | `Sandbox/ChibiLocomotion.cs` | Facade with the stable API, plus player glue: lock-on and attack facing, head look-at. |
| `Logic/*` | `Logic/*.cs` | Pure, tested rules: `JumpMath`, `GravityProfile`, `SprintRamp`, `SkidRule`, `JumpChain`, `SpinGesture`, `ForceFieldChannel`. |

### The frame

`MovementMotor.Update` runs in this order:

1. Read the intent and run the ground check. Rising is never grounded, which replaced the old
   0.15 s post-jump latch.
2. Drain the force-field accumulator, tick the movement lock, the Gravity Anchor and the spin
   gesture.
3. Up to four hops of `Interrupt() ?? current.Next()`, switching states as needed.
   - Interrupts, in the old order: mount, tether latch, dash. Each state can refuse them
     (`AllowsDash`, `AllowsTether`, `AllowsMount`, `AllowsAnchor`).
4. `current.Tick(dt)`, then apply the capsule height the move asked for. Growing the capsule is
   refused under a ceiling.

Because deciding and integrating are separate, a state that changes on a press (Ground → Jump)
has the new state move the character on that same frame. Each switch runs `Exit` and `Enter`,
then raises `MoveChanged(from, to)`, so listeners already see the new move's data (jump kind,
dash direction, tether anchor).

### Moves (`MoveKind`, int values are the Animator's `Move` param — append only)

`Ground 0, Sprint 1, RunStop 2, Crouch 3, SkidTurn 4, Jump 5, Fall 6, AirJump 7, Dive 8,
BellySlide 9, SpinJump 10, Dash 11, Tether 12, Skate 13`.

`JumpKind` (the Animator's `JumpKind` param): `None 0, Single 1, Double 2, Triple 3, SideFlip 4,
Rollout 5`.

### Events and who listens

| Event | Raised by | Listened to by |
|---|---|---|
| `IMovementState.MoveChanged(from, to)` | `MovementMotor.Switch` | `MovementAnimatorDriver`, `MovementEffects`; later the sword abilities (dive and spin slashes) |
| `MovementMotor.TrickPerformed` | Ground / Sprint / RunStop on F / Y | `MovementAnimatorDriver` (Flip trigger) |

Cross-system channels (`Scripts/Events/EventChannels.cs`) are not used yet. Movement events stay
inside the player.

### The stable API (`ChibiLocomotion`, forwarding to the motor)

All of these keep their exact contracts:
- `IsBusy` (MeleeCombat)
- `IsGrounded` (MeleeCombat)
- `Velocity` and `AppliedFieldVelocity` (FluxionBeam; the `Velocity − AppliedFieldVelocity`
  identity is tested)
- `AddMomentum` (Apple knockback)
- `AddAcceleration` and `LockMovement` (GravityWell)
- `IsMovementLocked`, `IsAnchored`

Two vertical levers were added for combat (sword abilities):
- `SetVerticalVelocity(m/s)`: set once per event. Upward speed lifts him off the ground.
- `SuspendGravity(seconds)`: an air hang, refreshed rather than accumulated.

The serialized field `lockOn` keeps its name, because `LockOnSetup` writes it by string. New code
should hold an `IMovementState` (`ChibiLocomotion.Movement`) instead.

### Animator

`ChibiSandboxSetup.BuildController` builds the legacy graph. It is unchanged apart from the speed
values, and is still driven by the triggers Jump, Stop, Flip and Dash, with the same expiry
hygiene as before. `MovementSetup.AddMovementStates` then adds:

- `SkidTurn`, `SpinJump`, `Dive` and `BellySlide`: entered from Any State on `Move == kind`, left
  only when `Move` changes, so nothing can latch.
- `JumpDouble`, `TripleJump`, `SideFlip` and `Rollout`: entered on the `JumpVariant` trigger plus
  `JumpKind`. A trigger is needed because `Move == Jump` holds for the whole arc and would
  re-enter the state.
- The flips (triple, side flip, rollout, feather) are time-scaled to the jump's real airtime
  through `FlipSpeed`.
- Foot IK is off on every new state. The driver also sets `ChibiFootIK.Suppressed` during the
  belly slide.

**Placeholder clips** (until the Mixamo set in `Pickball-mixamo-inbox` is imported):

| State | Placeholder |
|---|---|
| SkidTurn | Run To Stop's braking plant, played at 2× |
| JumpDouble | Running Jump |
| TripleJump | the trimmed front flip |
| SideFlip | the trimmed front flip played backwards |
| Rollout | the trimmed front flip |
| SpinJump | Falling Idle, with the model spun about Y in code |
| Dive | the airborne stretch of Running Jump, with the model pitched 60° |
| BellySlide | Falling Idle, pitched 82° and lowered 0.4 m |

`Sprint Turn.fbx` was evaluated for the redirect and rejected. It is a 1.0 s curving turn of about
60°, not a 180° snap-back.

## 3. How to tune

Enter Play mode and edit `ScriptableObjects/Movement/MovementConfig.asset`. Every field has a
tooltip with its unit and its Sunshine value.

- **Sprint:** `sprintSpeed`, `sprintDelay`, `sprintRampTime`, `sprintGrace`, `sprintTurnRate`.
- **Redirect:** `skidDuration` (0 = instant flip), `skidSpeedKeep`, `skidPlantFraction`,
  `skidSlide`, `skidMinAngle`, `skidMinSpeed`.
- **Jump feel:**
  - Heights: `singleJumpHeight`, `doubleJumpHeight`, `tripleJumpHeight` (metres; launch speeds
    are derived).
  - Gravity: `riseGravity`, `apexGravity` and `apexBand` (the hang), `fallGravity`,
    `maxFallSpeed`.
  - Release: `jumpReleaseSpeed` (how short a tap is), `minJumpHoldTime`.
  - Chain: `chainWindow`, `tripleMinSpeed`.
- **Air control:** `airAcceleration` (vector steering: pulling back slows him), `airDrag`.
- **Dive and slide:** `diveSpeedBoost`, `diveMaxSpeed`, `diveHopSpeed`, and `bellySlideDecay*`
  (exponential, 1/s).
- **Spin:** `spinRequiredDegrees`, `spinWindow`, `spinJumpHeight`, `spinFallGravity`,
  `spinFallSpeed`, `spinInAir`.

Presentation lives on `MovementAnimationConfig.asset`:
- **Model placement:** `modelYOffset`, live in the editor.
- **Measured values:** the strides and `skateRideHeight` are re-measured by every setup run.
- **Clip timing:** `flipAirborneSeconds`, `triggerLifetime`, `airborneLatch`.
- **Placeholder poses:** `divePitch`, `bellySlidePitch`, `bellySlideOffset`, `spinYawSpeed`, and
  `proceduralPoses` to turn them off once real clips land.

## 4. How to extend

### Add a move in 6 steps

1. Append a value to `MoveKind`. Append only: the int is an Animator parameter value.
2. Add `States/YourMove.cs : MoveState`. Implement `Kind`, `Next` (decisions only) and `Tick`
   (integrate with the motor's helpers: `SteerHeading` / `SteerAir` / `SteerAtRate`,
   `GroundVertical` / `AirVertical`, `MoveStandard`). Override `Enter`, `Exit` and the `Allows*`
   flags as needed.
3. Add it to the `_states` array in `MovementMotor.EnsureRuntime`, at the index equal to its enum
   value. An assert checks this.
4. Return its kind from the `Next()` of every move it can be entered from.
5. Put its tunables on `MovementConfig`, each with a `[Tooltip]` that states the unit.
6. Presentation: add a state in `MovementSetup.AddMovementStates` (`OnMove` + `Leave`), and a
   clip role in `ChibiSandboxSetup.ClipDefs` if it has its own clip. Then re-run **Setup
   Movement**.

### React to movement from another system

Hold an `IMovementState` (e.g. `GetComponent<ChibiLocomotion>().Movement`), subscribe to
`MoveChanged` in `OnEnable` and unsubscribe in `OnDisable`. For example, a dive slash checks
`to == MoveKind.Dive`. Never call back into the motor except through the push API.

### Replace a placeholder clip

Add a row to `ChibiSandboxSetup.ClipDefs` with the role name `AddMovementStates` looks up. The
roles are `skidTurn`, `jumpDouble`, `tripleJump`, `sideFlip`, `rollout`, `spinJump`, `dive` and
`bellySlide`. Put the FBX in `Animation/Chibi/Movement/` and re-run Setup Movement. If the clip
carries its own pose, turn off `proceduralPoses`. For a new flip clip, set `flipAirborneSeconds`
to its airborne length.

## 5. Known limitations and open questions

- **Placeholder animation.** Dive, slide and spin are tilted or spun stock clips. The 14 Mixamo
  clips are downloaded and awaiting import recipes (see the side-session notes).
- **No slope physics.** The belly slide does not accelerate downhill, and running uphill is not
  slower. Sunshine does both.
- **Combat facing still reads `MeleeCombat.IsAttacking`.** `ChibiLocomotion` does this as glue.
  The motor itself doesn't know combat exists, but the player assembly does until combat pushes
  its own facing request.
- **Tether from the skateboard is now refused.** It used to latch, and both modes then ran at
  once.
- **Air control is vector steering.** It approximates Sunshine's "no turning in the air, plus a
  strafe". Air turns are wider than before.
- **Not yet in:** backflip (crouch + jump), wall kick, ground pound, and staying grounded over
  small drops (Sunshine keeps Mario grounded over 1 m; here a step down is a short Fall).
- **Gamepad conflict, pre-existing:** buttonWest is both skateboard Mount and MeleeCombat's
  attack.
