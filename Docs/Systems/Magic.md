# Magic (mana + aimed spell)

A Terraria-style mana pool and an Expedition-33-style aimed spell, the Arcane Orb.

Snapshot as of `f876b63` plus branch `feat/mana-magic`. Nothing here has been play-tested yet.

- **Code:** `Assets/_Project/Scripts/Sandbox/Magic/` (namespace `Pickleball.Sandbox.Magic`), with
  `Magic/Presentation/MagicOrbVisual.cs`, and the HUD views `Scripts/UI/ManaBarView.cs` and
  `Scripts/UI/SpellCrosshairView.cs` (namespace `Pickleball.UI`). The shared seam is
  `Scripts/Sandbox/IAimState.cs`.
- **Data:** `Assets/_Project/ScriptableObjects/Magic/` (`ManaConfig.asset`, `ArcaneOrb.asset`).
- **Prefab:** `Assets/_Project/Prefabs/Magic/MagicOrb.prefab`.
- **Setup:** **Tools ▸ Pickleball ▸ Setup Magic** (`Scripts/Editor/MagicSetup.cs`), which calls
  `MagicOrbPrefabBuilder` and `MagicHudBuilder`.
- **Tests:** `Assets/_Project/Tests/EditMode/Magic/` (`ManaPoolTests`, `ManaTests`, `OrbMathTests`).

## 1. Purpose and player-facing feel

### Mana

Mana works exactly like Terraria 1.4's, with one number changed at the user's request: the
longest wait before regeneration starts is 3 s instead of Terraria's 3.3 s.

- The bar holds **100** mana. The Arcane Orb costs **15**, so a full bar is six casts with 10 left
  over.
- **Casting pauses regeneration.** The pause scales with how empty the cast left you: 0.53 s if
  the bar is still nearly full, up to **3 s** at empty. Standing still makes the pause count down
  twice as fast, so an emptied bar waits 1.5 s if you stop moving.
- **Then it starts slow and speeds up.** Regeneration at empty runs at 20% of its rate near full.
  On the move that is 3.5 mana/s at empty, 11.5 at half and 19 near full. Standing still roughly
  doubles every figure: 7.5, 23 and 38 mana/s.
- **Empty to full** takes about **10.7 s on the move** and **5.4 s standing still**, plus the
  pause (3 s moving, 1.5 s still). Refilling one cast from full takes under a second.
- **A failed cast costs nothing.** Pressing fire without enough mana changes nothing: it doesn't
  restart the pause. The HUD shows the failure instead (`Mana.SpendFailed`, `SpellCaster.CastFailed`).
- **Reaching full plays a cue once.** `Mana.Filled` fires on the tick the bar reaches its maximum
  from below. In Terraria that is the sparkle and chime.

These times barely depend on the maximum. Regeneration scales with the maximum too, so a 200 mana
bar refills in the same 10.7 s. That is Terraria's design, and it means raising `maxMana` buys
more casts per fight without slowing the refill.

### The Arcane Orb

The spell aims like Expedition 33's free-aim shot:

- **Hold aim** and the camera eases over the player's right shoulder, a crosshair appears, and
  the body turns to face the point under the crosshair while the stick still steers. The player
  strafes.
- **Fire** launches a slow (9 m/s), glowing orb from over the right shoulder (`SpellOrigin`)
  towards the point under the crosshair. If a damageable was under the crosshair at launch
  (within a 0.35 m aim assist), the orb homes on it gently, at 25°/s, so it bends after a target
  that sidesteps, but can't turn a miss into a hit. It homes only while the target is ahead, so a
  shot that has passed its target flies on instead of boomeranging back.
- **On contact the orb bursts:** 14 damage to what it hit, plus a blast that does 10 damage at the
  centre, falling to 4 at its 2.2 m edge, with 4 m/s knockback. Each damageable is hit at most
  once per burst. After 4 s without hitting anything, it fizzles.
- **Cooldown** is 0.45 s between casts. A press during the cooldown is ignored and doesn't count
  as a failed cast.
- Aiming is dropped while the player dashes, tethers or skates (`ChibiLocomotion.IsBusy`).

Damage goes through `IDamageable.TakeHit`, so the orb hurts Newton and the training dummies.
Newton's regain credit is paid on any hurt, so orb damage refunds the player's grey health like a
sword hit.

### Controls

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Hold to aim | Right mouse, only while the camera has captured the cursor | LT |
| Fire, while aiming | Left mouse | RT |
| Sword attack (unchanged) | Left mouse | West |
| Lock-on toggle (unchanged) | Tab / middle mouse | RT, when not aiming |
| Tether | Q. **Right mouse no longer tethers.** | LB |
| Sprint | Shift | **L3** (was LT) |

These bindings are confirmed by the user. They copy Expedition 33: on PC, right mouse aims and
left mouse shoots; on a pad, LT aims and RT shoots.

Two bindings players have today are gone:
- **Right mouse no longer tethers.** Q and LB still do. It moved because right mouse is now aim.
- **LT no longer sprints on a pad.** Sprint is now L3, because LT is now aim.

**Planned, not wired yet:** while aiming, the sword attack and the lock-on toggle should step
aside, and movement should be capped to a walk. See §5. Until that lands, left mouse while aiming
both fires and swings, and RT while aiming both fires and toggles lock-on.

## 2. Architecture

```
SpellInput (RMB/LT held, LMB/RT pressed; polled, actions rebuilt in OnEnable)
     │ read every frame
     ▼
SpellCaster ── IAimState.IsAiming ─────────▶ SandboxOrbitCamera (aimSource → AimBlend, shoulder offset)
     │      ── IFacingSource (urgent while aiming) ─▶ ChibiLocomotion.SetCombatFacingSource → motor strafes
     │      ── reads ChibiLocomotion.IsBusy (drop aim during dash / tether / skate)
     │      ── TrySpend(cost) ─────────────▶ Mana ── owns ──▶ ManaPool (plain C#, 60 Hz ticks)
     │                                         │  reads ICombatMovement (ChibiMovementAdapter) for "standing still"
     │                                         ├─ Changed / Spent / SpendFailed / Filled ─▶ ManaBarView
     │      ── Instantiate(spell.OrbPrefab).Launch(...)
     │                                         ▼
     │                                     MagicOrb ── IDamageable.TakeHit ──▶ Health (Newton, dummies)
     │                                         └─ Launched / Burst / Fizzled ─▶ MagicOrbVisual (same prefab)
     └─ AimStarted / AimEnded / Cast / CastFailed ─▶ SpellCrosshairView
```

### Dependencies point one way

- **Gameplay** is `ManaPool`, `Mana`, `SpellCaster`, `MagicOrb` and `OrbMath`. It raises C#
  events and never touches UI, VFX or audio.
- **Input** is `SpellInput` alone. Like `SwordInput`, it counts mouse buttons only while the
  orbit camera has captured the cursor and not on the frame it re-locks, so the click that
  recaptures the cursor never fires a spell.
- **Presentation** is `MagicOrbVisual` (the orb's glow, halo, trail, light and burst flash),
  `ManaBarView` and `SpellCrosshairView`. Each subscribes to the gameplay events.
- **Movement** is read, never edited. `Mana` reads `ICombatMovement.Velocity` through
  `ChibiMovementAdapter`, the same seam combat uses. `SpellCaster` reads `ChibiLocomotion.IsBusy`
  and pushes facing through `SetCombatFacingSource`. Movement doesn't know magic exists.
- **The camera** knows only `IAimState`, not `SpellCaster`. Any future aim mode (a bow, a
  thrown weapon) can drive the same shoulder camera by implementing the one-property interface.

### Components

| Where | Component | Job | Kind |
|---|---|---|---|
| Player root | `Mana` | Wraps a `ManaPool`, ticks it from `Update` with "standing still" from movement, raises the events | Gameplay |
| Player root | `SpellInput` | Aim held, fire held, fire pressed this frame | Input |
| Player root | `SpellCaster` | Aim state, the crosshair ray and aim assist, cooldown, spending, spawning and launching orbs, facing | Gameplay |
| Player child `SpellOrigin` | (Transform) | Launch point, local (0.3, 1.3, 0.45): over the right shoulder, where the aim camera sits | Data |
| Orb prefab | `MagicOrb` | Flight, homing, contact test, burst damage, lifetime | Gameplay |
| Orb prefab | `MagicOrbVisual` | Everything visible about the orb | Presentation |
| `FightHUD/PlayerManaBar` | `ManaBarView` | The mana bar, directly above the health bar | Presentation |
| `FightHUD/SpellCrosshair` | `SpellCrosshairView` | The crosshair, shown while aiming | Presentation |

### Events

| Raiser | Event | Raised when | Listeners |
|---|---|---|---|
| `Mana` | `Changed(Mana)` | `Current` changes: a spend, a regenerated point, a refill | `ManaBarView` |
| `Mana` | `Spent(cost)` | A spend succeeded | `ManaBarView` |
| `Mana` | `SpendFailed(cost)` | A spend couldn't be afforded | `ManaBarView` |
| `Mana` | `Filled` | The bar reached its maximum from below | `ManaBarView` |
| `SpellCaster` | `AimStarted`, `AimEnded` | Aim begins or ends, including when a dash or tether drops it | `SpellCrosshairView` |
| `SpellCaster` | `Cast(MagicOrb)` | After the orb is launched | `SpellCrosshairView` |
| `SpellCaster` | `CastFailed` | Fire pressed while aiming without enough mana. Cooldown-gated presses are not failures. | `SpellCrosshairView` |
| `MagicOrb` | `Launched`, `Burst(orb, point, hits)`, `Fizzled` | Launch, contact, lifetime out | `MagicOrbVisual` |

### Mana: Terraria's algorithm, and how our numbers map to it

Terraria 1.4 runs `Player.UpdateManaRegen` once per 60 Hz game tick. `ManaPool` runs the same
steps in fixed 1/60 s ticks. An accumulator turns Unity's variable frame time into whole ticks, so
the result is integer-exact and doesn't depend on frame rate.

**On a spend** the regeneration delay is set from how empty the spend left the bar:

```
missing    = 1 − mana / max                 (after paying)
Terraria:  delayTicks = 0.7 × (45 + 240 × missing)        → 0.525 s full … 3.325 s empty
Ours:      delay = lerp(regenDelayAtFull, regenDelayAtEmpty, missing)  → 0.525 s … 3.0 s
```

**Each tick:**

```
if delayed:
    delay −= 1 tick
    if standing still: delay −= (stillDelaySpeedup − 1) ticks     # Terraria: one extra tick, i.e. 2×
else:
    regen = floor(max × baseRegenShareOfMax) + flatRegen          # Terraria: max/3 + 1
    if standing still: regen += floor(max × stillRegenShareOfMax) # Terraria: + max/3
    fill  = mana / max × (1 − regenAtEmptyFraction) + regenAtEmptyFraction   # Terraria: 0.8 × fill + 0.2
    regen = floor(regen × fill × regenMultiplier)                 # Terraria: × 1.15
    count += regen
    while count ≥ 120:                                            # ManaPool.CountPerPoint
        count −= 120
        if mana < max: mana += 1
        # reaching max from below: Filled (Terraria: chime + sparkle, and count resets to 0)
```

| Terraria | Value | Ours (`ManaConfig` field, or `ManaPool` constant) | Value |
|---|---|---|---|
| Delay at full, `0.7 × 45` ticks | 0.525 s | `regenDelayAtFull` | 0.525 s |
| Delay at empty, `0.7 × 285` ticks | 3.325 s | `regenDelayAtEmpty` | **3.0 s** (user's choice) |
| Extra delay countdown while still or grappling | 2× | `stillDelaySpeedup` | 2 |
| Base regen, `statManaMax2 / 3 + 1` | 34 at 100 max | `baseRegenShareOfMax`, `flatRegen` | 1/3, 1 |
| Standing-still bonus, `+ statManaMax2 / 3` | 33 at 100 max | `stillRegenShareOfMax` | 1/3 |
| Fill scaling, `0.8 × fill + 0.2` | 0.2 at empty | `regenAtEmptyFraction` | 0.2 |
| Final multiplier | 1.15 | `regenMultiplier` | 1.15 |
| Regen count per mana point | 120 | `ManaPool.CountPerPoint` | 120 |
| Tick rate | 60 Hz | `ManaPool.TicksPerSecond` | 60 |
| "Standing still": `velocity == 0` exactly | — | `standingStillSpeed` | 0.15 m/s |
| Max mana (starting character) | 20, up to 400 | `maxMana` | 100 |

The feel numbers in §1 come from running these steps with `maxMana` 100: 645 ticks (10.75 s)
from empty to full on the move, 323 ticks (5.38 s) standing still.

Terraria's speed test is an exact zero. Ours is a threshold (`standingStillSpeed`), because a
`CharacterController` on uneven ground rarely reports exactly zero.

## 3. How to tune

All mana tuning is on `ScriptableObjects/Magic/ManaConfig.asset`. All spell tuning is on
`ScriptableObjects/Magic/ArcaneOrb.asset`. Setup Magic creates both only if they are missing, so
edits survive a re-run.

**Mana (`ManaConfig`):**
- **More casts per fight:** raise `maxMana`. The refill time stays about the same (see §1).
- **Shorter pause after emptying the bar:** lower `regenDelayAtEmpty`. `regenDelayAtFull` is the
  pause after a cast from a nearly full bar.
- **Standing still matters more or less:** `stillDelaySpeedup` (pause countdown) and
  `stillRegenShareOfMax` (regen bonus). Set them to 1 and 0 to remove the bonus.
- **Flatter or steeper curve:** `regenAtEmptyFraction`. 1 is a flat rate; lower starts slower.
- **Faster or slower overall:** `regenMultiplier` scales every rate.
- **"Standing still" is too strict or too loose:** `standingStillSpeed` (m/s).

**Spell (`SpellDefinition`, `ArcaneOrb.asset`):**
- **Cost and pace:** `manaCost`, `cooldown` (s), `autoRepeat` (hold fire to keep casting).
- **Aim:** `maxRange` (m), `aimAssistRadius` (m; 0 turns assist off), `minAimDistance` (m; aim
  points closer than this launch along the camera instead, so the orb never fires sideways into
  the player's own feet), `aimMoveScale` (0–1 stick cap while aiming; see §5).
- **Orb:** `orbSpeed` (m/s), `orbRadius` (m), `orbLifetime` (s), `homingTurnRate` (°/s; 0 flies
  straight), `orbPrefab`.
- **Blast:** `directDamage`, `blastRadius` (m), `blastDamageMax` / `blastDamageMin` (centre /
  edge), `knockback` (m/s), `hitMask`.

**Scene:** `SpellCaster.aimMask` filters the crosshair ray. Setup Magic sets it to everything
except layers only the player's colliders use. Today that is everything, because the player is on
Default and the caster skips its own colliders anyway. `SpellOrigin`'s position is set only when
Setup Magic creates it, so you can move it by hand.

**Look:** the orb's glow, halo, trail and burst are built in code by `MagicOrbPrefabBuilder` and
regenerated each run. Change the builder, not the prefab.

## 4. How to extend

**Add a new spell in 4 steps:**
1. **Assets ▸ Create ▸ Pickleball ▸ Magic ▸ Spell**, saved under `ScriptableObjects/Magic/`.
2. Set its numbers. For `orbPrefab`, reuse `MagicOrb.prefab`, or add a build method for a new
   look to `MagicOrbPrefabBuilder` and call it from `MagicSetup`.
3. Assign it to the player's `SpellCaster.spell`. Setup Magic fills that field only when it is
   blank, so a hand-assigned spell survives a re-run. Setup Chibi Sandbox builds a fresh scene,
   though, so to make the new spell the default, change `MagicSetup.SpellPath`.
4. Play-test the cost against the regen curve in §1. Six casts a bar is the current baseline.

**A spell that isn't an orb** (a beam, a ground ring): give it its own gameplay component that
raises events, plus a presentation component that subscribes, following `MagicOrb` and
`MagicOrbVisual`. `SpellCaster` today always instantiates `orbPrefab` and calls
`MagicOrb.Launch`, so a second delivery type means a small interface in place of the concrete
`MagicOrb`.

**Several spells on one character** (a spell wheel, a quick swap): `SpellCaster` holds one
`SpellDefinition`. Add a selector component that swaps it, rather than a list inside the caster.

**Anything else that spends mana** (a sword technique, a dash): call `Mana.TrySpend(cost)` on the
player's `Mana`. It returns false and changes nothing when the cost can't be paid, so the caller
needs no `CanAfford` check first. The bar, the pause and the HUD's failure cue all follow
automatically.

**Mana pickups, potions, or a regen buff:** call `Mana.Refill()` for a full refill. Anything
partial, or Terraria's mana-regeneration buff (no fill scaling, delay capped at 20 ticks), needs a
new `ManaPool` method with tests in `ManaPoolTests` first.

**Another aim mode** (a bow, a thrown weapon): implement `IAimState` and point the camera's
`aimSource` at it. Only one aim source is wired today, so two would need a small "any of these is
aiming" component.

## 5. Known limitations and open questions

- **Not play-tested.** The aim, the shoulder camera, the facing and the orb have only been
  compiled and EditMode-tested. Expect tuning of `aimMoveScale`, the camera shoulder offset,
  `orbSpeed` and the aim assist.
- **No aim restrictions yet.** The aim's walk cap and the input claim move to a later component
  the lead adds after the dialogue session's `PlayerInputGate` is merged. That component will call
  `Claim(this, InputBlock.Sword | InputBlock.LockOn)` on aim start, release it on aim end, and set
  `PlayerMovementInput.DriveLimit` to `spell.AimMoveScale` while aiming. `DriveLimit` exists and
  works, but nothing lowers it yet, and `SpellCaster` has no reference to `PlayerMovementInput`.
  Until then:
  - left mouse while aiming both fires the orb and swings the sword;
  - RT while aiming both fires and toggles lock-on;
  - aiming doesn't slow the player, and sprint still works while aiming.
- **Two bindings were taken away.** Right mouse no longer tethers (Q / LB still do), and the pad's
  sprint moved from LT to L3. Players who tether with the mouse lose that habit.
  `Docs/Systems/Movement.md` still lists "Q / right mouse" for tether and LT for sprint; its owner
  needs to update it.
- **Tether doesn't count as standing still.** Terraria counts grappling as still, so a grappled
  player regenerates at the faster rate. Ours reads movement velocity, and the tether swing moves.
- **No mana pickups, no mana potions, no mana sickness**, no regen buff, no mana-regeneration
  accessories. Terraria's `manaRegenBonus` and `manaRegenDelayBonus` have no equivalent.
- **No PlayMode tests.** `ManaPool` (the algorithm) and `OrbMath` (blast falloff, homing steer,
  launch direction) have EditMode tests, and `Mana` is tested through its `Tick` seam. The caster,
  the orb's flight and collisions, the input and the HUD are untested. The project has no PlayMode
  assembly yet.
- **One spell, one caster.** No spell selection, no UI for spell choice.
- **Scene-only setup.** Mana, SpellInput and SpellCaster are added to the open scene's player,
  not to `Chibi.prefab`, the same as `LockOnTargeting`. Scenes that instantiate the prefab, such
  as SpacetimeLab, have no magic.
- **Orbs are instantiated and destroyed,** one per cast. At 0.45 s per cast that is at most a
  handful alive at once, so there is no pool yet.
- **Open question:** should magic damage pay Lemmas (sword XP)? `CombatXpSource` listens to
  `MeleeCombat.HitLanded` only, so orb damage earns nothing today.

## For Docs/Architecture (to fold in after merge)

The QA session owns `Docs/Architecture/` while this branch is open. Fold these three parts into it
after the merge, and update each page's snapshot line to the merge commit.

### A. `CombatMovementBoundary.md`: pad and key table, and the aim seam

Replace these rows in "Pad and key layout contract":

| Owner | Keyboard / mouse | Gamepad |
|---|---|---|
| `PlayerMovementInput` | WASD / arrows; Shift sprint; Space jump; Ctrl crouch; F flip; R mount; E dash; **Q tether**; G anchor | Left stick; **L3 sprint**; South jump; East crouch; North flip; D-pad Right mount; RB dash; LB tether; R3 anchor |
| `SpellInput` | **Right mouse hold aim; left mouse fire** (both ignored while the cursor is free, and on the re-lock frame) | **LT hold aim; RT fire** |
| `SwordInput` | Left mouse (ignored while the cursor is free). *Should step aside while aiming once the input gate lands.* | West attack |
| `LockOnTargeting` | Tab / middle mouse toggle. *Should step aside while aiming once the input gate lands.* | RT toggle, when not aiming |

Add under the table: "Right mouse left the tether and LT left sprint for the spell's aim (feat/mana-magic).
Players lose the right-mouse tether; Q and LB still tether. Left mouse and RT are shared between
the spell's fire and the sword attack / lock-on toggle. Until the `PlayerInputGate` claim lands, a
press while aiming triggers both."

Add to "Decisions":

> **Aiming is one read-only bit, `IAimState.IsAiming`.** `SpellCaster` implements it, and
> `SandboxOrbitCamera` reads it through a serialized `aimSource` (a `MonoBehaviour` cast to the
> interface). No source means never aiming. The camera never names magic. A later component
> (after the dialogue merge) will use the same state to claim `PlayerInputGate` for
> `InputBlock.Sword | InputBlock.LockOn` and to lower `PlayerMovementInput.DriveLimit` to a walk.

And in "Facing", item 1 is no longer "None is registered yet":

> 1. A registered combat facing source (`SetCombatFacingSource`). `SpellCaster` registers itself in
>    `OnEnable` and clears the slot in `OnDisable` only if it still holds it. It returns the aim
>    point, *urgent* only while aiming, so the motor strafes. When not aiming it returns false
>    and facing falls through to the lock-on.

### B. `EditorSetup.md`: menu list, run order, asset ownership

- **Menu verbs → Setup:** add Setup Magic.
- **Run order:** insert after Setup Newton Fight and Setup Lock-On:
  > 5. **Setup Magic**. It needs the FightHUD canvas from Setup Newton Fight; without it, it
  >    creates a bare FightHUD with the same Canvas settings and warns. Re-run it after Setup
  >    Newton Fight, so the mana bar sits on the health bar and draws over the rebuilt damage
  >    numbers.

  Setup Chibi Sandbox builds a new scene, so re-run Setup Magic after it. Build Training Stage
  copies the saved ChibiSandbox, so save the sandbox after Setup Magic and the stage carries it.
  Setup Magic doesn't touch `Chibi.prefab`.
- **Asset ownership:**
  - *Seeded once:* `ScriptableObjects/Magic/ManaConfig.asset` and `ArcaneOrb.asset`. Created from
    code defaults if missing; on an existing spell, only a blank `orbPrefab` is filled.
  - *Regenerated every run:* `Prefabs/Magic/MagicOrb.prefab` (GUID kept), and the
    `PlayerManaBar` and `SpellCrosshair` children of FightHUD (destroyed and rebuilt by
    `MagicHudBuilder`).
  - *Scene wiring:* component-to-component references are rewritten every run. The player's data
    references (`Mana.config`, `SpellCaster.spell`) are filled only when blank. `SpellOrigin` is
    created at (0.3, 1.3, 0.45) only if missing.
- **Scene-dirty policy:** Setup Magic is "Mark dirty and never save", and refuses to run in Play
  mode.
- **Entry point for an orchestrator:** `MagicSetup.AttachToPlayer(ChibiLocomotion)`. It marks the
  scene dirty and saves nothing; call `AssetDatabase.SaveAssets` after it.
- **Known hazards:**
  - add Mana, SpellInput and SpellCaster to the "`Chibi.prefab` lags the scene" list;
  - add "FightHUD" (Setup Magic finds it by name too) and "SpellOrigin" to the "lookups by name"
    line;
  - Setup Magic finds the camera by type (`SandboxOrbitCamera`), not through `Camera.main`.
- **Count:** three new global-namespace editor classes: `MagicSetup`, `MagicOrbPrefabBuilder`,
  `MagicHudBuilder`.

### C. `Communication.md`: event rows and the interfaces row

Add to "Events that cross system boundaries (Sandbox)":

| Raiser | Event | Listeners |
|---|---|---|
| `Mana` (Magic/) | `Changed(mana)`, `Spent(cost)`, `SpendFailed(cost)`, `Filled` | `ManaBarView` |
| `SpellCaster` | `AimStarted`, `AimEnded`, `Cast(orb)`, `CastFailed` | `SpellCrosshairView` |
| `MagicOrb` | `Launched`, `Burst(orb, point, hits)`, `Fizzled` | `MagicOrbVisual` (on the same prefab) |

In the mechanisms table, add `IAimState` (aiming → camera) to the "Small read-only interfaces at a
boundary" row, next to `IFacingSource`, which `SpellCaster` now also implements.
