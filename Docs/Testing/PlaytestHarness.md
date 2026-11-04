# Playtest harness

A PlayMode test assembly (`Pickleball.Tests.PlayMode`, `Assets/_Project/Tests/PlayMode/`) that loads the
real scenes, drives them through the **real Input System** with virtual devices, records telemetry, and
writes screenshots and a summary per scenario. It *records*; it does not judge feel. Only a logged
exception, or the harness itself breaking, fails a scenario.

## Running it

Every scenario is `[Explicit]` and in category `Playtest`, so an unfiltered PlayMode or EditMode run skips
them. Run them by category from a project whose Editor is **closed** (batch mode needs the project lock):

```powershell
$wt = "C:\path\to\worktree"
$env:PICKBALL_PLAYTEST_RUN = "r1"          # optional; default is a timestamp
& "C:\Program Files\Unity\Hub\Editor\6000.3.5f2\Editor\Unity.exe" -batchmode -projectPath $wt `
  -runTests -testPlatform PlayMode -assemblyNames Pickleball.Tests.PlayMode -testCategory Playtest `
  -testResults "$wt\Logs\playmode-results.xml" -logFile "$wt\Logs\playmode.log"
```

- **Do not pass `-nographics`.** Screenshots render the camera to a texture and need a GPU.
- **Do not pass `-quit`.** Decide pass or fail from the results XML.
- One scenario: replace `-testCategory Playtest` with
  `-testFilter "Pickleball.Tests.PlayMode.Scenarios.MovementScenario.Movement"`. Several: separate names with `;`.
- The first run in a fresh worktree imports for a long time; seed `Library/` as described in
  [README.md](README.md#headless-batch-mode).
- Mind `ChibiSandboxSetup.AutoRunOnce` (see README): on a machine whose EditorPrefs token does not match
  the code, opening the project rebuilds the sandbox scene.
- A run takes real time: frames are paced at 60 fps wall-clock (see below). All scenarios together are
  about 3 minutes on top of editor start-up.

## Output

Outside the repo. Root is `PICKBALL_PLAYTEST_OUT` (default `C:\Users\willr\Pickball-playtest\screens`):

```
<root>\<runId>\<scenario>\
    01-walk.png ...      screenshots, 1280x720, with the overlay UI
    telemetry.json       one ScenarioReport: samples (every frame), events, metrics, errors, notes
    summary.md           human-readable: what could NOT be driven, notes, metrics, screenshots, timeline
```

`<runId>` is `PICKBALL_PLAYTEST_RUN` or `yyyyMMdd-HHmmss`, shared by every scenario in one editor session.

### What a sample holds

Position, velocity, planar speed, grounded, `MoveKind` and jump kind, time in move; sword
swinging / hitstop / step index / name / role / track / progress; lock-on target; dialogue phase and
line; Newton HP and phase; player HP and mana; camera position and rotation, camera-to-chest distance,
the player's viewport position (steady while moving = a tight camera) and camera speed; scaled frame
delta, wall time between frames and frame cost before the pacing sleep. Events add move changes, swings,
chain ends, hits, hurts, deaths, dialogue starts / lines / ends, teleports, steps and screenshots.
Application `Error`/`Assert` logs are recorded; `Exception` logs fail the scenario in `Complete()`.

### Derived metrics (`Metrics.Compute`)

Jump apex height, air time and carry per airborne episode (teleport falls excluded), max speed per move
state, first time each move state was seen, swing timeline with gaps between swing starts (the combo
window, in practice), hit count, lock-on changes, dialogue phase timeline, boss and player HP, mana
low point, camera distance range and speed, frame cost percentiles and hitches over 50 ms.

## How input works

`VirtualInput` adds a `Keyboard`, `Mouse` and `Gamepad` for the length of a scenario and queues full
device state events. `InputTestFixture` is not used: `Packages/manifest.json` has no `testables` entry for
`com.unity.inputsystem`. (A one-line change, `"testables": ["com.unity.inputsystem"]`, would make it
available; the harness does not need it.)

An unfocused or headless Editor drops keyboard and mouse state, so `PlaytestSession` sets the Input
System's `editorInputBehaviorInPlayMode` to `AllDeviceInputAlwaysGoesToGameView` and `backgroundBehavior`
to `IgnoreFocus` **in memory** before creating the devices, and restores both on dispose. Nothing is
saved. A self-check at the start of every scenario reports whether a key reaches the game.

## Pacing

`Time.captureDeltaTime` fixes game time to 1/60 s per frame, so "2 seconds" is 120 frames. The recorder
also sleeps each frame to 1/60 s of real time, because the dialogue typewriter and some camera recovery
run on `unscaledDeltaTime`; without the sleep they would run 15x too slowly relative to game time.
The scenario clock (`Rec.Now`) counts frames, so it keeps running while a conversation sets
`Time.timeScale` to 0.

## Scenarios

| Class.Test | Scene | Drives | Known limits |
|---|---|---|---|
| `ProbeScenario.Probe_*` | both | Harness self-check: actors found, one W press, one Space press, screenshots. Run it first when something looks wrong. | |
| `MovementScenario.Movement` | TrainingStage | walk, run, sprint, run-stop, skid turn, tap and held jump, double and triple jump, dive, belly slide, ground and air dash, crouch, wall contact, four ramps | Abilities are granted through `PlayerAbilities.Grant`. The player is teleported between sections. There is no wall-kick move in `MoveKind`. |
| `SwordScenario.Sword` | TrainingStage | single swing, 3-click chain, slow clicks, mash, finisher (all sword abilities granted), air combo, Tab lock-on, right-stick switching | Lock-on switching depends on dummy layout and line of sight; the summary says what it locked. |
| `AirCombatScenario.AirCombat` | TrainingStage | air homing with no lock, on a lock and out of range; a launcher into the full air chain; the air finisher's throw and the recovery; a long combo breaking out. Notes the distances, heights and reactions. | Abilities are granted through `SwordAbilities.Grant`. It approaches straight down the lane, because a diagonal clips `PlatformRight`'s corner at jump height. |
| `SwingTwistScenario.Swing3Contortion` | TrainingStage | swing 3 masked vs full body when standing: 1-2-3 standing, walking throughout, starting to walk mid-swing 3, and 1-2-3-4, each with Step_Slash3's `fullBodyWhenStanding` off and on. Records per frame the full-body layer weight, SwingTwist and WeaponClearance corrections, waist twist and bend, foot travel and steps, and chest, hip and foot jumps (the pop check). Takes gameplay and front-right shots of swing 3. | The flag is set by reflection in memory and restored. Writes `swing3-frames.csv` and `swing3-table.md`. The earlier SwingTwist-strength experiment is in git history (`f1d5634`). |
| `BirdyScenario.TalkToBirdy` | ChibiSandbox | walk to Birdy, wait for the prompt, E to start, E through every line, end, move again | Walking is straight-line steering with no path-finding; it teleports 2.5 m short if blocked and says so. |
| `BossScenario.NewtonFirstMinute` | ChibiSandbox | lock on, close in, swing, jump, 60 s or death | The fight starts with the scene (no trigger). The bot does not read telegraphs. |
| `BossScenario.NewtonFirstMinute_Observer` | ChibiSandbox | stays ~9 m back, never swings, screenshots every 4 s | Shows attack patterns, not combat. |
| `TrainingStageScenario.TrainingStage` | TrainingStage | mouse look, WASD + sprint + jump (readout), sword on a dummy, spell aim and fire, labelled-area tour | Spell needs a `SpellDefinition` on the player's caster; otherwise logged as not driven. |

## Adding a scenario

1. Add a class under `Scenarios/` with `[Category("Playtest")]` and `[Explicit("...")]`.
2. Write the test as `[UnityTest, Timeout(900000)] public IEnumerator X() => UniTask.ToCoroutine(Run);`
   with `static async UniTask Run()`.
3. Open with `using var s = await PlaytestSession.Begin("MyScenario", PlaytestScenes.TrainingStage);`.
   `s.A` holds the actors (player, motor, melee, lock-on, director, boss, camera...), `s.In` the devices.
4. Drive with `s.Seconds(t)`, `s.Tap(Key.Space)`, `s.Click(MouseButton.Left)`, `s.In.SetLeftStick(v)`,
   `s.WalkTo(() => point, stopDistance, timeout)`, `s.Teleport(pos, lookAt)`.
5. Observe with `s.Shot("label")`, `s.Note(text)`, `s.Log(step)`, `s.Describe(fromSampleIndex)`.
6. When you cannot do something, say so: `s.CannotDrive("what and why")`. It appears in the summary.
7. Finish with `s.Complete()`; it writes the outputs and fails only on exceptions. If the test throws
   earlier, `Dispose` still writes what was recorded.

Rules the harness keeps: it never saves a scene or asset, never writes `EditorPrefs`, never runs a
`Tools` menu, and never edits build settings, `ProjectSettings/` or `Packages/`. Scenes load with
`EditorSceneManager.LoadSceneAsyncInPlayMode(path, ...)`, so the build list is not needed.
Set-up shortcuts (`Teleport`, `GrantAll*Abilities`) log themselves so a report never credits the game
for something the harness handed over.
