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
- **The ground chain resets whenever a swing ends without a follow-up press.**
- **Swinging still never moves or stops you.** The only exception is a step's one-off lunge
  impulse; Area Under the Curve steps in 2.5 m/s.

With the shipped curve (base 50 Lemmas, growth 1, 1.5 Lemmas per HP of damage), levels 2–5 cost
50, 150, 300 and 500 Lemmas in total. That is roughly 34, 100, 200 and 333 HP of sword damage,
so the whole slice unlocks inside one Newton fight (600 HP), with a +250 defeat bonus on top.

## 2. Architecture

```
SwordInput ──AttackPressed──▶ MeleeCombat ◀── IMovementState / IMovementImpulses ── ChibiMovementAdapter ── ChibiLocomotion
                                   │  asks ComboResolver(context, SwordMoveset, SwordAbilities.All)
                                   │  owns SwingTimeline (clock) + ComboProgress (chain counters)
                                   │  sweeps the blade, runs AreaBurst rings, deals damage via IDamageable
                                   ├─ SwingStarted ──────▶ SwordAnimatorDriver (crossfade, layer weight, hitstop freeze)
                                   ├─ BladeLiveChanged ──▶ SwordEffects (trail on/off)
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
- **Movement** is read only through `IMovementState`, and pushed only through
  `IMovementImpulses`, which offers a single impulse and a brief move-lock. `ChibiMovementAdapter` is
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
| `ChibiMovementAdapter` | Implements `IMovementState` and `IMovementImpulses` over ChibiLocomotion's public API. | adapter |
| `PlayerLevel` | Converts Lemmas into levels. Raises `XpGained` and, once per level gained, `LeveledUp(new, previous)`. | progression |
| `CombatXpSource` | Turns `HitLanded` into Lemmas, plus a defeat bonus when a `Health` dies on that hit. | progression |
| `LevelUnlocks` | Grants the abilities listed in the level table as levels are reached. | progression |
| `SwordAnimatorDriver` | Crossfades the step's Attack-layer state, fades the layer weight, freezes `animator.speed` during hitstop. | presentation |
| `SwordEffects` | Blade trail tint and emit, impact sparks, pooled hit and burst effects, camera shake and FOV kick. | presentation |
| `ProgressionToast` | Shows the Q.E.D. / New-technique banner queue and the `LV n · x / y Lemmas` readout (on the `SwordHUD` canvas). | presentation |

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
  signals. A long frame that crosses the whole active window still reports both edges, so the
  blade still gets swept.
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
- The five base step assets mirror `WeaponSetup.AttackClips` exactly (`Attack1–4`, `AirAttack`),
  so the two scripts agree on those states.
- `WeaponSetup` calls back into `SwordAbilitySetup`, so every rebuild of the Attack layer restores
  the ability states (see §5).
- New states get their hip turn baked by `SwingTwistSetup.Bake`, which the setup menu calls.
  Without the bake they would bring back the "swatting flies" pose.

## 3. How to tune

Tune everything live in Play mode on the assets.

- **One swing's feel:** the `Step_*` asset.
  - `playbackSpeed` sets speed. Every window is a fraction, so the strike stays inside its window.
  - `windows.comboOpen` sets how early a buffered press chains.
  - `hit.hitstop` sets weight.
  - `hit.knockback`: keep mid-chain hits at or under about 1.5 m/s, or a long chain shoves the target
    out of reach.
- **Finisher rings:** the `burst` block.
  - `radius` (m) and `expandSeconds` (s): together these set how readable and jumpable the ring is.
  - `triggerAt`: line it up with the clip's impact frame.
  - `heightTolerance` (m): small for a ground wave, large for an air spin.
- **Look:** the `presentation` block.
  - `trailColor`, `shakeAmount` and `shakeSeconds`, `fovPunch`.
  - `SwordEffects.lastHitShakeScale` boosts every chain's last hit.
- **Chain shape:** `Moveset_Compass` sets the base hits; an ability's `chainOrder` and `priority`
  set where it lands.
- **Pace of levelling:** on `Leveling_Player`:
  - `baseXp` sets the pace of the whole curve;
  - `growth` above 1 makes late levels grind;
  - `xpPerDamage` and `xpPerDefeat` set earning;
  - the `unlocks` table sets the order.
- **Input forgiveness:** on `MeleeCombat`:
  - `pressBufferSeconds` is how long a press made mid-dash or in hitstop is remembered;
  - `airborneGraceSeconds` absorbs grounded flicker on slopes, which would otherwise drop a ground
    chain.
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
  to `IMovementState` or `IMovementImpulses` and one line to `ChibiMovementAdapter`, once the
  movement rework exposes it. See open questions.

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
  - Verified on 2026-10-26 in a headless Unity 6000.3.5f2 on the branch's own worktree:
    - Setup Sword Abilities ran clean, adding the 4 states and baking swing twist for all 9 swings.
    - All 115 EditMode tests passed, 41 of them in `Tests/EditMode/Combat/`.
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
- **Two sources for the base states:** `WeaponSetup.AttackClips` still builds the `Attack1–4` and
  `AirAttack` states, and the base step assets re-apply their own clip and speed straight after. A
  later cleanup could build the layer from the step assets alone.
- **Behaviour change at level 1:** the air attack used to be repeatable while falling. It is now one
  air chain per airtime (KH-faithful): one hit at level 1, two at level 3. If that feels stingy,
  give the base air chain a second hit, or refill it on double jump. Don't remove the rule:
  without it Air Combo Plus is meaningless.
- **No hang time or plunge yet:**
  - The adapter can only add *horizontal* momentum, because ChibiLocomotion drops the y of
    `AddMomentum` and gravity outgrows any field acceleration.
  - KH's air-combo hang time ("Stationary Point") and plunges ("Vertical Asymptote") need a vertical
    hook from the movement rework, for example `IMovementImpulses.SetVerticalVelocity` or a hover
    request.
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
