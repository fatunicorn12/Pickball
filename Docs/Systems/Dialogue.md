# Dialogue

Breath-of-the-Wild-style NPC talk: walk up to (or lock on to) a character, press E, the world
stops, the camera pushes in over your shoulder, and text types out in a dark box at the bottom
of the screen.

Code: `Assets/_Project/Scripts/Sandbox/Dialogue/` (contract: `Talkable`, `DialogueDefinition`,
`DialogueLine`, `DialogueEmote`, `ConversationEndReason`; runtime: `DialogueDirector`,
`DialogueRunner`, `PlayerInteractor`, `DialogueCamera`, `TalkTargetSelector`, `TalkEligibility`,
`DialogueFraming`, `TimeScalePause`, `TalkableSet`, `DialogueConfig` and small value types), the
input seams `Sandbox/InteractInput.cs`, `Sandbox/PlayerInputGate.cs` and `Sandbox/InputBlock.cs`,
and the views `UI/DialogueView.cs` and `UI/InteractPromptView.cs`. Setup:
`Editor/DialogueSetup.cs` (**Tools ▸ Pickleball ▸ Setup Dialogue**). Tests:
`Tests/EditMode/Dialogue/`.

## 1. Purpose and player-facing feel

- **Finding someone to talk to.** A small "[E] Talk" pill floats above the NPC's head when you
  are within about 2.4 m, roughly facing them (70° either side of your body's facing), and the
  pill would be on screen. If you are locked on (Tab / RT) to a character that can talk, it gets
  the prompt from up to 4.5 m whatever way you face. If you are locked on to something that
  can't talk (Newton), no NPC gets a prompt and E stays Dash for the whole fight. The pill shows
  **RB** instead of **E** once you last pressed a pad button.
- **Only when free.** No prompt while airborne (past a 0.15 s grace for ground-check flicker),
  dashing, tethered, on the skateboard, swinging the sword or in 4D. A press starts a talk only
  while really grounded, so the first instant of a jump can't freeze you in mid-air.
- **Starting.** Press E (pad: RB). Your input is cut, the world keeps running for a quarter of a
  second so you slow to idle instead of freezing mid-stride, the camera starts to push in, and
  then time stops (`Time.timeScale` 0). The rest of the HUD fades out and the box fades and
  scales in.
- **Talking.** The camera settles just above your head, out past your **left** shoulder (the
  sword is on the right), looking nearly level. Your head and shoulder frame the right of the
  shot; the NPC stands whole on the left, its head on the upper third and its feet just above
  the box. The field of view is chosen from the NPC's height to make that so. The camera swings
  out far enough that no part of your body or cloak covers any part of the NPC, and when the NPC
  is very close it comes forward toward a side two-shot instead. Text types at 45 characters a
  second with short pauses after commas and full stops. The first press shows the whole line,
  the next goes to the next line; a pulsing ▼ marks a finished line. A hurried double-tap can't
  skip a line unread (0.12 s lockout after each change).
- **Ending.** After the last line the box fades, the camera swings back to exactly where it
  was, time resumes, and a frame later your controls return. For 0.35 s afterwards E can neither
  reopen the talk nor dash.
- **The pause is total.** Newton, apples, wells, beams, damage ticks and your own animation all
  freeze. Only the NPC (whose animator runs in unscaled time), the camera and the UI move.

### Buttons

| Action | Keyboard | Gamepad |
|---|---|---|
| Talk (with a prompt) | E | RB |
| Advance / skip text | E | RB |

E and RB are already Dash. Every pad button is taken (A jump, B crouch, X attack, Y flip, LB
tether, RB dash, LT sprint, RT lock-on, R3 anchor, d-pad zoom / skateboard / Spacetime), so
Talk shares the dash button: **only while a prompt is showing, and during a conversation and its
cooldown, is the dash suppressed.** Jump and everything else are never touched by a prompt.
Advancing never uses left click (it re-captures the cursor and queues attacks) or Space. The
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
PlayerInteractor.TargetChanged ─▶ InteractPromptView (HUD)
```

**Components and who listens to what.**

| Component | Where | Raises | Listens to |
|---|---|---|---|
| `Talkable` (contract) | NPC root | `ConversationStarted`, `LineStarted`, `LineRevealed`, `ConversationEnded` | nothing; the director calls `Notify*` |
| `InteractInput` | player root | `Pressed` | its own E / RB action |
| `PlayerInteractor` | player root | `TargetChanged(Talkable)` | `InteractInput.Pressed`; polls the `TalkableSet` and eligibility state each frame (no events exist for those) |
| `DialogueDirector` | "Dialogue" object | `ConversationStarting`, `ConversationStarted`, `LineStarted`, `CharactersRevealed`, `LineRevealed`, `ConversationEnding`, `ConversationEnded` | `InteractInput.Pressed`; its runner's events |
| `DialogueRunner` (plain C#) | inside the director | `LineStarted`, `CharactersRevealed`, `LineRevealed`, `Finished` | nothing; `Tick(dt)` and `Advance()` |
| `DialogueCamera` | Main Camera | — | director: Starting, Ending, Ended |
| `DialogueView` | HUD | — | director: Started, LineStarted, CharactersRevealed, LineRevealed, Ending, Ended |
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
dt 0 once converged → next frame the gate claim is released → 0.35 s restart cooldown. The
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
(`Movement`, `Sword`, `LockOn`, `Camera`, `Spacetime`, `InteractButton`); each reader checks only
its own flag. Claims from different owners never clear each other. Callback-latched presses go
through `PlayerInputGate.TakeLatched`, which **drops** a press made while blocked instead of
saving it for the release. The readers' gate fields are optional; empty keeps the old behaviour.

| Reader | Flag | While blocked |
|---|---|---|
| `PlayerMovementInput.Read` | `Movement` | returns an empty intent |
| `PlayerMovementInput.Read` | `InteractButton` | `DashPressed` is false (E / RB talk instead) |
| `SwordInput.OnPerformed` | `Sword` | the press is dropped, never queued on `MeleeCombat` |
| `LockOnTargeting.Update` | `LockOn` | toggle dropped; flicks skipped and accumulators zeroed; a held lock is kept |
| `SpacetimeView.Update` | `Spacetime` | T / d-pad left dropped |
| — | `Camera` | no reader yet: the dialogue camera disables `SandboxOrbitCamera` instead |

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
a wall pulls the camera in, the composition is re-solved for the new spot. The blend in and out
swings around the player/NPC midpoint (`OrbitBlend`): yaw turns the short way, radius and height
are lerped, so the camera never passes through either body.

**Typewriter counting** matches TextMeshPro's `maxVisibleCharacters`: only TMP-valid tags
(`<`, optional `/`, a letter or `#`, a closing `>` with no `<` before it) are tags; formatting
tags are free; `<br>`, `<cr>`, `<nbsp>`, `<zwsp>`, `<zwj>`, `<shy>` and `<sprite…>` count as one
character (`<br>` pauses like a line break); a surrogate pair (most emoji) is one character.

**Data assets.** `ScriptableObjects/Dialogue/DialogueConfig.asset` (every tunable),
`TalkableSet.asset` (runtime set; Talkables join in `OnEnable`), and one `DialogueDefinition` per
conversation (speaker name + lines with an emote each), owned by the NPC's content. The folder's
GUID is fixed (`182ae63664be4615bb6e2329a9d64209`) so the NPC branch and this one agree.

**No per-frame scene searches or allocations.** The interactor scans the `TalkableSet` (a
handful of entries) into reused lists; the typewriter is two indices and
`TMP_Text.maxVisibleCharacters`; the camera's sphere sweep runs only while blending in;
lifecycle events allocate an invocation list a few times per conversation.

## 3. How to tune

Everything is on `DialogueConfig` (Inspector tooltips give units). It is read live, so tune in
Play mode. Setup Dialogue creates `DialogueConfig.asset` only when it is missing and the asset is
not committed, so a checkout that ran the menu before a change of defaults keeps the old values.
To take the new ones, use the asset's ⋮ ▸ Reset in the Inspector, which keeps its GUID and every
reference to it.

| Feel | Fields |
|---|---|
| Prompt appears too far / too close, or too fussy about facing | `talkRange`, `facingConeDegrees`, `lockedTalkRange`, `maxHeightDifference` |
| Prompt flickers between two NPCs or at the edge | `stickyMargin`, `stickyConeDegrees` |
| Allow talking mid-air or mid-move | `requireFree`, `groundedGraceSeconds` (the grace only keeps the prompt up) |
| Player freezes mid-stride / takes too long to stop | `settleSeconds` |
| Text speed and rhythm | `charactersPerSecond` (0 = instant), `commaPauseSeconds`, `sentencePauseSeconds` |
| Presses feel ignored, or lines get skipped | `advanceLockoutSeconds`, `textStartDelaySeconds` |
| E reopens the talk too easily | `restartCooldownSeconds` |
| Camera position | `pushInDistance`, `cameraHeight` (above the Head bone; near 0 is level), `sideOffset` (sign = shoulder), `maxSideOffset`, `playerFocusHeight` (pivot only without a Head bone) |
| Player's body or cloak still covers the NPC, or the camera swings too wide | `playerClearanceMargin` (added to the controller radius), `playerFallbackRadius`, `npcHalfWidth`, `maxSideOffset` |
| Where the NPC sits on screen, how big | `npcHeadScreenHeight`, `npcFeetScreenHeight`, `npcScreenSide`, `minFieldOfView`, `maxFieldOfView` |
| Camera speed | `blendInSeconds`, `closeSeconds` (also how long time stays stopped after the last line) |
| Camera clipping | `cameraCollisionRadius`, `minCameraDistance`, `cameraObstructionMask` |
| Box look | `boxColour`, `nameTabColour`, `nameColour`, `textColour`, `continueColour`, `boxFadeInSeconds`, `boxFadeOutSeconds`, `boxScaleFrom`, `continuePulseHz`, `continueBob` |
| Prompt look | `promptHeightOffset`, `promptFadeSeconds`, `promptLabel`, `keyboardKeyLabel`, `gamepadKeyLabel` |
| Silence during talk | `pauseAudio` (dialogue sounds need `AudioSource.ignoreListenerPause`) |

Box geometry (60% of a 1920-wide reference, 232 tall, 52 above the bottom; font sizes) is laid
out by Setup Dialogue. Change it in `DialogueSetup.BuildBox` and re-run the menu. If the box
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

**Branching or choices** would go in `DialogueRunner` (a choice line type and an `Advance(choice)`
overload) plus a choice list in `DialogueView`; the director and camera need no change.

## 5. Known limitations and open questions

- **Play-tested headlessly only.** QA's PlayMode integration run (real Input System events,
  Birdy merged) passed every functional check. The redesigned shot was then re-run headlessly
  with the real Birdy: natural walk-ups from in front at about 1.1, 1.5 and 2.1 m, from his left
  and right, and with the orbit camera starting in front and to the side. Birdy's head and body
  were 0% hidden in every case, his lowest vertex sat above the box, and the blend kept 2 m from
  both bodies. Nobody has judged the shot by eye in the Editor yet.
- **The shot is usually at the side-offset cap.** The chibi's controller plus the cloak margin
  is a wide body, so at natural talk distances the required offset reaches `maxSideOffset` (2 m)
  and the push-in shortens instead. The result is a wide over-the-shoulder two-shot rather than
  a tight one. Lowering `playerClearanceMargin` tightens it, at the cost of the cloak's edge.
- **Body sizes are assumptions.** The NPC's height is focus minus root, so its root must be at
  its feet. Anything sticking out past `npcHalfWidth` or the NPC's capsule (a big wing or staff)
  can still be crossed by the player; raise `npcHalfWidth` for such an NPC.
- **No touch input.** The Sandbox has no touch controls at all, so there is no way to talk on a
  phone yet. The box layout is phone-safe (width-relative, bottom-centre, clear of the notch).
- **The lock-on reticle stays visible during a talk** (locked ring or hint arrow over the NPC).
  Hiding it needs a change to `LockOnReticle`, pending its owner's approval. The hint arrow can
  also overlap the "[E] Talk" pill over an NPC that has a `Health`.
- **Settle is measured in unscaled time.** It equals scaled time because nothing else changes
  the time scale. If something ever does (slow-mo), decide which one the settle should follow.
- **The player is not turned to face the NPC.** The shot is built on the player → NPC line, so it
  frames correctly either way, but a lock-on talk with the NPC behind shows the player's front.
- **Tall NPCs are cut off by the box.** The field of view stops at `maxFieldOfView`; a
  human-height NPC's legs go under the box, as in BotW.
- **A press in the airborne grace does nothing**: the prompt (and its claim on E) stays up for
  0.15 s after leaving the ground, but the press neither talks nor dashes.
- **Escape** doesn't free the cursor during a talk (the orbit camera owns it and is disabled).
- **Lock-on fallback.** Locked on to a talkable NPC that is out of `lockedTalkRange`, the prompt
  falls back to walk-up selection (another NPC nearby can get it).
- **MeleeCombat start buffer.** A swing pressed just *before* a talk and still buffered
  (`MeleeCombat.cs` ~248, `Time.time <= _startBufferedUntil`) can start during the pause,
  because `Time.time` is frozen and the buffer never expires. Rare (the prompt needs
  `!IsAttacking`); not fixed, it is combat's file.
- **TrainingStage** Backspace still respawns mid-talk (polled, not gated; low risk).
- `PlayerMovementInput`'s gate can't be unit-tested without simulated devices (`InputTestFixture`
  isn't in the manifest); QA's PlayMode run covered it.
