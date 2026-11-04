# Enemies

Code: `Assets/_Project/Scripts/Sandbox/Enemies/` (namespace `Pickleball.Sandbox.Enemies`)
Tests: `Assets/_Project/Tests/EditMode/Enemies/`, `Tests/PlayMode/Scenarios/EnemyScenario.cs`
Setup: `Scripts/Editor/EnemySetup.cs` (Tools ▸ Pickleball ▸ Enemies)

`Enemy` is the base of every ordinary enemy, and on its own it is the basic one. **Bosses don't
inherit from it.** They script whole fights and ignore launches, so they share only `Health`.

## 1. Purpose and player-facing feel

The basic enemy stands still until you come within 14 m, runs at you, and hits for **10 damage
every 0.5 s** while you're in reach. It has **300 HP**.

- It runs at 4.5 m/s. You run at 5 and sprint at 7, so you can always get away, but it closes
  the moment you stop. It gives up once you're more than 20 m away, and stands where it is.
- The first strike comes 0.25 s after it reaches you, then one every 0.5 s.
- Jumping clears its reach (1.4 m of height), so you can dodge by jumping over it.
- Every sword hit stuns it. Launchers juggle it (Medium weight), and finishers knock it away.
  After 150 damage in one stretch it breaks out of the combo and fights through hits for 2 s.
- Hitting it wins back your grey health, as hitting a boss does. It also pays XP.
- It dies when it lands, then topples and shrinks away after 2 s.

## 2. Architecture

| Piece | Job |
|---|---|
| `Enemy` | Gameplay: gathers senses, picks a state, times strikes, reacts to hits. Virtual hooks for subclasses. |
| `EnemyConfig` | All the numbers, one asset per enemy type. Shipped: `ScriptableObjects/Enemies/BasicEnemy.asset`. |
| `EnemyBrain` | Plain C#: the awareness, reach, state and approach rules. |
| `EnemySenses` | Plain struct: what it knows this frame, handed to the rules and hooks. |
| `AttackCadence` | Plain C#: when a strike is due. |
| `EnemyMotor` | Owns the `CharacterController`: moves, falls, turns. |
| `EnemyWaddleView` | Presentation: poses the unrigged body (waddle, lunge, recoil, topple). |
| `Health`, `HurtFlash`, `StatusReceiver` | Existing components; `Health` takes its maximum from the config. |

**States:** `Idle`, `Chase`, `Attack`, `Stunned`, `Dead`. Priority runs dead, then stunned, then
what it wants to do (`EnemyBrain.Decide`).

**Events `Enemy` raises:** `StateChanged`, `Struck`, `Flinched`, `BrokeOut`, `Defeated`.
`EnemyWaddleView` listens to `Struck`, `Flinched` and `BrokeOut`, and reads `State`.

**Nothing else needs to know enemies exist.** Being a `Health` already makes it hittable by the
sword and spells, lockable by `LockOnTargeting`, and worth XP through `CombatXpSource`.

**A hit:** `Health.TakeHit` → `Hurt` (`Enemy` credits the player's grey pool) → `Impacted` →
`RevengeGauge.Add` → `JuggleState.Hit`. See `Juggle.md`.

**A juggle moves the root.** `EnemyMotor.Step` applies the juggle's height change directly while
carried, so the controller, lock-on and hit points follow the body and `Health.BodyOffset` stays
zero. The training dummy fakes this only because its root must stay planted on its measuring grid.

**Why strikes read a clock instead of summing `dt`.** The player's `Health` refuses discrete hits
for 0.5 s after the last one, the same as the attack interval. A timer that caught up on frame
overshoot would fire a hair under 0.5 s after the frame the last hit landed on, and the i-frames
would silently eat that hit, about every other strike. `AttackCadence` schedules the next strike at
`now + interval` on the same clock, so it falls due on the first frame the grace has closed.
`AttackCadenceTests.EveryStrike_LandsThroughThePlayersHitGrace` pins this at several frame rates.

**A strike is a discrete hit, not a continuous tick.** The base `Strike` calls the player's
`Health.Damage` with `continuous` left false, and 10 is above `hitGraceMinDamage` (2). So each strike
opens and respects the player's hit grace, plays the hurt flash, and triggers `CombatVoice`'s hurt sound
(which stays silent on continuous ticks). It is also recoverable, so 7 of each 10 goes into the grey pool.

**Why `Health.SetMaxHealth` exists.** Hit points live on `EnemyConfig` with every other number, so a new
enemy type is one new asset, and a spawner's `Configure` can change type at runtime. Baking the value into
the prefab would mean re-running the setup menu after each edit and a second place the number lives. The
setter only adds to `Health`: no existing caller's behaviour changes. The prefab's own field is also set to
the config's value, so the Inspector reads true outside Play mode.

**The one runtime search.** A prefab can't serialize a scene reference and a spawned enemy has no
setup script, so `Enemy` finds the player with `FindFirstObjectByType<ChibiLocomotion>` until it has
one, at most once a second, as `NewtonBoss` does. A spawner can call `SetTarget` to skip it.

## 3. How to tune

`ScriptableObjects/Enemies/BasicEnemy.asset`. Live in Play mode, except `Max Health`, which applies
at spawn. `Build Alien Enemy` only creates this asset when missing, so tuning survives a re-run.

| Field | Default | Feel |
|---|---|---|
| Max Health | 300 | |
| Corpse Seconds | 2 s | |
| Aggro Range / Lose Range | 14 / 20 m | Keep Lose above Aggro; the gap stops dithering at the edge. |
| Run Speed | 4.5 m/s | Above 5 you can't outrun it without sprinting. |
| Turn Rate | 10 /s | Lower swings wide. |
| Stop Distance | 1.3 m | Keep it under Attack Reach. |
| Attack Damage | 10 | |
| Attack Interval | 0.5 s | Below the player's 0.5 s hit grace, strikes start being refused. |
| First Hit Delay | 0.25 s | 0 hurts on contact. |
| Attack Reach / Height | 1.7 / 1.4 m | |
| Gravity / Max Fall Speed | 20 m/s² / 18 m/s | Walking off ledges only. |
| Juggle, Revenge | Medium presets | See `Juggle.md` §3. |

The look is on `EnemyWaddleView`, on the prefab.

## 4. How to extend

**A new enemy with the same behaviour:**
1. Create ▸ Pickleball ▸ Enemies ▸ Enemy Config, and set its numbers.
2. Duplicate the prefab's setup with a new model, or add a build method beside
   `EnemySetup.BuildPrefab`.

**A new enemy with new behaviour:** subclass `Enemy` and override what differs.

```csharp
public class SpittingEnemy : Enemy
{
    // Runs up to range rather than to contact.
    protected override Vector3 Steer(EnemyState state, in EnemySenses senses, float dt) => ...;

    // Throws something instead of hitting. The cadence still says when.
    protected override void Strike() => ...;
}
```

| Hook | Override to change |
|---|---|
| `Think` | Which state the senses call for |
| `Steer` | How it moves in a state (not called while thrown) |
| `Strike` | What an attack does |
| `OnStateChanged`, `OnDefeated` | Reactions, before listeners hear |
| `Despawn` | Remove the body, for pooling |

Unity's messages (`Awake`, `OnEnable`, `OnDisable`, `Update`, `OnDestroy`) are `protected virtual`
so that redeclaring one is a compiler warning rather than a silent replacement that stops the enemy
dead. Call `base`.

**Spawning at runtime:** instantiate the prefab, then optionally `Configure` and `SetTarget`.

**A rigged model:** add an Animator driver that listens to the same events, and remove
`EnemyWaddleView`.

## 5. Known limitations and open questions

- **Not hand-played yet.** `EnemyScenario` drives it with the real sword through the real input system, and
  its screenshots were looked at, but nobody has judged the feel by hand.
- **Measured one on one with the starting compass:** 300 HP took 23 sword hits, about 12 s of nonstop combo.
  The stun mostly holds it until it breaks out, and it then landed 4 strikes in a row during its 2 s of armor.
  With no combo running, it takes the player's 100 HP in about 5 s of contact.
- **One kill pays about 700 XP (`Leveling_Player`: 1.5 per damage plus 250 per defeat).** That took a new
  player from level 1 to level 5 in the playtest. Those rates were set with bosses and dummies in mind, so
  they need a decision before enemies appear in numbers.
- **It runs in a straight line, with no pathfinding.** It slides along walls but can get stuck behind
  one, and can't reach a platform that needs a route. No scene has a baked NavMesh today.
- **Enemies aren't in any shipped scene.** Use `Place Alien Enemies In Open Scene`, or the trial scene.
- **There is no enemy health bar.** `HealthBarView` is a HUD boss bar.
- **Spawned enemies show no damage numbers.** `DamageNumbers` has an explicit list, filled at setup
  time, so only enemies placed by the menu are on it. A runtime spawner needs an `AddSource` there.
- **Several enemies don't stack damage on the player.** The player's 0.5 s hit grace refuses a strike
  landing inside another's window, so a crowd deals the same 20 damage a second as one enemy (measured with
  two: still one landed hit per 0.5 s). That may be right for a crowd, or you may want grace to be shorter;
  it's a design call.
- **Player death still does nothing** (`Docs/Architecture/NewtonBoss.md`). Enemies go idle.
- **It has no idle wander, return-home or attack telegraph.** It stands where it gave up, and its first
  strike is only the 0.25 s delay.
- **The alien has 10.9k triangles,** over the 5k budget for a type meant to appear in groups. Mesh LODs
  help at a distance. Regenerate it lower if crowds cost too much on mobile.
