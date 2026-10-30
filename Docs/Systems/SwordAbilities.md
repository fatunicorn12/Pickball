# Sword Abilities

A Kingdom-Hearts-style sword ability system. As the player levels up, the combo gains slashes,
the air gets its own chain, and finishers with special effects cap both chains.

- **Code:** `Assets/_Project/Scripts/Sandbox/MeleeCombat.cs` and `Scripts/Sandbox/Combat/`, which
  contains `Progression/` and `Presentation/`.
- **Data:** `Assets/_Project/ScriptableObjects/Combat/`.
- **VFX:** `Assets/_Project/Art/VFX/Sword/`.
- **Setup:** `Scripts/Editor/SwordAbilitySetup.cs`, run from **Tools ▸ Pickleball ▸ Setup Sword Abilities**.
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
  - The resolver returns an invalid step once the chain is spent.
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
- **`SwordMoveset`** (`Moveset_Compass.asset`): the base ground chain plus its default finisher,
  and the base air chain plus an optional finisher.
- **`LevelingConfig`** (`Leveling_Player.asset`):
  - the curve: max level, base XP, growth;
  - earning: Lemmas per HP, defeat bonus;
  - the level → ability **unlock table**.

  The table lives here, not on the ability, so a different table can teach the same abilities in
  a different order. This is the same idea as KH's Dream Sword / Shield / Rod choice.

### Animator
- Every step plays a state on the masked upper-body **Attack** layer (built by Setup Weapon).
- `SwordAbilitySetup.WireAttackStatesFromAssets()` adds or updates one state per step asset (clip,
  speed, foot IK off). It never removes states.
- The five base step assets mirror `WeaponSetup.AttackClips`' clip and speed (`Attack1–4`,
  `AirAttack`), so the two scripts agree on those states. Hit windows live only on the step
  assets; `WeaponSetup` no longer carries any.
- `WeaponSetup` calls back into `SwordAbilitySetup`, so every rebuild of the Attack layer restores
  the ability states (see §5).
- New states get their hip turn baked by `SwingTwistSetup.Bake`, which the setup menu calls.
  Without the bake they would bring back the "swatting flies" pose.
- **Full body when standing** (Step_Slash3 only, so far):
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
