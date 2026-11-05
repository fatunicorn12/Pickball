# Lock-on

## 1. Purpose and feel

Zelda / Kingdom Hearts targeting, as a toggle. Tab, middle mouse or the right trigger locks the
enemy nearest the middle of the screen; the next press lets go. A flick of the mouse or right stick
hops to the next enemy on that side. With nothing in view, a press recentres the camera behind the
player instead.

Three rules define it:

- **The camera never spins.** Walking or dashing under a floating enemy, it holds its heading until
  you're well past, then makes one clean turn at a capped speed. Going round or to and fro under it,
  it sways but doesn't chase you round the clock.
- **The lock sits on what you can hit.** The ring, the camera and your swings aim at the region the
  sword connects with, wherever that body has moved to. They never aim at scenery it carries.
- **You can only lock what you can see.** A press locks only an enemy whose aim point is on screen
  with a clear line from the camera, and the hint shows exactly when a press would lock. A held lock
  lets go if the target stays out of view for `lostSightGrace`.

## 2. Architecture

| Piece | Job |
|---|---|
| `LockOnTargeting` (on the player) | Decides what is locked: input, selection, holding, the reticle. The source of truth for `AimPoint`, `TargetRadius` and in view. |
| `LockOnAim` (static) | Measures a target from what the sword can hit; `LockOnBody` is the result. |
| `LockOnView` (static) | The viewport half of "in view". |
| `SandboxOrbitCamera` (on the main camera) | Reads `AimPoint` to aim. Exposes `SightOrigin` for the sight line. |
| `LockOnSteering` (static) | The camera's yaw, pitch, tilt and pull-back maths. |
| `LockOnIgnore` | Marks scenery under a target (Hypatia's dial and halo) to leave out of the measure. |
| `LockOnReticle` | Presentation. `LockOnTargeting` tells it what to show each frame. |

**One answer to "where is the enemy".** `LockOnTargeting.BodyOf` serves scoring, the hint, the sight
line, the reticle, the camera (`AimPoint`), `ChibiLocomotion`'s facing and head look, and
`MeleeCombat`'s homing and combo distance (`AimPoint`, `TargetRadius`, `TryGetAttackTarget`).

A collider is measured when `MeleeCombat.TestCapsule` would strike this target through it: enabled,
not a trigger, on a layer in the blade's mask (`MeleeCombat.HittableLayers`), not under a
`LockOnIgnore`, with this `Health` as the nearest `IDamageable` above it. A Hypatia orbiter is its own
damageable, so it doesn't count for her. The aim is `aimChestBias` of the way from the middle of those
colliders' combined bounds to their top.

Bounds are computed from each collider's shape and transform, not `Collider.bounds`.
`Physics.autoSyncTransforms` is off, so `Collider.bounds` only updates at the physics step, and an aim
read from it stair-steps on a moving target. Nothing is cached, so the aim tracks a body moving
relative to its root on the same frame. `Health.BodyOffset` isn't added for colliders, because the body
that sets it (`TrainingDummy`) already moves its collider by that amount.

**With no hittable collider** (a hurtbox switched off), it falls back to the renderers, then to a
default chest 1.2 m above the root. Renderer bounds breathe with the animation, so a held lock freezes
that measure against the root and `BodyOffset` until a hittable collider comes back.

**One answer to "is it in view".** `LockOnTargeting.InView`: the aim point is inside the viewport and
the camera has a clear line to it. Other `Health` bodies, triggers and the player don't block. A press,
the hint, a flick and a death hand-off use `viewportInset`. A held lock is judged against the screen's
edge, with `lostSightGrace` to recover. Range and the cone are checked as well, as before.

The sight line starts at `SandboxOrbitCamera.SightOrigin`: the lens, unless occlusion has run out of
room (`occlusionMinDistance`) with a wall at the player's back, leaving the lens in or behind that
wall. Then it starts at the follow point, or backing into an arena wall would drop the lock.

**The camera steering.** The wanted yaw is the bearing of
`flat(follow point to aim) + lockHoldRadius * cameraForward`. Far away the first term wins. Close or
underneath the second wins and the yaw holds. It's defined at exactly zero, and there's no threshold.
`LockOnSteering.Turn` smooths toward it under a hard per-frame limit (`lockMaxYawRate`), and a turn
under way keeps its direction when the goal crosses the far side of the circle.

**Orbit and look are separate.** The orbit (`_yaw`, `_pitch`) places the camera, and its pitch stops
at `lockMinPitch`, which keeps the lens above the floor. A look tilt (`LockOnSteering.LookTilt`) then
raises only the view, by as little as brings the aim below `lockFrameTop`, without pushing the head
below `lockPlayerFloor`. `lockRisePullback` adds distance for a high target. The follow, the occlusion
sweep and `SafeBandCorrection` all still work from the orbit's pose.

**Hand-backs.**
- The tilt and pull-back scale with `_lockBlend`, and neither is written into `_yaw` or `_pitch`. So a
  release fades the tilt over about 0.33 s with no jump, and look resumes from the orbit.
- `OrbitCameraAccess.SnapView` clears `_lockBlend`, so a cutscene hand-back lands exactly on its pose.
  `OrbitCameraAccess` reads this class's private fields by name; don't rename them.
- A conversation disables the rig with the blend and tilt intact and restores the pose it recorded,
  so the fade carries on from there.
- At a `dt` of zero the steering does nothing (`SmoothDamp` can divide by it).

## 3. How to tune

On the main camera's `SandboxOrbitCamera`, Lock-on section. These are new fields, so scenes pick up
the inline defaults until someone saves a different value.

| Field | Default | Feel |
|---|---|---|
| `lockMaxYawRate` | 240 °/s | Top speed of the turn after you run past. Lower is calmer, but a dashing target can leave the frame for longer. |
| `lockHoldRadius` | 2.5 m | How close before the camera holds still. Larger sways less under a target but follows a close one more lazily, and lets you run further past before it turns. |
| `lockTurnSmoothTime` (existing) | 0.2 s | How quickly it settles on the goal. |
| `lockMaxPitchRate` | 120 °/s | Top speed of the orbit's pitch. |
| `lockMinPitch` | -6° | How low the orbit swings for a high target. Lower narrows the angle to fit, nearer the floor. |
| `lockFrameTop` | 0.88 | Highest the target sits on screen. Higher keeps more of the player in frame under a high target, with the ring nearer the screen's edge. |
| `lockPlayerFloor` | 0.1 | Lowest the player's head sits while tilted. Lower keeps a higher target framed, showing less of him. |
| `lockMaxTilt` | 50° | Most the view tilts past the orbit. |
| `lockTiltSmoothTime`, `lockMaxTiltRate` | 0.15 s, 120 °/s | How gently the tilt moves. |
| `lockRisePullback`, `lockPullbackFullRise` | 2.5 m, 60° | Extra distance for a high target, and the elevation at which all of it applies. |

On the player's `LockOnTargeting`: `viewportInset` (0.03: how far inside the screen a press needs),
`aimChestBias` (0.2: where on the body the lock sits), and the existing `lostSightGrace`, ranges, cone
and flick settings.

## 4. How to extend

- **A new enemy** needs a `Health` and a non-trigger collider somewhere under it, and is then
  lockable with no wiring. With no such collider it's still lockable, at the fallback.
- **A prop it carries**: give it no collider, or put a `LockOnIgnore` on it if it has one.
- **A part that takes its own hits**: give it its own `IDamageable`; it then won't count for the owner.
- **Testing**: `Tests/EditMode/LockOn/` (steering, aim, in view, hand-backs) and
  `Tests/EditMode/Cutscene/Cameras/OrbitHandoffFromLockTests`. `LockOnScenario.LockOn` is the
  before-and-after harness run; see `Docs/Testing/PlaytestHarness.md`.

**Measured** with `LockOnScenario.LockOn` at 60 fps, on a floating 2 m body, before and after this
design (the same scenario run against the old code):

| Pass (body 6 m up) | Max yaw °/s, old → new | Total yaw °, old → new | Frames off screen, old → new |
|---|---|---|---|
| Walk straight through the centre | 662 → 230 | 186 → 182 | 31/360 → 12/360 |
| Dash through | 662 → 230 | 185 → 181 | 27/210 → 20/210 |
| Circle 0.8 m out, 5 s | 382 → 121 | 1739 → 379 | 283/300 → 0/300 |
| Zigzag under it, 5 s | 529 → 163 | 994 → 394 | 272/300 → 0/300 |
| Stand under it (9 m up), 3 s | 659 → 0 | 180 → 0 | 180/180 → 0/180 |

## 5. Known limitations and open questions

- **Under a high target the player sits low in the frame.** The target has priority, so with a body
  6 m up you see his head and shoulders at the bottom of the screen. `lockFrameTop` and
  `lockPlayerFloor` trade that off. Not judged by hand yet.
- **Running straight past, a high target can leave the frame for a moment.** Just past the hold
  radius it is close above the camera and the head limit caps the tilt: up to 20 frames (0.33 s) for a
  dash at 6 m, 27 frames at 9 m. The `lostSightGrace` covers it and the turn brings it back. Tune
  `lockHoldRadius` and `lockRisePullback`.
- **Going round or to and fro right under a target, the camera sways.** About ±20° at up to 163 °/s,
  where it used to follow him round. A larger `lockHoldRadius` reduces it.
- **`FindObjectsByType<Health>` still runs every frame** while unlocked with the hint on (QA ST5).
- **Hard to lock a very high target from up close.** It's above the frame, by the in-view rule, so the
  player has to back off or look up first. That is deliberate.
- **The sight line goes to one point,** so a target half behind a pillar counts as hidden or not
  depending on its chest.
- **The ring and the homing follow the hittable radius,** which is smaller than the drawn body on
  Newton and Hypatia, so swing homing and the ring sit tighter than they did.
