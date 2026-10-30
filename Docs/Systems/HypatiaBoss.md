# Hypatia, the Astrolabe (boss fight)

The second Guardian. Hypatia hovers at the centre of the Rete, a brass astrolabe plate under
a night sky, and fights with mathematics you can read: rings that spiral out with one gap
each, conic sections whose shape the astrolabe's needle announces as an eccentricity, and in
phase 2 the floor of the Library burning band by band.

Code: `Assets/_Project/Scripts/Sandbox/Boss/Hypatia/` (contract and brain at the root; attacks
in `Rings/`, `Conics/`, `Burning/`; presentation in `Presentation/`). Setup:
`Editor/HypatiaFightSetup.cs` (menu, scene), `Editor/HypatiaFightArt.cs` (materials, meshes,
prefabs), `Editor/HypatiaFightModel.cs` (model import, controller). Tests:
`Tests/EditMode/Hypatia/**` and `Tests/EditMode/UI/BarkViewTests.cs`. Scene:
`Scenes/HypatiaFight.unity` (not in the build settings). Tuning:
`ScriptableObjects/Hypatia/HypatiaConfig.asset`.

## 1. Purpose and feel

- **Read, then punish.** Every attack is telegraphed on the floor or the dial before it can
  hurt you. Each conic volley ends in a **recalibration window**: she drops to 0.4 m, her glow
  and the astrolabe dim, her halo turns amber, a ring under her drains, and she takes **2×**
  damage. That window is the fight's punish beat.
- **Celestial Rings** expand from her at 4 m/s, one every 3.2 s (2.6 s in phase 2). Each has a
  30° gap; the gap turns 45° per ring, so the safe route is a spiral. A cyan wedge on the floor
  shows the next gap for 1 s before the ring launches. Low rings (0.8 m) are jumped; every third
  ring is **tall** (drawn 7 m with a column of light) and only the gap gets through. Phase-2
  rings burn (fire colours, always tall).
- **Conic Sections.** The needle swings to e on a dial marked 0, 1 and 2 and the dial shows
  "e = 0.62" for 1.2 s, tinted by kind:
  - **Ellipse** (e < 1, silver-blue, left of 1): 3–5 orbiters on Kepler orbits with her at a
    focus, each orbit traced faintly on the plate. They outlive the volley (12 s).
  - **Parabola** (e = 1 exactly, gold, on the 1 mark): a lob at your predicted position with a
    blast-sized landing ring that fills as it falls.
  - **Hyperbola** (e > 1, crimson, right of 1): the two asymptotes are drawn on the plate with
    the exact path as marching dashes, then one fast shot flies a path that passes through
    where you stood when it fired.
- **Alexandria's Burning** (phase 2, at 50% health): the floor burns from the rim inward, one
  band every 15 s. A band smoulders (flickering red) for 2 s, then burns (flames, 8 HP/s,
  grey/recoverable, only below 1.2 m). The 6 m hub never burns.
- **Slows.** She and everything she throws are slowable (Freeze Zone, and the planned
  Stationary Point). Freezing her freezes her timers, the needle, her hover and her animator;
  what she has already thrown keeps its own clock.

## 2. Architecture

```
HypatiaBoss (root, plate centre, turns to face the player)
├─ Health, StatusReceiver (+ MagicStatusVfx)          slowable, damageable
├─ CelestialRingEmitter     IRingAttack   (RingField: pure rules)
├─ ConicCaster + OrbiterRegistry  IConicAttack  (prefabs: Lob, Orbiter, Shot)
├─ ArenaBurn                IArenaBurn    (BurnSchedule, FireRule)
├─ ScrollKeeper             optional scrolls (prefab: Scroll); off by default
├─ HypatiaBarks             IBarkSource
├─ HypatiaBoss              the brain: WHEN; attacks own HOW
├─ Body                     hover/bob (brain moves local Y); CapsuleCollider hurtbox
│  ├─ HypatiaModel          Humanoid, Hypatia.controller (idle)
│  ├─ Glow (Light), Halo     Halo carries LockOnIgnore
├─ Astrolabe                mater, limb, ticks e=0..2, NeedlePivot, Readout (faces player); LockOnIgnore
└─ WindowRing               recalibration drain ring; LockOnIgnore
TheRete (arena centre)      plate (MeshCollider), lip, band rings, spokes, ticks, pointers,
                            PerimeterWall (invisible, Ignore Raycast), Stars
HypatiaFightViews           one child per presentation component (below)
```

**Who raises, who listens.** Gameplay never calls presentation; presentation subscribes in
`OnEnable`, unsubscribes in `OnDisable`, pools what it shows and never calls a gameplay setter.

| Raiser | Event | Listener |
|---|---|---|
| HypatiaBoss | `ConicTelegraphed(kind, e, lead)` | AstrolabeNeedle |
| HypatiaBoss | `RecalibrationStarted(duration)` / `RecalibrationEnded` | AstrolabeNeedle (rest), RecalibrationView (polls `IsRecalibrating`, `RecalibrationRemaining`) |
| HypatiaBoss | `PhaseTwoStarted`, `Defeated` | AstrolabeNeedle (rest on defeat); HUD reads Health |
| CelestialRingEmitter | `GapForecast`, `RingLaunched`, `RingRetired`, `RingHitPlayer`; `TryGetRing`, `TryGetForecast`, `TryGetRingById` | GapForecastView, CelestialRingView (reads `TryGetRing` every frame) |
| ConicCaster | `LobAimed`, `LobLanded`, `Stopped` | LobMarkerView |
| ConicCaster | `OrbiterSpawned`, `OrbiterRetired`, `Stopped` | OrbitPathView |
| ConicCaster | `AsymptotesTelegraphed`, `ShotReleased`, `ShotFinished`, `Stopped` | AsymptoteTelegraphView |
| ArenaBurn | `BandSmouldering`, `BandIgnited`, `Extinguished` (`PlayerBurning` unused) | BandFireView |
| ScrollKeeper | `ScrollSpawned`, `ScrollCollected`, `ScrollDespawning` | none: the scroll prefab draws itself |
| HypatiaBarks | `Spoke` (`IBarkSource`) | BarkView on FightHUD |
| Health | `Changed`, `Hurt` | HealthBarView ("Hypatia — The Astrolabe", notch at Phase Two At), DamageNumbers |

The attack contract (`IHypatiaAttack.Bind(HypatiaContext)`, `IRingAttack`, `IConicAttack`,
`IArenaBurn`) is rebound by the brain in every `OnEnable`, so a script reload in Play mode
re-binds every attack. Each attack's rules live in plain C# (`RingField`, `KeplerOrbit`,
`HyperbolaFlyby`, `BurnSchedule`, `ConicPicker`, `RecalibrationWindow`) and are EditMode-tested.

**Clocks.** Her clock is `HypatiaContext.BossDelta(dt) = dt × StatusReceiver.SpeedMultiplier`.
On it: every brain timer (rings, conics, needle lead, burn interval), the recalibration window,
her hover, turn and animator speed, ring forecasts, the caster's staggers and asymptote lead,
the smoulder countdown, the scroll spawn timer, and the needle's swing. On real time: launched
rings (they keep expanding), the fire's damage, barks. On each projectile's own clock
(`HypatiaProjectile.ScaledDeltaTime`, its own `SlowLedger`): lobs, orbiters, the shot, scrolls.
Projectiles carry a trigger `SphereCollider` on the Ignore Raycast layer (every spell's
`HitMask` is `~0`), which is how the Freeze Zone (it queries triggers) finds them.

**Lock-on.** `LockOnTargeting` measures a target's aim point and size from every renderer under
its `Health`. The astrolabe, halo and window ring ride on her root so they turn and hover with
her, so each carries a `LockOnIgnore` marker that lock-on skips (`LockOnTargeting.MeasureAim`).
Without it the aim sat on the dial 3 m above her head with a 2 m radius, and the reticle and
camera framed the dial instead of her. Anything new parented to her root that is not her body
needs the marker too.

**Data assets.** One `HypatiaConfig` holds every gameplay number; the setup creates it once and
never overwrites it. Presentation colours and sizes are serialized on the view components with
defaults in code; the scene is regenerated, so change those defaults in code.

**Setup.** `Tools ▸ Pickleball ▸ Setup Hypatia Fight` (see `Docs/Architecture/EditorSetup.md`):
1. Creates the config if missing. Configures the FBX compare-before-write: Humanoid (Create
   From This Model; a failed avatar is an error, never a Generic fallback), import scale for
   1.7 m (measured from bounds, currently 1.566), material remap to `HypatiaPrincess.mat`
   (URP Lit, created once), texture max size 2048.
2. Fingerprints its inputs (ChibiSandbox.unity bytes, every Hypatia script, BarkView, this
   builder, the config's values, the model, Newton's idle clip and the shared VFX textures). If
   it matches `Art/Characters/Hypatia/Generated/HypatiaFight.fingerprint.txt`, it stops: a second
   run is a no-op. **Setup Hypatia Fight (Force Rebuild)** ignores the fingerprint.
3. Otherwise rebuilds materials and meshes (`Generated/`), the four prefabs
   (`Prefabs/Hypatia/`), `Animation/Hypatia/Hypatia.controller` (one Idle state on Newton's
   Humanoid idle, created once), then byte-copies ChibiSandbox over `HypatiaFight.unity` (the GUID
   is kept), strips Newton, the academy, Birdy, tether anchors and the skateboard/tether pickups
   (double-jump and dash pickups move beside the spawn), builds the Rete, Hypatia, the views,
   rebinds the copied FightHUD (boss bar, notch, damage numbers, bark line) and sets a night
   (solid indigo sky, star particles, moonlight, fog, no sky reflection; the orbit camera's
   occlusion mask drops Ignore Raycast so the perimeter wall never pulls it in).

Only assets under the fight's own folders are saved (`SaveAssetIfDirty`), so a run never
writes other systems' dirty assets.

## 3. How to tune

Everything gameplay is on `HypatiaConfig.asset`; every field has a tooltip with its unit.

| Group | Fields | Feel |
|---|---|---|
| Fight | Max Health 700, Phase Two At 0.5, Unprompted Bark Interval | length; when the floor starts burning |
| Body | Hover Height 2.6, Recalibration Hover Height 0.4, Hover Settle Rate, Bob, Turn °/s | above ~2.5 m only jump attacks reach her outside the window |
| Arena | Arena Radius 13, Band Outer Radii {6, 8.33, 10.67, 13} | plate size and burn bands (re-run setup: the plate and band rings are built from these) |
| Celestial Rings | First Ring Delay, Interval P1/P2, Speed 4, Start Radius 1.5, Thickness, Low Ring Height 0.8, Tall Ring Visual Height 7, Tall Every 3, Gap Snap/Width/Step, Forecast Lead 1, damages, Max Live, Rings During Recalibration | density and readability of the spiral |
| Conic Sections | First Conic Delay, Interval P1/P2, Needle Lead 1.2, kind weights, No Repeat | how often and which |
| Parabola | Flight Time, Marker Lead, Gravity, Lead Fraction, Max Lead, Count P1/P2, Stagger, Blast Radius, Damage | how predictive and how punishing the lob is |
| Ellipse | Count Min/Max, Max Live, e Min/Max, Periapsis Min, Apoapsis Max, Period Min/Max, Lifetime, Stagger, Height, Radius, Damage, Rehit Delay | how crowded the orbits get |
| Hyperbola | e Min/Max, Periapsis, Asymptote Lead, Speed, Reach, Exit At Player, Height, Radius, Damage | how sharp and fast the flyby is |
| Recalibration | Duration 2.5, Damage Multiplier 2, Normal Multiplier 1 | the punish beat |
| Alexandria's Burning | First Burn Delay, Burn Interval 15, Warning Lead 2, Min Safe Radius 6, Fire DPS 8, Fire Height 1.2 | how fast the arena shrinks |
| Scrolls | Enabled (off), Interval, Max Live, Drift, Lifetime, Height, Pickup Radius, Health Cost | optional collectibles in the fire |

Presentation (on `HypatiaFightViews/*` and the boss's parts): ring, wedge, needle and window
colours, column height, flicker rate, flame density, line widths. `HypatiaBarks` (on her root)
has the lines, `minGap`, `attackChance` and `lineDuration`.

## 4. How to extend: add an attack in 6 steps

1. Add its numbers to `HypatiaConfig` under a new `[Header]`, with tooltips and units.
2. Write the rules as a plain C# class and EditMode-test them (as `RingField`, `BurnSchedule`).
3. Write the component: implement `IHypatiaAttack` (or a new `IXxxAttack` with what the brain
   needs to call), read everything through the bound `HypatiaContext`, time yourself on
   `Context.BossDelta(dt)`, make released objects `HypatiaProjectile`s, raise C# events for
   presentation, and make `StopAll` idempotent.
4. Give the brain a serialized `MonoBehaviour` field and an independent `BossTimer` for it in
   `HypatiaBoss` (bind it in `OnEnable`, stop it in `StopFighting`).
5. Add a view under `Presentation/` that only subscribes (pool what it shows, no per-frame
   allocation), and wire it, plus any prefab, in `HypatiaFightSetup`/`HypatiaFightArt`.
6. Add a `HypatiaBarks.Occasion` if she should comment on it; re-run Setup Hypatia Fight.

## 5. Known limitations and open questions

- **Standing directly under her is ring-safe during recalibration.** Rings are born at 1.5 m
  (Ring Start Radius), so the hub inside that never meets a ring.
- **Gaps near her are tight.** The whole capsule must fit the gap (`RingMath.CapsuleFitsGap`), so
  at 2 m the 30° gap is ~1 m and barely passable; the floor wedge shows the angle, not that.
- **A refused conic volley still opens the window.** If `Fire` is refused (busy, unbound), the
  brain sees `IsBusy` false and finishes the volley, so she recalibrates without having attacked.
- **`ArenaBurn.StopAll` has no reset.** There is no retry path: after her death or the player's,
  the burn is inert until the scene reloads.
- **Scroll cost is passed as continuous damage** (`continuous: true`) so hit grace neither opens
  nor refuses it; it is a choice, not a hit.
- **Orbiters keep the focus where she was at spawn.** She never translates today, so this only
  matters if she ever moves.
- **Her recalibration window lengthens in real time when she is slowed.** Intended: it runs on
  her clock, so freezing her during the window holds it open.
- **The hyperbola aims its PATH through the player** (7377d1b), not the outgoing asymptote, so
  a player who stands still is hit; the path still stays between the drawn asymptotes.
- **A frozen orbiter still hurts on contact.** Freezing stops its motion, not its hit test.
- **Tall rings ignore height**: the player's best combo reaches ~9 m (`Movement.md`) and the
  Gravity Anchor higher, so a tall ring hits at any height and only the gap gets through.
- **Heads-up for the 4D Stationary Point:** `FreezeZone` now queries trigger colliders (for her
  projectiles), which also reaches any trigger-only targets the Stationary Point uses.
- **Placeholder look.** Primitives and procedural rings; her only animation is Newton's idle,
  retargeted (no cast, hit or death clips). The perimeter is an invisible 40 m wall at the rim;
  the decorative lip sits outside it.
- **`ArenaBurn.PlayerBurning`** has no listener; the HUD shows fire damage only through the
  health bar and damage numbers.
- **Editor-only quirk**: rebuilding renders the copied HUD, and TMP may then hold its dynamic
  font atlases dirty. The setup no longer saves them, but anything else that calls
  `AssetDatabase.SaveAssets` will write them (`TextMesh Pro/.../Fonts & Materials`).
