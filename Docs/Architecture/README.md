# Architecture notes

This folder records design decisions that are visible in the code but weren't written down
anywhere: *why* things are built the way they are, the contracts between systems, and the known
places where the code departs from `Docs/CodingStandards.md`. Read the relevant page before
changing code in that area.

How this folder relates to the other docs:
- `CLAUDE.md`: entry point and pickleball setup.
- `Docs/CodingStandards.md`: the rules.
- `Docs/Systems/<System>.md`: one doc per gameplay system, owned by that system's agent.
- `Docs/Architecture/`: decisions that cross systems, plus decision notes for systems that have
  no system doc yet.
- `Docs/Testing/`: how to test, and what still needs tests.

| Page | Read it when you are… |
|---|---|
| [Communication.md](Communication.md) | adding an event, a listener or a reference between systems; touching the pickleball event channels |
| [CombatMovementBoundary.md](CombatMovementBoundary.md) | changing anything where combat, movement, lock-on or input bindings meet |
| [RuntimeConventions.md](RuntimeConventions.md) | writing any runtime component: timing, execution order, hot-reload safety, allocations, test seams |
| [EditorSetup.md](EditorSetup.md) | writing or running a `Tools ▸ Pickleball` menu, or changing assemblies or project settings |
| [PickleballCore.md](PickleballCore.md) | working on the original pickleball game (ball, court, AI, scoring) |
| [NewtonBoss.md](NewtonBoss.md) | working on the Newton fight, `Health`, damage or the fight HUD (until `Docs/Systems/NewtonBoss.md` exists) |

## Keeping it true

- Each page says which commit it describes. When you change a decision, update the page in the
  same commit, or delete the line if it no longer applies.
- Bugs listed here are **known and unfixed** as of that commit. When you fix one, remove it here
  and from `Docs/Testing/QA-Assessment.md`.
- System-specific behaviour belongs in that system's doc under `Docs/Systems/`. Only cross-cutting
  decisions live here.
