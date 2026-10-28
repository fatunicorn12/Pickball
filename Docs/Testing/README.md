# Testing

How tests are organised and run in this project, and the conventions new tests should follow.
For **what** still needs testing, see [QA-Assessment.md](QA-Assessment.md).

## What exists (as of the P0 batch, on top of `f876b63`)

| Assembly | Folder | State |
|---|---|---|
| `Pickleball.Tests.EditMode` | `Assets/_Project/Tests/EditMode/` | 275 tests: 267 pass, 8 skipped as known bugs. References `Pickleball.Runtime` only. |
| `Pickleball.Tests.PlayMode` | `Assets/_Project/Tests/PlayMode/` | **Playtest harness** only: `[Explicit]`, category `Playtest`, so no normal run executes it. Loads the real scenes, drives virtual keyboard / mouse / gamepad, writes telemetry and screenshots outside the repo. See [PlaytestHarness.md](PlaytestHarness.md). No assertion-style PlayMode tests yet. |
| Editor-tool tests | — | **None.** No test assembly references `Pickleball.Editor`, so setup menus can't be tested. |
| CI | — | **None.** There is no `.github/` and no build or test scripts. |

Subfolders mirror `Scripts/Sandbox/`: `Combat/`, `Movement/`, `Weapon/`, `Boss/`, `Cloth/`.
`PickleballCore/` covers the original game. Its namespace avoids `…Tests.Pickleball` and
`…Tests.Court`, which would shadow the `Pickleball` namespace and the `Court` type. `Validation/`
holds the read-only shipped-asset checks. The older boss and Spacetime tests sit at the root of
`EditMode/`.

## Conventions for EditMode tests

- **Test logic, not scenes.** Put rules and maths in plain C# or static classes and test those
  directly (CodingStandards §6). [Architecture/RuntimeConventions.md](../Architecture/RuntimeConventions.md#test-seams-the-code-already-offers)
  lists the seams that already exist.
- **Sweep frame rates.** Anything integrated over `dt` gets `[TestCase(1f/30)]`, `[TestCase(1f/60)]`
  and `[TestCase(1f/144)]`, and asserts the same outcome within a tolerance.
- **Inject time.** EditMode has no meaningful `Time.deltaTime` or `Time.time`. Give time-based
  components a `Tick(…, now, dt)` seam, like `SpacetimeView.Tick`, rather than calling `Update`
  by reflection.
- **`AddComponent` in EditMode does not run `Awake`, `OnEnable` or `Start`.** Either design the
  component to initialise lazily (as `SwordAbilities` and `PlayerLevel` do), or invoke the
  method by reflection in the fixture. Remember that a component may then hold null references
  that only `Awake` would have set.
- **Use the fixtures.**
  - `BossTestFixture`: tracked `HideFlags.DontSave` hosts, destroyed in `TearDown`;
    `SetTunable(component, "field", value)` writes a private serialized float through
    `SerializedObject` and fails if the field is renamed; `Tick` calls a private `Update`.
  - `Combat/CombatTestFixture`: builds combat data objects through their `CreateRuntime`
    factories.
  - The DontSave-host helper is duplicated in `SpacetimeToggleTests` and `ProgressionTests`.
    Prefer a shared base class for new fixtures.
- **Clean up in `finally` or `TearDown`.** A `Mesh`, `Material` or enabled `InputAction` created
  in a test body leaks if an assertion fails first. `OnDestroy` doesn't run for objects destroyed
  in EditMode.
- **Physics queries:** `Physics.autoSyncTransforms` is off in this project. Call
  `Physics.SyncTransforms()` after moving objects and before any overlap or raycast in a test.
- **Don't touch the open scene, `EditorPrefs` or tracked assets** in ordinary tests. Tests that
  must read shipped assets (asset validation) use `AssetDatabase.LoadAssetAtPath` read-only and
  get a category (below).

## Categories

| Category | For | Run in the shared Editor? |
|---|---|---|
| *(none)* | Pure logic and component tests | Yes |
| `Assets` (in use: `Validation/`) | Read-only validation of shipped ScriptableObjects, prefabs, controllers and scenes. Reads YAML and `AssetDatabase`, and never opens a scene or writes an asset. | Yes |
| `Ship` (proposed) | Release guards, for example "Spacetime is off in the shipped settings asset". Mark them `[Explicit]` too, because they fail on purpose while a local test toggle is on. | Only when run explicitly |
| `EditorSetup` (proposed) | Smoke and idempotency tests that **run setup menus and write tracked assets** | **No.** Run only in a disposable worktree. |

**Known-bug tests.** A test that pins a bug from `QA-Assessment.md` §2 is committed with
`[Ignore("Known bug <ID>: … See Docs/Testing/QA-Assessment.md.")]`. The suite stays green and the
bug shows as a skipped test with its ID. Whoever fixes the bug deletes the `Ignore` in the same
commit.

## Running tests

### In the shared Editor

One Unity Editor is shared by every agent session. **Before running tests, ask the agent-manager
session** ("Agent task conflict management"). Never save a `.cs` file while the Editor is in Play
mode. Run tests from **Window ▸ General ▸ Test Runner**, or with the Unity MCP `run_tests` tool.

### Headless (batch mode)

The shared Editor holds the main checkout's project lock, so batch runs must target a **separate
worktree**. A fresh worktree has no `Library/`, and a full import takes a long time. Seed it
instead by copying the `Library/` of another worktree whose Editor is closed (no
`Temp/UnityLockfile`). Ask the agent-manager session which one is free. Unity re-imports only
what differs. Run each command from PowerShell:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.5f2\Editor\Unity.exe" -batchmode -nographics -projectPath C:\path\to\worktree -runTests -testPlatform EditMode -assemblyNames Pickleball.Tests.EditMode -testResults C:\path\to\worktree\Logs\editmode-results.xml -logFile C:\path\to\worktree\Logs\editmode.log
```

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.5f2\Editor\Unity.exe" -batchmode -projectPath C:\path\to\worktree -runTests -testPlatform PlayMode -assemblyNames Pickleball.Tests.PlayMode -testResults C:\path\to\worktree\Logs\playmode-results.xml -logFile C:\path\to\worktree\Logs\playmode.log
```

- **Don't pass `-quit` with `-runTests`;** Unity exits on its own when the run finishes.
- **Decide pass or fail from the XML** (`<test-run … result="Passed">`), not the exit code.
- **Write results under `Logs/`.** That folder is gitignored and `TestResults/` is not. Without
  `-testResults`, the XML lands in the project root.
- **Exclude mutating tests:** add `-testCategory "!EditorSetup"`.
- **Beware `ChibiSandboxSetup.AutoRunOnce`.** On a machine or worktree whose EditorPrefs token
  doesn't match, it runs the full **Setup Chibi Sandbox** after the first domain reload, *during*
  the test run. See [Architecture/EditorSetup.md](../Architecture/EditorSetup.md#known-hazards).

## Adding the PlayMode assembly

The assembly now exists (the playtest harness, above). Two lessons from building it: an assembly that
is `Editor`-only in `includePlatforms` is run as EditMode (it showed up as zero tests), so a PlayMode
assembly must leave `includePlatforms` empty and guard Editor-only calls with `#if UNITY_EDITOR`; and
`-testCategory Playtest` (or `-testFilter` with a test name) is what selects the `[Explicit]` scenarios. The
steps below are the original plan for assertion-style PlayMode tests.

1. Create `Assets/_Project/Tests/PlayMode/Pickleball.Tests.PlayMode.asmdef` with these contents:
   ```json
   {
     "name": "Pickleball.Tests.PlayMode",
     "rootNamespace": "Pickleball.Tests.PlayMode",
     "references": ["Pickleball.Runtime", "UnityEngine.TestRunner", "UnityEditor.TestRunner",
                    "UniTask", "Unity.InputSystem", "Unity.InputSystem.TestFramework",
                    "Unity.TextMeshPro"],
     "includePlatforms": [],
     "overrideReferences": true,
     "precompiledReferences": ["nunit.framework.dll"],
     "autoReferenced": false,
     "defineConstraints": ["UNITY_INCLUDE_TESTS"]
   }
   ```
2. For scripted keyboard and gamepad input via `InputTestFixture`, add
   `"testables": ["com.unity.inputsystem"]` to `Packages/manifest.json`. That file is shared, so
   clear it with the agent-manager session first.
3. In `[UnitySetUp]`:
   - Set `Application.runInBackground = true`. The project ships with it off, so an unfocused
     Editor stops ticking.
   - Set `Time.captureDeltaTime` (for example `1f/60`) for deterministic frames.
4. Write async tests as `[UnityTest] public IEnumerator X() => UniTask.ToCoroutine(async () => { … });`.
   This keeps the project's no-coroutine rule.
5. Build the arena in code: `SceneManager.CreateScene`, a floor collider, and prefabs loaded with
   `AssetDatabase` under `#if UNITY_EDITOR`. Or load a real scene with
   `EditorSceneManager.LoadSceneAsyncInPlayMode`, which doesn't need the build list. Prefer
   `TrainingStage.unity` over `ChibiSandbox.unity`, which carries the large academy world.
6. Any unexpected `Debug.LogError` fails the test automatically, so "the scene boots for N frames"
   is a useful smoke test on its own.

The first PlayMode tests worth writing are listed in
[QA-Assessment.md](QA-Assessment.md#playmode-first-wave).

## Adding editor-tool tests

Create `Assets/_Project/Tests/EditorTools/Pickleball.Tests.EditorTools.asmdef`. Make it
Editor-only, referencing `Pickleball.Editor` and `Pickleball.Editor.WorldLook`. Tag every test
that runs a setup menu `[Category("EditorSetup")]`.

The strongest idempotency check is done with git, in a throwaway worktree:
1. Run the menu in batch mode.
2. Commit the result.
3. Run the menu again.
4. Require `git status --porcelain` to be empty.
