# Editor setup scripts

Snapshot as of `a930952`. `CodingStandards.md` §4 says setup menus are the source of truth for
scene and asset wiring, and that they are idempotent. This page records how the 19 setup scripts
in `Scripts/Editor/` actually behave, the conventions they share, and where they break the rule.

## Assemblies

| Assembly | Why it exists |
|---|---|
| `Pickleball.Runtime` | All gameplay code. It was split out of Assembly-CSharp because test assemblies can't reference Assembly-CSharp (commit `d489f33`). |
| `Pickleball.Editor` | Setup menus. References Runtime, UniTask, Input System, UGUI and TMP, but **not URP**. |
| `Pickleball.Editor.WorldLook` | World Look and Clean World. It is a separate assembly only because it needs the URP runtime assemblies (`d9f8f05`). `CleanWorldSetup` lives here although it doesn't use URP. |
| `Pickleball.Tests.EditMode` | References Runtime only, so **no setup script can be tested today**. |

18 of the 19 editor classes are in the global namespace, not `Pickleball.EditorTools`. Batch
`-executeMethod` calls use those global names, so namespacing them changes the command lines.

## Menu verbs

All menus are under **Tools ▸ Pickleball**. The verb tells you what a menu touches:

| Verb | Meaning | Examples |
|---|---|---|
| **Setup** | Wires components into the **open** scene. Usually marks it dirty and never saves. | Setup Weapon, Setup Sword Abilities, Setup Lock-On, Setup Newton Fight, Setup Inventory, Spacetime ▸ Add To Sandbox |
| **Rebuild** | Rewrites an asset in place and keeps its GUID. | Rebuild Chibi Animator, Rebuild Newton Animator, Setup Full-Body Swings (it only touches `ChibiLocomotion.controller`'s AttackBody layer and the flagged clips' import, never a scene or prefab) |
| **Build / Create** | Makes a new scene and **saves** it. | Setup Chibi Sandbox (despite its name), Build Training Stage, Spacetime ▸ Create Spacetime Lab Scene, Create Assets |
| **Bake / Clean / Organize** | One-off asset processing. | Bake Swing Twist, Clean World, Organize Project (moves and **deletes** assets) |

## Run order

No orchestrator menu exists. The order is enforced only by error messages inside individual
scripts:

1. **Setup Chibi Sandbox** creates a new ChibiSandbox scene and saves it. It does not call any of
   the later menus, so **re-running it wipes everything they added**.
2. **Setup Newton (Boss)**. It destroys and rebuilds the boss root.
3. **Setup Newton Fight**. It must come after the boss setup.
4. **Setup Lock-On**
5. **Setup World Look**
6. **Clean World**
7. **Spacetime ▸ Add To Sandbox** (only if Spacetime is enabled).
8. **Setup Dialogue**. After Lock-On (it gates the lock toggle and uses the lock for talk
   targeting), Newton Fight (the box goes inside FightHUD) and Spacetime (it gates the T toggle).
   NPC setups that call `DialogueSetup.MakeTalkable` can run before or after it. Then save the
   scene.
   - **Setup Birdy NPC** right after it, with ChibiSandbox open (places Birdy under `AcademyNpcs`, which Build Training Stage strips; works before Setup Dialogue too), then save.
   - **Setup Birdy UAL Trial** only to re-make Birdy's death clip (`Animation/Birdy/Death.anim`, copied from the Quaternius UAL FBX) or the all-UAL trial controller. Both are committed, so normally skip it. If it does run, run **Setup Birdy NPC** after it.
9. **Setup Inventory**. Setup Chibi Sandbox already runs it as its last step, so run it by hand
   only after a menu that changes what it points at: **Setup Weapon** (rebuilds `WeaponSocket`,
   `BladeBase`, `BladeTip` and `WeaponVisual`), **Setup Magic** (new spell assets get `Magic_*`
   entries; the Magic tab itself finds the selector at runtime), and whenever `Held_Compass`,
   `Held_Ruler`, `Moveset_Ruler` or `RulerWeaponClearance` land (it fills blank weapon references by
   path). It must come after Setup Chibi Sandbox, which wipes it like every later menu, and before
   Build Training Stage. Save the scene afterwards. Details: `Docs/Systems/Inventory.md`.
10. **Build Training Stage**. It copies ChibiSandbox, then runs `InventorySetup.AttachToPlayer` on the
    copied player.
11. **Spacetime ▸ Create Spacetime Lab Scene**
12. **Build Academy Greybox**. It copies ChibiSandbox too, and keeps Birdy (see
    `Docs/World/TheAcademy.md` §8).
13. **Setup Hypatia Fight**. Needs nothing else; it generates its own scene,
    `HypatiaFight.unity`, as a copy of ChibiSandbox, so re-run it after ChibiSandbox changes
    (it rebuilds only when its inputs changed; see `Docs/Systems/HypatiaBoss.md`).

Setup Chibi Sandbox already runs the player-level setups as part of its build:
- `MovementSetup.WirePlayer` and `EnsureConfigs`
- `WeaponSetup.PrepareAssets` and `AttachToPlayer`, which in turn call `SwingTwistSetup.Bake`,
  `WeaponClearanceSetup.Attach` and `SwordAbilitySetup.AttachToPlayer`. The last one's
  `WireAttackStatesFromAssets` also runs `FullBodySwingSetup.Wire`, so every Attack-layer rebuild
  (Setup Weapon, Setup Sword Abilities, Rebuild Chibi Animator) keeps the AttackBody layer.
- `ClothSetup.Apply`
- `InventorySetup.AttachToPlayer`, last, after the prefab is saved, so its components are scene
  overrides and `Chibi.prefab` doesn't change.

Each of those menus can also be run on its own against the open scene.

## Asset ownership

Two policies coexist. Check which one an asset follows before you edit it by hand.

- **Seeded once, then the asset is the source of truth.** Setup creates it only if missing and
  fills only blank references. Hand-tuning survives a re-run. Examples: combat step, ability and
  leveling assets, Post FX, VFX made by `SandboxVfxBuilder`.
  - *Caveat:* `SandboxVfxBuilder`'s get-or-create never re-applies material settings, so
    `DashGhost.mat` has drifted from the blending the code intends.
- **Regenerated every run.** Hand edits are lost. Examples: scenes, `Chibi.prefab`, pickups,
  sword VFX prefabs (GUIDs kept), Clean World outputs, and the Newton fight components.
  - **Newton fight tuning lives in code defaults.** Setup Newton Fight re-adds the five fight
    components on each run, discarding Inspector edits.

Large generated binaries (meshes, textures, `.glb`) go to Git LFS on purpose (`.gitattributes`).

## Scene-dirty policies

Three are in use:
- **Prompt to save:** Setup Chibi Sandbox and Build Training Stage. Chibi Sandbox ignores the
  answer, so Cancel still discards the open scene. Training Stage honours it.
- **Refuse to run if any scene is dirty:** Create Spacetime Lab Scene.
- **Mark dirty and never save:** most Setup menus. Setup Newton (Boss) forgets to mark the scene
  dirty; Setup Weapon Clearance was fixed in `e90e5e3`. Setup Cloth marks every open scene dirty.

## Known hazards

- **`ChibiSandboxSetup.AutoRunOnce` is an `[InitializeOnLoadMethod]`.** On any domain reload where
  the EditorPrefs token `Pickleball.ChibiSandbox.AutoRunToken` differs from the code's
  `AutoRunToken`, it runs the full Setup Chibi Sandbox on the next `delayCall`: organize (delete
  and move assets), new scene, save.
  - EditorPrefs are per-machine, so every worktree shares the token.
  - Bumping `AutoRunToken` therefore rebuilds the sandbox in whichever Editor next recompiles.
  - On a fresh machine or CI runner it fires in the middle of a `-runTests` run.
  - It predates the Unity MCP tooling, which removed its original reason ("the Editor holds the
    project lock").
- **Build Training Stage deletes and re-copies the scene,** so `TrainingStage.unity` gets a new
  GUID on every run. Build Academy Greybox does the same to `AcademyGreybox.unity`.
- **Organize's duplicate-model sweep** deletes any model outside `_Project` whose file name
  matches one inside it, anywhere in the tree. Its comment says root level only.
- **Setup World Look writes a GPU-captured panorama** to a tracked LFS PNG on every run. That is
  non-deterministic, and it can't run with `-nographics`.
- **`Chibi.prefab` lags the scene.** `SwordAbilities`, `WeaponClearance`, `LockOnTargeting` and
  `Health` exist only as ChibiSandbox overrides, and so do the inventory's four components
  (`Inventory`, `WeaponEquipper`, `InventoryInput`, `InventoryMenu`). Scenes that instantiate the prefab, such as
  SpacetimeLab, don't get them.
- **The `GetComponent<T>() ?? AddComponent<T>()` pattern** (still in `ChibiSandboxSetup` and
  `NewtonBossSetup`) is unsafe on Unity's "fake null" objects, and `WeaponClearanceSetup` warns
  against it. Use `TryGetComponent` or an explicit `if (c == null)`, as `WeaponSetup` and
  `SwingTwistSetup` now do.
- **Lookups are by GameObject name** (`GameObject.Find("Chibi")`, "NewtonBoss", "AcademyWorld",
  "FightHUD"; Setup Inventory finds "BladeBase", "BladeTip" and "WeaponVisual" under the `HandGrip`,
  and its own "InventoryHUD" and "StowedWeapons"), mixed with lookups by type. Renaming a scene object breaks the menus.
- **Several menus have no Play-mode guard:** Chibi Sandbox, World Look, Clean World, Lock-On,
  Weapon, Cloth and Newton.

## Batch mode

Only two batch entry points exist: `ChibiSandboxSetup.BatchRebuildAnimators` and
`SwordAbilitySetup.Batch` (`cde2de1`). Neither calls `EditorApplication.Exit(code)`. Failed
preconditions log an error and return, so a batch run exits 0 even when it failed. Run headless
work against a **separate worktree**, because the shared Editor holds the main checkout's project
lock. See `Docs/Testing/README.md` for command lines.

## Project settings these scripts and the runtime rely on

| Setting | Value | Why it matters |
|---|---|---|
| `Physics.bounceThreshold` | 2 | Why the pickleball bounce is manual |
| `Physics.autoSyncTransforms` | off | Setup scripts call `Physics.SyncTransforms()` before edit-time raycasts; tests must too |
| `Time.fixedDeltaTime` | 0.02 | Only the pickleball ball uses FixedUpdate |
| Active input handling | Input System only | All bindings are built in code; the registered `.inputactions` asset is unused |
| `runInBackground` | off | Play mode driven over MCP doesn't tick unless it is set at runtime (see memory notes) |
| Build scenes | GameScene + template SampleScene | The Windows build shipped only ChibiSandbox; that scene list isn't recorded in any tracked file |

## Unity MCP tooling

- `com.unity.pipeline` `0.8.0-exp.1` (experimental) and `.mcp.json` were added in `ccc18a4`.
- `.mcp.json` hard-codes absolute paths under `C:/Users/willr`, so MCP calls from any worktree
  drive the main checkout's Editor.
- UniTask comes from an unpinned git URL; only `packages-lock.json` pins its hash.
