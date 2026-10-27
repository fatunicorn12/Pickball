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

### Controls

Bound in `PlayerMovementInput`. Moves marked * are gated by `PlayerAbilities`.

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move | WASD / arrows | Left stick |
| Jump (chain, side flip, rollout; spin jump after rotating the stick) | Space | A (South) |
| Sprint now (skips the 1 s build-up) | Left Shift | LT |
| Crouch; dive at speed or in the air | Ctrl | B (East) |
| Front-flip trick | F | Y (North) |
| Skateboard mount / dismount * | R | **D-pad Right** (moved off X/West, which is the sword attack) |
| Dash * | E | RB |
| Tether * | Q / right mouse | LB |
| Gravity Anchor * | G | R3 |
| *Not movement's:* sword attack | left mouse | X (West) |
| *Not movement's:* lock-on | Tab / middle mouse | RT, plus a right-stick flick to switch |
| *Not movement's:* camera | mouse | right stick; d-pad Up/Down zoom |

D-pad Left and View are still free.

### Where the numbers come from

The primary source is the **doldecomp/sms** decompilation, read at commit `912aea9` (main on
2026-10-26). Paths below are relative to that repo. It gives two kinds of value:

- **Formulas and literals** in Mario's move code (`src/Player/Mario*.cpp`).
- **Code defaults** for every tunable, as `PARAM_INIT` entries in `src/Player/MarioInit.cpp`.

**The shipped game overrides those defaults.** Each parameter block ends with
`TParams::load("/Mario/Jump.prm")`, or `Run.prm`, `Mario.prm` and so on, which reads them from
the disc's parameter archive. Where a shipped value differs from the code default, the "Shipped"
column below gives the value from ProjectShine's decode of that archive
(github.com/JasonAbsolute/ProjectShine, `assets/Sms/SmsPhysics.cs`). That is one source, **not
yet verified against a real disc**. Those cells are marked †, and the user's own copy can confirm
them. Only numbers are read from it; no game files go in the repo.

#### Unit conversion

Mario is 160 units tall, mapped to our 1.7 m (k = 0.010625 m per unit). Sunshine renders at 30 fps
but simulates four 120 Hz ticks per frame (`src/System/MarDirectorDirect.cpp`). So:

- velocities are units per 30 fps frame: × 0.31875 → m/s;
- per-tick accelerations: × 38.25 → m/s²;
- angles are 16-bit BAM per tick: × 0.659 → °/s.

"At 1.7 m" is Sunshine at true speed on our character.

#### Two deliberate departures, both asked for

- **Time runs ×1/1.458** (gravity 38.25 → 18 m/s²): floatier and more hang. So speeds are ÷1.458,
  accelerations ÷2.125, durations ×1.458.
- **Heights are tempered to about 0.8×** so the scanned, human-scale academy still reads.

#### Fidelity key

- **exact**: same rule and value.
- **time-scaled**: the Sunshine value at our time scale.
- **tempered**: deliberately lower.
- **changed**: a different rule, on purpose.

#### Run

| Value | doldecomp/sms source | Code default | Shipped | Sunshine at 1.7 m | Ours (`MovementConfig`) | Fidelity |
|---|---|---|---|---|---|---|
| Top speed | `TRunParams::mMaxSpeed`, MarioInit.cpp:595; clamp MarioRun.cpp:471–473 | 32 u/f | 32 | 10.2 m/s | `sprintSpeed` 7.0 (`runSpeed` 4.2 before the sprint) | time-scaled; the auto-sprint is ours |
| Acceleration | `+= AddBase − v·AddVelDiv` per tick, MarioRun.cpp:481–482; AddBase 1.1 MarioInit.cpp:597, AddVelDiv 1/43 MarioInit.cpp:598 | 1.1, 1/43 | same | 42 − 2.79·v m/s² (0 → top in 0.40 s) | `runAcceleration` 18, `sprintAcceleration` 12 (constant) | approximated |
| Start kick | `fwd = min(mag, 8)`, MarioMove.cpp:685–689 | 8 u/f | — | 2.55 m/s | `runStartSpeed` 1.5 | time-scaled (1.75), rounded down |
| Start-to-walk tilt | `mStartToWalkLevel`, MarioInit.cpp:751, used MarioWait.cpp:103 | 15 (≈48% tilt) | unknown | — | none; moves at any tilt | changed |
| Stick curve | `mag = 32·(dist/64)²` after `LengthMult^LengthMultTimes`, MarioMove.cpp:1380–1385; MarioInit.cpp:753–754 | 0.935^10 | 0.78² † | quadratic, full at 62% tilt | `stickExponent` 1.6, `stickFullTilt` 0.75 | approximated |
| Stop on release | `FConverge(v, 0, 1, 1)` per tick, MarioRun.cpp:1009 | 1 u/f/tick | — | 38.25 m/s² (0.27 s) | `stopDeceleration` 12, `runDeceleration` 22 | time-scaled is 18; stop softened for the Run To Stop clip |
| Turn rate | `rotSp = Min + v/32·(Max − Min)` BAM/tick, MarioRun.cpp:494–502; `mRunningRotSpMin/Max` MarioInit.cpp:471–472 | 0x200 / 0x400 | 1500 / 1500 † | 989°/s at any speed | `runTurnRate` 900 → `sprintTurnRate` 680 °/s | time-scaled at sprint |

#### Skid turn

| Value | doldecomp/sms source | Code default | Shipped | Sunshine at 1.7 m | Ours (`MovementConfig`) | Fidelity |
|---|---|---|---|---|---|---|
| Skid angle | `abs(yawDiff) > 0x471C`, MarioRun.cpp:94 | 100° | — | 100° | `skidMinAngle` 120° | changed (diagonals steer) |
| Skid minimum speed | `fwd >= mTurnNeedSp`, MarioRun.cpp:682; MarioInit.cpp:614 | 10 u/f | — | 3.19 m/s | `skidMinSpeed` 2.6 | time-scaled is 2.19 |
| Skid brake | `FConverge(v, 0, 4, 4)` per tick, MarioRun.cpp:818 | 4 u/f/tick | — | 0.067 s from top speed | `skidDuration` 0.14 s, 40% planted | time-scaled is 0.10 s |
| Skid exit speed | `setPlayerVelocity(8)`, MarioRun.cpp:826 | 8 u/f (25% of top) | — | 2.55 m/s | `skidSpeedKeep` 0.92 × entry | **changed** (keeps momentum, as asked) |
| Side somersault | `TurnJumpForce`, launched MarioMove.cpp:529–531 (fwd = 8, new facing); MarioInit.cpp:549 | 62 | 70 † | 22.3 m/s, 6.5 m apex | `sideFlipHeight` 4.2 m, `sideFlipSpeed` 2.5 m/s | speed exact; height tempered |

#### Jumps

| Value | doldecomp/sms source | Code default | Shipped | Sunshine at 1.7 m | Ours (`MovementConfig`) | Fidelity |
|---|---|---|---|---|---|---|
| Single jump | `setPlayerJumpSpeed(0.25, 42)`, MarioMove.cpp:442 | 42 + 0.25·v | — | 13.4 m/s: 2.34 m standing, 3.32 m at top speed | `singleJumpHeight` 2.0 m (+38% at sprint) | tempered (0.85×) |
| Single jump: takeoff speed | `mForwardVel *= 0.8`, MarioMove.cpp:443 | 0.8 | — | 0.8 | `jumpTakeoffSpeedKeep` 0.8 | exact |
| Double jump | `SecJumpForce` / `SecJumpSpeedMult` / `SecJumpXZMult`, MarioMove.cpp:470–472; MarioInit.cpp:570–572 | 52, 0, 0.8 | 52, 0.25 † | 3.59 m standing, 4.78 m at top speed | `doubleJumpHeight` 2.8 m (+38% at sprint) | tempered (0.8×) |
| Triple jump | `UltraJumpForce` / `UltraJumpSpeedMult` / `UltraJumpXZMult`, MarioMove.cpp:477–479; MarioInit.cpp:574–576 | 70, 0.25, 0.8 | 75, 0.15, 1.0 † | 25.4 m/s: 8.46 m (5 body heights), keeps 100% of speed | `tripleJumpHeight` 4.5 m, keeps 100% | tempered (0.53×); speed keep exact |
| Double jump minimum speed | `mSecJumpEnableSp`, MarioInit.cpp:569 | 20 u/f | 0 † | any speed | any speed | exact |
| Triple jump minimum speed | `mTriJumpEnableSp`, MarioInit.cpp:573 | 20 u/f | 25 † | 7.97 m/s (78% of top) | `tripleMinSpeed` 5.0 m/s (71% of sprint) | time-scaled (5.47), rounded |
| Chain window | landing-state timer `mMaxTimer`, checked MarioRun.cpp:1541 | 16 ticks | — | 0.133 s | `chainWindow` 0.2 s | time-scaled (0.194) |
| Gravity | `mVel.y −= mGravity` per tick, MarioPhysics.cpp:478; `TJumpParams::mGravity` MarioInit.cpp:543 | 0.8 | 1.0 † | 38.25 m/s², one value | `riseGravity` 18, `apexGravity` 10, `fallGravity` 26 | time-scaled (18), plus an asked-for apex hang and heavier fall |
| Terminal fall speed | clamp at −75, MarioPhysics.cpp:468–469, 484–485 | 75 u/f | — | 23.9 m/s | `maxFallSpeed` 18 | time-scaled is 16.4 |
| Variable height | `if (!A && vy > 20) vy *= 0.75` instead of gravity, MarioPhysics.cpp:458, 472 | 20 u/f, ×0.75 | — | cut to ≤ 6.4 m/s; minimum tap 0.70 m | `jumpReleaseSpeed` 5 (instant cap) | time-scaled (4.4); tap measured 0.69 m |
| Air acceleration | `fwd += mag·cos(Δ)·JumpAccelControl`, MarioJump.cpp:107; MarioInit.cpp:547 | 0.5 | 0.008 † | 9.8 m/s² forward/back | `airAcceleration` 6 (vector) | ≈ time-scaled (4.6) + strafe |
| Air strafe | `side = mag·sin(Δ)·JumpSlideControl`, MarioJump.cpp:108; MarioInit.cpp:548 | 0.5 | 0.3 † | ≤ 3.06 m/s that tick | folded into the vector steer | changed |
| Air drag / caps | `×JumpSpeedBrake` MarioJump.cpp:81 (0.999, MarioInit.cpp:546); >32 −0.2, <−16 +0.4 MarioJump.cpp:112–115 | — | — | τ 8.3 s; soft cap 10.2 m/s | `airDrag` 1 m/s²; cap at takeoff speed | changed |

#### Dive and belly slide

| Value | doldecomp/sms source | Code default | Shipped | Sunshine at 1.7 m | Ours (`MovementConfig`) | Fidelity |
|---|---|---|---|---|---|---|
| Ground dive minimum speed | `fwd > mDoJumpCatchSp`, MarioRun.cpp:38; MarioInit.cpp:613 | 15 u/f | — | 4.78 m/s | `diveMinSpeed` 3.2 | time-scaled (3.28) |
| Ground dive hop | `mVel.y = 20`, MarioRun.cpp:40 | 20 u/f | — | 6.38 m/s, 0.53 m | `diveHopSpeed` 4.4 | time-scaled (4.37) |
| Dive speed | `min(15 + v, 48)`, MarioMove.cpp:554–556 | +15, cap 48 | — | +4.8, cap 15.3 m/s | `diveSpeedBoost` 3.3, `diveMaxSpeed` 10 | time-scaled (3.28 / 10.5) |
| Air dive speed | `setPlayerVelocity(JumpJumpCatchSp)`, MarioJump.cpp:298 (then capped at 48); MarioInit.cpp:586 | 50 | 80 † | 15.3 m/s, vertical speed kept | `diveAirSpeed` 9, vertical speed kept | time-scaled (10.5), rounded |
| Slide friction | `fwd ×= SlipFriction + 0.02·(mag/32)·cos` per tick, MarioRun.cpp:410; MarioInit.cpp:858 | 0.9 | 0.97 † | e^(−3.66t) neutral / e^(−1.21t) forward / e^(−6.16t) back | `bellySlideDecay*` 2.5 / 0.8 / 4.2 | time-scaled |
| Slide stop | `mSlideStopCatch`, MarioInit.cpp:864 | 15 u/f | 1 † | 0.32 m/s | `bellySlideStopSpeed` 0.3 | exact (shipped) |
| Rollout trigger | A with `fwd > ClashSpeed`, MarioRun.cpp:1192; `TDeParams::mClashSpeed` MarioInit.cpp:507 | 50 u/f | 40 † | > 12.75 m/s; slower gives a small hop-up | any speed | changed |
| Rollout launch | `RotBroadJumpForce` / `ForceY`, MarioMove.cpp:574–575; MarioInit.cpp:566–567 | 60, 20 | 55, 38 † | 17.5 m/s forward, 1.92 m | `rolloutHeight` 1.3 m, `rolloutSpeedKeep` 1.0 | tempered |

#### Spin jump

| Value | doldecomp/sms source | Code default | Shipped | Sunshine at 1.7 m | Ours (`MovementConfig`) | Fidelity |
|---|---|---|---|---|---|---|
| Spin input | yaw history of `mStickRotateTime` ticks, all 4 quadrants, MarioMove.cpp:1512–1595 (buffer 1567); MarioInit.cpp:752 | 24 ticks | 40 † | 4 quadrants within 0.333 s | `spinRequiredDegrees` 270 within `spinWindow` 0.5 s | time-scaled (0.49 s) |
| Spin launch | `setPlayerJumpSpeed(0.25, RotateJumpForceY)`, fwd ×0.8, MarioMove.cpp:490–491; MarioInit.cpp:556 | 62 | 70 † | 22.3 m/s, 6.5 m | `spinJumpHeight` 4.0 m, `spinHorizontalKeep` 0.6 | tempered |
| Spin descent | `vy −= SpinJumpGravity` while falling, MarioPhysics.cpp:476; MarioInit.cpp:544 | 0.6 | 0.35 † | 13.4 m/s² (35% of gravity) | `spinFallGravity` 8, `spinFallSpeed` 5 | time-scaled (6.3) plus a cap |
| Spin from the air | `considerJumpRotate`, MarioJump.cpp:273–289 | — | — | converts a jump into the spin descent, no lift | `spinInAir` | exact |

#### Not in Sunshine at all

These are ours: coyote time and the jump buffer (Sunshine instead keeps Mario grounded over drops
of up to 100 units), the automatic sprint, the apex hang, and the skid keeping its momentum.

### Measured in Play mode (2026-10-26)

Measured in a dedicated Editor on the branch, with scripted input on a flat test slab (the
harness is described in §5):

| Move | Measured |
|---|---|
| Sprint | Starts 1.0 s after W; tops out at 7.00 m/s; RunStop from 5.9 m/s comes to rest in 0.49 s |
| Skid turn at 7 m/s | 0.149 s plant + turn; exits at 6.44 m/s (92%), still Sprint |
| Single jump, held | 1.97 m apex (authored 2.0), 1.05 s airtime |
| Single jump, tap | 0.69 m apex, 0.45 s (Sunshine's minimum tap: 0.70 m) |
| Running single jump from sprint | 2.72 m, 1.21 s; lands still at 7 m/s and sprinting |
| Chain jumps (re-jump on landing) | Single 2.73 m → Double 3.76 m → Triple 4.46 m; 1.07 / 1.20 / 1.49 s |
| Side somersault out of the skid | 4.16 m, 1.45 s |
| Dive from sprint | 10.0 m/s, 0.42 m hop; slide holding forward lasted 3.8 s |
| Rollout from the slide | 1.25 m at 7.4 m/s; lands sprinting |
| Spin jump | 3.95 m, 1.81 s |
| Feather mid-air jump, dash, crouch, skateboard (6.2 m/s), Gravity Anchor rise (4.5 m/s), tether latch → swing → jump release | all work |
| Lock-on facing via the facade | Locked and standing, he squares back up to Newton from 172° off within about 0.2 s |

The Animator followed every move into its own state (SkidTurn, JumpDouble, TripleJump, SideFlip,
Dive, BellySlide, Rollout, SpinJump) and left it cleanly.

Bugs the play-test found and this branch fixes:
- **Ground probe.** It reached 0.8 m below the feet, so jumps were "landed" 0.8 m up and sank the
  rest of the way at 2 m/s. This was inherited from ChibiLocomotion.
- **Capsule too short.** A capsule shorter than its own diameter made the controller pop him out
  of the floor, giving a 41 m/s Velocity spike.
- **Standing turn-around.** Turning round from a standstill came out as a skid.
- **Slide stand-up.** Standing up from a stopped belly slide reported "Sprint".

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

`SetCombatFacingSource(IFacingSource)` lets combat supply where a swing must face; it takes
priority over the lock-on square-up. Until combat registers one, the facade falls back to reading
`MeleeCombat.IsAttacking`.

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
  clips are downloaded, and per-clip import and motion-matching recipes are in the side-session
  notes. Importing them is the next step. It needs per-role exceptions in `SetupClips`: Body
  orientation for the turning roles, a per-role Y bake for prone roles, and the canonical X Bot
  skeleton. Once they're in, set `divePitch`, `bellySlidePitch` and `bellySlideOffset` to 0.
- **The skid swing drags a planted foot** about 0.4 m. The body turns 180° in about 0.06 s over
  planted feet. A dedicated turn clip and foot IK off (already off on SkidTurn) only hide part
  of it.
- **The capsule can't get very low.** It is wide (radius 0.76 m, sized from the renderer bounds),
  so it never goes below its diameter, about 60% of standing height. The belly slide and crouch
  therefore keep a tall capsule.
- **Tether latch beside geometry.** Latching while the capsule overlaps the scanned ground can
  depenetrate him about 0.8 m and fling him at the maximum swing speed. It also shows as a
  one-frame Velocity spike. This is pre-existing: the swing code is a verbatim port.
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
- **Play-testing in an unfocused Editor.**
  - Set `Application.runInBackground = true` at runtime, or Play mode stops ticking.
  - With `editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView`, the real
    keyboard leaks into the game even while you type elsewhere. Scripted tests should send keys
    to a virtual keyboard and disable the real keyboard and mouse, then restore them afterwards.
