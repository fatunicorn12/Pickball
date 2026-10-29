# Dialogue

Breath-of-the-Wild-style NPC talk: walk up to (or lock on to) a character, press E, the world
stops, the camera pushes in over your shoulder, and text types out in a dark box at the bottom
of the screen. Conversations can branch: the player picks an answer from a list of options, the
pick is remembered, and later talks read it.

Code: `Assets/_Project/Scripts/Sandbox/Dialogue/` (contract: `Talkable`, `DialogueDefinition`,
`DialogueNode`, `DialogueOption`, `DialogueLine`, `DialogueEmote`, `ConversationEndReason`;
runtime: `DialogueDirector`, `DialogueRunner`, `DialogueValidator`, `PlayerInteractor`,
`DialogueCamera`, `TalkTargetSelector`, `TalkEligibility`, `DialogueFraming`, `DialogueOrbit`,
`DialogueLookInput`, `DialogueChoiceInput`, `ChoiceNavigator`, `ChoiceSelection`, `TalkFacing`,
`TimeScalePause`, `TalkableSet`, `DialogueConfig` and small value types), the choice store in
`Dialogue/Choices/` (contract: `IChoiceStore`, `IChoiceStoreProvider`, `FactValue`, `ChoiceRecord`,
`FactCondition`, `FactEffect`; `ChoiceStore`, `ChoiceStoreHost`, `ChoiceStoreSave`), the
input seams `Sandbox/InteractInput.cs`, `Sandbox/PlayerInputGate.cs` and `Sandbox/InputBlock.cs`,
and the views `UI/DialogueView.cs` (with `UI/DialogueViewOptionRow.cs`) and
`UI/InteractPromptView.cs`. Setup:
`Editor/DialogueSetup.cs` (**Tools ▸ Pickleball ▸ Setup Dialogue**). Tests:
`Tests/EditMode/Dialogue/`.

## 1. Purpose and player-facing feel

- **Finding someone to talk to.** A small "[E] Talk" pill floats above the NPC's head when you
  are within about 2.4 m, roughly facing them (70° either side of your body's facing), and the
  pill would be on screen. If you are locked on (Tab / RT) to a character that can talk, it gets
  the prompt whatever way you face, but from no further: as in BotW, locking on doesn't let you
  talk across the field, you walk up first (and the talk shot is built for walk-up distances).
  If you are locked on to something that can't talk (Newton), no NPC gets a prompt and E stays
  Dash for the whole fight. The pill shows **RB** instead of **E** once you last pressed a pad
  button. Over an NPC that lock-on can target, the pill sits beside the lock-on hint ring rather
  than on top of it.
- **Only when free.** No prompt while airborne (past a 0.15 s grace for ground-check flicker),
  dashing, tethered, on the skateboard, swinging the sword or in 4D. A press starts a talk only
  while really grounded, so the first instant of a jump can't freeze you in mid-air.
- **Starting.** Press E (pad: RB). Your input is cut and any lock-on drops (Tab locks again
  straight after the talk). The world keeps running for a quarter of a second so you slow to
  idle instead of freezing mid-stride, you turn quickly to face the NPC, and the camera starts to
  push in. Then time stops (`Time.timeScale` 0). The rest of the HUD fades out and the box fades
  and scales in.
- **Talking.** The camera settles just above your head, out past your **left** shoulder (the
  sword is on the right), looking nearly level. Your head and shoulder frame the right of the
  shot; the NPC stands whole on the left, its head on the upper third and its feet just above
  the box. The field of view is chosen from the NPC's height to make that so. The camera swings
  out far enough that no part of your body or cloak covers any part of the NPC, and when the NPC
  is very close it comes forward toward a side two-shot instead. If a wall, slope or prop would
  hide the NPC from over the left shoulder, or crowd the camera into your back, and the right
  does better, the shot goes over the right, mirrored. The NPC turns to face you; if he is still
  turning round when the text is due, the first line waits (up to a second) until he is within
  20° of you. Text types at 45 characters a second with short pauses after commas and full stops. The first press shows the whole line,
  the next goes to the next line; a pulsing ▼ marks a finished line. A hurried double-tap can't
  skip a line unread (0.12 s lockout after each change).
- **Choosing.** In a branching conversation, the moment a line has typed out (or been skipped
  to its end) the answers appear: no extra press, as in BotW. They stand in a column of dark
  plates on the box's top edge at its right end, first answer on top, with the line still in the
  box below them. The highlighted answer has the accent ► beside it, lifts a little (5%) and its
  plate brightens; an answer you can't give yet is greyed out and the cursor skips it. Move with
  the **left** stick, the d-pad, the arrow keys or W/S (hold to repeat), or point with the mouse;
  pick with E / RB, a click, or the number keys 1–9. The right stick still swings the camera; the
  mouse stops turning it while the answers are up, because the cursor is set free to point with
  (it is captured again the moment you pick). For 0.3 s after the answers appear nothing picks,
  and every E inside that time restarts it, so mashing E through the text never picks the first
  answer unread; a click or number key inside it only moves the cursor. A click only ever picks
  an answer, never advances a line. In the rare mirrored shot (over the right shoulder, the NPC
  on the right), the column moves to the box's left end, above the name tab, so it never covers
  the NPC.
- **Remembering.** What you picked is recorded and can set facts; a later talk can open
  differently. Talk to Birdy, promise to help, walk off and talk again: "You came back! Still up
  for helping?" A talk cut short while the answers are up (the NPC killed, a reload) records
  nothing.
- **Looking around.** Once the shot has settled, the mouse (while the cursor is captured) or the
  right stick swings the camera round the NPC, at the same speed and sensitivity as the normal
  camera, while the world stays frozen. The NPC's head stays where the shot put it on screen
  however you turn: all the way round, from level with its head up to 60° above it. The camera
  comes in at once in front of walls and terrain and eases back out after; where there is no
  room at all (the NPC's back to a wall) it stops turning, like a third-person camera against a
  wall, and never goes into the wall or the NPC. It slides round your body rather than through
  it. It doesn't fight you: steer behind yourself and your body hides the NPC.
  Advancing a line keeps your view, and the end swings back from wherever you left it. Moving
  the mouse or stick never advances a line.
- **Ending.** After the last line the box fades, the camera swings back to exactly where it
  was, time resumes, and a frame later your controls return. E then has to be left alone for
  0.35 s before it can open a talk again: every press inside that window restarts it, so mashing
  E to skip text never reopens the conversation from the first line. E doesn't dash meanwhile.
- **The pause is total.** Newton, apples, wells, beams, damage ticks and your own animation all
  freeze. Only the NPC (whose animator runs in unscaled time), the camera and the UI move.

### Buttons

| Action | Keyboard | Gamepad |
|---|---|---|
| Talk (with a prompt) | E | RB |
| Advance / skip text | E | RB |
| Move between answers | ↑ ↓ or W S, mouse hover | left stick, d-pad ↑ ↓ |
| Pick an answer | E, click, 1–9 | RB |

E and RB are already Dash. Every pad button is taken (A jump, B crouch, X attack, Y flip, LB
tether, RB dash, LT sprint, RT lock-on, R3 anchor, d-pad zoom / skateboard / Spacetime), so
Talk shares the dash button: **only while a prompt is showing, and during a conversation and its
cooldown, is the dash suppressed.** Jump and everything else are never touched by a prompt.
Advancing never uses left click (it re-captures the cursor and queues attacks) or Space; while
answers are up, the cursor is free and a click picks one, nothing else. The
E / RB Interact binding lives in one place, `InteractInput` on the player; the interactor and
the director both listen to its `Pressed`. (`PlayerMovementInput` keeps its own Dash binding.)

## 2. Architecture

```
InteractInput (player) ──Pressed──▶ PlayerInteractor (player, order −50) ──TryStart──▶ DialogueDirector
                      └─Pressed──▶ DialogueDirector (advance)                   │ owns DialogueRunner,
PlayerInteractor reads TalkableSet, LockOnTargeting, ChibiLocomotion,            │ TimeScalePause
  MeleeCombat, SpacetimeView, the main camera; claims InteractButton             ▼ Notify* ▶ Talkable (NPC)
DialogueDirector claims InputBlock.All ─▶ PlayerInputGate (player) ◀─ read by PlayerMovementInput,
                                                    SwordInput, LockOnTargeting, SpacetimeView
DialogueDirector events ─▶ DialogueCamera (Main Camera), DialogueView (HUD)
DialogueDirector ─choiceStore (IChoiceStoreProvider)─▶ ChoiceStoreHost ("Dialogue" object) ─▶ ChoiceStore ◀─ save file
DialogueDirector reads option keys / d-pad / left stick itself (DialogueChoiceInput); DialogueView adds mouse hover / click
DialogueDirector ─TalkFacing (settle only)─▶ MovementMotor.FacingSource (player)
DialogueCamera reads mouse delta + right stick itself (DialogueLookInput) ─▶ DialogueOrbit
PlayerInteractor.TargetChanged ─▶ InteractPromptView (HUD)
```

**Components and who listens to what.**

| Component | Where | Raises | Listens to |
|---|---|---|---|
| `Talkable` (contract) | NPC root | `ConversationStarted`, `LineStarted`, `LineRevealed`, `ChoicesShown`, `ChoiceMade`, `ConversationEnded` | nothing; the director calls `Notify*` |
| `InteractInput` | player root | `Pressed` | its own E / RB action |
| `PlayerInteractor` | player root | `TargetChanged(Talkable)` | `InteractInput.Pressed`; polls the `TalkableSet` and eligibility state each frame (no events exist for those) |
| `DialogueDirector` | "Dialogue" object | `ConversationStarting`, `ConversationStarted`, `LineStarted`, `CharactersRevealed`, `LineRevealed`, `ChoicesShown`, `ChoiceHighlighted`, `ChoiceMade`, `ConversationEnding`, `ConversationEnded` | `InteractInput.Pressed`; its runner's events; the option keys, d-pad and left stick while answers are up (`DialogueChoiceInput`) |
| `DialogueRunner` (plain C#) | inside the director | `LineStarted`, `CharactersRevealed`, `LineRevealed`, `ChoicesShown`, `ChoiceHighlighted`, `ChoiceMade`, `Finished` | nothing; `Tick(dt)`, `Advance()`, `MoveHighlight`, `Highlight`, `Choose` |
| `ChoiceStoreHost` | "Dialogue" object | (its store's `ChoiceRecorded`, `FactChanged`, `Reset`) | its store, to save it |
| `DialogueCamera` | Main Camera | — | director: Starting, Ending, Ended; reads mouse delta and the right stick through `DialogueLookInput` while the shot is held |
| `DialogueOrbit`, `DialogueLookInput` (plain C#) | inside the camera | — | nothing; look-around math and device read |
| `TalkFacing` (plain C#) | inside the director | — | installed as `MovementMotor.FacingSource` from the talk press to the pause |
| `DialogueView` | HUD | — | director: Started, LineStarted, CharactersRevealed, LineRevealed, ChoicesShown, ChoiceHighlighted, ChoiceMade, Ending, Ended; the mouse while answers are up (calls `HighlightChoice` / `ConfirmChoice`) |
| `InteractPromptView` | HUD | — | interactor: `TargetChanged` |
| `PlayerInputGate` | player root | — | nothing; claimants call `Claim` / `Release` |

**The fixed order.** Start: claim `All` on the gate → `ConversationStarting` (the camera
disables the orbit camera in this same Update, before any LateUpdate, and starts its blend; the
prompt hides) → settle (0.25 s, time running) → `Time.timeScale = 0` (if something else stopped
time during the settle, the talk aborts instead of recording 0) → `Talkable.NotifyConversationStarted`
→ `ConversationStarted` (box fades in) → text starts after 0.2 s. End: `ConversationEnding` (box
fades, camera swings back, time still 0) → after 0.35 s `Talkable.NotifyConversationEnded(Completed)`
→ time scale restored to the **previous** value → `ConversationEnded` (camera puts the recorded
pose and FOV back exactly) → the camera re-enables the orbit camera on its first `LateUpdate`
whose `Time.deltaTime > 0`, because the orbit camera's `SmoothDampAngle` divides by zero at
dt 0 once converged → next frame the gate claim is released → the restart cooldown, a 0.35 s
debounce (any Interact press inside it restarts it). The
interactor keeps `InteractButton` claimed for the whole conversation and cooldown.

**Execution order.**

| Order | Component | Callback | Why |
|---|---|---|---|
| −50 | `PlayerInteractor` | Update | Decides the Interact claim before `MovementMotor` (0) reads input, so the press that opens a talk never dashes |
| 0 | `DialogueDirector` | Update | Drives the conversation in unscaled time; its first frame is the talk frame, where presses are ignored |
| 0 | `DialogueCamera` | LateUpdate | The orbit camera is already disabled, so there is no ordering race; hands the orbit camera back here |
| 1000 | `InteractPromptView` | LateUpdate | Projects after the orbit camera has moved, so the pill doesn't trail by a frame |

**Aborts and exception safety.** Every frame the director checks its Talkable: destroyed,
inactive, disabled, no longer in the conversation, or its root `Health` dead → it ends with
`ConversationEndReason.Aborted` through the same order (input released a frame later).
`DialogueDirector.OnDisable` (which a script reload in Play mode runs first) does the whole
unwind at once, input included. Lifecycle events (Starting, Started, Ending, Ended) are invoked
one subscriber at a time; a subscriber that throws is logged and skipped, and restoring time,
leaving the conversation and releasing input sit in `finally` blocks besides, so a broken
listener can never leave the game frozen or the player stuck. `DialogueCamera.OnDisable`
restores the pose and the orbit camera; `DialogueView.OnDisable` un-hides the HUD;
`PlayerInputGate.OnDisable` drops every claim; `Talkable.OnDisable` ends its own conversation as
Aborted. A subscriber that changes anything for a conversation must undo it in its own
`OnDisable` too.

**The input gate** is general, not dialogue-specific. An owner claims a set of `InputBlock` flags
(`Movement`, `Sword`, `LockOn`, `Camera`, `Spacetime`, `InteractButton`, `Spell`); each reader
checks only its own flag. Claims from different owners never clear each other: the talk's `All`
outlives the spell's own `Sword | LockOn` aiming claim (`AimInputClaims`), and the other way round. Callback-latched presses go
through `PlayerInputGate.TakeLatched`, which **drops** a press made while blocked instead of
saving it for the release. The readers' gate fields are optional; empty keeps the old behaviour.

| Reader | Flag | While blocked |
|---|---|---|
| `PlayerMovementInput.Read` | `Movement` | returns an empty intent |
| `PlayerMovementInput.Read` | `InteractButton` | `DashPressed` is false (E / RB talk instead) |
| `SwordInput.OnPerformed` | `Sword` | the press is dropped, never queued on `MeleeCombat` |
| `LockOnTargeting` | `LockOn` | a claim releases any lock through `Release()` and the ring hides (the lock-on fix); toggles and flicks are dropped while blocked |
| `SpacetimeView.Update` | `Spacetime` | T / d-pad left dropped |
| `SpellInput` | `Spell` | aim and fire read as released, so a talk started while aiming ends the aim; the orbit camera eases out of its aim view once it is handed back |
| — | `Camera` | no reader yet: the dialogue camera disables `SandboxOrbitCamera` and reads look input itself |

**The camera shot** (`DialogueFraming`, pure and tested). Pivot: the player's humanoid Head
bone (without one, his root raised by `playerFocusHeight`). Position: on the flat line from the
player's body axis (his root) to the NPC's feet, `pushInDistance` back from the axis,
`cameraHeight` above the pivot, and shifted toward the configured shoulder. The shift is the
larger of `sideOffset` and the least offset at which **every** sightline from the camera to the
NPC (to its head and to its feet, at either edge of its half-width) passes outside the player's
body: a vertical capsule round his axis, from his feet to the top of his head, of radius
CharacterController radius × scale (`playerFallbackRadius` without one) + `playerClearanceMargin`.
In plan view all those sightlines collapse to two lines, and clearing the vertical cylinder
there clears the capsule at any height, including when the player stands above or below the
NPC. The tightest one, from the camera at (−p, s) to the NPC's far edge at (d, −w), must satisfy
(s·d − w·p) / √((d + p)² + (s + w)²) ≥ r; `RequiredSideOffset` solves that exactly as the larger
root of a quadratic. The NPC's half-width is the larger of `npcHalfWidth` and its
`CapsuleCollider` radius; both radii are read once when the talk starts. If the required offset
exceeds `maxSideOffset`, `PushInFor` shortens the push-in (bisection) until the capped offset
clears, so a very close NPC gets a side two-shot rather than a hidden face. Rotation: solved
exactly (no roll) so the NPC's focus lands at (0.5 ∓ `npcScreenSide`, `npcHeadScreenHeight`),
on the side away from the player's shoulder. Field of view: chosen from the NPC's height (focus
above its root) so its feet reach `npcFeetScreenHeight`, above the box, clamped to
`minFieldOfView`–`maxFieldOfView`; the floor keeps the player's head and shoulder in frame. When
a wall pulls the camera in, the composition is re-solved for the new spot. The shoulder is
chosen once, at the talk press: the configured one (`sideOffset`'s sign), unless its shot is
spoiled and the mirrored one is better. Spoiled means rays from the shot to the NPC's head, middle
and feet are cut by something on `cameraObstructionMask` (not the player or the NPC), or the
pull-in took more than `shoulderSwapPullIn` of its distance (crowded into the player's back).
Better means fewer cut rays, or as few and a pull-in smaller by that margin. The composition
mirrors with it. The
blend in and out swings around the player/NPC midpoint (`OrbitBlend`): yaw turns the short way,
radius and height are lerped, so the camera never passes through either body.

**Looking around** (`DialogueOrbit`, pure and tested). Input is read only once the blend-in has
finished, so the shot always starts as composed; until the first input the camera stays on it
exactly. The camera orbits the NPC's focus point at the shot's distance: yaw is an offset from
the shot's bearing, free and wrapped to (−180°, 180°], keeping the unwrapped remainder so a fast
spin never turns back the short way; pitch is the camera's elevation above the focus, clamped to
`lookMinPitch`–`lookMaxPitch` (widened to include the shot's own elevation, so the first touch
never snaps). Both follow the input with exponential damping over **unscaled** time; a zero step
changes nothing (no `SmoothDamp`, so no divide by zero at dt 0). Each frame while it moves, a
sphere sweep (`cameraCollisionRadius`, `cameraObstructionMask`, the player and NPC ignored) from
the focus along the orbit direction gives the clear distance; the camera comes in to it at once,
however close (collision wins), and eases out over `lookPullOutSeconds`. Where the clear distance
would bring it nearer the NPC's head than `minCameraDistance` (or the NPC's half-width plus the
camera radius, if larger), the step is refused: the camera keeps whichever of the yaw and pitch
halves still fits, else neither, and drops the rest of the blocked turn so pushing into the wall
builds no backlog and turning back answers at once. It is then
pushed out of the player's capsule (feet to Head bone, the framing clearance radius) so it slides
round him, and `DialogueFraming.Compose` re-aims it and re-picks the field of view, so the NPC's
head stays at the shot's screen point with no roll. Once settled nothing is recomputed. Mouse
look needs the cursor captured (`Cursor.lockState` Locked and the orbit camera's
`CursorCaptured`); the stick never waits for it. Rates match `SandboxOrbitCamera` (0.12°/count,
200 / 120 °/s, deadzone 0.15, curve 1.6), but its fields are private, so the dialogue keeps its
own copies on `DialogueConfig`. `DialogueCamera.Look(Vector2)` takes look input from any source
(touch later, tests). The blend-out starts from the camera's current pose.

**Facing the NPC** (`TalkFacing`). Movement takes facing requests through `IFacingSource` on
`MovementMotor.FacingSource` (lock-on and attack square-up use it). From the talk press to the
pause the director installs an urgent request for the NPC's focus in front of whatever source was
there, so the body turns fast even while it is still sliding to a stop, then hands the old source
back (only if its own is still installed). The motor doesn't tick at dt 0, so nothing turns
during the pause. No movement code changed.

**Typewriter counting** matches TextMeshPro's `maxVisibleCharacters`: only TMP-valid tags
(`<`, optional `/`, a letter or `#`, a closing `>` with no `<` before it) are tags; formatting
tags are free; `<br>`, `<cr>`, `<nbsp>`, `<zwsp>`, `<zwj>`, `<shy>` and `<sprite…>` count as one
character (`<br>` pauses like a line break); a surrogate pair (most emoji) is one character.

### Branching and choices

**The data.** A `DialogueDefinition` has a stable conversation `id` (project-unique, e.g.
`birdy.intro`; the choice log is keyed by it), a `startNodeId`, and a list of `DialogueNode`s
linked by stable string ids, never list positions. A node is one of three kinds
(`DialogueNodeKind`), in one flat class so the list reads top to bottom in the Inspector and
serializes without `[SerializeReference]`; each kind uses only some fields:

| Kind | Fields | What it does |
|---|---|---|
| **Line** | `text`, `emote`, `next` | The speaker says it; a press goes to `next`. |
| **Choice** | `options`; optional prompt in `text` / `emote`; `next` = fallback | The options appear once the line before (or the node's own prompt) has typed out. With no option pickable it goes to the fallback. |
| **Branch** | `conditions`, `next`, `elseNext` | Shows nothing: `next` if every condition holds, else `elseNext`. How a later talk checks a fact. |

An empty link ends the conversation. A `DialogueOption` has an `id` (unique in its node), `text`,
`next`, `conditions` (`FactCondition[]`) with `whenUnmet` = **Hide** (left out of the list) or
**Disable** (greyed, skipped, can't be picked), and `effects` (`FactEffect[]`). Conditions can test
a fact (`FactEquals`, `AtLeast`, `Exists`, …) or the log (`Chose` / `NotChose` an option at a node;
an empty conversation id means this one).

**Migration: compatibility, not conversion.** A definition with no nodes plays its old `lines`
array exactly as before: the runner reads it as an implicit chain of line nodes `line.0 → line.1 →
… → end`, with the same indices, texts, emotes and events, so nothing had to be migrated for the
existing content to keep working. An asset changes form only when someone gives it nodes: by
hand, through the asset's ⋮ ▸ **Convert Lines To Nodes** (the same `line.N` ids, undoable), or
through its seeding menu. Once it has nodes its lines are ignored (the validator warns). Birdy's
asset was upgraded by its seeder: Setup Birdy NPC, or **Tools ▸ Pickleball ▸ Seed Birdy Dialogue**
on its own, rewrites `Birdy_Intro.asset` in place (GUID kept) only while it is still, word for
word, the original six-line seed; any hand edit and it is left alone.

**The validator** (`DialogueValidator`, plain C#) runs in the definition's `OnValidate` (one
warning log per change of result, never an error log) and in `DialogueAssetTests`, which
validates every definition in the project and checks conversation ids are unique. Errors (the
test fails): no conversation id on a graph; an empty or duplicate node id; an empty or unknown
start; a `next`, `elseNext`, option or fallback link to a node that doesn't exist; a choice with no
options or more than 9; an empty or duplicate option id; a choice whose every option has
conditions and that has no fallback (it could strand the player); branches looping into each
other with nothing shown. Warnings: unreachable nodes; a loop with no way out (cycles are fine
if some path out of them ends the talk); a branch with no conditions; empty text; an option over
48 characters; a condition or effect with no key; a `Chose` condition naming a node or option this
conversation doesn't have; linear lines left beside nodes. The analysis is static and
optimistic: an exit behind a condition counts.

**The runner** walks the graph. Branches are resolved the moment they are reached (against the
store), a choice with nothing pickable is replaced by its fallback, and a broken link or a run of
64 branches with nothing shown ends the talk with a warning instead of hanging. When a line is
revealed it already knows what comes next: if that is a choice with no prompt, the options go
up at once under the line; otherwise the next node waits for a press. A **pick**, in order:
leaves the choosing state, `IChoiceStore.RecordChoice(conversationId, nodeId, optionId)`, the
option's effects in order (`FactEffect.ApplyAll`), `ChoiceMade`, then the option's `next`.
Store exceptions are logged and the talk goes on. Nothing else ever writes to the store, so an
abort mid-choice (the NPC disabled or killed, the director disabled by a reload) unwinds exactly
as before and records nothing. `LineIndex` and `LineStarted`'s index are the order lines were
shown in this talk (for a linear definition, the line's index); `Talkable.CurrentLine` and
`CurrentNodeId` say which.

**The lockouts.** Lines keep `advanceLockoutSeconds` (0.12 s after a line starts or is revealed).
Options add `choiceLockoutSeconds` (0.3 s after they appear), a debounce for Interact: a press
inside it restarts it. A click or number key inside it only moves the cursor there. Everything
is unscaled time; the world is paused.

**Input.** The input gate keeps its single `InputBlock.All` claim; no new flag. While answers are
up the director reads the option input itself (as the camera reads look input):
`DialogueChoiceInput` combines the left stick's y, the d-pad and the arrow / W S keys into one
axis, and `ChoiceNavigator` turns it into steps (one per push, then after 0.4 s a repeat every
0.15 s; at most one per frame; a direction already held when the answers appear does nothing
until let go and pushed again). `ChoiceSelection` keeps the cursor on enabled options and wraps.
Number keys 1–9 (top row or numpad) pick the Nth answer shown, so a hidden answer shifts the
numbers. Interact picks the highlighted one. The right stick is untouched and still orbits the
camera; `DialogueCamera` ignores the mouse while `IsChoosing`.

**The view.** Setup Dialogue builds the column inside the box (`DialogueBox/Choices`, a
VerticalLayoutGroup and ContentSizeFitter, so every plate is as wide as the longest answer, at
least 300 units) with nine rows `Option1…9` (plate, `Label` 32 pt, a `Cursor` that is the box's ▼
sprite turned a quarter). It starts hidden. On `ChoicesShown` the view fills and shows one row
per answer, lays them out at once (so the mouse can hit them that frame), fades the column in
over `optionFadeInSeconds`, and eases each row toward plain or highlighted (scale, plate and text
colour) with a time constant of `optionHighlightSeconds`; the ► bobs with the ▼'s pulse. The
mouse moves the highlight only when it moves, so a resting pointer never fights the keys. The
cursor is freed when the answers appear, only if it was captured, and captured again on
`ChoiceMade`, on the end of the talk and in `OnDisable`, the way the dialogue hands the cursor
back to the orbit camera. The column is mirrored to the box's left end when the talk camera
(`worldCamera`) has the NPC's head on the right half of the screen. Re-enabled mid-choice (a
reload), the view puts the list back.

**Store wiring.** The director has one serialized `MonoBehaviour choiceStore` field, checked for
`IChoiceStoreProvider` (`OnValidate` takes the provider on a dragged-in object, or clears anything
else). Setup Dialogue adds a `ChoiceStoreHost` to the "Dialogue" object (only if missing) and wires
it. The store is fetched when a talk starts. With none wired, talks still run: conditions see an
empty store, effects are dropped, and the director warns once.

### Choices and facts: the store

Everything the player has chosen, and the facts those choices set: the one place quests,
reputation or later branches read from, so none of them needs to know about dialogue.

**The API** (`IChoiceStore`, implemented by `ChoiceStore`, plain C#):
- the log, append-only: `RecordChoice(dialogueId, nodeId, choiceId)`, `Choices` (every
  `ChoiceRecord`, oldest first, each stamped with cumulative play time), `HasChosen(dialogue, node,
  choice = null)` (null: anything there), `LastChoice(dialogue, node)`;
- the facts, a key/value table of `FactValue` (bool, int or string; reads convert, so an int reads
  as a bool by being non-zero): `TryGetFact`, `SetFact`, `ClearFact`, `Facts`, and the extensions
  `GetBool` / `GetInt` / `GetString` with a fallback for facts never set;
- events, raised after the state is final: `ChoiceRecorded(record)`, `FactChanged(FactChange)` (key,
  previous and current value, whether each existed; only when the value really changes), and
  `Reset` (after a new game or a load: anything caching store data re-reads it).
Bad input (an empty key or id) is ignored with a warning, never thrown. A listener that throws is
logged and the other listeners still run.

**The host** (`ChoiceStoreHost`, on the "Dialogue" object, implements `IChoiceStoreProvider`)
builds the store on first use (so `Store` is never null), loads the save into it, and writes it
back: at most once per frame in `LateUpdate` after any change (which still runs while a talk has
time stopped), and on application pause, quit and disable. Fields: `persist` (off: memory only,
nothing read or written; for tests and quick runs) and `fileName`.

**The save.** `<persistentDataPath>/Saves/choices.json` (`ChoiceStoreSave`), JSON with a format
version, the play time, the log and the facts (sorted by key, so it diffs cleanly). Writes are
atomic: a `.tmp` file, flushed, then swapped in with `File.Replace`, so a crash mid-save leaves
the previous file whole. A path inside the project's Assets folder, or a file name with folders,
is refused. Loading always leaves the store consistent and raises `Reset` once (no per-fact
events):
- **missing**: an empty store, a new game;
- **corrupt** (unparsable, or no version): renamed to `choices.corrupt-<timestamp>.json` beside it,
  empty store;
- **older version**: migrated in memory, written back at the current version on the next save;
- **newer version** (or unreadable through an I/O error): refused and left untouched, empty
  store, and saving is off for the session so the file survives.

**New game.** `ChoiceStoreHost.NewGame()` (also the component's ⋮ ▸ **New Game (clear choices)**)
empties the store, restarts play time and deletes the save file; it is also the explicit way past
a refused save.

**Reading it from another system.** Hold a serialized reference to the host (as the director
does), read facts when you need them, and listen for changes:

```csharp
[SerializeField] ChoiceStoreHost choices;   // wired by a setup menu

void OnEnable()
{
    choices.Store.FactChanged += OnFact;
    choices.Store.Reset += Refresh;          // after a load or a new game
    Refresh();
}

void OnDisable()
{
    choices.Store.FactChanged -= OnFact;
    choices.Store.Reset -= Refresh;
}

void OnFact(FactChange change)
{
    if (change.Key == "birdy.promised_help") Refresh();
}

void Refresh() => questMarker.SetActive(choices.Store.GetBool("birdy.promised_help"));
```

Tests never touch the real save: use an in-memory `IChoiceStore` (the dialogue tests'
`FakeChoiceStore`), a host with `persist` off, or `ChoiceStoreHost.UseSaveDirectory(tempDir)`.

**Data assets.** `ScriptableObjects/Dialogue/DialogueConfig.asset` (every tunable),
`TalkableSet.asset` (runtime set; Talkables join in `OnEnable`), and one `DialogueDefinition` per
conversation (speaker name, conversation id, and nodes, or linear lines with an emote each),
owned by the NPC's content. The save file lives outside the project (above). The folder's
GUID is fixed (`182ae63664be4615bb6e2329a9d64209`) so the NPC branch and this one agree.

**No per-frame scene searches or allocations.** The interactor scans the `TalkableSet` (a
handful of entries) into reused lists; the typewriter is two indices and
`TMP_Text.maxVisibleCharacters`; the camera's sweeps run only while blending in and while a
look-around is still moving, into a reused hit buffer; lifecycle events allocate an invocation
list a few times per conversation.

## 3. How to tune

Everything is on `DialogueConfig` (Inspector tooltips give units). It is read live, so tune in
Play mode. Setup Dialogue creates `DialogueConfig.asset` only when it is missing and the asset is
not committed, so a checkout that ran the menu before a change of defaults keeps the old values.
To take the new ones, use the asset's ⋮ ▸ Reset in the Inspector, which keeps its GUID and every
reference to it.

| Feel | Fields |
|---|---|
| Prompt appears too far / too close, or too fussy about facing | `talkRange` (also the lock-on range), `facingConeDegrees`, `maxHeightDifference` |
| Prompt flickers between two NPCs or at the edge | `stickyMargin`, `stickyConeDegrees` |
| Allow talking mid-air or mid-move | `requireFree`, `groundedGraceSeconds` (the grace only keeps the prompt up) |
| Player freezes mid-stride / takes too long to stop | `settleSeconds` |
| Player doesn't (or shouldn't) turn to face the NPC | `turnToFaceNpc` (the turn happens during `settleSeconds`; speed is `MovementConfig.lockAttackTurnTime`) |
| Text speed and rhythm | `charactersPerSecond` (0 = instant), `commaPauseSeconds`, `sentencePauseSeconds` |
| NPC starts talking with his back half turned, or the first line waits too long | `waitForNpcFacingDegrees`, `waitForNpcFacingSeconds` |
| Presses feel ignored, or lines get skipped | `advanceLockoutSeconds`, `textStartDelaySeconds` |
| Mashing picks an answer, or a careful pick feels slow | `choiceLockoutSeconds` (a debounce for E / RB) |
| The answer cursor: stick sensitivity, hold-to-repeat | `choiceStickThreshold`, `choiceRepeatDelaySeconds`, `choiceRepeatIntervalSeconds` |
| The answer column's look | `optionPlateColour`, `optionHighlightPlateColour`, `optionTextColour`, `optionHighlightTextColour`, `optionDisabledTextColour`, `optionCursorColour`, `optionCursorBob`, `optionHighlightScale`, `optionHighlightSeconds`, `optionFadeInSeconds` |
| E reopens the talk too easily, or too slowly after a deliberate press | `restartCooldownSeconds` (a debounce: presses inside it restart it) |
| Camera position | `pushInDistance`, `cameraHeight` (above the Head bone; near 0 is level), `sideOffset` (sign = shoulder), `maxSideOffset`, `playerFocusHeight` (pivot only without a Head bone) |
| Player's body or cloak still covers the NPC, or the camera swings too wide | `playerClearanceMargin` (added to the controller radius), `playerFallbackRadius`, `npcHalfWidth`, `maxSideOffset` |
| Where the NPC sits on screen, how big | `npcHeadScreenHeight`, `npcFeetScreenHeight`, `npcScreenSide`, `minFieldOfView`, `maxFieldOfView` |
| Camera speed | `blendInSeconds`, `closeSeconds` (also how long time stays stopped after the last line) |
| Camera clipping, or the shot not swapping shoulder round a prop or slope | `cameraCollisionRadius`, `minCameraDistance`, `cameraObstructionMask` (props need colliders on these layers), `shoulderSwapPullIn` |
| Looking around: on or off, feel | `lookEnabled`, `lookMouseSensitivity`, `lookStickYawSpeed`, `lookStickPitchSpeed`, `lookStickDeadzone`, `lookStickExponent`, `lookInvertStickY` |
| Looking around: how far up and down, how floaty | `lookMinPitch`, `lookMaxPitch` (camera elevation above the NPC's head), `lookSmoothTime`, `lookPullOutSeconds` |
| Box look | `boxColour`, `nameTabColour`, `nameColour`, `textColour`, `continueColour`, `boxFadeInSeconds`, `boxFadeOutSeconds`, `boxScaleFrom`, `continuePulseHz`, `continueBob` |
| Prompt look | `promptHeightOffset`, `promptHintClearance` (sideways, clear of the lock-on hint), `promptFadeSeconds`, `promptLabel`, `keyboardKeyLabel`, `gamepadKeyLabel` |
| Silence during talk | `pauseAudio` (dialogue sounds need `AudioSource.ignoreListenerPause`) |

Box geometry (60% of a 1920-wide reference, 232 tall, 52 above the bottom; font sizes) is laid
out by Setup Dialogue. Change it in `DialogueSetup.BuildBox` and re-run the menu. The answer
column's geometry (64-unit rows, 10 apart, at least 300 wide, 32 pt text, 28 in from the box's
right end and 18 above its top) is in `DialogueSetup.BuildOptionList`. If the box
height changes, move `npcFeetScreenHeight` with it.

## 4. How to extend

**Make an NPC talkable** (two steps):
1. Create a `DialogueDefinition` asset (speaker name, lines, an emote per line).
2. From the NPC's setup menu call `DialogueSetup.MakeTalkable(npcRoot, definition, headBone)`
   (and `DialogueSetup.EnsureTalkableSet()` if it needs the set itself; both are stable API).
   It adds or updates the `Talkable` and registers it in the shared `TalkableSet`. Put any
   `Health` on the same root, and the root at the NPC's feet (the camera reads its height from
   focus minus root). Drive the NPC's presentation from the Talkable's events, in unscaled time
   (`Animator.updateMode = UnscaledTime` during the talk, restored in `OnDisable`).

**Add a new claimant on the input gate** (e.g. a menu, or aiming): `gate.Claim(this,
InputBlock.Sword | InputBlock.LockOn)` and `gate.Release(this)` in its own end path and
`OnDisable`.

**Gate a new input reader:** add an optional `[SerializeField] PlayerInputGate inputGate`, check
`inputGate.IsBlocked(InputBlock.YourFlag)` (or route a callback latch through `TakeLatched`), add
the flag to `InputBlock` if none fits, and fill the field from `DialogueSetup.WireGate`.

**Something else on Interact** (a door, a pickup): listen to `InteractInput.Pressed`, and claim
`InputBlock.InteractButton` while it is the thing E would act on, as `PlayerInteractor` does.

**Write a branching conversation** (no code):
1. On the `DialogueDefinition` asset set a project-unique **Id** (`npc.topic`) and never change it
   once shipped; picks are recorded under it.
2. Add **Nodes** with ids (`intro.1`, `ask`, …) and link them by id. End a path with an empty next.
   Put the answers on a Choice node right after the line that asks; give it a prompt only if the
   question isn't the line before.
3. To open a later talk differently, make the start node a **Branch** that tests a fact the
   first talk set (an option effect), or `Chose` an option at a node.
4. Hide an answer until it makes sense (`whenUnmet` Hide), or show it greyed as a hint
   (Disable). If every answer at a node has conditions, give the node a fallback.
5. Read the Console: the validator lists any broken link or dead end as you type, and
   `DialogueAssetTests` fails on errors.
For an asset seeded by a menu, change the seeder (as `BirdyNpcSetup.BirdyIntro` does), not the
YAML.

**React to choices from another system** (a quest, a door): read the store through the host, as
the snippet in "Choices and facts: the store" shows. Don't listen to the dialogue for it.

**NPC presentation during a choice:** `Talkable.ChoicesShown` / `ChoiceMade` and
`IsAwaitingChoice` (e.g. a thinking idle while the player decides).

## 5. Known limitations and open questions

- **Choices were play-tested headlessly only.** A PlayMode run in ChibiSandbox with Birdy (real
  queued keyboard, gamepad and mouse events) rendered the column and drove every input path:
  arrows, left stick, d-pad, number keys, mouse hover and click, E; the second talk opened with
  "You came back!"; an abort mid-choice recorded nothing. Nobody has judged it by eye in the
  Editor, and the cursor release and re-capture can't be seen headless (the cursor never reads
  Locked in batch mode).
- **The Inspector shows every node field for every kind.** There is no custom drawer; the
  tooltips say which fields each kind uses. A drawer (or a graph view) would be an Editor-side
  addition.
- **No on-screen number hints.** The number keys work but nothing shows them.
- **At most 9 answers per choice** (the rows and the number keys); the validator enforces it.
- **No node-level effects.** Facts change only when an answer is picked. "Has met Birdy" is
  read from the log instead (`Chose` any option at the first question).
- **A prompt-less choice at the very start** shows its answers over an empty box.
- **Npc.md still says Birdy_Intro is seeded once and never overwritten**; it is now also upgraded
  once from the original linear seed (that doc belongs to the NPC system).

- **Play-tested headlessly only.** QA's PlayMode integration run (real Input System events,
  Birdy merged) passed every functional check. The redesigned shot was then re-run headlessly
  with the real Birdy: natural walk-ups from in front at about 1.1, 1.5 and 2.1 m, from his left
  and right, and with the orbit camera starting in front and to the side. Birdy's head and body
  were 0% hidden in every case, his lowest vertex sat above the box, and the blend kept 2 m from
  both bodies. Looking around was run the same way in ChibiSandbox (to the side, behind Birdy,
  top and bottom of the pitch range, the real right stick, into a wall and into the ground):
  no frame clipped, Birdy's head never left the screen, time stayed at 0 and nothing in the world
  moved. Nobody has judged either by eye in the Editor yet, and the mouse path could not be
  driven headlessly (the cursor never reads Locked in batch mode).
- **Props without colliders are invisible to the camera.** Every camera cast is physics, and
  `ChibiSandboxSetup` strips the colliders from the tether-anchor rocks. `TetherAnchor_3` stands
  next to Birdy's spot, so from some walk-ups the shot sits right behind its pale rock, which
  then fills a corner of the frame or hides Birdy's legs. Looking around can pass through it too.
  With a MeshCollider added to that rock (tried in the headless harness), the push-in's sweep
  stops the camera in front of it and Birdy was fully clear in all five walk-ups. The fix
  belongs to the scene: give the rocks a collider on an obstruction layer, or move Birdy a couple
  of metres away.
- **The shot is usually at the side-offset cap.** The chibi's controller plus the cloak margin
  is a wide body, so at natural talk distances the required offset reaches `maxSideOffset` (2 m)
  and the push-in shortens instead. The result is a wide over-the-shoulder two-shot rather than
  a tight one. Lowering `playerClearanceMargin` tightens it, at the cost of the cloak's edge.
- **Body sizes are assumptions.** The NPC's height is focus minus root, so its root must be at
  its feet. Anything sticking out past `npcHalfWidth` or the NPC's capsule (a big wing or staff)
  can still be crossed by the player; raise `npcHalfWidth` for such an NPC.
- **No touch input.** The Sandbox has no touch controls at all, so there is no way to talk on a
  phone yet. The box layout is phone-safe (width-relative, bottom-centre, clear of the notch).
- **Settle is measured in unscaled time.** It equals scaled time because nothing else changes
  the time scale. If something ever does (slow-mo), decide which one the settle should follow.
- **Only the body turns to face the NPC, and only during the settle.** There is no head
  look-at: `ChibiLocomotion` writes `ChibiFootIK.LookTarget` every frame from the lock-on, with no
  hook for anyone else to ask, and the player's animator is paused for the talk anyway. (Turned
  90° and 150° away, he faced Birdy within the 0.25 s settle in the headless run.)
- **Looking around never hands its yaw to the orbit camera.** The end blends back to the orbit
  camera's pre-talk pose, because `SandboxOrbitCamera` has no way to be given a yaw and pitch. With
  one (say `SetOrbit(float yaw, float pitch)`, zeroing its velocities, plus a way to read the
  pose it would take for them), the blend could end on the view the player chose.
- **With the cursor free, the mouse can't look during a talk.** After Escape (Editor) the cursor
  stays free: mouse look is off, and a click neither re-captures it nor advances a line (the
  orbit camera, which owns capture, is disabled). The stick still looks. Re-capturing from the
  dialogue would need `SandboxOrbitCamera` to expose its capture (and mark `JustRelocked`).
- **Escape during a talk.** The orbit camera is off, so it never sees the Escape and still
  believes it owns the cursor afterwards, while the Editor has freed it. When the dialogue hands
  the orbit camera back it captures the cursor again to match, so mouse look and clicks behave
  as in normal play (the camera has no way to be told it lost the cursor). The OS cursor can't be
  checked headless; this wants one look in the Editor.
- **Looking around ignores the NPC's body below its head.** The sweep skips the NPC; the
  minimum distance keeps the camera off its head and shoulders, and the 0° default pitch floor
  keeps it from dropping to the legs. A much lower `lookMinPitch` can bring it close to them.
- **Talks aborted during the settle never reach the Talkable.** The Talkable is told only when
  time stops, so if the NPC dies (or is disabled) inside the 0.25 s settle, its listeners hear
  nothing and `Talkable.LastEndReason` keeps the previous talk's value. The camera snaps back.
- **Locked on to a friendly NPC from further than `talkRange`, E dashes** (toward them), as for
  any lock target. Walk closer and the prompt appears.
- **The frozen pose can point the drawn sword at the NPC**, and a talk started while aiming
  blends back to the aim view before the orbit camera eases out of it. Both belong to their
  owners (sword presentation; `SandboxOrbitCamera`, which needs a pose query).
- **Tall NPCs are cut off by the box.** The field of view stops at `maxFieldOfView`; a
  human-height NPC's legs go under the box, as in BotW.
- **A press in the airborne grace does nothing**: the prompt (and its claim on E) stays up for
  0.15 s after leaving the ground, but the press neither talks nor dashes.
- **Escape** doesn't free the cursor during a talk in a build (the orbit camera owns it and is
  disabled); in the Editor it does, see above.
- **Lock-on fallback.** Locked on to a talkable NPC that is out of `talkRange`, the prompt
  falls back to walk-up selection (another NPC nearby can get it).
- **MeleeCombat start buffer.** A swing pressed just *before* a talk and still buffered
  (`MeleeCombat.cs` ~248, `Time.time <= _startBufferedUntil`) can start during the pause,
  because `Time.time` is frozen and the buffer never expires. Rare (the prompt needs
  `!IsAttacking`); not fixed, it is combat's file.
- **TrainingStage** Backspace still respawns mid-talk (polled, not gated; low risk).
- `PlayerMovementInput`'s gate can't be unit-tested without simulated devices (`InputTestFixture`
  isn't in the manifest); QA's PlayMode run covered it.
