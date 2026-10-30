# The combat ↔ movement ↔ lock-on boundary

Snapshot as of `a930952`, updated for the QA fixes through `f876b63`. Movement and sword combat each have a system doc
(`Docs/Systems/Movement.md`, `Docs/Systems/SwordAbilities.md`). This page records how the three
player systems meet, because that seam belongs to neither doc and is where regressions hide.

## The chain

```
MeleeCombat ──(ICombatMovement, ICombatImpulses)──▶ ChibiMovementAdapter
                                                         │  (public facade API only)
                                                         ▼
LockOnTargeting ◀──reads── ChibiLocomotion (facade + glue, IFacingSource) ──▶ MovementMotor
                                   │                                             (states)
                                   └── reads MeleeCombat.IsAttacking (known back-edge)
```

## Decisions

**Combat sees movement only through two small interfaces.** `ICombatMovement` (`IsGrounded`,
`IsBusy`, `Velocity`, `IsMovementLocked`) and `ICombatImpulses` (`AddImpulse`, `LockMovement`, `SuspendGravity`).
`ChibiMovementAdapter` is the one file that changes when movement changes; the combo code never
names a movement class. `MeleeCombat` takes the source as a serialized `MonoBehaviour` cast to
the interface, falling back to `GetComponent`.
- *Unused today:* `Velocity`, `IsMovementLocked` and `LockMovement`.
- `AddImpulse` maps to `AddMomentum`, which **drops the y component**. A step's lunge is applied
  once at step start, never per frame (see the `ICombatImpulses` docs).

**No movement means a free swordsman.** With no movement source, combat treats the player as
grounded and never busy. Test rigs can rely on this: a bare object with `MeleeCombat` still swings.

**`IsBusy` is "an exclusive move is running".** It is `MoveKind.IsExclusive()`: Dash, Tether and
Skate. `MeleeCombat` won't start a swing while it is true, and a press during it is dropped, not
buffered.

**`ChibiLocomotion` is a facade, not the movement system.** It was a 1,342-line monolith. It now
forwards a frozen API (`IsBusy`, `IsGrounded`, `Velocity`, `AddMomentum`, `AddAcceleration`,
`AppliedFieldVelocity`, `LockMovement`, `IsMovementLocked`, `IsAnchored`) to `MovementMotor`.
MeleeCombat, the Newton fight and the setup scripts depend on that API. New code should read
`ChibiLocomotion.Movement` (the motor) instead.

**Facing: lock-on and combat decide where, movement decides when.** `ChibiLocomotion` implements
`IFacingSource` and installs itself on the motor in `OnEnable`. Priority order:
1. A registered combat facing source (`SetCombatFacingSource`). None is registered yet.
2. The lock-on target, which is *urgent* while `MeleeCombat.IsAttacking` and only applied once
   the player stops otherwise.

Reading `MeleeCombat.IsAttacking` is the one remaining movement-side dependency on combat. It
is meant to go once combat pushes its own facing request.

**Lock-on feeds the combo resolver, not the swing code.** `MeleeCombat` passes `hasTarget` and
the horizontal *edge* distance (`AimPoint − TargetRadius`) to `ComboResolver`. Abilities use those
values in `ComboCondition` (`requireLockOn`, min/max distance), for example to pick a gap-closer
or a finisher.

**While locked, camera look turns into target flicks.** `SandboxOrbitCamera` ignores mouse and
right-stick look, and `LockOnTargeting` reads them as left/right target hops. The lock's aim point
is measured once, at lock time, so the camera doesn't "breathe".

**The input gate ends targeting (Breath of the Wild).** While any claimant on `PlayerInputGate`
blocks `LockOn` (a conversation, an aim), `LockOnTargeting` drops the toggle and flicks, releases
a held lock through `Release()` as a second Tab would, and shows no hint until the last claim
lifts; `TetherSystem` hides its marker while `Movement` is blocked. The reticle fades on unscaled
time, so the ring is gone inside a talk's 0.25 s settle, and Tab locks again once the claim lifts.

**Grounding is deliberately asymmetric.** Landing refills the air chain at once (commit
`1e38c1c`); leaving the ground waits `airborneGraceSeconds`. Since `6991c60`, one pure
`GroundContact` gives the graced answer, and that answer both picks a new swing's ground/air
track and decides when the ground chain is dropped. A one-frame flicker can no longer start an
air step.

**Landed air hits hang through `SuspendGravity`.** `ICombatImpulses.SuspendGravity` goes through
the adapter to `ChibiLocomotion.SuspendGravity`, then to the motor. `MeleeCombat` refreshes it every
frame while an air-track swing plays and a hit landed within `airHangPerHitSeconds` (see `AirHang`).
The hang ends when hits stop landing, the chain ends, the player lands (since `f876b63`), or a dash
or tether takes the body. `SetVerticalVelocity` is still unwired. A
plunge would add it to the interface the same way.

## Pad and key layout contract

There is no `.inputactions` asset for the Sandbox. Each component builds its own `InputAction`s
in code, and `SandboxOrbitCamera` and `TrainingStage` poll `Gamepad.current` / `Keyboard.current`
directly. Grep for `AddBinding`, `Button(` and `.current` across `Sandbox/` before binding
anything new.

| Owner | Keyboard / mouse | Gamepad |
|---|---|---|
| `PlayerMovementInput` | WASD / arrows; Shift sprint; Space jump; Ctrl crouch; F flip; R mount; E dash; Q / right mouse tether; G anchor | Left stick; LT sprint; South jump; East crouch; North flip; **D-pad Right mount**; RB dash; LB tether; R3 anchor |
| `SwordInput` | Left mouse (ignored while the cursor is free) | **West** attack |
| `LockOnTargeting` | Tab / middle mouse toggle | RT toggle |
| `SandboxOrbitCamera` | Mouse look (polled) | Right stick look, D-pad Up/Down zoom (polled) |
| `SpacetimeView` | T | D-pad Left (both bound **only while Spacetime is enabled**) |
| `TrainingStage` | Backspace respawn; Shift+Backspace also revokes abilities (polled) | — |
| `InteractInput` (read by `PlayerInteractor`, `DialogueDirector`) | **E Interact**: talk while a talk prompt is showing, and advance text during a conversation | **RB**, the same (all pad buttons are taken) |

Mount moved from West to D-pad Right in `ddb5c9d`, which resolved the "pad clash" with attack.

## Testing implications

- `MeleeCombat` can be tested in PlayMode with a stub `ICombatMovement`/`ICombatImpulses`
  component (settable `IsGrounded`/`IsBusy`) and `QueueAttack()`, with no motor at all.
- The motor's state `Next()` reads `Time.time` and motor internals directly, so state
  transitions can't be unit-tested until there is a context interface. See
  `Docs/Testing/QA-Assessment.md`.
