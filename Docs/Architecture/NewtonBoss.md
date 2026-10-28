# Newton boss fight: decisions

Snapshot as of `a930952`. Code: `Scripts/Sandbox/Boss/`, `Sandbox/IDamageable.cs`,
`UI/{BarkView,DamageNumbers,HealthBarView}.cs`. Setup: `Editor/NewtonBossSetup.cs`,
`NewtonFightSetup.cs`, `NewtonFightArt.cs`. Design intent is in `ROADMAP.md` §5.

There is no `Docs/Systems/NewtonBoss.md` yet, even though CodingStandards §5 requires one. Until
it exists, this page is the reference. When someone writes the system doc, move this content
there.

## What sits on the boss root

- `Health`: 600 HP, no grey/regain pool for the boss. Setup forces both.
- `NewtonBoss`: the brain.
- `FluxionBeam`, with its `BeamMuzzle` line renderer.
- `InverseSquareVolley`
- `AppleBarrage`
- `NewtonBarks`

Prefabs live in `Prefabs/Boss/` (gravity well, apple, impact marker). The HUD is a `FightHUD`
canvas with `HealthBarView`s, `DamageNumbers` and `BarkView`, holding direct references to the
boss's and player's `Health`.

## Decisions

**Attacks are four timers running at once, not a sequencer.** `NewtonBoss.Update` runs
independent cooldowns for wells, beam, volley and barrage. Overlap is intended. Approximate
defaults:

| Attack | Phase 1 | Phase 2 |
|---|---|---|
| Gravity wells | first after 2 s, then every 4.5 s; at most 3 live; a blocked drop retries after 0.25 s | every 6.5 s |
| Fluxion beam | every 9 s; charges 1.1 s, fires 2.4 s | every 7 s |
| Inverse-square volley | first at 3.6 s, then every 6 s; the timer runs 2.5× faster when the player is close | every 4.5 s, as two rings 0.65 s apart |
| Apple barrage | — | starts 1.5 s into phase 2, then 6 s on / 5 s off |

The **animator decides what actually plays**: `Channeling` blocks other casts, Death wins over
everything, and unused triggers expire after 0.3 s. Animator parameter names are a string contract
between `NewtonBossSetup.BuildController` and the hashes in `NewtonBoss`. Rename one, rename both.

**Phase 2 is one-way.** It is entered when health is at or below `phaseTwoAt` (55%) and never
reverts, even after a heal. There is no retry path: `Phase`, the barrage flag and the animator's
Dead flag are never reset.

**The damage pipeline.**
1. A hit arrives through `IDamageable.TakeHit` and becomes `Health.Damage(amount, at, recoverable: true)`.
2. The owner's `IncomingDamageFilter` runs. Newton's filter applies the **weak-point range
   multiplier** (inverse-square with distance), so the sword doesn't need to know what it hit.
3. `Hurt(applied, at)` fires, with `LastHit*` valid inside the handler.
4. `Died` fires once.

Details:
- `IncomingDamageFilter` is a **single slot**. `NewtonBoss` overwrites it in `OnEnable` and nulls
  it in `OnDisable`.
- Knockback is forwarded for the receiver to interpret, and Newton ignores it.
- The beam deals `recoverable: false` damage. That damage skips the grey pool, but it **does**
  restart the grace timer, so beam damage stops grey from draining.

**The boss pays the player's regain, not the weapon.** `NewtonBoss.OnHurt` passes the post-filter
damage to the player's `Health.CreditDamageDealt`, which converts grey back into health at
`regainPerDamageDealt` (0.9×) on the *player's* `Health`. Hitting training dummies never refunds
grey. The rolling-HP pool is configured on `Health`:
- `regainShare` (0.7): share of a hit that goes grey.
- `regainGrace` (2.5 s)
- `regainDecay` (14 HP/s)

**Apples and orbs move themselves, without Rigidbodies.** Contacts are swept segment tests with an
Update/LateUpdate split. Two apples converging cancel each other ("convergence is cover"); the
closest-approach test is exact within a frame.

**Shared static lists.** `GravityWell.Active` and `Apple.Airborne` let the player's motor and
Spacetime find live wells and apples without the boss knowing about either. `Collapse` removes a
well from `Active` immediately, not at its end-of-frame destroy. These lists deviate from
CodingStandards §1 on purpose.

**Tuning lives in code defaults.** Setup Newton Fight re-adds the five fight components on every
run, so Inspector edits are lost. Change the `[SerializeField]` defaults in code, then re-run the
menu. Some values are coupled across files and must change together:

| Coupling | Files |
|---|---|
| Well strength ↔ the player's `fieldDamping` | the drift at 5 m must stay noticeable (bug fixed in `d489f33`) |
| Apple terminal speed ↔ spawn height | terminal speed must exceed free-fall speed from the spawn height |
| Marker size ↔ apple hit radius | |
| Hand-copied `windUp` / `chargeTime` | `NewtonBossSetup` (animation timing) |
| HUD phase notch | baked into the HUD at setup time |

**Setup order.** Setup Newton (Boss) destroys and rebuilds the boss root, so Setup Newton Fight
must run after it. See [EditorSetup.md](EditorSetup.md#run-order).

## Known gaps

As of `a930952`, none of these is fixed:
- **Player death does nothing.** Nothing subscribes to the player's `Died`.
- **A script reload in Play mode breaks the boss.** `NewtonBoss.Health` and the cached `_player`
  in five components are set only in `Awake`/`Start`. After a reload, `NewtonBoss` throws in
  `OnEnable`/`Update` and every attack silently stops. Re-fetch these in `OnEnable`, per
  CodingStandards §4.
- **`Health.Damage(NaN)` kills instantly.** The `<= 0` guard lets NaN through. `Heal(NaN)` and
  `CreditDamageDealt(NaN)` misbehave the same way.
- **Barrage ground height is sampled once, under the boss,** who stands about 1.7 m above the
  arena floor. Markers and splats near the player may float.
- **Several orbs of one volley ring can hit at point blank,** and `Health` has no invulnerability
  frames.
- **Apples spawned close together can cancel each other** almost immediately after spawning.

## Adding an attack

1. Add the component to the boss root in `NewtonFightSetup.SetupBoss`.
2. Add a timer in `NewtonBoss.Update`, with telegraph, bark and fire calls.
3. Add the trigger in `NewtonBossSetup.BuildController` and its hash in `NewtonBoss`. Grow
   `TriggerHashes` and `_triggerExpiry` together.
4. Add a bark `Occasion` with lines in `NewtonBarks`.
5. Stop it in `NewtonBoss.OnDied`.
6. Add EditMode tests for its maths, using `BossTestFixture`.
