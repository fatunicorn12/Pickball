# Spacetime (4D worldlines)

Code: `Assets/_Project/Scripts/Sandbox/Spacetime/` (namespace `Pickleball.Sandbox`)
Tests: `Assets/_Project/Tests/EditMode/WorldlineTests.cs`, `SpacetimeToggleTests.cs`
Assets: `Assets/_Project/ScriptableObjects/Spacetime/SpacetimeSettings.asset`,
`Assets/_Project/Art/Spacetime/Worldline.mat`
Scene: `Assets/_Project/Scenes/SpacetimeLab.unity`
Menus: **Tools ▸ Pickleball ▸ Spacetime ▸** Enable · Disable · Add To Sandbox · Remove From Sandbox ·
Create Spacetime Lab Scene

> **Off by default.** The feature has not been play-tested yet, so it ships switched off. While off
> there is no player-visible behaviour: no input binding is registered, nothing records, the view
> never flips, and no beams appear. ChibiSandbox and the Windows build are unchanged, because
> **Add To Sandbox** refuses to touch a scene while the switch is off. In a scene that already
> has the components (SpacetimeLab, or a sandbox wired while the switch was on), they are not
> completely idle:
> - `SpacetimeView.Update` and `WorldlineRecorder.LateUpdate` still run every frame, and return
>   early.
> - Recorders still register on the settings asset's `Live` list and subscribe to `Changed`.
> - The view still applies its `onlyIn4D` / `onlyIn3D` dressing on enable. Objects listed in
>   `onlyIn4D` are hidden even while the feature is off.

## 1. Purpose and feel

Each world has a 2D, a 3D and a 4D version, in the spirit of Super Paper Mario's flip. 4D is
**time made spatial**. Flip into it and every recorded object's last few seconds turn into a
solid beam you can stand on: its *worldline*.

- A lobbed ball leaves an arched **bridge**.
- A platform swinging side to side leaves a **ramp** up to where it is now.
- A platform moving in a circle leaves a **spiral staircase**.
- Newton's apple barrage becomes a **forest of frozen falls**.

The past is drawn as a spacetime diagram. Each sample is drawn at
`position − timeAxis × age`, where `age = now − sampleTime`; in code this is
`position + timeAxis × (sampleTime − now)`. So the past is shifted *against* the axis. With the
default upward axis older positions sit lower, and the present (age 0) sits exactly where the
object is.
Colour runs from violet (past) to cyan (present).

The loop: **in 3D you change things, in 4D you walk on the results.** The beams are a snapshot
taken at the moment of the flip, and they stay put while the world keeps moving. Flipping back
removes them, and if you are standing on one you fall. 4D runs on a meter (6 s full, refilling
in 3D), so a beam is a crossing to commit to, not a floor to live on.

### Controls

Bound in `SpacetimeView`, and **only while Spacetime is enabled**.

| Action | Keyboard | Gamepad |
|---|---|---|
| Flip 3D ↔ 4D | **T** | **D-pad Left** |

D-pad Up/Down is the camera zoom and D-pad Right is the skateboard, so Left is the only free
d-pad direction. T is not bound by anything else (movement uses R, E, Q, G, F, Space, Shift and
Ctrl; lock-on uses Tab).

The press is latched from the action's `performed` callback and consumed by the next `Update`,
the same pattern as lock-on, rather than polled with `WasPressedThisFrame`. Polling dropped
presses in an unfocused Editor and with simulated input (ST6). A press latched while the switch
is being turned off is discarded.

## 2. Architecture

| Piece | Job |
|---|---|
| `SpacetimeSettings` (ScriptableObject) | The on/off switch, every tunable, and the runtime channel between the view and the recorders (live recorders, ended histories, the 4D state and its `Changed` event). Runtime parts are `[NonSerialized]` and rebuilt on domain reload. |
| `WorldlineRecorder` | On anything that moves. Samples its position in `LateUpdate` into a `WorldlineHistory`. When disabled, destroyed or teleported, it hands the history to `settings.Ended`, so a splatted apple's past still shows. |
| `WorldlineHistory` (plain C#) | Timestamped samples with pruning and the spacetime projection. Outlives its object. |
| `WorldlineMesh` (static) | Sweeps a flat-topped slab along a path. The side axis stays horizontal so the top is level, and the sweep splits at hairpins and seeds vertical starts so it never twists or pinches. |
| `SpacetimeView` | Owns the flip, the meter and the beams. Reads input, builds one beam per history on entering 4D (colliders cooked in parallel with `Physics.BakeMesh`), and destroys them on leaving. Records apples itself, by attaching recorders to `Apple.Airborne`, so the Apple prefab is untouched. |
| `SpacetimeDemoMover` | Oscillate, orbit and lob movers for the lab. A test rig, not a gameplay pattern. |

**Events.** `SpacetimeSettings.Changed(bool in4D)` is raised by the view on every flip.
`WorldlineRecorder` listens, and uses it only for `hideWhileIn4D`.

**The switch.** Every component is wired to the one settings asset by a setup menu. With the asset
missing or `Enabled` false, `SpacetimeView.Tick` disposes the input action and leaves 4D, and
`WorldlineRecorder.Sample` drops its history and returns. Switching off mid-play takes effect
on the next frame.

**One view per settings asset.** `In4D` is a single state on the asset, so only one
`SpacetimeView` may drive it. The first view to tick claims it (`SpacetimeSettings.TryClaim`).
A second view wired to the same asset logs a warning once and does nothing, and the claim is
released in `OnDisable`. Use a second settings asset if two independent 4D worlds are ever
needed.

**The ended-line cap.** `SpacetimeSettings.Ended` holds at most 256 histories (`MaxEnded`). Past
that, the oldest is dropped to make room. A long apple barrage can hit this, and then the
earliest apples' lines vanish before they would have aged out.

**Beams and the player.** A beam built through the player's capsule (above step height) starts
with its collider disabled, and becomes solid once the player is clear. Each beam ignores
collisions with its own object's colliders, so a physics-driven object is not kicked out of its
own past.

## 3. How to tune

Everything is on `SpacetimeSettings.asset`:

- **Shape:** `width`, `thickness`, `timeAxis` (the projection), `maxPointsPerLine`.
- **Look:** `worldlineMaterial`, `pastColor`, `presentColor`.
- **Budget:** `maxDuration` (0 = unlimited, useful while building levels), `rechargeRate`,
  `minChargeToEnter`, `flipCooldown`.
- **What gets recorded:** `recordApples`.

Per object, on `WorldlineRecorder`:
- `settings`: the asset it records into. Empty means it is inert.
- `historySeconds`, `sampleSpacing`, `sampleInterval`: read when a history is *created* (on
  enable, after a teleport, or after the switch comes back on), so changing them mid-play only
  affects the next history.
- `teleportDistance`: a single-frame jump bigger than this splits the line. A long hitch on a
  fast object can split it falsely.
- `traceOffset`: the traced point, relative to the pivot in world space.
- `width`, `thickness`: per-object overrides. 0 means use the settings value.
- `hideWhileIn4D`: hides the object's renderers and non-`CharacterController` colliders while in
  4D.

Worth knowing: with an upward time axis, a back-and-forth mover whose period is shorter than about
twice `historySeconds` stacks the legs of its zigzag too close together to walk between. The lab's
swinging platform uses an 8 s period against a 4 s history for that reason.

## 4. How to extend

**Record a new object:** add `WorldlineRecorder` and wire its `settings` field to
`SpacetimeSettings.asset`. Do it in a setup menu, not by hand. For objects spawned at runtime,
call `recorder.Bind(settings)` after `AddComponent`, as `SpacetimeView.RecordApples` does.

**Wire Spacetime into another scene:** enable it, open the scene, and run **Add To Sandbox**. It
needs a `ChibiLocomotion` in the scene. It adds one `Spacetime` object with the view (bound to the
settings and the chibi's controller) and a recorder on Newton if he is there. It never saves. It
is idempotent in what it creates, with three caveats:
- A second run re-applies its wiring, so it overwrites manual edits to the view's `settings` /
  `player` and to Newton's recorder `settings` / `traceOffset`.
- It marks the scene dirty even when nothing changed.
- If the scene already has a `SpacetimeView` anywhere, it rewires that one instead of creating
  the `Spacetime` object.

**Remove From Sandbox** is broader than an undo of Add. It removes *every* `SpacetimeView` and
*every* `WorldlineRecorder` in the open scenes, except recorders on `SpacetimeDemoMover`s:
- That includes recorders added by hand or by other menus.
- A view's GameObject is deleted only if it is named `Spacetime` and holds nothing else.
  Otherwise just the component goes.

It runs whatever the switch says, and only works outside Play mode.

**React to 4D from another system** (a sky tint, music, a HUD meter): subscribe to
`SpacetimeSettings.Changed`, or list objects in the view's `onlyIn4D` / `onlyIn3D` arrays.

A HUD meter can read `SpacetimeView.Charge01`. It is always in [0, 1]: it reads 1 before the
first tick, while the meter is disabled (`maxDuration` 0) and while unwired. Before the QA fix
(ST1) it read −0.17 before the first tick. Check `SpacetimeView.Active` first and
hide the meter while the feature is off. Otherwise it shows a full meter for a feature that
does nothing.

## 5. Known limitations and open questions

- **Not play-tested for feel.** The lab and the tests prove it works. Whether it is fun is the open
  question, and the reason it ships switched off.
- **The flip timing changes the shape.** Flipping mid-swing gives one long ramp, and flipping near
  a turnaround gives a hairpin. That may be a puzzle hook or may feel random.
- **Beams are a snapshot,** so they do not move with the world. A live, growing worldline is
  possible but would need per-frame mesh rebuilding.
- **No 2D mode yet.** 2D (an orthographic side view of the same geometry) is the other half of the
  2D/3D/4D idea.
- **The player is not recorded.** Walking on your own past is an obvious next experiment.
  `OverlapsPlayer` already copes with beams that appear at the player's feet.
- **Stacked intangible beams** are re-tested every frame while in 4D, so many of them at once costs
  a physics query each per frame. The query reads into a 64-entry buffer, so in a crowded spot a
  beam can be missed and stay intangible longer than it should (ST8).
- **Beams are on the Default layer** (ST8). In 4D they are ordinary static colliders:
  - They block lock-on's line of sight and the tether's reach.
  - They can trap Newton or anything else that moves into them.
  - Only the beam's own object ignores it.
  
  A dedicated layer, excluded from those queries, would fix it, but needs a `ProjectSettings` tag
  and layer change, which is a shared file.
- **Ended lines can go stale** (ST7). `Ended` is only pruned while the switch is on, and the
  settings asset outlives scenes in Play mode. Two consequences:
  - Switch off, wait, then switch back on. Until the next tick, `Ended` still holds lines from
    before the switch went off, so a scripted `Enter()` in that window draws them. The key can't
    hit the window, because the binding only appears on that first tick, which also prunes.
  - After a scene change, the previous scene's ended lines stay until they age out
    (`historySeconds`). Flipping during that window draws the old scene's ghosts.

  Clearing `Ended` when the switch goes off and when the scene changes would fix both. That isn't
  done yet.
- **Coding-standards deviation:** `SpacetimeView` reads the static `Apple.Airborne` list to find
  apples to record. The alternative was editing the Apple prefab, which this feature does not own.
