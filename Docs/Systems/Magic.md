# Magic (mana + spells)

A Terraria-style mana pool and five Expedition-33-style aimed spells: the Arcane Orb, Fireball,
Thunderbolt, Freeze Zone and Heal, with a spell selector.

Snapshot as of `dff1d38` plus the spells branch (`feat/spells`). Nothing here has been play-tested
by a person yet. The spells were checked by a headless Play-mode harness on TrainingStage (see §5).

- **Code:** `Assets/_Project/Scripts/Sandbox/Magic/` (namespace `Pickleball.Sandbox.Magic`), with
  the visuals in `Magic/Presentation/`, and the HUD views `Scripts/UI/ManaBarView.cs`,
  `SpellCrosshairView.cs`, `SpellSelectorView.cs` and `SpellChannelView.cs` (namespace
  `Pickleball.UI`). The shared seam is `Scripts/Sandbox/IAimState.cs`.
- **Data:** `Assets/_Project/ScriptableObjects/Magic/` (`ManaConfig.asset`, `ArcaneOrb.asset`,
  `Fireball.asset`, `Thunderbolt.asset`, `FreezeZone.asset`, `Heal.asset`).
- **Prefabs:** `Assets/_Project/Prefabs/Magic/` (`MagicOrb`, `Fireball`, `LightningStrike`,
  `FreezeZone`, `Burn`).
- **Setup:** **Tools ▸ Pickleball ▸ Setup Magic** (`Scripts/Editor/MagicSetup.cs`), which calls
  `MagicOrbPrefabBuilder`, `MagicSpellArtBuilder` and `MagicHudBuilder`. Build Training Stage
  calls `MagicSpellArtBuilder.AttachStatusReceiver` on each dummy it builds.
- **Tests:** `Assets/_Project/Tests/EditMode/Magic/` (`ManaPoolTests`, `ManaTests`, `OrbMathTests`,
  `AimInputClaimsTests`, `SpellChannelTests`, `ChannelSignalsTests`, `SpellCooldownsTests`,
  `SpellSelectionTests`, `SlowLedgerTests`, `FreezeZoneTests`, `BurnTests`, `StrikeMathTests`; shared
  `MagicTestFixture`).

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
  strafes. The camera sits 1.2 m right of, 0.55 m above and 3 m behind the chibi's head
  (`SandboxOrbitCamera.aimShoulderOffset`, `aimDistance`), so the head is in the lower-left
  third and the crosshair is clear.
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

### Spells

Every spell is cast the same way: hold aim, then fire. Each has its own cooldown, so switching
never resets one.

| Key | Spell | Delivery | Cost | Cooldown | What it does |
|---|---|---|---|---|---|
| 1 | **Arcane Orb** | Orb | 15 | 0.45 s | Unchanged (above). |
| 2 | **Fireball** | Orb | 25 | 0.9 s | A fast (20 m/s) fireball with a flame trail. 16 to what it hits, a 3.2 m blast (12 at the centre, 5 at the edge), then everything the burst hit **burns**: 2.5 every 0.5 s for 3 s (15 more). |
| 3 | **Thunderbolt** | Strike | 30 | 1.5 s | A 0.35 s telegraph (an amber ring tightening, a storm cloud gathering), then lightning: 34 to the target, a 1.6 m splash (12 to 5), a 0.6 s **stagger** if the target accepts one, and a **chain** to the 2 nearest other enemies within 6 m for 10 each. |
| 4 | **Freeze Zone** | Zone | 35 | 6 s | A 3 m-radius, 2.5 m-tall disc of frost on the ground for 4 s. Anything inside that accepts a slow runs at **30%** speed, and for 0.35 s after it leaves. No damage. |
| 5 | **Heal** | Channel | 40 | 3 s | Hold fire for **2 s, standing still**, to restore **30** health. |

**Balance against the 100-point bar** (proposed; needs the user's OK): a full bar is six orbs,
four fireballs, three thunderbolts, two freeze zones or two heals. Per mana on one target the orb
deals 0.93 (14 for 15), the fireball 1.24 (16 + 15 burn for 25) and the thunderbolt 1.13 (34 for
30). The fireball and thunderbolt pay for their extra damage with a longer cooldown and crowd
reach; the thunderbolt also cannot miss once cast on an enemy and staggers it. The freeze zone and
heal are utility, priced high so they are choices, not habits. Every number is on the spell's asset.

**Fireball.** Aimed and homed exactly like the orb, but faster (homing 12°/s), with a white-hot core,
an orange halo, a flame trail and a bigger, ember-throwing burst. The burn is a `BurnStatus` parented
to each victim. A second fireball on a burning target refreshes the burn (longest time, highest
damage) and never stacks it, so damage per second has a ceiling.

**Thunderbolt.** Cast with the crosshair on an enemy, the strike **follows that enemy** through the
telegraph and always lands: the telegraph says where, not "dodge this". Cast on the ground, it lands
where aimed, and whoever stands nearest the point (within half the splash radius) takes the direct
hit. The aim assist is wider (0.5 m) because picking the target is the whole aim. The stagger needs
the target to implement `IStaggerable` (see §4).

**Freeze Zone.** Lands on the floor under the crosshair (a crosshair on a wall or a chest drops to
the ground below, up to 12 m). Its rim drains round like a clock as it runs out. It slows through a
new opt-in interface, `ISlowable`: the strongest slow wins, nothing multiplies, so two zones are not
a freeze. The training dummies opt in through `StatusReceiver`; they never move, so for them the slow
is a visible status (a frost shell and ice crust). **Newton does not opt in yet** (see §4).

**Heal.** Hold aim (the casting stance; no crosshair is shown), then hold fire for 2 s:
- **Any movement input cancels it**: the stick past 0.2, crouch, flip, mount or anchor. So do
  **jump** (or leaving the ground), **dash or tether** (or any move that makes
  `ChibiLocomotion.IsBusy`), **being hurt**, **letting go** of fire or aim, and the input gate
  **blocking Spell** (a conversation). Switching spell cancels it too.
- The channel never claims the input gate and never zeroes input: the player stays free to move,
  and moving is how they cancel. It reads movement input through `PlayerMovementInput.Read()`
  (read-only; the press edges are per-frame, so reading twice changes nothing).
- **Mana is paid on completion** (proposed; needs the user's OK). The cost must be affordable to
  start, but a cancelled heal costs nothing. `ChannelManaTiming.OnStart` on the asset switches to
  paying up front and losing it on a cancel.
- It **refuses to start at full health** (no mana risked for nothing), and the cooldown starts only
  on completion.
- It heals through the existing `Health.Heal`, which also trims the grey regain pool so the bar never
  overfills.
- Presentation: a green ring fills clockwise round the feet over a faint track, a rune turns and
  brightens, a glow and rising motes build, and on completion a gold ring and a spray of motes burst
  out. A cancel drains the ring red. Under the screen centre a bar fills with the spell's name, and
  says what happened ("+30", "Moved", "Hit!", "Not enough mana", "Health is full").

### Choosing a spell

The selector sits right of the mana bar: one slot per spell with its icon in the spell's colour,
its number key, and a dark sweep while it recovers. The selected slot stands taller, the selected
spell's name and cost sit above the row, and a spell the bar cannot pay for now is dimmed.

### Controls

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Hold to aim (the casting stance) | Right mouse, only while the camera has captured the cursor | LT |
| Cast the selected spell, while aiming | Left mouse | RT |
| Heal: hold to channel, while aiming, standing still | Hold left mouse 2 s | Hold RT 2 s |
| Pick spell 1-5 | **1-5** (and numpad 1-5), aiming or not | — |
| Previous / next spell | — | **D-pad up / down, while aiming** |
| Sword attack (unchanged) | Left mouse | West |
| Lock-on toggle (unchanged) | Tab / middle mouse | RT, when not aiming |
| Tether | Q. **Right mouse no longer tethers.** | LB |
| Sprint | Shift | **L3** (was LT) |

The aim and fire bindings are confirmed by the user. They copy Expedition 33: on PC, right mouse
aims and left mouse shoots; on a pad, LT aims and RT shoots.

**The spell-selection bindings are new and need the user's confirmation.** Every existing binding
was checked first (`AddBinding` and polled devices across `Sandbox/`):
- **Number keys** were free everywhere, so 1-9 pick slots directly, aiming or not.
- **The pad cycle was asked for on d-pad left/right, but both are taken.** D-pad right is the
  skateboard mount (always), and d-pad left is the Spacetime 4D toggle (while that feature is on).
  Cycling there would mount the board mid-aim, which drops the aim. D-pad up/down are the camera
  zoom, which the orbit camera already ignores while aiming, so **while LT is held they belong to
  nobody**: up is previous, down is next. Outside aim they still zoom. The two binding paths are
  serialized on `SpellSelectInput`, so moving the cycle to left/right is an Inspector change once
  mount and Spacetime have moved.
- **Heal uses aim + held fire**, like every other spell, because left mouse / RT without aiming
  already belong to the sword and lock-on. Holding aim alone would also work, but would make aim mean
  "cast" for one spell only.

Two bindings players had before the magic branch are gone:
- **Right mouse no longer tethers.** Q and LB still do. It moved because right mouse is now aim.
- **LT no longer sprints on a pad.** Sprint is now L3, because LT is now aim.

**While aiming, the sword attack and the lock-on toggle step aside, and movement is capped to a
walk** (`AimInputClaims`, wired since `d199f3d`). Left mouse and RT only cast while aim is held. Any
claim on `InputBlock.LockOn` also drops a held lock and hides the lock-on hint (lock-on fix
`1d10103`).

## 2. Architecture

```
SpellSelectInput (1-9; pad up/down while aiming) ──▶ SpellSelector ── SetSpell ──▶ SpellCaster
                                                        └─ SelectionChanged ─▶ SpellSelectorView
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

SpellCaster, by SpellDefinition.Delivery:
  Orb     ── Instantiate(OrbPrefab).Launch  ──▶ MagicOrb ── burst ─▶ BurnStatus.Ignite (Fireball)
  Strike  ── Instantiate(StrikePrefab).Begin ─▶ LightningStrike ── TakeHit, IStaggerable.Stagger
  Zone    ── Instantiate(ZonePrefab).Place ──▶ FreezeZone ── ISlowable.ApplySlow ─▶ StatusReceiver
  Channel ── SpellChannel (plain C#) ── Health.Heal; reads Health.Hurt, PlayerMovementInput.Read(),
             ChibiLocomotion.IsBusy / IsGrounded, PlayerInputGate (Spell)
  └─ ChannelStarted / ChannelCompleted / ChannelCancelled ─▶ HealChannelVisual, SpellChannelView
Each spawned prefab carries its own visual on a child (LightningStrikeVisual, FreezeZoneVisual,
BurnVisual), which listens and detaches itself to finish when the gameplay object is destroyed.
```

### Dependencies point one way

- **Gameplay** is `ManaPool`, `Mana`, `SpellCaster`, `SpellSelector`, the deliveries (`MagicOrb`,
  `LightningStrike`, `FreezeZone`, `BurnStatus`), `StatusReceiver`, and the plain-C# rules
  (`OrbMath`, `SpellChannel`, `ChannelSignals`, `SpellCooldowns`, `SpellSelection`, `SlowLedger`,
  `BurnTicker`, `ZoneMath`, `StrikeMath`, `GroundProbe`). It raises C# events and never touches UI,
  VFX or audio.
- **Input** is `SpellInput` and `SpellSelectInput`. Like `SwordInput`, `SpellInput` counts mouse
  buttons only while the orbit camera has captured the cursor and not on the frame it re-locks, so
  the click that recaptures the cursor never fires a spell. `SpellSelectInput` reads nothing while
  the gate blocks Spell, and its pad cycle only while `IAimState.IsAiming`.
- **Presentation** is `MagicOrbVisual` (the orb's and the fireball's glow, halo, trail, light and
  burst), `LightningStrikeVisual`, `FreezeZoneVisual`, `BurnVisual`, `StatusReceiverVisual`,
  `HealChannelVisual`, and the HUD views `ManaBarView`, `SpellCrosshairView`, `SpellSelectorView` and
  `SpellChannelView`. Each subscribes to the gameplay events or polls read-only state.
- **Targets opt in** to slows and staggers through two small interfaces in the Magic namespace,
  `ISlowable` and `IStaggerable`. `StatusReceiver` implements both. A spell never knows how a
  target moves; a target that implements neither just takes the damage.
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
| Player root | `SpellCaster` | Aim state, the crosshair ray and aim assist, per-spell cooldowns, spending, the delivery switch (orb, strike, zone, channel), facing | Gameplay |
| Player root | `SpellSelectInput` | Number keys 1-9; pad previous / next while aiming | Input |
| Player root | `SpellSelector` | The carried spells (slots) and the selected one; hands it to the caster | Gameplay |
| Player child `HealChannelVfx` | `HealChannelVisual` | Channel ring, rune, glow, motes, completion burst, cancel drain | Presentation |
| Player child `SpellOrigin` | (Transform) | Launch point, local (0.3, 1.3, 0.45): over the right shoulder, where the aim camera sits | Data |
| Orb prefab | `MagicOrb` | Flight, homing, contact test, burst damage, lifetime | Gameplay |
| Orb prefab | `MagicOrbVisual` | Everything visible about the orb | Presentation |
| Fireball prefab | `MagicOrb` + `MagicOrbVisual` | The same components in fire colours, with a child "Flames" system | Gameplay + presentation |
| Burn prefab | `BurnStatus` + child `BurnVisual` | Damage ticks on one victim; flames, embers, light | Gameplay + presentation |
| Strike prefab | `LightningStrike` + child `LightningStrikeVisual` | Telegraph, tracking, the strike, splash, stagger, chain; ring, cloud, bolt, arcs | Gameplay + presentation |
| Zone prefab | `FreezeZone` + child `FreezeZoneVisual` | Membership and slows each frame; frost, rim timer, crystals, snow | Gameplay + presentation |
| Each training dummy | `StatusReceiver` + child `MagicStatusVfx` (`StatusReceiverVisual`) | Slow ledger and stagger timer; frost shell, ice crust, stars | Gameplay + presentation |
| `FightHUD/PlayerManaBar` | `ManaBarView` | The mana bar, directly above the health bar | Presentation |
| `FightHUD/SpellCrosshair` | `SpellCrosshairView` | The crosshair, shown while aiming a crosshair spell (not Heal) | Presentation |
| `FightHUD/SpellSelector` | `SpellSelectorView` | The slot row right of the mana bar | Presentation |
| `FightHUD/SpellChannelBar` | `SpellChannelView` | The channel bar under the crosshair, and the channel's result word | Presentation |

### Events

| Raiser | Event | Raised when | Listeners |
|---|---|---|---|
| `Mana` | `Changed(Mana)` | `Current` changes: a spend, a regenerated point, a refill | `ManaBarView` |
| `Mana` | `Spent(cost)` | A spend succeeded | `ManaBarView` |
| `Mana` | `SpendFailed(cost)` | A spend couldn't be afforded | `ManaBarView` |
| `Mana` | `Filled` | The bar reached its maximum from below | `ManaBarView` |
| `SpellCaster` | `AimStarted`, `AimEnded` | Aim begins or ends, including when a dash or tether drops it | `SpellCrosshairView` |
| `SpellCaster` | `Cast(SpellDefinition)` | After any cast is paid and delivered: an orb launched, a strike called, a zone placed, a channel completed (after the heal). Was `Cast(MagicOrb)`. | `SpellCrosshairView` |
| `SpellCaster` | `CastFailed` | Fire pressed while aiming without enough mana (for a channel: at its start, or when its completion cannot pay). Cooldown-gated presses are not failures. | `SpellCrosshairView`, `ManaBarView` |
| `SpellCaster` | `SpellChanged(spell)` | `SetSpell` changed the spell | `SpellSelector` |
| `SpellCaster` | `ChannelStarted(spell)`, `ChannelCompleted(spell)`, `ChannelCancelled(spell, reason)` | A channel began; completed (after `Health.Heal`, before `Cast`); was refused or cancelled, with the `ChannelStopReason` | `HealChannelVisual`, `SpellChannelView` |
| `SpellSelector` | `SelectionChanged(selector)` | The selected slot changed | `SpellSelectorView` |
| `MagicOrb` | `Launched`, `Burst(orb, point, hits)`, `Fizzled` | Launch, contact, lifetime out | `MagicOrbVisual` |
| `LightningStrike` | `Telegraphed`, `Chained(strike, from, to)`, `Struck(strike, point, hits)` | Begin; each chain link (before `Struck`); the bolt lands | `LightningStrikeVisual` |
| `FreezeZone` | `Placed`, `Expired` | Placement; duration out | `FreezeZoneVisual` |
| `BurnStatus` | `Ignited`, `Ticked(burn, damage)`, `Extinguished` | Lit or refreshed; each damage tick; the burn ends | `BurnVisual` |
| `StatusReceiver` | `SlowStarted`, `SlowEnded`, `Staggered(receiver, seconds)` | The first slow lands; the last runs out; a stagger is accepted | (none; `StatusReceiverVisual` polls) |

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

**The four new spells** are tuned on their own assets, `Fireball.asset`, `Thunderbolt.asset`,
`FreezeZone.asset` and `Heal.asset`, with the same Cost and Aim fields plus their delivery's group.
Setup Magic writes the seeds in `MagicSetup` only when an asset is created. Each tooltip names the
delivery that reads the field.
- **Identity:** `displayName`, `delivery`, `themeColour` (HUD tint), `icon` (generated if blank).
- **Art:** `modelPrefab`, `secondaryModelPrefab`, `modelScale` (see §4, "Swap in a model").
- **Fireball** (Orb fields plus Burn): `orbSpeed`, `homingTurnRate`, `directDamage`, `blastRadius`,
  `blastDamageMax` / `Min`, `knockback`; `burnDamagePerTick`, `burnTickInterval` (s; the first tick
  lands one interval after the hit), `burnDuration` (s; ticks = duration ÷ interval),
  `burnBlastVictims` (off = only the direct hit burns).
- **Thunderbolt** (Blast plus Strike): `strikeDelay` (s of telegraph), `strikeTracksTarget`,
  `directDamage`, `blastRadius` (the splash), `blastDamageMax` / `Min`, `staggerSeconds`,
  `chainCount` (0-4), `chainRadius` (m), `chainDamage`, `aimAssistRadius`, `groundProbeDistance`.
- **Freeze Zone** (Zone): `zoneRadius`, `zoneHeight` (m), `zoneDuration` (s), `slowMultiplier`
  (0-1 of normal speed; 0 is a full freeze), `slowLinger` (s after leaving), `groundProbeDistance`.
- **Heal** (Channel): `channelSeconds`, `channelManaTiming` (OnCompletion / OnStart), `healAmount`,
  `channelMoveDeadzone` (raw stick still counted as standing), `refuseAtFullHealth`, `cooldown`.
- **The slot order** is the `spells` list on the player's `SpellSelector` (slot 1 = key 1). Setup
  Magic fills it only when empty.
- **The pad cycle buttons** are the `padPreviousBinding` / `padNextBinding` paths on
  `SpellSelectInput`, with `padCycleOnlyWhileAiming`.
- **A target's stagger cap** is `StatusReceiver.maxStaggerSeconds` (1.5 s).

**Look:** every visual's colours, sizes and timings are serialized on its component in the prefab
(or on the scene child for the heal and the dummies), but the prefabs are regenerated by Setup
Magic, so change the builder (`MagicSpellArtBuilder`, `MagicOrbPrefabBuilder`), not the prefab.
The parts that must read on any floor (the bolt's glow, the telegraph ring, chain arcs, the frost
disc and rim, the heal ring and rune, the flames) blend normally; the glows, lights and sparks are
additive.

**Scene:** `SpellCaster.aimMask` filters the crosshair ray. Setup Magic sets it to everything
except layers only the player's colliders use. Today that is everything, because the player is on
Default and the caster skips its own colliders anyway. `SpellOrigin`'s position is set only when
Setup Magic creates it, so you can move it by hand.

**Look:** the orb's glow, halo, trail and burst are built in code by `MagicOrbPrefabBuilder` and
regenerated each run. Change the builder, not the prefab.

## 4. How to extend

**Add a spell of an existing kind (a frost bolt, a bigger zone) in 4 steps:**
1. **Assets ▸ Create ▸ Pickleball ▸ Magic ▸ Spell**, saved under `ScriptableObjects/Magic/`.
2. Pick its `delivery` and set its numbers. Reuse a prefab (`MagicOrb`, `Fireball`,
   `LightningStrike`, `FreezeZone`, `Burn`), or add a build method for a new look to the builders
   and call it from `MagicSetup` (a new seed there makes it part of every fresh setup).
3. Add it to the player's `SpellSelector.spells`. Setup Magic fills that list only when it is empty,
   so a hand-arranged loadout survives a re-run. Re-run Setup Magic so the HUD gets a slot for it.
4. Play-test the cost against the regen curve in §1.

**Add a new kind of spell (a beam, a shield)** in 5 steps: append a `SpellDelivery` value (never
reorder; it is stored as an int); give it a gameplay component that raises events and a
presentation component on a child that listens and detaches to finish, following
`LightningStrike` / `LightningStrikeVisual`; add its prefab field and group to `SpellDefinition`;
add a case to `SpellCaster.TryFire` and `SpellCaster.HasPayload`; build its prefab in
`MagicSpellArtBuilder`. Put its rules in a plain C# class with EditMode tests.

**Opt a target in to slows and staggers:** put a `StatusReceiver` on the object that carries its
`IDamageable` (or any parent of its hit colliders), then read `SpeedMultiplier` wherever it moves or
counts down an attack, and `IsStaggered` / `Staggered` wherever an action can be interrupted. Or
implement `ISlowable` / `IStaggerable` yourself, keeping the contract on `ISlowable` (strongest slow
wins, one entry per source). For a visual, call `MagicSpellArtBuilder.AttachStatusReceiver` from
the target's setup menu.

**What Newton needs** (boss code is the boss owner's; Magic does not edit it):
- **Freeze Zone:** a `StatusReceiver` (or an `ISlowable`) on the NewtonBoss object that carries his
  `Health`, added by Setup Newton Fight, since it rebuilds his fight components each run. Then his
  movement speed and turn rate, and the wind-up, telegraph and recovery timers of `NewtonBoss`,
  `GravityWell`, `FluxionBeam`, `AppleBarrage` and `InverseSquareVolley`, must scale by
  `SpeedMultiplier` (multiply `dt` while slowed). His animator can follow through
  `StatusReceiver.animatorToSlow` only if no hitbox is timed by the animation; otherwise slow it
  in code alongside the timers. His owner should decide whether projectiles already in flight slow
  too (the zone itself only touches `ISlowable` colliders).
- **Thunderbolt stagger:** `IStaggerable` (or `StatusReceiver.Staggered`) on the same object, and a
  rule for what a stagger interrupts: probably cancel a wind-up or telegraph and delay the next
  attack by the stagger time, but not a committed beam. `StatusReceiver` caps a stagger at 1.5 s
  and never adds staggers together, so chained strikes cannot stun-lock him.
- **Already working on Newton with no change:** the damage of every spell, the Fireball's burn
  (through `IDamageable`, each tick through his `IncomingDamageFilter` at his body centre, and each
  tick paying the regain credit), and the strike's tracking and chain.

**Swap in a model (Tripo meshes):** set the spell asset's `modelPrefab` (and, for the Thunderbolt,
`secondaryModelPrefab`). Colliders and rigidbodies on it are stripped when it spawns
(`SpellModel.Spawn`), so it can never block a sweep. What each slot replaces, and the authoring
convention:
- Fireball (`modelPrefab`): the core, scaled to the core's diameter × `modelScale` and spun about
  its travel; author it about 1 m across, centred.
- Thunderbolt (`modelPrefab`): the drawn bolt, stretched to the cloud height (7 m); author it 1 m
  tall with its pivot at the bottom. (`secondaryModelPrefab`): the cloud puffs, × `modelScale`.
- Freeze Zone (`modelPrefab`): the procedural crystals (the frost disc and rim stay), scaled by the
  radius × `modelScale`; author it for a 1 m radius.
- Heal (`modelPrefab`): the rune under the feet, scaled to the ring's diameter × `modelScale`;
  author it 1 m across, flat, facing up.

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

- **Lightly play-tested.** The manager's first Play check (TrainingStage) confirmed aim, the gate
  claims and a 15-mana cast. Two findings were fixed after it:
  - M-1: the fire press is latched from its callback, so a press processed outside the frame's
    own input update is no longer lost.
  - M-2: the aim camera moved from (0.6, 0.1, 0) at 2.1 m to (1.2, 0.55, 0) at 3 m, because the
    chibi's head filled the bottom-centre. That framing is worked out geometrically, not yet seen.
    These are field defaults; a scene that saved the old values keeps them until they are set
    on its camera.

  Expect tuning of `aimMoveScale`, the camera framing, `orbSpeed` and the aim assist.
- **The four new spells are not play-tested by a person.** They were exercised by a throwaway
  headless Play-mode harness on TrainingStage (a virtual gamepad and keyboard driving the real
  `SpellInput`, so M-1's latched fire press included; 47 checks): selection by keys and by the pad
  cycle; each spell's cost; orb, fireball, thunderbolt and chain damage; the burn's six ticks; the
  stagger; the zone's slow on two dummies and its end; the heal completing in 2 s standing still,
  paying 40 at completion, and cancelling on movement, a hit, letting go and a jump. That harness
  is not committed.
- **Needs the user's OK:** the selection bindings (pad cycle on d-pad up/down while aiming, not
  left/right; see Controls), the costs and cooldowns in the Spells table, Heal's mana timing (on
  completion) and its input (aim + held fire), and the Thunderbolt following its target.
- **Freeze Zone and stagger only affect targets that opt in.** Today that is the training dummies,
  which never move, so on them the slow is only a status you can see. Newton needs his owner's
  change (§4).
- **"Standing still" for Heal reads input, not speed.** Being dragged by a force that does no
  damage (Newton's gravity well) does not cancel it; a hit does. The channel reads
  `PlayerMovementInput.Read()` a second time each frame, read-only.
- **The spell scenes are not committed.** Scenes are the agent manager's: re-run Setup Magic on
  ChibiSandbox and save, then Build Training Stage (its dummies now opt in to slows and staggers
  as it builds them), to get the selector, the heal visual and the new HUD in both scenes.
- **Spell prefabs are instantiated and destroyed per cast**, like the orb, with no pool. A burn is
  one prefab per burning target, parented to it.
- **One crosshair rule for every aimed spell.** The strike and zone reuse the orb's crosshair, cost
  label and aim assist; there is no ground preview of the zone's radius before casting.
- **Aim restrictions live in `AimInputClaims`.** On `AimStarted` it releases any held lock-on,
  then claims `InputBlock.Sword | InputBlock.LockOn` on the player's `PlayerInputGate`, so left
  mouse / RT only fire. While aiming it sets `PlayerMovementInput.DriveLimit` to
  `spell.AimMoveScale` (a walk, no sprint), and on `AimEnded` or disable it releases its own claim
  and puts the limit back to 1. A conversation claims `InputBlock.All`, which includes
  `InputBlock.Spell`: `SpellInput` then reads every button as released, so aim ends and nothing
  casts mid-conversation. None of this is play-tested yet; the claim and the cap have EditMode
  tests (`AimInputClaimsTests`).
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
- **Scene-only setup.** Mana, SpellInput, SpellCaster and AimInputClaims are added to the open scene's player,
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
| `SwordInput` | Left mouse (ignored while the cursor is free, and while aiming: `AimInputClaims` claims `InputBlock.Sword`) | West attack |
| `LockOnTargeting` | Tab / middle mouse toggle (dropped while aiming: `AimInputClaims` releases the lock and claims `InputBlock.LockOn`) | RT toggle, when not aiming |

Add under the table: "Right mouse left the tether and LT left sprint for the spell's aim (feat/mana-magic).
Players lose the right-mouse tether; Q and LB still tether. Left mouse and RT are shared between
the spell's fire and the sword attack / lock-on toggle; while aiming, `AimInputClaims`' gate claim
gives them to the spell alone."

Add to "Decisions":

> **Aiming is one read-only bit, `IAimState.IsAiming`.** `SpellCaster` implements it, and
> `SandboxOrbitCamera` reads it through a serialized `aimSource` (a `MonoBehaviour` cast to the
> interface). No source means never aiming. The camera never names magic. `AimInputClaims` follows
> the caster's `AimStarted` / `AimEnded` to claim `PlayerInputGate` for
> `InputBlock.Sword | InputBlock.LockOn` and to lower `PlayerMovementInput.DriveLimit` to a walk.
> `InputBlock.Spell` (in `All`) lets a conversation quiet the spell's own buttons.

And in "Facing", item 1 is no longer "None is registered yet":

> 1. A registered combat facing source (`SetCombatFacingSource`). `SpellCaster` registers itself in
>    `OnEnable` and clears the slot in `OnDisable` only if it still holds it. It returns the aim
>    point, *urgent* only while aiming, so the motor strafes. When not aiming it returns false
>    and facing falls through to the lock-on.

Also, for the spells: add rows for `SpellSelectInput` (keyboard 1-9 and numpad; pad d-pad up/down
**while aiming only**) and note under the table that d-pad up/down are shared with the camera zoom,
which the orbit camera ignores while aiming. Add to "Decisions": "Slows and staggers are opt-in
interfaces in the Magic namespace (`ISlowable`, `IStaggerable`); `StatusReceiver` is the ready-made
implementation. The Heal channel never claims the input gate: movement cancels it."

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
  - *Seeded once:* `ScriptableObjects/Magic/ManaConfig.asset`, `ArcaneOrb.asset`, `Fireball.asset`,
    `Thunderbolt.asset`, `FreezeZone.asset` and `Heal.asset`. Created from code defaults (the orb)
    or the seeds in `MagicSetup` if missing; on an existing spell, only a blank prefab or icon is
    filled.
  - *Regenerated every run:* `Prefabs/Magic/MagicOrb`, `Fireball`, `LightningStrike`,
    `FreezeZone` and `Burn` (GUIDs kept); the textures and `IceCrystal` mesh in
    `Art/VFX/Magic/`; the spell icons and slot sprites in `Art/UI/Magic/`; the
    `PlayerManaBar`, `SpellSelector`, `SpellCrosshair` and `SpellChannelBar` children of FightHUD
    (destroyed and rebuilt by `MagicHudBuilder`); the player's `HealChannelVfx` child and each
    dummy's `MagicStatusVfx` child.
  - *Scene wiring:* component-to-component references are rewritten every run. The player's data
    references (`Mana.config`, `SpellCaster.spell`, `SpellSelector.spells`) are filled only when
    blank. `SpellOrigin` is created at (0.3, 1.3, 0.45) only if missing. Every `TrainingDummy` in
    the open scene gets a `StatusReceiver` if it has none.
  - *Run order:* Build Training Stage builds its dummies with `StatusReceiver` and their visual
    (`MagicSpellArtBuilder.AttachStatusReceiver`), so it no longer needs Setup Magic re-run after it.
- **Scene-dirty policy:** Setup Magic is "Mark dirty and never save", and refuses to run in Play
  mode.
- **Entry point for an orchestrator:** `MagicSetup.AttachToPlayer(ChibiLocomotion)`. It marks the
  scene dirty and saves nothing; call `AssetDatabase.SaveAssets` after it.
- **Known hazards:**
  - add Mana, SpellInput, SpellCaster and AimInputClaims to the "`Chibi.prefab` lags the scene" list;
  - add "FightHUD" (Setup Magic finds it by name too) and "SpellOrigin" to the "lookups by name"
    line;
  - Setup Magic finds the camera by type (`SandboxOrbitCamera`), not through `Camera.main`.
- **Count:** four new global-namespace editor classes: `MagicSetup`, `MagicOrbPrefabBuilder`,
  `MagicSpellArtBuilder`, `MagicHudBuilder`.

### C. `Communication.md`: event rows and the interfaces row

Add to "Events that cross system boundaries (Sandbox)":

| Raiser | Event | Listeners |
|---|---|---|
| `Mana` (Magic/) | `Changed(mana)`, `Spent(cost)`, `SpendFailed(cost)`, `Filled` | `ManaBarView` |
| `SpellCaster` | `AimStarted`, `AimEnded`, `Cast(orb)`, `CastFailed` | `SpellCrosshairView` |
| `MagicOrb` | `Launched`, `Burst(orb, point, hits)`, `Fizzled` | `MagicOrbVisual` (on the same prefab) |
| `SpellCaster` | `SpellChanged`, `ChannelStarted`, `ChannelCompleted`, `ChannelCancelled(spell, reason)`; `Cast` now carries the `SpellDefinition` | `SpellSelector`; `HealChannelVisual`, `SpellChannelView` |
| `SpellSelector` | `SelectionChanged` | `SpellSelectorView` |
| `LightningStrike`, `FreezeZone`, `BurnStatus` | their telegraph / strike / placement / tick / end events | their visuals, on the same prefab |

In the mechanisms table, add `ISlowable` and `IStaggerable` (spells → opted-in targets) to the
"Small read-only interfaces at a boundary" row. In the mechanisms table, add `IAimState` (aiming → camera) to the "Small read-only interfaces at a
boundary" row, next to `IFacingSource`, which `SpellCaster` now also implements.
