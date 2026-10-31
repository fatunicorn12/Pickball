# Enemy Juggle (Castle Crashers weight classes)

Code: `Assets/_Project/Scripts/Sandbox/Juggle/` (namespace `Pickleball.Sandbox.Juggle`)
Tests: `Assets/_Project/Tests/EditMode/Combat/JuggleStateTests.cs`
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

## 2. Architecture

All of it is plain C#, simulates only the vertical axis, and is driven by `dt`. Everything is
EditMode-tested.

- **`JuggleState`:** one per enemy. It holds `Height`, `VerticalSpeed`, `AirHits` (n),
  `HitstunLeft` and `DefeatPending`.
  - `Hit(launch)` refreshes hitstun.
    - In the air it applies `vy = max(vy, relaunch × weightScale × decay^n)` and counts the hit.
    - On the ground only a launcher lifts it, at `launchSpeed × weightScale`.
    - The `max` means a hit never slows a body that is already rising faster.
  - `Tick(dt)` counts hitstun down and flies the body with exact constant-gravity steps, so the
    hit where a juggle gives out doesn't depend on frame rate.
    - It returns `JuggleLanding.Landed`, or `LandedDefeated` when the body lands carrying a
      defeat.
    - Landing resets n.
  - `Defeat()` returns true when the enemy should die now. When it is airborne and
    `defeatOnLanding` is set, the defeat is held until `LandedDefeated` instead.
  - `CanAct` is false while the enemy is in hitstun or airborne. An enemy's AI should check it
    before acting.
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

**Wiring in `TrainingDummy`:**
- `Health.Knocked` calls `Hit(launch)` and `Health.Died` calls `Defeat()`.
- `Update` ticks the juggle, and the visual rides `Height`. The root and its collider stay
  planted, so lock-on and the measuring grid still work.
- A dummy knocked out in the air doesn't refill until it lands, then waits `refillDelay` more.
- The `JuggleState` is built lazily (`TrainingDummy.Juggle`), because plain C# state comes back
  null after a hot reload.

Dependencies point one way: the sword knows only `IDamageable`, and the juggle knows nothing
about the sword.

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

**A Stoveface-style "heavier the longer it's up":** this would mean adding a time term to
`RelaunchFor` (decay per second airborne as well as per hit). Add a test that pins the new
counts.

## 5. Known limitations and open questions

- **Tested headless, not play-tested (2026-10-30).** `JuggleStateTests` pin the weight classes
  at 30, 60 and 144 fps. Nobody has watched a dummy juggle in Play mode yet.
- **The decay rarely matters today.** How many air hits a player can land per jump is set by the
  player's air chain (one air combo per airtime), so a Medium dummy usually lands before it
  gives out. The decay starts to matter once there are longer air chains, spells or co-op.
- **Vertical only.** Horizontal knockback still only rocks the dummy.
- **The hit's own `launchSpeed` (`HitSettings`) is not used.** The target's weight decides how
  high it goes.
- **Every dummy is Medium.** All four TrainingStage dummies get the default, because the scene
  predates the field. Giving each its own class (say, Light, Medium and Heavy on the floor,
  platform and top) would need a `TrainingStageSetup` change and a rebuild.
- `TrainingStage.unity` still serializes the removed `launchHeight` field. Unity ignores it, and
  it goes away the next time the scene is saved.
- Two hits in the same frame on a just-launched target count the second one as an air hit.
