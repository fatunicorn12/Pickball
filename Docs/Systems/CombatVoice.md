# Combat Voice

## Purpose and feel

The player grunts as each swing's blade goes live, the grunts climb in vigor through the chain,
and a finisher always gets a full yell. Being hit plays a hurt sound. It should read like a classic
action-adventure hero: short and physical, never words.

## Architecture

| Piece | Job |
|---|---|
| `CombatVoice` | Presentation component on the player, as a scene override. Listens to `MeleeCombat.BladeLiveChanged` and `Health.Hurt`, and plays clips. Gameplay does not know it exists. |
| `GruntLadder` | Pure logic: which tier a swing gets. |
| `ShuffleBag` | Pure logic: deals clips so each plays before any repeats, never twice in a row. |
| `CombatVoiceBank` | Asset: the five clip groups and playback settings. |
| `CombatVoiceSetup` | **Tools ▸ Pickleball ▸ Setup Combat Voice**: import settings, bank, wiring on the open scene's player. Setup Chibi Sandbox and Build Training Stage also call it. |

**The pattern.** Ordinary hits ramp evenly from Light to Heavy across however many there are, read
from `MeleeCombat.PreviewChain`. A step with the `Finisher` role always plays the Finisher group.

| Ordinary hits | Example | Pattern, then the finisher |
|---|---|---|
| 2 | Ruler | Light, Heavy |
| 3 | Compass | Light, Medium, Heavy |
| 4 | Compass + Plus C | Light, Medium, Medium, Heavy |
| 5 | A longer chain to come | Light, Medium, Medium, Heavy, Heavy |

The air chain follows the same rule: Air Slash + Phase Shift is Light, Heavy, then Unit Circle's
finisher yell.

**One voice.** A new sound fades out the one playing, using two `AudioSource`s swapped on each
sound. Hurt and attack sounds share it, and the newest always wins.

**Hurt.** Only discrete hits (`Health.LastHitDiscrete`) make a sound, so a beam's per-frame damage
stays silent, and `hurtMinIntervalSeconds` limits how often it can play.

## How to tune

On `ScriptableObjects/Combat/CombatVoiceBank.asset`:
- Drag a clip out of a group to retire it, or into a group to add it.
- Each group's `volume`, plus `pitchVariation`, `handoffFadeSeconds`, `hurtMinIntervalSeconds`.

The installed clip levels are set in `Tools/Voice/grunt_ladders.json` under `install.rms_db`.

## How to extend

**More clips:** add ladders to `grunt_ladders.json`, run `generate_grunts.py probe`, then
`generate_grunts.py install`, then Setup Combat Voice. A group that already has clips keeps its
list, so drag new clips in by hand or empty the group to refill it.

**A new kind of sound (jump, dash):** add a group to the bank and a folder to `CombatVoiceSetup`,
then have `CombatVoice` listen to the event that should trigger it.

## Known limitations

- **A chain abandoned early has no finisher sound,** because no finisher step played.
- **The base air chain has no finisher** until Unit Circle is learned, so its single slash plays
  Medium rather than a yell.
- **Fast hits cut long grunts off.** Compass hits start 0.33-0.43 s apart, and many Medium and Heavy
  clips run 0.5-0.7 s, so their tails fade out. Retire the longest clips if that sounds abrupt.
- **No mixer routing.** The project has no combat mixer group yet; the sources play to the master.
- **The trim can pick the wrong sound.** `generate_grunts.py` keeps the last loud sound in each
  generated line, and a clip's cut-off tail can't be recovered without recutting.
