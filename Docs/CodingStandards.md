# Coding Standards — Pickball / Guardians of Mathematics

Every agent and every change follows these rules. They describe how the newer Sandbox code is
already meant to work; where older code (`ChibiLocomotion.cs` in particular) breaks them, new
work moves it toward them rather than adding to the problem.

## 1. Separation of concerns

- **One job per component.** A component owns exactly one of: reading input, simulating
  gameplay state, or presenting it (Animator, VFX, audio, UI). If you need "and" to describe a
  class, split it.
- **Gameplay never drives presentation directly.** Movement and combat code raise C# events
  (inside one system) or `ScriptableObject` event channels (between systems,
  `Scripts/Events/EventChannels.cs`). Animator drivers, VFX, audio and HUD components subscribe.
  Gameplay code does not call `ParticleSystem.Play`, spawn UI or play sounds.
- **Dependencies point one way.** Combat may read movement state through a small read-only
  interface. Movement never knows that combat exists. UI and VFX know about gameplay; gameplay
  never knows about UI and VFX.
- **No hidden coupling.** No singletons, no `FindObjectOfType` / `GameObject.Find` at runtime,
  no static mutable state. References are serialized and wired by setup scripts.
- **Explicit state machines.** New movement or combat modes are small state classes, one per
  file, behind a common interface. Do not add another bool flag and `if` chain to a monolith.
  `ChibiLocomotion.cs` is already 1,300+ lines: extract from it, don't grow it.

## 2. Data-driven tuning

- Every tunable lives on a `ScriptableObject` config or definition asset (`MovementConfig`,
  `SwordAbilityDefinition`, …), never as a magic number in code.
- Every serialized field has a `[Tooltip]` that states its **unit** (m, m/s, m/s², s, °) and
  what changing it feels like in play.
- Group fields with `[Header]`. Clamp with `[Range]` / `[Min]` where a bad value would break play.

## 3. Code style (match the surrounding code)

- Namespaces mirror folders: `Pickleball.Sandbox.Movement`, `Pickleball.Sandbox.Combat`, …
- One public type per file; the file name matches the type name.
- `PascalCase` for types, methods, properties, events and constants; `_camelCase` for private
  fields; `[SerializeField] private` instead of public fields.
- Expose state through read-only properties and events; keep setters private.
- Frame-rate independent: scale by `Time.deltaTime` / `fixedDeltaTime`, use exponential
  smoothing (`1 - Mathf.Exp(-rate * dt)`) instead of raw lerp factors.
- No per-frame allocations: no LINQ, `new` collections, closures or string building in
  `Update` / `FixedUpdate` / `LateUpdate`. Cache components in `Awake`.
- Async sequences use UniTask, not coroutines (see `CLAUDE.md`).

## 4. Unity-specific safety

- **Hot-reload safe.** Several agents save `.cs` files while the shared Editor is open. Never
  keep runtime-built objects in `readonly` fields; rebuild or re-fetch them in `OnEnable`.
- **Setup scripts are the source of truth for wiring.** Scene objects, animator states and
  component references are created by idempotent `Tools ▸ Pickleball ▸ …` editor menus, never
  by hand-editing scene, prefab or `.controller` YAML. Running a menu twice must give the same
  result as running it once.
- **Shared Editor rules.** Ask the agent-manager session (*Agent task conflict management*)
  before Play mode, test runs, recompiles or setup menus in the shared Editor, and never save a
  `.cs` file while it is in Play mode.

## 5. Documentation

- **XML docs on every public type and member.** Say *why* it exists and what it guarantees, not
  a restatement of the name. Keep the existing habit of explaining the design decision and the
  bug it avoids in the class summary.
- **One system doc per system** in `Docs/Systems/<System>.md`, written with the code and updated
  whenever behavior changes. Sections:
  1. Purpose and player-facing feel
  2. Architecture: components, who raises and who listens to which events, data assets
  3. How to tune (which asset, which fields)
  4. How to extend (e.g. "add a new move / ability in N steps")
  5. Known limitations and open questions
- **Commit messages** explain why the change was made and what was or wasn't play-tested.

## 6. Tests

- Pure logic (combo resolution, XP curves, unlock rules, movement math such as jump-height to
  launch-velocity) gets EditMode tests in `Assets/_Project/Tests/EditMode/`.
- Keep that logic in plain C# classes so it can be tested without a scene.

## 7. Parallel work

- Each code-writing agent works in its own git worktree with a disjoint set of files, agreed
  with the agent-manager session before starting.
- Shared files (`CLAUDE.md`, `ROADMAP.md`, `Packages/manifest.json`, `ProjectSettings/`, scenes,
  prefabs, `.controller` and other Unity YAML assets) have exactly one owner at a time.
