# Dialogue

Breath-of-the-Wild-style NPC talk: walk up to (or lock on to) a character, press E, the world
stops, the camera pushes in over your shoulder, and text types out in a dark box at the bottom
of the screen.

Code: `Assets/_Project/Scripts/Sandbox/Dialogue/` (contract: `Talkable`, `DialogueDefinition`,
`DialogueLine`, `DialogueEmote`, `ConversationEndReason`; runtime: `DialogueDirector`,
`DialogueRunner`, `PlayerInteractor`, `DialogueCamera`, `TalkTargetSelector`, `TalkEligibility`,
`DialogueFraming`, `DialogueOrbit`, `DialogueLookInput`, `TalkFacing`, `TimeScalePause`,
`TalkableSet`, `DialogueConfig` and small value types), the
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
  does better, the shot goes over the right, mirrored. Text types at 45 characters a
  second with short pauses after commas and full stops. The first press shows the whole line,
  the next goes to the next line; a pulsing ▼ marks a finished line. A hurried double-tap can't
  skip a line unread (0.12 s lockout after each change).
- **Looking around.** Once the shot has settled, the mouse (while the cursor is captured) or the
  right stick swings the camera round the NPC, at the same speed and sensitivity as the normal
  camera, while the world stays frozen. The NPC's head stays where the shot put it on screen
  however you turn: all the way round, up to 60° above it and 10° below. The camera comes in at
  once in front of walls and terrain, eases back out after, and slides round your body rather
  than through it. It doesn't fight you: steer behind yourself and your body hides the NPC.
  Advancing a line keeps your view, and the end swings back from wherever you left it. Moving
  the mouse or stick never advances a line.
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
DialogueDirector ─TalkFacing (settle only)─▶ MovementMotor.FacingSource (player)
DialogueCamera reads mouse delta + right stick itself (DialogueLookInput) ─▶ DialogueOrbit
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
| `DialogueCamera` | Main Camera | — | director: Starting, Ending, Ended; reads mouse delta and the right stick through `DialogueLookInput` while the shot is held |
| `DialogueOrbit`, `DialogueLookInput` (plain C#) | inside the camera | — | nothing; look-around math and device read |
| `TalkFacing` (plain C#) | inside the director | — | installed as `MovementMotor.FacingSource` from the talk press to the pause |
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
the focus along the orbit direction gives the clear distance; the camera comes in to it at once
and eases out over `lookPullOutSeconds`, never closer than `minCameraDistance`. It is then
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

**Data assets.** `ScriptableObjects/Dialogue/DialogueConfig.asset` (every tunable),
`TalkableSet.asset` (runtime set; Talkables join in `OnEnable`), and one `DialogueDefinition` per
conversation (speaker name + lines with an emote each), owned by the NPC's content. The folder's
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
| Prompt appears too far / too close, or too fussy about facing | `talkRange`, `facingConeDegrees`, `lockedTalkRange`, `maxHeightDifference` |
| Prompt flickers between two NPCs or at the edge | `stickyMargin`, `stickyConeDegrees` |
| Allow talking mid-air or mid-move | `requireFree`, `groundedGraceSeconds` (the grace only keeps the prompt up) |
| Player freezes mid-stride / takes too long to stop | `settleSeconds` |
| Player doesn't (or shouldn't) turn to face the NPC | `turnToFaceNpc` (the turn happens during `settleSeconds`; speed is `MovementConfig.lockAttackTurnTime`) |
| Text speed and rhythm | `charactersPerSecond` (0 = instant), `commaPauseSeconds`, `sentencePauseSeconds` |
| Presses feel ignored, or lines get skipped | `advanceLockoutSeconds`, `textStartDelaySeconds` |
| E reopens the talk too easily | `restartCooldownSeconds` |
| Camera position | `pushInDistance`, `cameraHeight` (above the Head bone; near 0 is level), `sideOffset` (sign = shoulder), `maxSideOffset`, `playerFocusHeight` (pivot only without a Head bone) |
| Player's body or cloak still covers the NPC, or the camera swings too wide | `playerClearanceMargin` (added to the controller radius), `playerFallbackRadius`, `npcHalfWidth`, `maxSideOffset` |
| Where the NPC sits on screen, how big | `npcHeadScreenHeight`, `npcFeetScreenHeight`, `npcScreenSide`, `minFieldOfView`, `maxFieldOfView` |
| Camera speed | `blendInSeconds`, `closeSeconds` (also how long time stays stopped after the last line) |
| Camera clipping, or the shot not swapping shoulder round a prop or slope | `cameraCollisionRadius`, `minCameraDistance`, `cameraObstructionMask` (props need colliders on these layers), `shoulderSwapPullIn` |
| Looking around: on or off, feel | `lookEnabled`, `lookMouseSensitivity`, `lookStickYawSpeed`, `lookStickPitchSpeed`, `lookStickDeadzone`, `lookStickExponent`, `lookInvertStickY` |
| Looking around: how far up and down, how floaty | `lookMinPitch`, `lookMaxPitch` (camera elevation above the NPC's head), `lookSmoothTime`, `lookPullOutSeconds` |
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
- **Looking around ignores the NPC's body.** The sweep skips the NPC, so with a very low pitch
  limit the ground can pull the camera in close to the NPC's legs; the default −10° keeps clear.
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
