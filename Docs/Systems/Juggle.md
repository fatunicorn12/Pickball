# Enemy Juggle (Castle Crashers weight classes, Kingdom Hearts II reactions)

Code: `Assets/_Project/Scripts/Sandbox/Juggle/` (namespace `Pickleball.Sandbox.Juggle`)
Tests: `Assets/_Project/Tests/EditMode/Combat/JuggleStateTests.cs`, `HitReactionTests.cs`, `RevengeGaugeTests.cs`
Used by: `Assets/_Project/Scripts/Sandbox/Training/TrainingDummy.cs`

This doc covers the enemy's half of the air game. The player's half is in `SwordAbilities.md`:
air chains, air hang, and one air combo per airtime.

## 1. Purpose and player-facing feel

Castle Crashers mostly limits juggles on the enemy's side, by weight. This system does the same:

- A **launcher** pops a target into the air. Every **air hit** after that relaunches it, by less
  each time for heavier targets:
  - **Light:** every air hit lifts it as high as the first, so it juggles for as long as you keep
    hitting.
  - **Medium:** the lift shrinks with each hit. It stops lifting on the 11th air hit and lands on
    about the 17th (at one hit every 0.3 s).
  - **Heavy:** launches lower, lifts once, and is down by its 3rd air hit.
- Once a target's lift is spent, each hit still stops its fall for a moment. It steps down to
  the ground instead of dropping.
- **Hitstun:** every hit refreshes it. A stunned or airborne enemy can't act.
- **Death waits for the ground:** a target knocked out in the air dies only when it lands, and
  can be juggled on the way down.
- The training dummies are Medium. Bosses (Newton, Hypatia) ignore launches.

Each attack also says what kind of blow it is (`HitReaction`, from the way KH2's attack data
splits them):

| Reaction | On the ground | In the air | Shipped on |
|---|---|---|---|
| **Flinch** | stuns | relaunches by the weight rule | every mid-chain hit |
| **Launch** | pops it up | relaunches by the weight rule | Rising Finish, Area Under the Curve, the ruler's slam rings |
| **Heavy** | stuns for `heavyStunScale` × as long | same, and relaunches by the weight rule | the ruler's two slams |
| **Knock Away** | throws it clear on an arc | same; ends the juggle | Unit Circle (blade and ring), the ruler's Phase Shift |

A thrown body takes no further reaction until it has landed and recovered (`recoverSeconds`). So
a finisher's ring can't re-lift what its blade just threw, and a finisher buys a breather.

**Enemies break out of long combos** (KH2's revenge value). Damage in one stretch fills a tally
(`RevengeGauge`). At the limit the enemy shrugs the stun off and for `armorSeconds` takes hits
without flinching, being lifted or being pushed. It still takes the damage.

## 2. Architecture

All of it is plain C#, simulates only the vertical axis, and is driven by `dt`. Everything is
EditMode-tested.

- **`JuggleState`:** one per enemy. It holds `Height`, `VerticalSpeed`, `AirHits` (n),
  `HitstunLeft` and `DefeatPending`.
  - `Hit(launch)` refreshes hitstun.
    - In the air it applies `vy = max(vy, relaunch × weightScale × decay^n)` and counts the hit.
    - On the ground only a launcher lifts it, at `launchSpeed × weightScale`.
    - The `max` means a hit never slows a body that is already rising faster.
  - `Hit(reaction, knockSpeed)` is the same with the kind of blow. A knock-away sets
    `IsKnockedAway` and `KnockSpeed`, and sets its vertical speed outright. Any hit on a thrown or
    recovering body changes nothing.
  - `Tick(dt)` counts hitstun and recovery down and flies the body with exact constant-gravity
    steps, so the hit where a juggle gives out doesn't depend on frame rate.
    - It returns `JuggleLanding.Landed`, or `LandedDefeated` when the body lands carrying a
      defeat.
    - Landing resets n, and turns a throw into `RecoverLeft`.
  - `Defeat()` returns true when the enemy should die now. When it is airborne and
    `defeatOnLanding` is set, the defeat is held until `LandedDefeated` instead.
  - `BreakOut()` drops the hitstun at once. A body in the air still has to land.
  - `CanAct` is false while the enemy is in hitstun, airborne or recovering. An enemy's AI should
    check it before acting.
- **`RevengeGauge`:** one per enemy, and asked before the juggle. `Add(damage)` returns true on the
  one hit that breaks it out. `IsArmored` says the body shouldn't react. `Tick(dt)` runs the armor
  down and sheds the tally after a delay. It counts damage rather than hits, so a heavy weapon's
  fewer, bigger swings fill it no faster than a light one's. **`RevengeSettings`** holds its
  tuning; `For(JuggleWeight)` has the presets.
- **`JuggleSettings`** (a serializable struct, inline on the enemy like `HitSettings` is on a
  step) holds the tuning and the rule's helpers:
  - `RelaunchFor(n)`: a relaunch under `minRelaunch` counts as 0, so the `max` then only stops the
    fall. That gives a decaying juggle a definite hit where it gives out, instead of hovering on
    ever-smaller taps.
  - `CeilingSpeed(height)`: every lift is capped so its arc peaks at `maxHeight`. Without the
    cap, any lift strong enough to hold a body up also makes it climb a bit more with every hit,
    and a lightweight would rise without limit.
  - `For(JuggleWeight)`: the Light / Medium / Heavy presets.
- **`JuggleWeight`** is the class label. **`JuggleLanding`** is what `Tick` reports.

**How a hit reaches it:** `MeleeCombat` calls `IDamageable.TakeHit(HitImpact)`. `Health` raises
`Impacted` with the reaction, and still raises the older `Knocked`. A receiver with no use for the
reaction needn't implement the overload: a default interface method forwards it as the plain call,
with only a launch surviving. Spells make the plain call, which `Health` turns into a Flinch or
Launch impact, so they reach a dummy as before.

**Wiring in `TrainingDummy`:**
- `Health.Impacted` asks the gauge, then calls `Hit(reaction, flatSpeed)`. `Health.Died` calls
  `Defeat()`.
- `Update` ticks both. The visual rides `Height` plus the distance thrown, and walks back once
  `CanAct` is true. The root stays planted, so the measuring grid still works.
- **The body collider rides with the visual** (`CapsuleCollider.center`), and
  `Health.BodyOffset` tells `LockOnTargeting.AimPoint` where the body went. Otherwise an air
  swing that matched the visual's height would pass over a hitbox left on the ground.
- A dummy knocked out in the air doesn't refill until it lands, then waits `refillDelay` more.
- `JuggleState` and `RevengeGauge` are built lazily (`TrainingDummy.Juggle`, `.Revenge`), because
  plain C# state comes back null after a hot reload.
- `BrokeOut` is raised once per break-out. The dummy pulses its visual; an enemy's AI would start
  its counterattack here.

Dependencies point one way: the sword knows only `IDamageable`, and the juggle knows nothing
about the sword. `HitReaction` and `HitImpact` sit beside `IDamageable`, in `Pickleball.Sandbox`,
for that reason.

## 3. How to tune

Tune the `juggle` block on each `TrainingDummy`. Edits apply on the next hit, live in Play mode.

- `weight` is the class label. **TrainingDummy ▸ ⋮ ▸ Apply Weight Preset** restores that class's
  numbers.
- `launchSpeed` (m/s): the pop from a ground launcher. 3.7 m/s is the dummy's old 0.7 m hop.
- `relaunchSpeed` (m/s) and `weightScale`: the first air hit's lift. To hold a target up, an air
  hit needs about gravity × hit interval ÷ 2, about 1.5 m/s at 0.3 s.
- `decay`: the share of the lift kept per air hit already taken. 1 juggles forever; Medium's 0.9
  gives out after about 17 hits; Heavy's 0.5 after 1-3.
- `minRelaunch` (m/s): lifts under this only stop the fall. Raise it to make a decaying juggle
  give out sooner.
- `maxHeight` (m): how high air hits can carry it, measured from where it stands.
- `hitstun` (s); `gravity` (m/s², lower is floatier); `defeatOnLanding`.
- `heavyStunScale`: how much longer a Heavy blow stuns.
- `knockAwayLift` (m/s, before `weightScale`), `knockAwayCarry` (share of the blow's speed) and
  `recoverSeconds`: a knock-away's arc and how long it stays down.

The `revenge` block on each `TrainingDummy`, also live:

- `limit` (damage in one stretch, 0 = never): Light 220, Medium 150, Heavy 100. A compass with
  every ability learned deals about 130 per ground-and-air loop.
- `decayPerSecond` and `decayDelay` (s): how soon a pause forgives it (30 and 1 in every class).
- `armorSeconds` (s): how long it ignores reactions after breaking out (2 in every class).

Which reaction an attack deals is `reaction` in the step's `hit` block (and its `burst`'s). A Flinch
with a `launchSpeed` above 0 counts as a Launch, so steps tuned before reactions existed need no edit.

Preset numbers, as (launch, relaunch, weightScale, decay, minRelaunch, maxHeight, hitstun):

| Class | Numbers | Air hits before landing (one per 0.3 s) |
|---|---|---|
| Light | 3.7, 4, 1.1, 1, 1.5, 3, 0.6 | never lands |
| Medium | 3.7, 4, 1, 0.9, 1.5, 3, 0.5 | 17 |
| Heavy | 3.7, 4, 0.6, 0.5, 1.5, 3, 0.35 | 3 |

At one hit every 0.25 / 0.35 / 0.4 s, Medium lands on its 20th / 15th / 14th air hit and Heavy on its 3rd / 2nd / 2nd.
If you change a preset, update the counts pinned in `JuggleStateTests`.

## 4. How to extend

**Give an enemy a juggle:**
1. Serialize a `JuggleSettings` on it, defaulting to `JuggleSettings.For(...)`.
2. Build a `JuggleState` from it lazily, as `TrainingDummy.Juggle` does.
3. Call `Hit(launch)` from `Health.Knocked`. Call `Defeat()` from `Health.Died`; if it returns
   false, die when `Tick` reports `LandedDefeated`.
4. Tick it every frame, move the body or visual by `Height`, and gate the enemy's AI on `CanAct`.
5. For the reactions, subscribe to `Health.Impacted` instead of `Knocked` and pass
   `Hit(impact.Reaction, flatKnockbackSpeed)`. Move along the knock direction by `KnockSpeed` per
   second. If the root stays planted, set `Health.BodyOffset` and move the collider too.
6. For break-outs, serialize a `RevengeSettings`, ask `RevengeGauge.Add(impact.Damage)` first, skip
   the reaction while `IsArmored`, call `Juggle.BreakOut()` on the break, and start the
   counterattack there.

**Add a new kind of blow:** add it to `HitReaction`, handle it in `JuggleState.Hit`, and pin it in
`HitReactionTests`. It's then a dropdown on every step.

**A Stoveface-style "heavier the longer it's up":** this would mean adding a time term to
`RelaunchFor` (decay per second airborne as well as per hit). Add a test that pins the new
counts.

## 5. Known limitations and open questions

- **Tested headless, not play-tested (2026-10-30).** `JuggleStateTests` pin the weight classes
  at 30, 60 and 144 fps. Nobody has watched a dummy juggle in Play mode yet.
- **The decay rarely matters today.** How many air hits a player can land per jump is set by the
  player's air chain (one air combo per airtime), so a Medium dummy usually lands before it
  gives out. The decay starts to matter once there are longer air chains, spells or co-op.
- **Only a knock-away moves it across.** Other horizontal knockback still only rocks the dummy.
- **Dummies have no counterattack.** A break-out shows as the dummy pulsing and then not flinching.
  There's no enemy AI to use `BrokeOut` yet, so the "enemy retaliates" half of KH2's revenge value
  is still to come. Bosses don't use the gauge: they already ignore launches and knockback.
- **The numbers are starting guesses,** not measured from KH2 and not tuned by hand.
- **The hit's own `launchSpeed` (`HitSettings`) only marks a launcher.** The target's weight decides
  how high it goes.
- **Every dummy is Medium.** All four TrainingStage dummies get the default, because the scene
  predates the field. Giving each its own class (say, Light, Medium and Heavy on the floor,
  platform and top) would need a `TrainingStageSetup` change and a rebuild.
- `TrainingStage.unity` still serializes the removed `launchHeight` field. Unity ignores it, and
  it goes away the next time the scene is saved.
- Two hits in the same frame on a just-launched target count the second one as an air hit.
