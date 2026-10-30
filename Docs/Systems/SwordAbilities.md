# Sword Abilities

A Kingdom-Hearts-style sword ability system. As the player levels up, the combo gains slashes,
the air gets its own chain, and finishers with special effects cap both chains.

- **Code:** `Assets/_Project/Scripts/Sandbox/MeleeCombat.cs` and `Scripts/Sandbox/Combat/`, which
  contains `Progression/` and `Presentation/`.
- **Data:** `Assets/_Project/ScriptableObjects/Combat/`.
- **VFX:** `Assets/_Project/Art/VFX/Sword/`.
- **Setup:** `Scripts/Editor/SwordAbilitySetup.cs`, run from **Tools ▸ Pickleball ▸ Setup Sword Abilities**.
  The ruler sword's moveset: `Scripts/Editor/RulerMovesetSetup.cs` (**Tools ▸ Pickleball ▸ Setup Ruler
  Moveset**, also run by Setup Sword Abilities), with the clip audit and contact sheets in
  `RulerMovesetAudit.cs` / `RulerMovesetPreview.cs`.
- **Tests:** `Tests/EditMode/Combat/`.

## 1. Purpose and player-facing feel

At level 1 you have the original kit:
- **Ground:** a four-hit chain that alternates swing direction and ends on a rising launcher.
- **Air:** one air slash.

Landing sword hits earns **Lemmas**, the game's XP ("small results that add up to theorems").
Each level-up shows a **Q.E.D.** banner. Some levels also teach a technique:

| Lv | Technique | KH analogue | What changes |
|---|---|---|---|
| 2 | **+ C** | Combo Plus | A fast 4th ground hit before the finisher. Gold trail, and a "+" pops at each hit. The ground chain is now 5 hits. |
| 3 | **Phase Shift** | Air Combo Plus | A second air hit: a downstroke after the air slash, with a violet trail. |
| 4 | **Unit Circle** | Aerial Finish / Sweep | An air finisher. A wide sweep releases a 1.8 m ring all round you, with a radius line sweeping round like θ. |
| 5 | **Area Under the Curve** | Ripple Drive / Explosion | Replaces the ground finisher. A heavier rising slash that steps in and sends out a 3 m ring along the ground, with Riemann-sum bars standing on it. The ring grows over 0.35 s, so a grounded target is hit when the edge reaches it. |

How the chains behave:
- **Ground and air chains are separate, as in KH.** If you jump mid-combo, your next press starts the air
  chain, and landing starts the ground chain fresh.
- **One jump buys one air combo.** The air chain refills only when you land, which is what makes
  Air Combo Plus worth learning.
- **Air hits that land hold you up.** Each air hit that connects (blade or ring) turns your gravity
  off for `airHangPerHitSeconds` (0.6 s), and your vertical speed eases to rest. Each hit refreshes
  the hold, so you float as long as you keep connecting.
  - Swinging at nothing doesn't float you.
  - You drop as soon as you stop landing hits, the air chain ends, you land, or a dash or tether
    takes over.
  - The longest float is bounded by the air chain (one per airtime), so every air hit you learn
    lengthens it.
- **The ground chain resets whenever a swing ends without a follow-up press.**
- **Mashing never cuts a hit short.** A buffered press chains at the step's `comboOpen`, but never
  before its blade window has closed or its finisher ring has gone out.
- **Swinging still never moves or stops you.** The only exception is a step's one-off lunge
  impulse; Area Under the Curve steps in 2.5 m/s.

With the shipped curve (base 50 Lemmas, growth 1, 1.5 Lemmas per HP of damage), levels 2–5 cost
50, 150, 300 and 500 Lemmas in total. That is roughly 34, 100, 200 and 333 HP of sword damage,
so the whole slice unlocks inside one Newton fight (600 HP), with a +250 defeat bonus on top.

Only damage actually dealt pays, and each target pays out its worth (max health in damage plus one
defeat bonus) **once per session**. So the refilling training dummy can't be farmed, and a Newton
rematch pays nothing new.

### The ruler sword

The ruler is the heavy weapon: a one-handed buster blade, about a metre long. Its own combo
(`Moveset_Ruler`) is **three big, slower swings that hit harder**:

- **Ground:** *Straightedge*, a 270° flat cleave from his left round to his right; *Reflex
  Angle*, a spinning diagonal cleave back from his right; and *Plumb Line*, a leap and down-chop
  that lands a 2.6 m shock ring where the blade strikes.
- **Air:** *Full Rotation*, one committed spin that sweeps all the way round.

Each hit deals 2–2.4× the compass hit in the same slot, after a wind-up 1.5–3× as long, with
more hitstop, shake and FOV punch. A mashed chain takes about 1.5× as long. Damage per second is
almost the compass's (1.04× on the blade), so the trade is **commitment**: fewer, later, longer
swings that are harder to cancel, not more damage. The numbers are in §3, "Ruler tuning".

At rest he carries it in a **low trail**: arm relaxed, fist by the hip, the blade running back
and a little out, edge down, clear of the head, hair, cloak and floor (§2, "Carry pose"). The
compass keeps the locomotion clips' own arm.

Learned abilities follow the weapon (§2, "Abilities per weapon"). With the ruler, + C is a third
cleave at ruler pace, Phase Shift is an overhead chop, and Area Under the Curve is a slower slam
with a 3.6 m Riemann ring. The ruler declines Unit Circle: its air spin is already the circle.

## 2. Architecture

```
SwordInput ──AttackPressed──▶ MeleeCombat ◀── ICombatMovement / ICombatImpulses ── ChibiMovementAdapter ── ChibiLocomotion
                                   │  asks ComboResolver(context, SwordMoveset, SwordAbilities.All)
                                   │  owns SwingTimeline (clock) + ComboProgress (chain counters)
                                   │  sweeps the blade, runs AreaBurst rings, deals damage via IDamageable
                                   ├─ SwingStarted ──────▶ SwordAnimatorDriver (crossfade, layer weight, hitstop freeze)
                                   ├─ BladeLiveChanged ──▶ SwordEffects (trail on/off)
                                   ├─ HitConnecting ─────▶ CombatXpSource (target health before the hit)
                                   ├─ HitLanded ─────────▶ SwordEffects (sparks, hit pop, shake) · CombatXpSource (Lemmas)
                                   ├─ BurstReleased ─────▶ SwordEffects (ring prefab, sized from the event)
                                   └─ ChainEnded ────────▶ SwordEffects (trail off)
CombatXpSource ──AddXp──▶ PlayerLevel ──LeveledUp──▶ LevelUnlocks ──Grant──▶ SwordAbilities ──Unlocked──▶ ProgressionToast
                                      └─LeveledUp / XpGained──────────────────────────────────────────────▶ ProgressionToast
PlayerAbilities (movement unlocks) ──Unlocked──▶ ProgressionToast
```

### Dependencies point one way
- **Gameplay** is `MeleeCombat`, the pure-logic classes, and `SwordAbilities`. It knows nothing about
  progression or presentation.
- **Progression** (`CombatXpSource`, `PlayerLevel`, `LevelUnlocks`) listens to gameplay.
- **Presentation** (`SwordAnimatorDriver`, `SwordEffects`, `ProgressionToast`) listens to both.
- **Movement** is read only through `ICombatMovement`, and pushed only through
  `ICombatImpulses`, which offers a single impulse and a brief move-lock. `ChibiMovementAdapter` is
  the one file that touches `ChibiLocomotion`.
- **One leftover in the other direction:** `ChibiLocomotion` reads `MeleeCombat.IsAttacking` to
  square up to a lock-on target. It predates this work and is kept as-is, because combat must not
  edit locomotion.

### Components (on the player root)

| Component | Job | Kind |
|---|---|---|
| `SwordInput` | Reads Attack (left mouse / gamepad West) and raises `AttackPressed`. Ignores the click the camera uses to re-capture the cursor. | input |
| `MeleeCombat` | Resolves the next step, runs its timeline and hitstop, sweeps the blade, runs bursts, deals damage, raises events. Keeps `IsAttacking`. | gameplay |
| `SwordAbilities` | The learned set (`Grant` / `Has` / `Revoke` / `Unlocked` event). The sword's counterpart to `PlayerAbilities`. | gameplay |
| `ChibiMovementAdapter` | Implements `ICombatMovement` and `ICombatImpulses` over ChibiLocomotion's public API. | adapter |
| `PlayerLevel` | Converts Lemmas into levels. Raises `XpGained` and, once per level gained, `LeveledUp(new, previous)`. | progression |
| `CombatXpSource` | Reads the target's `Health` on `HitConnecting` and `HitLanded` and pays what `XpLedger` says the hit was worth. | progression |
| `LevelUnlocks` | Grants the abilities listed in the level table as levels are reached. | progression |
| `SwordAnimatorDriver` | Crossfades the step's Attack-layer state, fades the layer weight, freezes `animator.speed` during hitstop. | presentation |
| `SwordEffects` | Blade trail tint and emit, impact sparks, pooled hit and burst effects, camera shake and FOV kick. | presentation |
| `WeaponCarryPose` | Plays the equipped weapon's carry pose on the masked Carry layer and fades its weight (`WeaponCarryBlend`). | presentation |
| `ProgressionToast` | Shows the Q.E.D. / New-technique banner queue and the `LV n · x / y Lemmas` readout (on the `SwordHUD` canvas). | presentation |

### Hit detection (`MeleeCombat`)
- The blade and the rings both overlap-query the `hittable` mask into one buffer of **64**
  colliders. It is that large because a 3 m ring on a busy map also returns floor and wall
  colliders; with the old 16 slots they could crowd the real target out of the results.
- Each collider is first **resolved** to the `IDamageable` it belongs to; the player's own colliders
  are skipped. Anything that isn't a target is dropped before it is measured.
- Resolving a target, checking whether it was **already struck**, and **marking** it struck are
  separate steps:
  - The blade marks a target as soon as it resolves: one hit per target per swing.
  - A ring skips targets it already struck, measures the rest, and marks a target only once its
    edge has reached it. Marking on resolve would make an expanding ring skip, for good, a target
    its edge hadn't reached yet.
- Hit points come from `HitPoint.Nearest`, never `Collider.ClosestPoint` directly (see below).

### Pure logic (plain C#, EditMode-tested)
- **`ComboResolver`:** picks the next step. A chain on either track is
  `[opener?] base hits… extenders by chainOrder… [finisher]`. The rules:
  - **Extenders** (Combo Plus) are appended after the base hits, sorted by `chainOrder`, with ties
    broken by asset name. Unlock order never affects the order.
  - **Finisher:** the highest-priority unlocked finisher whose condition holds. If none holds, the
    moveset's default finisher plays; if there is none, the chain has no finisher.
  - **Opener:** applies on the first press only. The highest-priority qualifying opener replaces
    hit 1, and the chain continues from hit 2.
  - **Weapon first:** every learned ability is passed through the moveset
    (`SwordMoveset.StepFor`) before any of this. The weapon gives it a swing (its own or the
    ability's) or declines it, and a declined ability takes no slot at all. See "Abilities per
    weapon" below.
  - The resolver returns an invalid step once the chain is spent.
- **`ComboTiming`:** how long a mashed chain lasts, each step's wind-up, hitstop and damage,
  computed from the step assets by `SwingTimeline`'s chain-window rule (chain at the later of
  `comboOpen` and `activeEnd`, after any burst, never past `comboClose`). `ComboTimingTests`
  checks it against the real timeline at 30, 60 and 144 fps. The compass-vs-ruler tables and
  `RulerMovesetAssetTests` use it.
- **`ComboProgress`:** ground and air hit counters. Ground resets when the chain is dropped or you
  leave the ground; air resets only on landing.
- **`SwingTimeline`:** the swing clock, with blade-on and blade-off, chain-window, burst and finish
  signals.
  - The chain window opens at `comboOpen`, but never before `activeEnd` or before the step's burst
    has been released.
  - A burst still pending when the swing ends is released then, not dropped.
- **`BladeSweep`:** which capsules to overlap-test each frame.
  - The blade is sampled on every frame of a swing, live or not.
  - A frame whose interval touched the active window tests the blade where it is plus the arc its tip
    and base swept since last frame.
  - So the first live frame, the frame the blade stops, and a long frame that jumps the whole
    window are all tested.
- **`HitPoint`:** where on a collider a hit lands.
  - Box, sphere, capsule and convex-mesh colliders get Unity's exact `ClosestPoint`.
  - Anything else (a non-convex level mesh, a `CharacterController`, terrain) gets the nearest point
    on its world bounds. On those shapes Unity's `ClosestPoint` logs a warning and returns a junk
    point.
  - The rings used to call it on map geometry, which logged about 1,300 warnings in one TrainingStage
    session and mis-measured their reach.
- **`GroundContact`:** "airborne" means off the ground for longer than the grace.
  - That one answer picks the chain for a press and drops the ground chain, so a one-frame flicker
    on a slope does neither.
  - Landing counts at once.
- **`AirHang`** (in `CombatFeel.cs`): the hang timer.
  - `MeleeCombat.Deal` calls `HitLanded` for every connecting hit from an air-track step that has
    `hangInAir`. Hits refresh the timer rather than stack, so a ring striking three targets buys one
    hit's worth.
  - Each frame, `Tick` holds while the timer runs and an air-track swing is playing (hitstop
    included). It clears the timer when the chain ends, you land, or an exclusive move owns the body.
  - `MeleeCombat` passes the answer to `ICombatImpulses.SuspendGravity`. The adapter forwards it to
    `MovementMotor.SuspendGravity`, which eases vertical speed to rest at
    `MovementConfig.anchorHaltRate` and clears the hang on landing.
  - A hold is two frames long, because the motor may tick its countdown before the refresh lands.
- **`XpLedger`:** what a hit is worth.
  - Only damage actually applied pays (health before minus after).
  - The defeat bonus is paid only for the blow that takes a target from above zero to zero.
  - Each target pays at most its max health plus one bonus, once per session.
- **`AreaBurst`:** the ring's growth (`GrowthFraction`, an ease-out shared with the visual), its
  height-band test and its centre.
- **`ExperienceCurve`:** XP from level L to L+1 is `baseXp·L^growth`, with totals and the inverse.
- **`LevelUnlockRules`:** which rows of the level table a level-up teaches.

### Data assets (`ScriptableObjects/Combat/`)
- **`ComboStepDefinition`** (`Steps/Step_*.asset`) is one swing:
  - **Animation:** Attack-layer state name, clip, playback speed, blend-in.
  - **Timing and hit:** `SwingWindows` (active, comboOpen, comboClose fractions) and
    `HitSettings` (damage in HP, knockback and launch in m/s, hitstop in s).
  - **Movement:** an optional lunge impulse.
  - **Area burst:** an optional `AreaBurst` ring.
  - **Presentation:** `SwingPresentation` (trail colour, shake, FOV punch, hit and burst effect
    prefabs).
  - `Duration` is computed as `clip.length / playbackSpeed`. That removes the old
    "durations written in by the setup script" step that could drift out of sync.
- **`SwordAbilityDefinition`** (`Abilities/Ability_*.asset`): the role (Extender, Finisher or
  Opener), the track, the step, chain order, priority, a `ComboCondition` (lock-on and target
  distance) and the toast text.
- **`SwordMoveset`** (`Moveset_Compass.asset`, `Moveset_Ruler.asset`): the base ground chain
  plus its default finisher, the base air chain plus an optional finisher, and the weapon's
  **ability rules** (below).
- **`LevelingConfig`** (`Leveling_Player.asset`):
  - the curve: max level, base XP, growth;
  - earning: Lemmas per HP, defeat bonus;
  - the level → ability **unlock table**.

  The table lives here, not on the ability, so a different table can teach the same abilities in
  a different order. This is the same idea as KH's Dream Sword / Shield / Rod choice.

### Abilities per weapon
Abilities belong to the player and survive a weapon swap, but their swings are timed for the
compass. Before this rule, the resolver laid every learned ability onto whatever moveset was
active, so the ruler would have got the compass's 3× + C in the middle of its heavy chain.

Each `SwordMoveset` now says how learned abilities play with it:
- **`abilitySteps`** rows (`AbilityStepOverride`): an ability and the weapon's own step for it,
  played in the ability's place. An **empty step declines** the ability for this weapon.
- **`acceptOtherAbilities`**: whether an ability with no row plays its own step. It defaults to
  on, so `Moveset_Compass`, which has no rows, plays exactly as before.
- **What stays the ability's:** role, track, chain order, priority and condition always come
  from the ability, so the chain keeps its shape on every weapon. `ResolvedStep.Source` is still
  the ability, so toasts and effects credit it.
- **A declined ability takes no slot.** It doesn't lengthen the chain, and a declined finisher
  or opener can't win its slot and then play nothing; the next qualifying one, or the default,
  plays instead.
- `MeleeCombat` needs no change: it already passes the moveset to the resolver, so an
  `Equip(moveset)` swap takes the new rules from the next press.

The ruler sets `acceptOtherAbilities` off and lists its rows:

| Ability | With the ruler |
|---|---|
| + C | `Step_RulerPlusC`: the Straightedge cleave at 1.3×, gold, with the "+" pop |
| Phase Shift | `Step_RulerPhaseShift`: an overhead down-chop (`Ruler_Chop`), violet |
| Area Under the Curve | `Step_RulerAreaUnderCurve`: the slam at 1.0×, 44 damage, a 3.6 m Riemann ring |
| Unit Circle | declined (empty row) |
| anything new | declined until the ruler gets a row |

### Animator
- Every step plays a state on the masked upper-body **Attack** layer (built by Setup Weapon).
- `SwordAbilitySetup.WireAttackStatesFromAssets()` adds or updates one state per step asset (clip,
  speed, foot IK off). It never removes states. It finds every step under
  `ScriptableObjects/Combat`, subfolders included, so the ruler's `Steps/Ruler/` steps get their
  `Ruler_*` states by the same path.
- The five base step assets mirror `WeaponSetup.AttackClips`' clip and speed (`Attack1–4`,
  `AirAttack`), so the two scripts agree on those states. Hit windows live only on the step
  assets; `WeaponSetup` no longer carries any.
- `WeaponSetup` calls back into `SwordAbilitySetup`, so every rebuild of the Attack layer restores
  the ability states (see §5).
- New states get their hip turn baked by `SwingTwistSetup.Bake`, which the setup menu calls.
  Without the bake they would bring back the "swatting flies" pose.
- **Full body when standing** (Step_Slash3, and the ruler's two cleaves, + C and both slams):
  - **The layer.** A step flagged `fullBodyWhenStanding` also gets a state on the full-body
    **AttackBody** layer. That layer sits at index 1: Override, no mask, weight 0, no IK pass,
    Foot IK on its states.
  - **Who builds it.** `FullBodySwingSetup.Wire` builds the layer, called from
    `WireAttackStatesFromAssets`, so every Attack-layer rebuild keeps it. The menu is
    **Tools ▸ Pickleball ▸ Setup Full-Body Swings**. It also bakes the flagged clip's
    root height into the pose. Otherwise its crouch is lost to root motion, which is off, and
    the feet float.
  - **When it plays.** `SwordAnimatorDriver` plays the flagged state there in step with the
    masked layer, if the swing starts grounded with the Animator's damped `Speed` at or under
    `standingMaxSpeed` (0.12, the same as the foot plant's). `FullBodySwing` (plain C#,
    `FullBodySwingTests`) fades the weight: in over the step's blend-in; out over
    `bodyReleaseSeconds` (0.15 s) when he moves, leaves the ground, the swing ends, or a masked
    swing chains in. It never re-engages mid-swing.
  - **What changes hands.** The masked Attack layer above still owns the arms, spine and head;
    AttackBody only gives the legs and hips. While the clip supplies the hips, SwingTwist's
    correction drops to about 0° by itself, so nothing is corrected twice. Being a full-body
    override, the layer also replaces the base layer's foot-IK pose, so ChibiFootIK's plant
    cannot pin the feet against the clip's footwork. Root motion stays off: the step's scripted
    lunge moves him, and with the clip's XZ extracted the planted feet stay put while it does.

### Carry pose

The locomotion clips hold the sword for the 0.85 m compass. With the 1.05 m, 22 cm-wide ruler
their rest arm laid the blade sideways behind the head, the idle's flourish swung it through
the head, and the run carried it through the hair (measured over the clips: the blade 16-34 cm
inside the head's clearance padding). A weapon can now bring its own rest pose for the weapon arm.

**The pose: a low trail, not a shoulder rest.** Both were solved and rendered from the game
camera, the side and the front:
- *Shoulder rest* (flat of the blade on the right shoulder, edge up, tip behind and up): on the
  chibi the shoulder joint (0.94 m) sits right under the hair, whose clearance sphere reaches
  down to 0.91 m. The best pose the solver found still touched the head's clearance padding
  (gap 0 cm standing, -0.2 cm at a walk) with the fist 15 cm off target. From the game camera the
  head hides the blade, and at a walk the chest turns it out sideways at head height.
- *Low trail* (chosen): the arm hangs relaxed with the fist beside the hip; the blade runs back
  and a little out, dropping about 12°, edge down and out so the flat tilts up toward the camera.
  From behind and above it reads as a long yellow diagonal off his right hip; from the side, as a
  blade dragged behind him. It suits a heavy buster blade, and nothing about it is near the head.

**Two poses, blended on Speed.** The arm's muscles are relative to the chest, and the locomotion
clips turn the chest anywhere from -3° to 67° (the sword-and-shield idle stands side-on at 58°;
the walk faces ahead at 5-16°). One pose authored over the idle swung the blade straight out to
his side at a walk. So the ruler has two: `Carry_Ruler_Stand`, authored over the idle's first
frame with the blade 15° out, and `Carry_Ruler_Move`, over the walk with the blade 35° out. The
Carry state is a 1D blend tree on the Animator's damped `Speed`: standing at 0, moving at the
base layer's walk threshold (0.97 m/s), the same blend the legs use for idle and walk.

**How the clips are made** (`WeaponCarryPoseBuilder`, run by Setup Weapon Carry):
- A spec gives the pose as geometry in his frame: fist position, blade direction, edge direction.
- The builder poses Chibi.prefab over the base clip in a preview scene and seats `Held_Ruler` on
  the solved grab point.
- It searches the nine right-arm muscles (deterministic coordinate descent) for the pose that
  meets the spec. The cost includes the ruler's clearance volumes plus 4 cm, a 25 cm floor margin
  for the blade, and a soft limit on the wrist muscles.
- Result: blade 0° off, fist 3-4 cm off, 6 cm outside every clearance volume.
- The clip is written in place, so re-running gives byte-identical files.

**The layer.** `Carry`, built by `WeaponCarrySetup.Wire`. `WireAttackStatesFromAssets` calls it
right after `FullBodySwingSetup.Wire`, so every Attack-layer rebuild keeps it.

| Index | Layer | Mask | Who weights it |
|---|---|---|---|
| 0 | Base Layer | none | always 1 |
| 1 | AttackBody | none (full body) | `SwordAnimatorDriver` / `FullBodySwing` |
| **2** | **Carry** | **`Carry/RightArm.mask`: the right arm only, shoulder to hand** | **`WeaponCarryPose` / `WeaponCarryBlend`** |
| 3 | Gesture | `UpperBody` | `MovementAnimatorDriver` (hook throw) |
| 4 | Attack | `UpperBody` | `SwordAnimatorDriver` |

The layer is Override, default weight 0, with no IK pass and Foot IK off on its states. It holds
one state per weapon with a carry pose (named after its standing clip) and an empty None default.
The fingers aren't in the mask: FingerCurl closes the fist after the Animator at curl weight 1,
so the fist stays shut round the ruler's handle (checked in a Play-mode close-up).

**How it combines with the rest:**
- *Swings:* Attack (and AttackBody under it) override the whole arm at full weight. The carry is
  **held at full weight under every swing**. A swing then blends in from the carry, and the
  Attack layer's 0.08 s fade at the end of the chain reveals the carry. Dropping it would show the
  clips' own arm in both blends, flashing the blade through the head.
- *Gesture:* the hook throw sits above Carry, so it wins while it plays.
- *SwingTwist* reads only the Attack layer's weight, so the carry doesn't change it.
- *Foot IK* is the base layer's IK pass. The Carry layer has none and masks no leg.
- *WeaponClearance* still runs after the Animator. In Play mode it applied 0° of correction
  through idle, walk, run, jumps, landing and the dash, because the pose already clears every
  volume.

**Weight rules** (`WeaponCarryBlend`, plain C#, `WeaponCarryBlendTests`):
- Equipping a weapon with a carry fades it in to `carryWeight` over `carryBlendSeconds`. Equipping
  one without fades it out over that weapon's blend time (the compass: 0.25 s).
- A swing or its hitstop never moves it (see above). A move that would fade it out during a swing
  waits for the swing to end.
- The moves in `WeaponCarryPose.fadeOutDuring` fade it out over `suppressSeconds` (0.1 s). It
  comes back over the weapon's blend time.
- Fades are linear and scaled by dt, so they take the same seconds at any frame rate.
- The component rebuilds the rules on enable and seeds them from the layer's current weight, so a
  script reload doesn't pop the arm.

**Which states fade it out.** They are read through `IMovementState.Current` (the MovementMotor).

| State | Carry | Why |
|---|---|---|
| Ground, Sprint, RunStop, SkidTurn | on | the point of it |
| Jump (all kinds), Fall, AirJump, SpinJump, landing | on | the blade stays clear in the air, and the trail reads |
| Dash | on | it lasts 0.22 s, and the blade trails cleanly in the dash pose |
| **Crouch** | **off** | the crouch drops the fist 0.3 m, and the trail went 16 cm into the ground. The crouch clip's own arm holds the blade forward and low |
| **Tether** (Pendulum) | **off** | the hang holds the rope overhead with both hands; the throw is on Gesture anyway |
| **Skate** | **off** | the skate clips balance with the arms out, and a trailing blade would hit the board |
| **Dive, BellySlide** | **off** | both hands reach ahead |
| Spell aiming and casting | on | magic has no arm animation. The over-the-shoulder camera sees less of the low trail than of the clips' arm, which held the blade behind the head |
| Dialogue | on | the player has no dialogue gestures; emotes drive NPCs only |

## 3. How to tune

Tune everything live in Play mode on the assets.

- **One swing's feel:** the `Step_*` asset.
  - `playbackSpeed` sets speed. Every window is a fraction, so the strike stays inside its window.
  - `windows.comboOpen` sets how early a buffered press chains. It never takes effect before
    `activeEnd` or the step's burst; the shipped steps set it equal to `activeEnd`.
  - `hit.hitstop` sets weight.
  - `hit.knockback` (m/s) mostly sets how far a target rocks or slides.
    - The base hits push 3–4 m/s, inherited from the original chain. That is fine against targets
      that stay planted: Newton ignores knockback, and the training dummy only rocks.
    - For enemies that really slide, keep mid-chain hits at or under about 1.5 m/s, as + C does, or
      a long chain shoves them out of reach before the finisher.
- **Air hang:**
  - Untick `hangInAir` on a step that should drop instead, such as a plunge finisher. The flag is
    ignored on the ground chain.
  - `MeleeCombat.airHangPerHitSeconds` (s) is how long one landed air hit holds you up. It has to
    cover the gap to the next hit in the chain, about 0.45–0.6 s with the shipped steps, or a mashed
    chain dips between hits. Shorter rewards only a tight combo; 0 turns the hang off.
  - How quickly a rising or falling jump is stopped is `MovementConfig.anchorHaltRate` (m/s²),
    shared with the Gravity Anchor.
- **Finisher rings:** the `burst` block.
  - `radius` (m) and `expandSeconds` (s): together these set how readable and jumpable the ring is.
  - `triggerAt`: line it up with the clip's impact frame.
  - `heightTolerance` (m): small for a ground wave, large for an air spin.
- **Whole-body swings:** tick `fullBodyWhenStanding` on a step whose clip is a whole-body move
  (a lunge, a step-in) that the upper body alone wrenches over standing legs. Then run **Setup
  Full-Body Swings**.
  - On `SwordAnimatorDriver`: `standingMaxSpeed` (m/s) decides what counts as standing, and
    `bodyReleaseSeconds` (s) how fast the legs hand back to locomotion. A shorter release pops; a
    longer one drags the lunge into the first stride.
- **Carry pose:**
  - On the `Weapon_*` asset: `carryWeight` (0-1) and `carryBlendSeconds` (s; the ruler 0.3).
  - On the player's `WeaponCarryPose`: `fadeOutDuring` (the moves that drop it) and
    `suppressSeconds` (0.1 s).
  - The pose itself is a spec in `WeaponCarryPoseBuilder` (`RulerCarryStanding`,
    `RulerCarryMoving`): fist position, blade and edge directions, floor margin. After
    changing one, run **Tools ▸ Pickleball ▸ Rebuild Carry Poses**.
- **Look:** the `presentation` block.
  - `trailColor`, `shakeAmount` and `shakeSeconds`, `fovPunch`.
  - `SwordEffects.lastHitShakeScale` boosts every chain's last hit.
- **Chain shape:** `Moveset_Compass` sets the base hits; an ability's `chainOrder` and `priority`
  set where it lands.
- **Pace of levelling:** on `Leveling_Player`:
  - `baseXp` sets the pace of the whole curve;
  - `growth` above 1 makes late levels grind;
  - `xpPerDamage` and `xpPerDefeat` set earning. Each target pays them out once per session (see
    `XpLedger`);
  - the `unlocks` table sets the order.
- **Input forgiveness:** on `MeleeCombat`:
  - `pressBufferSeconds` is how long a press made mid-dash or in hitstop is remembered;
  - `airborneGraceSeconds` absorbs grounded flicker on slopes. It decides both which chain a press
    continues and when the ground chain drops, so a press this soon after a real jump still plays a
    ground step.
- **Testing late unlocks:**
  - Raise `PlayerLevel.startingLevel`.
  - In Play mode, use **PlayerLevel ▸ ⋮ ▸ Debug: Gain One Level** or **Debug: Max Level**.
  - To test a moveset without levelling, add abilities to `SwordAbilities.startingAbilities`.

### Ruler tuning

The ruler's steps are `ScriptableObjects/Combat/Steps/Ruler/Step_Ruler*`, tuned the same way as
the compass's: speed and windows, not code. `RulerMovesetAssetTests` (category `Assets`) pins the
brief against the compass. If you change the brief on purpose, update its ranges:
- each ground hit deals 2–2.5× the compass hit in its slot;
- the mashed ground chain is 1.4–1.7× as long;
- blade damage per second is 0.95–1.2× the compass's;
- every wind-up is at least 1.4× as long, and every hitstop at least 1.5× as long.

**Measured values, from the step assets** (`RulerMovesetAudit.TimingTable`, written by
`-executeMethod RulerMovesetSetup.BatchVerify -timingOut <file>`). Wind-up is the time until the
blade goes live; "chain at" is when a buffered press starts the next step (for a finisher, when
the chain ends). Hitstop is per connecting hit.

| Slot | Step | Clip (take) | Speed | Duration s | Active | Wind-up s | Chain at s | Damage | Knockback m/s | Hitstop s | Shake | FOV ° | Lunge m/s | Ring |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Compass** ground 1 | Step_Slash1 | slash (5) | 2.5 | 0.547 | 0.32–0.60 | 0.175 | 0.328 | 10 | 2 | 0.05 | 0.15 | 0 | 1.5 | |
| ground 2 | Step_Slash2 | attack (3) | 2.5 | 0.693 | 0.35–0.62 | 0.243 | 0.430 | 12 | 2 | 0.05 | 0.15 | 0 | 1.2 | |
| ground 3 | Step_Slash3 | slash | 2.4 | 0.625 | 0.34–0.62 | 0.213 | 0.388 | 14 | 3 | 0.07 | 0.15 | 0 | 1.8 | |
| finisher | Step_Slash4 | slash (3) | 2.2 | 0.712 | 0.34–0.64 | 0.242 | 0.678 (end) | 20 (+12.8 up) | 8 | 0.10 | 0.25 | 0 | 0 | |
| air | Step_AirSlash | attack (4) | 2.2 | 0.455 | 0.25–0.60 | 0.114 | 0.455 (end) | 12 | 3 | 0.05 | 0.15 | 0 | 0 | |
| + C | Step_PlusC | slash (5) | 3.0 | 0.456 | 0.30–0.58 | 0.137 | 0.264 | 13 | 1.5 | 0.05 | 0.15 | 0 | 0 | |
| Area Under the Curve | Step_AreaUnderCurve | slash (3) | 2.0 | 0.783 | 0.34–0.64 | 0.266 | 0.744 (end) | 20 | 8 | 0.10 | 0.30 | 4 | 4 | 3 m, 12 dmg |
| **Ruler** ground 1 | Step_RulerCleave | Ruler_Cleave | 1.15 | 0.928 | 0.41–0.61 | 0.380 | 0.649 | 24 | 1.5 | 0.09 | 0.22 | 2 | 2.5 | |
| ground 2 | Step_RulerBackCleave | Ruler_BackCleave | 1.05 | 0.857 | 0.42–0.81 | 0.360 | 0.720 | 27 | 2 | 0.09 | 0.26 | 2.5 | 1.5 | |
| finisher | Step_RulerSlam | Ruler_Slam | 1.1 | 1.667 | 0.43–0.57 | 0.717 | 1.450 (end) | 40 | 9 | 0.16 | 0.45 | 6 | 5 | 2.6 m @0.53, 12 dmg, kb 6, up 3 |
| air | Step_RulerSpin | Ruler_Spin | 1.9 | 0.825 | 0.24–0.93 | 0.198 | 0.825 (end) | 24 | 3 | 0.09 | 0.25 | 3 | 0 | |
| + C | Step_RulerPlusC | Ruler_Cleave | 1.3 | 0.821 | 0.41–0.61 | 0.336 | 0.574 | 22 | 1.2 | 0.08 | 0.20 | 1.5 | 2 | |
| Phase Shift | Step_RulerPhaseShift | Ruler_Chop | 1.3 | 0.692 | 0.36–0.54 | 0.249 | 0.402 | 26 | 2.5 | 0.09 | 0.25 | 2 | 0 | |
| Area Under the Curve | Step_RulerAreaUnderCurve | Ruler_Slam | 1.0 | 1.833 | 0.43–0.57 | 0.788 | 1.595 (end) | 44 | 10 | 0.18 | 0.50 | 7 | 5.5 | 3.6 m @0.53, 16 dmg, kb 7, up 3 |

| Chain (mashed, one target) | Hits | Time s | + hitstop s | Blade damage | + rings | DPS blade | DPS + rings |
|---|---|---|---|---|---|---|---|
| Compass ground, Lv 1 | 4 | 1.822 | 2.092 | 56 | 56 | 26.8 | 26.8 |
| Ruler ground, Lv 1 | 3 | 2.819 | 3.259 | 91 | 103 | 27.9 | 31.6 |
| Compass ground, all learned | 5 | 2.154 | 2.554 | 69 | 81 | 27.0 | 31.7 |
| Ruler ground, all learned | 4 | 3.539 | 4.079 | 117 | 133 | 28.7 | 32.6 |
| Compass air, Lv 1 | 1 | 0.455 | 0.505 | 12 | 12 | 23.8 | 23.8 |
| Ruler air, Lv 1 | 1 | 0.825 | 0.915 | 24 | 24 | 26.2 | 26.2 |
| Compass air, all learned | 3 | 1.283 | 1.533 | 42 | 52 | 27.4 | 33.9 |
| Ruler air, all learned | 2 | 1.459 | 1.639 | 50 | 50 | 30.5 | 30.5 |

**Why DPS stays close to 1×.** The ruler pays in commitment: a wind-up 1.5–3× as long before
anything can be hit, chain windows that open only after the follow-through (`comboOpen` past
`activeEnd`), a third fewer hits, and so fewer moments to react or cancel. A higher DPS on top of
that would make the compass pointless; a lower one would make the ruler a trap. On one target it
ends up 1.04× on the blade, 1.18× when the slam's ring also lands. The ring is the reward for
finishing the whole chain, and it does more against a crowd.

**Tuning notes**
- **Knockback:** mid-chain hits stay at 1.5–2 m/s, under the "about 1.5" guidance plus a
  little, so a sliding target stays in reach of the long arcs. The weight comes from hitstop,
  shake and FOV punch. The finisher pushes 9 m/s and its ring 6 m/s outward with a 3 m/s pop.
- **Lunges:** the cleaves step in (2.5 and 1.5 m/s). Straightedge's arc crosses his front low,
  1.3 m out; at 1 m/s it fell short of the training dummy from where the compass's opener reaches
  it (play test r1). The slams lunge 5–5.5 m/s, which carries the leap forward.
- **Ring timing:** `burst.triggerAt` 0.53 is the slam's landing (source frame 0.535).
- **Clips are cut by take, not by window.** The `Ruler_*` takes (`RulerMovesetSetup.Takes`) trim
  the source FBXs as fractions of their default take; change the cut there and re-run the menu.
  Windows are fractions of the trimmed take.

## 4. How to extend

**Add a new slash (Combo Plus, air hit, finisher or opener) in four steps:**
1. **Create the step.** Right-click in `ScriptableObjects/Combat/Steps` ▸ **Create ▸ Pickleball ▸
   Combat ▸ Combo Step**. Give it a **unique `animatorState` name**, a Humanoid clip, a speed and
   windows.
2. **Create the ability.** **Create ▸ Pickleball ▸ Combat ▸ Sword Ability** in `Abilities/`. Set its
   role, track and step, plus `chainOrder` (extenders) or `priority` and `condition`
   (finishers / openers).
3. **Schedule it.** Add a row to `Leveling_Player.unlocks` with a level, or grant it from
   anywhere with `SwordAbilities.Grant` (for example a boss drop).
4. **Run Tools ▸ Pickleball ▸ Setup Sword Abilities.** It creates the Attack-layer state and bakes
   its swing twist.

That needs no code. New *behaviour* is small and contained:
- **A new condition** (target airborne, surrounded, player dashing): add a field to `ComboContext`,
  fill it in `MeleeCombat.BuildContext`, and check it in `ComboCondition.IsMetBy`. Add a resolver
  test.
- **A new effect glyph** ("!" for Factorial, "∴" for Corollary): build a prefab with a `PopFlashEffect`,
  or a new `PooledEffect` subclass, and drop it in the step's `presentation.hitEffect`.
- **A movement-coupled slash** (dive slash, spin slash, plunge, air hang time): add what you need
  to `ICombatMovement` or `ICombatImpulses` and one line to `ChibiMovementAdapter`.
  - The merged movement rework already exposes the levers: `SetVerticalVelocity`, `SuspendGravity`,
    and `ChibiLocomotion.Movement`.
  - See open questions.

**Give a new weapon its own moveset** (as the ruler did):
1. **Measure, don't guess.** Import candidate clips (`RulerMovesetSetup.ImportCandidatesForAudit`
   does the Generic ones; revert the .meta files you don't keep) and run
   `RulerMovesetAudit.Batch` with `-auditOut <csv>`. Per clip it reports the strike's wind-up end,
   peak and end (fractions), the number of strikes, the tip's peak speed, arc and direction, the
   hip turn, the foot lift and how close the blade comes to the head. It also writes per-sample
   traces. `RulerMovesetPreview.Batch` renders contact sheets (three views, 8 times, the active
   window outlined in red).
2. **Cut takes** out of FBXs that nothing else imports. WeaponSetup re-imports the compass's five
   with their default take only, which deletes any extra take cut from them.
3. **Seed steps and a moveset** from a setup class like `RulerMovesetSetup`: steps under
   `Steps/<Weapon>/`, state names prefixed `<Weapon>_`. Flag a step `fullBodyWhenStanding` when
   its clip turns the hips more than about 90° or moves the legs.
4. **Decide the ability rules:** turn `acceptOtherAbilities` off, and list a row per ability
   with the weapon's own step, or an empty step to decline it.
5. Run the menu, then **Setup Sword Abilities** on each scene with a player, to bake swing twist.

**Give a weapon a carry pose** (as the ruler did):
1. Add a `Spec` to `WeaponCarryPoseBuilder` with the weapon's held prefab and clearance
   profile, or two: standing over the idle and moving over the walk. Add a `BuildPose` line for
   each in `WeaponCarrySetup.EnsureAssets` and `RebuildPoses`.
2. Render it before you commit to it: `WeaponCarryPoseBuilder.Solve(spec, write: false, out r)`,
   then `RenderSheet` over any base clips (game camera, side, front three-quarter). The log
   gives the blade, edge and fist errors, the worst clearance gap and the lowest blade point.
3. Set the weapon's `carryPose` and `carryPoseMoving` (or seed them in `EnsureAssets`), then run
   **Setup Weapon Carry**. It adds the Carry state; the component needs no change.

**Using a clip from the pack that isn't wired yet:** 44 of the 49 Mixamo sword clips are still
Generic and must be re-imported as Humanoid with `WeaponSetup.ImportAttackClips`' settings first.
The setup menu warns about this. Candidates from the clip audit (2026-10-26, measured from the
FBX, not eyeballed):

| Clip | Length | Looks like | Use |
|---|---|---|---|
| `attack (2)` | 1.30 s | one fast strike peaking at 0.40, direction A | a distinct 5th ground hit |
| `attack` | 2.33 s | leap, then a big down-chop at 0.49 | leap-slam finisher; trimmed from the apex, an air plunge |
| `slash (4)` | 2.43 s | rising slash, then a jump spin down-chop | second air hit / spin-chop |
| `slash (2)` | 3.53 s | four strikes in a whirlwind | whirlwind finisher (needs several active windows) |
| `power up` / `casting` | 2.37 / 2.97 s | planted charge / sword plant | charge-up or Explosion wind-up |
| `kick` | 1.20 s | right-foot kick | opener / guard break (full-body only) |

**The ruler's takes** (measured 2026-10-29 on the chibi rig with a 0.9 m blade; fractions of the
source take; "hips" is the largest hip turn, which decided the full-body flags):

| Take | Source, cut | What it is | Strike (of the take) | Tip arc / peak speed at 1× | Hips | Feet |
|---|---|---|---|---|---|---|
| `Ruler_Cleave` | `attack (2)` 0.05–0.87 | 270° flat cleave, his left round to his right, tip 1.3 m out; the tail after 0.87 is a still hold | 0.42–0.60, peak 0.50 | 266°, 37 m/s | 180° | planted |
| `Ruler_BackCleave` | `slash (2)` 0.06–0.31 | the whirl's first strike: a spin, then a diagonal cleave from his right down to his left | 0.42–0.81, peak 0.51 | 352°, 33 m/s | 133° | planted |
| `Ruler_Spin` | `slash (2)` 0.06–0.50 | both strikes of the whirl, all the way round | 0.23–0.93 (two strikes) | 363°, 32 m/s | 133° | (air) |
| `Ruler_Slam` | `attack` 0.12–0.90 | crouch, leap (hips +0.5 m, feet +0.8 m), blade raised behind the head, down-chop landing at 0.53, crouch hold | 0.43–0.56, peak 0.48 | 189°, 23 m/s | 63° | leap in the pose |
| `Ruler_Chop` | `slash (4)` 0.42–0.80 | overhead, straight down-chop | 0.29–0.43, peak 0.38 | 156°, 30 m/s | 131° | (air) |

Rejected: `casting` (a sword plant: the blade goes through the face and into the floor),
`power up` and `kick` (short, low arcs), and `slash (2)` whole (one window can only hit a target
once, so its second and third strikes would be for show). None of the pack's unused clips is a
wide right-to-left cleave, so Reflex Angle uses the whirl's first strike.

### Ability roadmap (math-themed, from the brainstorm)

A boss drop can grant an ability early; its level is the fallback.

| Lv | Ability | KH analogue | Role | Alt boss drop |
|---|---|---|---|---|
| 2 | **+ C** ✅ | Combo Plus | ground extender | — |
| 3 | **Phase Shift** ✅ | Air Combo Plus | air extender | — |
| 4 | **Unit Circle** ✅ | Aerial Sweep | air finisher (360° ring) | Hypatia |
| 5 | **Area Under the Curve** ✅ | Ripple Drive | ground finisher (Riemann ring) | Riemann |
| 6 | **Induction Step** | Combo Plus | ground extender (true for n ⇒ n+1) | — |
| 7 | **Secant Line** | Sliding Dash | ground opener, target 3–7 m away | — |
| 8 | **Stationary Point** | Air Combo Plus | air hit with hang time (dy/dt = 0) | — needs movement hook |
| 9 | **Hypotenuse Cut** | — | attack out of the 45° dash | Pythagoras |
| 10 | **Unit Vector** | Aerial Dive | air opener, homes in on a target 3–8 m away | — |
| 11 | **Vertical Asymptote** | plunge | air finisher, slam to the ground | Newton |
| 12 | **Tangent Line** | ranged finisher | 6 m piercing line off the blade arc | — |
| 12 | **Factorial!** | Explosion | finisher when ≥2 enemies are near; damage = 4 × hits this chain | — |
| 13 | **Gradient Ascent** | Upper Slash | launcher (crouch + attack) | — |
| 14 | **Corollary** | Finishing Plus | a second finisher follows | Al-Khwarizmi |
| 15 | **Golden Spiral** | Aerial Spiral | air finisher on a juggled target, Fibonacci damage | Fibonacci |
| 15 | **Imaginary Unit** | — | each finisher echoes 0.2 s later, rotated 90° (×i) | Euler |
| — | **Mod 3** | Combo Boost-ish | every 3rd hit ×1.5 | Galois |
| ∞ | **The Limit Does Not Exist** | Berserk Charge | ground chain never ends at low HP | Singularity |

## 5. Known limitations and open questions

- **The ruler (2026-10-29): verified headless, not hand-played.**
  - EditMode: the whole suite passed headless (1664 run, 1659 passed, 0 failed, 5 skipped), 112
    of them in `Tests/EditMode/Combat/`. Re-running Setup Ruler Moveset changes nothing.
  - Play mode: `RulerMovesetScenario` (a harness scenario kept out of this branch, because `Tests/PlayMode/` belongs to no one here; the copy is in the session scratchpad) put the ruler on the
    TrainingStage player in memory, with a 0.78 m stand-in blade and swing twist baked locally.
    Against the floor dummy, mashed:
    - Hits landed 0.47–0.48 s into Straightedge (24), 0.48–0.50 s into Reflex Angle (27),
      0.42 s into + C (22), and 0.82 s into Plumb Line (40) with the ring at 1.08 s (12).
      Area Under the Curve: 0.90 s (44), ring 1.18 s (16). The air spin: 0.30 s (24).
    - Each swing lasted 0.75 / 0.83 / 0.67 (+ C) / 1.71–1.90 s, against the compass's
      0.38 / 0.48 / 0.48 / 0.80 s in the same run. These match `ComboTiming` within a frame
      plus hitstop.
    - The full-body layer reached 1.0 on every grounded ruler swing. The feet stayed within 1 cm
      of the stance on both cleaves and + C; the slams lift them 0.79 m in the pose (the leap).
    - With the first tuning, Straightedge (lunge 1 m/s) missed the dummy from the compass
      opener's distance. Its lunge is now 2.5 m/s and it hits.
  - Contact sheets (stand-in blade): big, readable arcs. Straightedge is the cleanest. Reflex
    Angle's spin carries the blade across the face around n≈0.2, and the slam raises it past the
    top of the head around n≈0.45.
- **Ruler: blade near the head.** With a 0.9 m blade on the raw takes, the blade enters a sphere
  fitted to the chibi's head for 5% of Straightedge's samples, 36% of Reflex Angle's, 21% of the
  spin's, 39% of the slam's and 29% of the chop's. The compass's raw clips score 11–51%, so they
  are no worse, but runtime `WeaponClearance` has to push the longer blade further. That depends
  on the ruler's clearance profile (`RulerWeaponClearance.asset`, owned by the grip work). Check
  Reflex Angle and the slam's raise once the real `Held_Ruler` is on the hand.
- **Ruler: the slam's leap is pose only.** The controller stays on the ground while the pose
  rises 0.8 m and lands, so it reads as a hop-slam, but nothing can pass under him. When he is
  moving it plays masked, and the legs keep running under the chop.
- **Ruler: masked when moving.** Straightedge turns the hips 178° (as the compass's Attack2
  does), so swung while moving, that turn goes into the waist. Full body covers standing only.
- **Ruler carry (2026-10-30): fixed by the Carry layer** (§2, "Carry pose").
  - Verified in Play mode in ChibiSandbox: equip, idle, walk, run, stop, standing and running
    jumps, landing, crouch, dash, skate, tether, the whole ground chain, a switch to the compass
    mid-swing, and back. 0 errors, 0 warnings, and WeaponClearance at 0° while the carry showed.
  - Still open: the ruler's swings snap back to the carry in the Attack layer's 0.08 s fade
    (`SwordAnimatorDriver.layerFadeSpeed`, shared with the compass). A per-weapon release time
    would let a heavy blade settle more slowly.
  - Still open: the arm is chest-relative, so it still turns with the idle's flourish (chest
    13-67°). The trail points 15-60° out over the idle cycle, and straight back at its most
    turned frame, where it reads foreshortened from directly behind.
  - Still open: in a hard landing the blade comes within about 9 cm of a flat floor, so on a
    steep down-slope it may touch. Nothing lifts it for terrain.
  - Crouch, skate, tether, dive and belly slide keep the clips' arm, which WeaponClearance
    corrects as before (up to 36° in the tether hang).
- **Ruler: Phase Shift's hang gap.** A Full Rotation hit can land as early as 0.2 s in, and
  Phase Shift's chop connects about 0.25 s after it starts. If the spin connects early, the gap
  can reach about 0.8 s, longer than `airHangPerHitSeconds` (0.6), so he may dip between them.
- **Abilities learned mid-chain re-resolve the chain (existing behaviour, seen with the ruler).**
  In play test r1, the slam's hits levelled the player up and taught + C. The chain became four
  long, so a buffered press played the slam again at index 3. The compass does the same thing
  if it levels up mid-finisher. A fix would freeze the learned set per chain in `MeleeCombat`.

- **Not play-tested yet.**
  - Verified in a headless Unity 6000.3.5f2 on the branch's own worktree:
    - 2026-10-26: Setup Sword Abilities ran clean, adding the 4 states and baking swing twist for
      all 9 swings.
    - 2026-10-27, after the QA fixes: all 198 EditMode tests passed, 61 of them in
      `Tests/EditMode/Combat/`.
    - 2026-10-27, after the `ClosestPoint` fix: 278 run, 270 passed, 0 failed, 64 of them combat.
      The 8 skipped are outside combat. The run logged no `ClosestPoint` warnings.
  - Nobody has watched the new swings in Play mode. The new states reuse clips that are already
    wired, at their own speeds: + C is Attack1's sweep at 3×, Phase Shift is the downstroke in the
    air, Unit Circle is the wide sweep, and Area Under the Curve is the launcher at 2×.
  - Expect a tuning pass on windows and speeds, and a decision on swapping in the dedicated clips
    above.
- **Rebuilds keep the combo:**
  - `WeaponSetup.AttachToPlayer` calls `SwordAbilitySetup.AttachToPlayer` last, after its SwingTwist
    bake and weapon clearance. That covers Setup Weapon and Setup Chibi Sandbox.
  - `WeaponSetup.RewireControllerFromImportedClips` calls `WireAttackStatesFromAssets`, which covers
    Rebuild Chibi Animator.
  - Run the menu by hand only after editing the step assets.
- **Swing twist puts the clip's whole hip turn into the waist.** `SwingTwist` faces the chest the
  way each clip authored it, while the legs stay with locomotion. The cost is the waist. This was
  measured on 2026-10-28 in a preview scene, with the Attack layer masked over the idle stance and
  the baked tracks applied, as at runtime.

  The chest's rotation relative to the hips peaks at this many degrees away from idle:

  | Swing | In the clip | At runtime |
  |---|---|---|
  | Attack1 | 50° | 82° |
  | Attack2 | 22° | 170–180° (it spins) |
  | Attack3 | 47° | 109° |
  | Attack4 | 35° | 139° |

  - Attack3 pairs that twist (up to −126° of yaw between the shoulder line and the hips) with the
    largest sideways bend at the waist of the four: 42°, against Attack1's 36° and at most 25°
    for the others. It holds that pose from n≈0.45 to 0.70, which covers the back half of its
    active window. The clip itself bends only 29°, because its hips tilt and drop 27 cm into a
    lunge.
  - The same investigation ruled these out, for every swing:
    - the bake: it matches the clip to 0°;
    - hemisphere flips: none between neighbouring samples;
    - long-way slerps: Unity's `Slerp` takes the short arc, and `SwingTwistMathTests` pins it;
    - pops at chain crossfades: in a frame-by-frame 1→2→3→4 run the chest never moved more than
      23° a frame;
    - the hips or legs moving under the masked layer: 0 cm, 0°;
    - clip import, mirror or state flags: identical across the four.
  - `WeaponClearance` adds up to 57° during Attack3's overhead wind-up. Attack1 gets more.
  - Swing 3 has been reported as "the whole body contorts". The playtest
    `SwingTwistScenario.Swing3Contortion` (see `Docs/Testing/PlaytestHarness.md`) confirmed it
    on 2026-10-28, with a standing 1-2-3 chain:
    - Swing 3 is the only swing with frames where the twist is over 100° and the sideways bend
      over 30° at the same time: 6 of its 38 frames, n≈0.45–0.6. Swings 1 and 2 have none.
    - Strength 0 removes those frames: twist 63°, bend 29°. But the chest then faces 34° off the
      clip on swing 3 and 81° off on swing 2, the old "swatting flies".
    - Strength 0.5 still bends swing 3 by 38°.
    - Step_Slash3's lunge of 0 leaves the pose unchanged (121°, 41°), but removes the 14
      frames of re-plant steps the 16 cm lunge forces during the strike.
  - **Fixed** by playing it full-body when standing (§2 Animator). Same scenario, same
    standing chain:
    - Swing 3's twist fell from 126° to 66° and its sideways bend from 41° to 29°. Those are
      the clip's own values, with 0 wrenched frames.
    - SwingTwist's correction is under 0.5° while the layer is at full weight.
    - Walking, swing 3 stays masked, exactly as before.
    - Starting to walk mid-swing hands the legs back over 0.15 s. Its worst single-frame foot
      jump was 12 cm, against 47 cm for the masked swing in the same test.
  - **Still open:**
    - Swung while moving, swing 3 still twists the waist about 97°.
    - Swings 1, 2 and 4 are unchanged; they read as a sweep, a spin and a launcher.
    - The full-body downstroke is a deep lunge; the back knee nearly touches the floor on the
      chibi.
- **Two sources for the base states:** `WeaponSetup.AttackClips` still builds the `Attack1–4` and
  `AirAttack` states, and the base step assets re-apply their own clip and speed straight after. A
  later cleanup could build the layer from the step assets alone.
- **Behaviour change at level 1:** the air attack used to be repeatable while falling. It is now one
  air chain per airtime (KH-faithful): one hit at level 1, two at level 3. If that feels stingy,
  give the base air chain a second hit, or refill it on double jump. Don't remove the rule:
  without it Air Combo Plus is meaningless.
- **Gamepad:** the sword attack (`SwordInput`) is Gamepad `buttonWest` and nothing else uses it.
  The skateboard Mount moved to D-pad Right in the movement rework.
- **Hang time is in; plunges are not yet:**
  - Landed air hits hold you up through `ICombatImpulses.SuspendGravity` (see `AirHang`). Every air
    step already hangs this way, so "Stationary Point" (Lv 8) needs a new identity: a hang that
    doesn't need a hit, a longer per-hit hold, or a hop up.
  - The first hit of a jump-attack usually connects while you are still rising, so the hang first
    stops the rise, over about 0.3 s from a full jump.
  - The hang reuses the Gravity Anchor's halt rate. If combat wants its own, it is a new
    `MovementConfig` field or a rate argument on `SuspendGravity`.
  - Plunges ("Vertical Asymptote") still need `SetVerticalVelocity` on `ICombatImpulses` and the
    adapter, plus a per-step vertical speed. They should untick `hangInAir`.
  - Dive- and spin-slashes still need `Movement.IMovementState` (MoveKind Dive, SpinJump…) wrapped
    in `ICombatMovement`.
- **Lock-on attack facing:** `ChibiLocomotion` still falls back to reading `MeleeCombat.IsAttacking`.
  - Since the merge it offers `SetCombatFacingSource(IFacingSource)`: a source's
    `TryGetFacingTarget(out point, out urgent)` beats the lock-on square-up, and urgent uses the fast
    attack turn.
  - Combat hasn't registered a source yet. Next step: a small one (the lock-on aim point while
    `IsAttacking`, urgent), registered from the setup menu. After that, locomotion can drop the
    fallback.
- **Costs of the QA fixes (2026-10-27):**
  - Mashed chains are a little slower, because each hit now keeps its whole blade window.
  - A jump-attack pressed within 0.08 s of take-off plays a ground step.
  - A boss rematch pays no new Lemmas.
- **Two unlock sets:**
  - `SwordAbilities` (assets) sits alongside `PlayerAbilities` (the fixed `AbilityKind` enum
    locomotion switches on). They share the Grant / Has / Unlocked contract, and the toast listens
    to both.
  - Folding them into one asset-based registry is worth doing once movement abilities become
    assets too.
- **No persistence:** level, Lemmas and learned abilities reset every session.
- **No poise or stagger:** KH bosses break out of long combos (its "revenge value"). Longer chains
  plus finisher rings will stunlock anything that respects knockback; Newton ignores it today.
- **KH2's fallback rule isn't copied:** in KH2, a lone equipped finisher ignores its own condition.
  Here conditions are always honoured, and the default finisher covers the gap. This is
  deliberate: it keeps a press predictable.
- **Ability equip / AP:** everything learned is always on. KH-style toggles would sit on
  `SwordAbilities` (a `disabled` set the resolver skips) with a menu on top.
- **The clip pack is direction-limited:** with fixed clips, swing direction can't alternate at
  every chain length. Extenders sit before the B-direction finisher, so + C is an A-direction
  sweep, and two A hits (Downstroke, then + C) meet on different planes.
