# NPCs (Birdy)

Talking characters that stand in the world, turn to face the player during a conversation,
gesture each line, and can be locked on to and cut down like anything else with a `Health`.
The first one is **Birdy**, a little helicopter bird in the academy courtyard who tips the player
off about Newton.

- **Code:** `Assets/_Project/Scripts/Sandbox/Npc/` (namespace `Pickleball.Sandbox.Npc`).
- **Contract it consumes:** `Scripts/Sandbox/Dialogue/` (`Talkable`, `DialogueDefinition`,
  `DialogueLine`, `DialogueEmote`, `ConversationEndReason`, `TalkableSet`). The dialogue director,
  camera, prompt and UI are a separate system (`Docs/Systems/Dialogue.md`); NPC code never talks
  to them.
- **Data:** `ScriptableObjects/Npc/NpcAnimationConfig.asset`,
  `ScriptableObjects/Dialogue/Birdy_Intro.asset`, and the shared
  `ScriptableObjects/Dialogue/TalkableSet.asset` (owned by DialogueSetup).
- **Art:** `Art/Characters/Birdy/` (`Birdy.fbx`, `Birdy.mat`, `Birdy_BaseColor.jpg`,
  `Birdy_Normal.jpg`); clips in `Animation/Birdy/` (the death is a Quaternius UAL `.anim`, from
  `Animation/Quaternius/`); controller `Animation/Controllers/Birdy.controller`; prefab
  `Prefabs/Npc/Birdy.prefab`.
- **Setup:** `Scripts/Editor/BirdyNpcSetup.cs`: **Tools ▸ Pickleball ▸ Setup Birdy NPC**,
  **Rebuild Birdy Animator**, **Diagnostics ▸ Report Birdy Poses**; and
  `Scripts/Editor/BirdyModelPostprocessor.cs` (import-time skin fix and the rotor's bone,
  Birdy.fbx only); `Scripts/Editor/BirdyUalTrialSetup.cs`: **Setup Birdy UAL Trial** (makes
  the death clip, and an all-UAL trial controller).
- **Tests:** `Tests/EditMode/Npc/`, plus `Tests/EditMode/Combat/XpExemptionTests.cs`. Three
  suites drive shipped assets (category `Assets`):
  - `BirdyControllerTests`: the shipped controller and prefab.
  - `BirdyRotorTests`: the rotor's spin and fall through the runtime components.
  - `BirdyUalControllerTests`: the trial controller.

## 1. Purpose and player-facing feel

Birdy stands near the player's spawn in ChibiSandbox, idling, on ground level enough that the
player walks right up to him to talk (where exactly, and why: section 5, placement).
Walk up (or lock on) and talk: the game pauses, Birdy turns to face you (on unscaled time,
easing in, capped so a 180° turn is a pivot rather than a snap), and gestures each line with
the line's emote: ordinary **Talk**, big **Excited** delivery, or slow **Thinking**. When a line
has finished typing, the gesture settles back to a listening idle; if you skip ahead, the next
gesture cuts into that settle instead of waiting for it. A line skipped with E still gestures
for a moment (`minGestureSeconds`), so skipping never reads as Birdy freezing. After the
conversation Birdy keeps watching you while you stay within 4 m, and turns back to the way it
was facing once you walk off.

Birdy's rotor turns lazily while he idles and listens, and whirs on an Excited line.

Birdy has 40 HP. The sword can kill it.
- **The death.** It plays once and stays down: a stagger, then a fall onto his back, ending
  feet-up like a cartoon dead bird.
- **The rotor.** Just before his head hits the ground, it is knocked off. It somersaults once
  through the air, lands upright on the ground beyond his head with a little hop, and winds
  down to a stop.
- **Grounding.** The model is eased up a little over the fall so the body rests on the ground.
  The tail, which sticks out behind, still dips into it.
- **Afterwards.** The collider switches off so the body is not an invisible pillar, and the
  Talkable stops offering a conversation (`Talkable.CanTalk` checks the `Health` on the same
  object).
- **No XP.** Killing Birdy pays no XP (`Health.grantsXp` is off on its prefab).

Birdy is 1.14 m tall in its idle, rotor included. The chibi measures 1.56 m in its bind pose,
so Birdy's rotor comes up to about the chibi's chin.

## 2. Architecture

```
DialogueDirector ──Notify…──▶ Talkable ──ConversationStarted / LineStarted / LineRevealed / ConversationEnded──▶ NpcAnimatorDriver ──▶ Animator (Birdy.controller)
                                                                                                                │      ├─▶ root yaw (faces Partner, then watches / returns to rest)
                                                                                                                │      └─▶ model height (death lift)
NpcAnimatorDriver (IsTalking, CurrentEmote, IsSpeakingAt, IsDead, DeathProgress) ──read in LateUpdate──▶ NpcRotor ──▶ Rotor bone (spin; knocked off on death)
MeleeCombat ──IDamageable.TakeHit──▶ Health ──Died──▶ NpcAnimatorDriver (Death) · NpcBody (collider off) · Talkable.CanTalk = false
CombatXpSource ──skips Health.GrantsXp == false (Birdy)
LockOnTargeting ──FindObjectsByType<Health>──▶ Health (Birdy is lockable)
PlayerInteractor ──TalkableSet──▶ Talkable (walk-up talk)
```

Prefab layout (`Prefabs/Npc/Birdy.prefab`):

| Object | Components | Why |
|---|---|---|
| `Birdy` (root) | `CapsuleCollider`, `Health` (40 HP, no grey pool, `grantsXp` off), `Talkable` (focus = Head bone, registry = `TalkableSet`), `NpcAnimatorDriver`, `NpcBody`, `NpcRotor` (rotor = the `Rotor` bone, rotorRenderer = `tripo_part_1`) | Talkable and Health must share the root: `CanTalk` only checks a Health on its own object, and lock-on locks the Health's object. The capsule is solid because the blade sweep and lock-on's line of sight ignore triggers. |
| `BirdyModel` | `Animator` (avatar, `Birdy.controller`, root motion off, `CullUpdateTransforms`) | The model under the root, as for Newton, so it can be nudged (and lifted on death) without moving the collider. |

Runtime classes:

| Type | Kind | Job |
|---|---|---|
| `NpcAnimatorDriver` | MonoBehaviour, presentation | Subscribes to the Talkable and Health in `OnEnable`, unsubscribes in `OnDisable`. Switches the Animator to `UnscaledTime` on the first ConversationStarted and restores the saved mode on ConversationEnded **and** in `OnDisable`. Pushes the parameters every frame, rolls idle variants, turns the root, applies the death lift. Reads the clocks only in `Update`; `Tick(unscaledNow, unscaledDt, scaledDt)` is the test seam, and the event handlers use the clock it was last given. `OnEnable` re-syncs a running conversation, or one that ended while disabled. |
| `NpcBody` | MonoBehaviour, gameplay | Disables the blocking collider on `Health.Died`. |
| `NpcRotor` | MonoBehaviour, presentation | Owns the `Rotor` bone, which no clip animates. It runs in `LateUpdate`, after the Animator has posed the head. **Alive:** it sets only the bone's local rotation (rest pose times the spin about its up), on unscaled time while talking. **Dead:** at `rotorPopAt` of the death it knocks the rotor off and sets its world pose from `NpcRotorFlight` every frame. The landing spot follows the head until touchdown, then stays put: `rotorLandDistance` beyond where the mast sat on the head, along the fall (hips to head, level), `rotorLandSide` to the side. The ground under it is found by a short downward ray that ignores Birdy, moving bodies and character controllers, falling back to the height of the feet. Once flown, its renderer updates off screen, since the mesh bounds follow the body. `OnDisable` puts the rotor back on the head; a re-enabled dead Birdy re-knocks it, straight to rest if the death has finished. `Tick(scaledDt, unscaledDt, unscaledNow)` is the test seam. |
| `NpcRotorFlight` | plain class | The knocked-off rotor's path. An arc from the head to the spot, with whole somersaults (`rotorTumbleTurns`), upright by 80% of the flight, a hop on landing, and a spin that winds down to zero. Exact at both ends; pure function of time and the landing spot. |
| `NpcRotorSpin` | static | The living spin's rules: idle or Excited target, exponential frame-rate-independent easing, angle wrap. |
| `NpcTalkTracker` | plain class | The conversation rules: talking, speaking (with the minimum gesture time), emote, dead (permanent, outranks everything). |
| `NpcFacing` | static | Yaw toward a point; exponential, capped, frame-rate independent step. |
| `NpcDeathLift` | static | How far to raise the model at a normalised time of the death clip: none before `deathLiftStart`, smoothstep to `deathGroundLift` by `deathLiftEnd`, held after. The driver times it by its `DeathProgress` (the Death state's normalised time). |
| `IdleVariantPicker` | plain class | Main idle repeats with `mainIdleChance`, else a random fidget; fidgets return to the main idle. |
| `NpcClipRoles`, `NpcClipRole` | static, enum | File-prefix → role rules (on an `.fbx` or an `.anim`) and the talk fallback chain. Only the setup menu calls them; they live in the runtime assembly so EditMode tests can reach them. |
| `NpcAnimatorParameters` | static | The parameter and state names shared by the setup menu and the driver. |
| `NpcAnimationConfig` | ScriptableObject | Every tunable (section 3). |

### Animator contract (`NpcAnimatorParameters`)

| Parameter | Type | Set by the driver to |
|---|---|---|
| `Talking` | bool | in a conversation |
| `Speaking` | bool | a line is typing out, or started less than `minGestureSeconds` ago |
| `Emote` | int | the current line's `DialogueEmote` (0 Talk, 1 Excited, 2 Thinking), read from `Talkable.CurrentLine` |
| `Dead` | bool | the Health died (never cleared) |
| `IdleVariant` | int | the idle to move to at the end of the current idle loop |

| State | Motion | In | Out |
|---|---|---|---|
| `Idle` (default), `Idle_1`… | one per idle clip | `Idle_i` → `Idle_j` at `idleExitTime` when `IdleVariant == j` and not Talking | → `TalkIdle` on Talking |
| `TalkIdle` | the main idle | from idles on Talking; from talk states when Speaking drops | → `Talk_<e>` on Speaking && Emote == e && !Dead; → `Idle` when not Talking |
| `Talk_Talk`, `Talk_Excited`, `Talk_Thinking` | the emote's clip, else `Talk_Talk`'s, else the main idle at `fallback…Speed` | Any State (and `TalkIdle`) on Speaking && Emote == e && !Dead, no self-transition | → `TalkIdle` when !Speaking (interruption source *Destination*, so `TalkIdle → Talk_<e>` can cut into it); → `Idle` when !Talking |
| `Death` | the death clip, not looping, held on its last frame | Any State on Dead, tested first | none |

Foot IK is on for every state except Death. There is no `ChibiFootIK` on Birdy: it reads scaled
time and would freeze while the conversation pauses the game.

### Import (what Setup Birdy NPC does, in order)

1. **Names.** A fresh Tripo download (`tripo_convert_<uuid>.fbx` + `.fbm/` textures) is renamed to
   `Birdy.fbx`, `Birdy_BaseColor.jpg`, `Birdy_Normal.jpg` with `AssetDatabase.MoveAsset` (GUIDs
   kept) and the empty `.fbm` folder is deleted. The FBX embeds no media (its texture nodes only
   point at the `.fbm` by relative path), so Unity never re-creates a `.fbm`.
2. **Textures.** Base map sRGB, 4096, high-quality compression; normal map imported as
   *Normal map*, 2048.
3. **Material.** URP Lit `Birdy.mat` (base + normal, smoothness 0.15), remapped onto the FBX's
   one material slot.
4. **Skin fix (on every import, `BirdyModelPostprocessor`; Birdy.fbx only).** Tripo got the
   weights wrong in ways that show when the wings (the arm chain) move:
   - *Accessories followed the wings.* The right shoulder strap (`tripo_part_15`) was 61%
     `R_UpperarmTwist02` while its left mirror (`tripo_part_14`) follows the spine, and the back
     radio (`tripo_part_9`) was 15% right forearm. The head, helmet, harness and compass carried
     11–38% clavicle and arm weight.
     - The right strap now copies its left mirror's weights vertex by vertex: nearest mirrored
       vertex, L/R bones swapped, 1.8 cm worst match.
     - Every non-wing part with more than 5% arm-chain weight (Clavicle, Upperarm, Forearm, Hand,
       twists) has that share moved per vertex to the nearest spine bone (Waist, Spine01, Spine02).
     - The wing planks (`tripo_part_4/5`) keep theirs.
     - Report Birdy Poses' "arm-chain weight share" line reads 0% for every non-wing part.
   - *The rotor bent across the face.* It was skinned about 97% to Head and the rest to the arm
     twist bones, giving up to 0.37 m of drift. The rotor part (average Head weight ≥ 0.9) is made
     100% Head, and every other vertex with Head weight ≥ 0.9 too.
   - *The rotor has its own bone.* On the head it could only follow the head, and lying on his
     back turns the head's up horizontal, so in the death the blades stood upright out of the
     floor.
     - A `Rotor` transform is added under `Head`, and every rotor vertex is skinned 100% to it.
     - Its origin is the bottom of the mast on the spin axis. Its up is the spin axis: the
       normal of the blade disc, found from the vertices (principal axes). The disc is tilted
       11.6° from the head's up, so the axis is measured, not assumed.
     - The blade radius is 0.42 m.
     - Resting at its origin with up pointing up, no part of the rotor is below the ground.
     - Nothing in the clips drives the bone; `NpcRotor` does. Rotor drift relative to the head is
       0.0 cm in every state, because in the Editor the bone stays at its rest pose on the head.
5. **Rig.** `globalScale` is solved so the model is `TargetHeight` (1.15 m) tall: the renderer
   bounds measure 0.986 m at scale 1, so the scale is **1.166**. Humanoid, *Create From This
   Model*, with the shared Tripo bone map (`ChibiSandboxSetup.SetupRigPublic`, the same map as
   the chibi and Newton):

   | Human | Tripo bone |
   |---|---|
   | Hips | `Hip` |
   | Spine / Chest / UpperChest | `Waist` / `Spine01` / `Spine02` |
   | Neck / Head | `NeckTwist01` / `Head` |
   | Shoulder / UpperArm / LowerArm / Hand | `L/R_Clavicle` / `L/R_Upperarm` / `L/R_Forearm` / `L/R_Hand` |
   | UpperLeg / LowerLeg / Foot / Toes | `L/R_Thigh` / `L/R_Calf` / `L/R_Foot` / `L/R_ToeBase` |

   Twist bones (`*Twist01/02`), `Pelvis`, `NeckTwist02` and `Root` stay unmapped. The wings are
   the arm chain: the wing meshes are skinned to `L/R_Upperarm…Hand` and their twist bones.
6. **Wing fold.** The upper arms' Down-Up muscle range is narrowed from Unity's 60° to
   `WingFoldDegrees` (40°), which scales every clip's arm-down motion: the idles' wings hang
   about 60–70° below horizontal and sit just off the body.
7. **T-pose.** Tripo binds the arms (wings) a few degrees below horizontal (3–8° measured). Unity
   treats the avatar's reference pose as the T-pose, which would add that droop to every
   gesture, so each upper arm and forearm in the stored reference skeleton is rotated to point
   straight out (Unity's *Enforce T-Pose*, arms only). The mesh bind pose is untouched. A re-run
   finds them straight and writes nothing.
8. **Clips** (next section), **controller** (rewritten in place, GUID kept; skipped when its
   inputs are unchanged, see section 5), **dialogue** and **config** (seeded once).
9. **Prefab**, edited in place with `LoadPrefabContents`, so component file IDs survive. The
   Talkable's registry is set to `DialogueSetup.EnsureTalkableSet()`, which creates the shared
   set if Setup Dialogue hasn't run yet, so walk-up talk works whichever menu runs first.
10. **Placement** (ChibiSandbox only; any other open scene only gets the prefab updated). One
    instance named `Birdy` under the `AcademyNpcs` root, facing the spawn.
    - **Every candidate must pass all of these:**
      - 36 directions × 3.5–12 m around the spawn;
      - flat ground within 1 m of spawn height, with room for Birdy;
      - at least 7 m from Newton, 3 m from training dummies and 2 m from other NPCs;
      - clear of every pickup's and tether anchor's renderer footprint (plus 0.8 / 0.6 m);
      - a **level talk ring**. On the half facing the spawn, at 1, 1.7 and 2.4 m out, the ground
        must sit within ±0.3 m of Birdy's. Otherwise the player stands a step above or below him
        to talk, the prompt's height margin shrinks, he hides behind the player in the orbit view,
        and the dialogue shot looks steeply down on him.
    - **Then, in order of preference:**
      1. in the starting camera's view and in sight of the player's eye: the most central at
         about 6 m, nearest spawn height;
      2. in sight but off the starting screen;
      3. out of sight.

      Visibility is relaxed before levelness, never the reverse.
    - **An existing instance** is kept where it is if its own talk ring is level, and moved if
      not, so a re-run with a valid Birdy changes nothing.
    - The scene is marked dirty, never saved.
11. **Saving.** Only Birdy's own assets are written (`SaveAssetIfDirty`), never
    `AssetDatabase.SaveAssets()`, which would also rewrite the TMP example fonts that loading
    ChibiSandbox upgrades in memory.

`AcademyNpcs` is a load-bearing name: **Build Training Stage** strips every root whose name
contains "Academy", which keeps Birdy out of TrainingStage.

### Clips

Clips live in `Animation/Birdy/`, named by role prefix (case-insensitive). Each is either:
- a Mixamo FBX (*FBX for Unity, Without Skin, 30 fps*, like `Animation/Chibi/`), whose first
  take is the clip; or
- an `.anim` copied out of a clip library whose FBX holds many takes (Quaternius UAL).

The prefixes:

| Prefix | Role | Loops |
|---|---|---|
| `Idle*` | idle; several become random variants, the first by name is the main idle | yes |
| `Talk_Talk*` | the Talk emote | yes |
| `Talk_Excited*` | the Excited emote | yes |
| `Talk_Thinking*` | the Thinking emote | yes |
| `Death*` | death, played once and held | no |

Import settings for an FBX, written by the menu:
- Humanoid, avatar *Create From This Model* (the Mixamo rig).
- One clip, named after the file.
- Loop Time and Loop Pose on, except for death.
- Root rotation, height and XZ all **baked into the pose** (based upon *Original*, height based
  upon *Feet*). Birdy never travels and its Animator has root motion off, so nothing drifts,
  and the death lands on the ground instead of at standing height.

An `.anim` carries the same settings, written by whatever made it. The menu only checks them,
warning if the clip isn't humanoid or its Loop Time is wrong for its role. It never rewrites
them, so two menus can't fight over one file.

**Shipped clips.** Mixamo offered a primary and an alternative for each role. Both were rendered
on Birdy through the shipped controller (contact sheets at 0.1 / 0.3 / 0.5 / 0.7 / 0.9 of the
clip, plus soles, hand-to-head and hand-to-body distances), and only the winner was kept, saved
under the role name. The death was then replaced by Quaternius UAL *Death01* (below):

| File | Mixamo clip | Length | Why this one |
|---|---|---|---|
| `Idle.fbx` (main idle) | *Happy Idle* v1 | 2.0 s | A calm sway that loops cleanly. |
| `Idle_HappyV2.fbx` | *Happy Idle* v2 | 2.9 s | The alternative idle. It reads well as a fidget, so it is kept as a second variant rather than dropped. |
| `Idle_LookAround.fbx` | *Unarmed Idle Looking Ver. 2* | 7.8 s | The only look-around offered. |
| `Talk_Talk.fbx` | *Talking, General Conversation* | 10.3 s | Primary. Varied gestures over a long loop, so repeats aren't obvious. The alternative, *Asking A Question With Two Hands*, is one gesture and then stands still for half the loop. |
| `Talk_Excited.fbx` | *Excited, Super Excited* | 6.6 s | Primary. Big waves while staying grounded. The alternative, *Joyful Jump*, lifts the soles 0.35 m off the ground mid-line. |
| `Talk_Thinking.fbx` | *Thinking While Standing* | 4.2 s | Primary. A hand to the chin reads as thinking at a glance. The alternative, *Thoughtful Head Shake*, is a subtle shake that reads as idling. |
| `Death.anim` | Quaternius UAL1 *Death01* | 2.4 s | From a plain stand, a stagger and a quick fall onto the back, ending feet-up like a cartoon dead bird. It beat both Mixamo deaths. *Standing React Death Backward* swings the rotor wildly mid-fall and takes 3.6 s. *Falling Back Death* starts fists-up (a fighting pose) and brings the hands to within 1 cm of the body axis, so the wings cut into the belly. |

Measured through the Animator with foot IK:
- **Soles** stay within −3 … +2 mm in every idle and talk state, so `ModelYOffset` (0.008 m)
  still holds.
- **Loops:** Loop Pose closes the seam on every looping clip.
- **Rigid parts:** rotor drift relative to the head is 0.0 cm in every state, and no non-wing
  part carries arm-chain weight, so the accessory rebinding holds under the real clips.

The rejected alternatives are not in the repo.

**Quaternius UAL (CC0).** `Animation/Quaternius/` holds UAL1 and UAL2 Standard (OpenGameArt,
CC0 1.0, licences beside the FBX). **Tools ▸ Pickleball ▸ Setup Birdy UAL Trial**
(`BirdyUalTrialSetup`) does three things:
- imports both FBX as humanoids with an explicit bone map;
- copies the mapped takes to `Animation/Quaternius/Birdy/*.anim`;
- builds `Animation/Quaternius/Birdy_UAL.controller`, an all-UAL controller with the live
  layout, used by nothing but `BirdyUalControllerTests`.

On Birdy, Mixamo reads better for idle, look-around and the three talk emotes, but UAL *Death01*
beats the Mixamo deaths. So the trial copies that one take straight into
`Animation/Birdy/Death.anim`, Birdy's live death, which the trial controller also plays. The
copy is redone only when its source changes (a hash in the `.anim`'s `.meta` userData).
`Death.anim` is committed, so Setup Birdy NPC doesn't need the trial menu to have run.

**Fallbacks.** A role with no clip in `Animation/Birdy/` uses `Animation/Birdy/Fallback/`. Every
role has a real clip now, so these are unused, but they stay as a safety net. The menu copies
them in when they are missing (copies, so their import settings can change without touching the
chibi's files):
- `Idle_ChibiIdle.fbx`, a copy of `Animation/Chibi/Idle.fbx`.
- `Death_SwordShieldDeath2.fbx`, a copy of `Animation/Chibi/SwordShield/sword and shield death (2).fbx`,
  a weapon clip whose first second is a fighting crouch.

There are no talk fallbacks: a talk emote with no clip of its own plays the Talk clip, and with
no Talk clip either, the main idle sped up (Talk ×1.2, Excited ×1.5) or slowed (Thinking ×0.8).
Only the first clip per talk emote and the first death clip are used (an `.fbx` and an `.anim`
of the same role count as two clips, so keep one).

## 3. How to tune

`ScriptableObjects/Npc/NpcAnimationConfig.asset` (seeded once; hand edits survive):

| Field | Unit | Feel |
|---|---|---|
| `turnRate` | 1/s | How fast the NPC turns to the player (6 ≈ settled in half a second) |
| `maxTurnSpeed` | °/s | Caps the start of a big turn so it pivots instead of snapping; 0 = no cap |
| `returnToRestFacing` | bool | Turn back to the placed facing once the player walks off |
| `restReturnDistance` | m | While the player stays within this after a conversation, the NPC keeps watching them |
| `restTurnRate` | 1/s | Speed of the turn back (slower than the turn in) |
| `deathGroundLift` | m | How far the model is eased up over the death. **Re-tune per death clip** (Report Birdy Poses, "death sink") |
| `deathLiftStart`, `deathLiftEnd` | normalised | Where in the death clip the lift starts (the body starts going down) and is complete (the body has landed; a lift still rising after that floats the corpse up). Re-tune per death clip |
| `rotorIdleSpin`, `rotorExcitedSpin` | °/s | Rotor spin while idle, listening or on a calm line (180), and while an Excited line plays (900) |
| `rotorSpinResponse` | 1/s | How fast the spin follows those (3 ≈ a second to change) |
| `rotorPopAt` | normalised | Where in the death clip the rotor is knocked off: just before the head hits the ground. **Re-tune per death clip** (Report Birdy Poses, "rotor fall": its lowest point in flight must not be below 0) |
| `rotorPopSpin`, `rotorSpinDownSeconds` | °/s, s | Spin in the air, and how long it takes to wind down after landing |
| `rotorFlightSeconds`, `rotorPopHeight`, `rotorTumbleTurns` | s, m, turns | The flight: time in the air, height of the arc, whole somersaults |
| `rotorLandDistance`, `rotorLandSide` | m | Landing spot: how far beyond where the mast sat on the head, along the fall, and to the side. Must clear the head by more than the blade radius (0.42 m) |
| `rotorBounceHeight`, `rotorBounceSeconds` | m, s | The hop on landing; 0 height = none |
| `minGestureSeconds` | s (unscaled) | Shortest gesture per line, even if the line is skipped |
| `mainIdleChance` | 0–1 | Chance the main idle repeats instead of a fidget |
| `talkBlend`, `settleBlend`, `idleBlend`, `deathBlend` | s | Cross-fades baked into the controller |
| `idleExitTime` | normalised | Where in an idle loop the next variant may start |
| `fallbackTalkSpeed`, `fallbackExcitedSpeed`, `fallbackThinkingSpeed` | × | Idle playback speed in a talk state with no talk clip |

The blend, exit-time and fallback-speed fields are baked into the controller: run
**Rebuild Birdy Animator** after changing them. The rest are read live.

The shipped death lift is **0.10 m from t = 0.35 to 0.6**, tuned for UAL *Death01* by looking at
its end pose rendered at 0, 0.06, 0.08, 0.10, 0.12 and 0.14 m, with the rotor hidden since it has
flown off by then.
- **Without a lift**, measured per mesh part through the Animator at the clip's end:

  | Part | Depth into the floor |
  |---|---|
  | tail (which sticks out behind) | 31 cm |
  | radio | 18 cm |
  | lower body | 11 cm |
  | head | 11 cm |
  | harness | 7 cm |
  | helmet | 6 cm |
  | wings | 5–6 cm |

  The feet are up, 1–4 cm off the floor.
- **At 0.10 m** the round lower body and the head rest on the floor (1 cm in), the wings lie on it,
  and the feet are up about 12 cm. The radio box (8 cm in) and the tail (21 cm in) stay hidden in
  the floor.
- **At 0.08 m** the body visibly sinks. **At 0.12 m and above**, the tail's fan shows under the
  body.
- **Timing.** The body goes down between t ≈ 0.35 and 0.5 and lies still from about 0.65, so the
  lift runs from 0.35 to 0.6. Ending at 1, as for the Mixamo deaths, would raise a still body by
  6 cm over its last second.

The rotor is knocked off at **t = 0.4**, just before the head hits the ground, while its lowest
point is still 0.24 m up. At 0.45 it would already be 5 cm in, and at 0.5 it is 36 cm in.
Report Birdy Poses' "rotor fall" line measures the result:
- lowest point in flight +0.000 m;
- at rest +0.000 m, upright to 0.0°;
- 0.81 m (level) from the head bone;
- 31 cm from the nearest body vertex.

In `BirdyNpcSetup` (code constants, since they define the asset):
- `TargetHeight` (1.15 m) and `MaxHealth` (40).
- `ModelYOffset` (0.008 m). Measured through the Animator (Rebind + Update, with foot IK, as at
  runtime): the idle's soles sit at ±1 mm. An earlier 0.045 m came from
  `AnimationClip.SampleAnimation`, which skips foot IK, and made Birdy hover 3.7 cm.
- `WingFoldDegrees` (40°), and the placement clearances.

Change them and re-run **Setup Birdy NPC**. The prefab's own Health and capsule values are
rewritten on every run. When clips change, run **Diagnostics ▸ Report Birdy Poses** and
re-check `ModelYOffset` against the idle's "soles" value and the death lift against
"death sink".

Content: edit `ScriptableObjects/Dialogue/Birdy_Intro.asset` directly. It is seeded once and
never overwritten.

## 4. How to extend

**Add clips to Birdy (the Mixamo scout's path):**
1. Download from Mixamo as *FBX for Unity*, *Without Skin*, 30 fps.
2. Save into `Assets/_Project/Animation/Birdy/` as `Idle_<Name>.fbx`, `Talk_Talk_<Name>.fbx`,
   `Talk_Excited_<Name>.fbx`, `Talk_Thinking_<Name>.fbx` or `Death_<Name>.fbx`. Trial candidates
   outside this folder: every `Idle*` file in it becomes an idle variant, and for the other roles
   the first file by name wins.
3. Run **Tools ▸ Pickleball ▸ Rebuild Birdy Animator** (or Setup Birdy NPC). A role that now has
   a real clip stops using its fallback.
4. Run **Diagnostics ▸ Report Birdy Poses**. Re-tune `ModelYOffset` against the idle's soles,
   `deathGroundLift` / `deathLiftStart` / `deathLiftEnd` against a new death's "death sink"
   line, and `rotorPopAt` against its "rotor fall" line.

A clip from a library FBX with many takes (like Quaternius UAL) goes in as an `.anim` with the
role prefix. Copy it out the way `BirdyUalTrialSetup` does, with its settings already right.

**Add another NPC in 7 steps:**
1. Put its model in `Art/Characters/<Name>/` (a Tripo humanoid takes the same bone map). If its
   accessories are skinned to the wrong bones, add a postprocessor like `BirdyModelPostprocessor`.
2. Copy `BirdyNpcSetup.cs` to `<Name>NpcSetup.cs`; change the paths, `InstanceName`,
   `TargetHeight`, `MaxHealth` and the menu names. (When a third NPC arrives, lift the shared
   steps into a helper instead of copying again.)
3. Put its clips in `Animation/<Name>/` with the role prefixes above; add fallbacks if wanted.
4. Write its dialogue: let the menu seed `ScriptableObjects/Dialogue/<Name>_<Topic>.asset`, then
   edit the asset.
5. Reuse `NpcAnimationConfig.asset`, or create a second config (a different temperament, or a
   different death lift for a different death clip) and point the menu at it.
6. Choose where it stands: change the placement scoring, or skip placement and drop the prefab
   by hand.
7. Run the new menu (before or after **Setup Dialogue**), then save the scene.

No runtime code changes are needed: `NpcAnimatorDriver`, `NpcBody` and the controller layout are
generic.

**A new emote** (a new `DialogueEmote` value): the controller grows a `Talk_<Emote>` state on the
next rebuild. Add a `NpcClipRole` and a prefix in `NpcClipRoles` so it can have its own clip;
until then it falls back to Talk.

## 5. Known limitations and open questions

- **Human clips on a bird.** The clips are Mixamo's and Quaternius's, made for a thin human, so:
  - In *Thinking While Standing* the raised wing crosses the chest and passes close to the beak
    (hand 15 cm from the head bone), with a slight overlap.
  - The death can't be fully grounded (see "The death is only approximately grounded").
  - There are no beak or wing-flap motions. The rotor's spin is procedural (`NpcRotor`).
- **The rotor's landing ignores what is there.** It lands on the floor under its spot (the
  first static collider within half a metre of his feet's height), even if that spot is inside a
  wall or on a table edge. The spot is about 1.3 m behind where Birdy stood, and his talk ring
  keeps the ground around him level, so it hasn't shown.
- **White slivers at the wing roots** when the wings fold. The harness tears are gone: the
  accessories no longer follow the arms (import step 4). What remains is the seam where each
  wing plank meets the body, at the front and back of the shoulder. The wings are flat planks
  hinged at the shoulder, so folding them opens that joint. Narrowing the fold (40°, tried at 25°)
  doesn't close it. It needs modelled geometry or blended weights across the seam (in Blender).
- **The tail follows the left leg.** `tripo_part_3` (the tail) is weighted 61% to `L_CalfTwist01`.
  It barely shows in the idles and talks. In the death it goes into the floor from the start of
  the fall (t ≈ 0.35) and stays there.
- **The death is only approximately grounded.** One lift for the whole model can't ground a body
  whose tail sinks 31 cm while its belly and head sink 11 cm (section 3). The shipped 0.10 m
  leaves the tail 21 cm and the radio 8 cm in the floor, hidden under the body. The rotor no
  longer counts: it is knocked off before the head lands. The tail would need its own bone, like
  the rotor, to fold up against the floor.
- **Lock-on treats Birdy like an enemy.** `LockOnTargeting` takes any live `Health`, with no
  priority for hostiles, so Tab can pick Birdy in the Newton fight, and when Newton dies the lock
  hands over to Birdy if Birdy is on screen. Both need changes in `LockOnTargeting`.
- **Birdy is not in the starting view.** Of 648 candidates (36 directions × 3.5–12 m), none
  that is level enough to talk on is on the starting screen. The view looks down onto a lawn a
  terrace below the spawn: its near edge is the step, and the rest is taken by Newton's 7 m
  clearance, the tether-anchor and pickup footprints, or uneven ground.
  - Placement therefore relaxed visibility and chose **(6.46, -0.49, 14.21)**, 5.5 m from the
    spawn on its own level (+0.06 m), in sight of the player's eye but behind and to the side of
    the starting camera.
  - Ground around him, relative to his root, at 1 / 1.7 / 2.4 m:
    - toward the spawn: −0.02 / +0.03 / +0.07
    - worst on the approach side: +0.22 m, at −90° and 2.4 m
    - behind him: −0.01 / +0.02 / −0.01
  - The earlier spot, (9.22, -1.35, 5.57), was in view but on that lower lawn. The player
    walking up stood 0.83 m above him (QA); the levelness check measures a 1.01 m step there and
    moves such an instance.
  - To put Birdy in the starting view, clear room on the lawn (move a tether anchor) or move the
    starting camera.
  - The committed ChibiSandbox has Birdy where the manager placed him, at
    **(3.87, -1.48, 4.86)**. His talk ring there passes the levelness check, so the menu leaves
    him there; whether that spot is on the starting screen hasn't been measured. The spot above
    is what the menu picks in a scene without him.
- **No hit reaction.** A sword hit only shows the sword's own flash and hitstop. No flinch clip,
  no damage numbers, no health bar. The boss bar and `DamageNumbers` are wired by Setup Newton
  Fight to exactly Newton and the player, and rewritten on every run of that menu, so adding
  Birdy there would vanish. They would also frame a friendly NPC as an enemy.
- **One clip per talk emote and one death.** Extra `Talk_*` or `Death*` clips are ignored, not
  randomised.
- **The UAL `.anim` copies can churn.** `BirdyUalTrialSetup` keys each copy on its source FBX's
  dependency hash.
  - Bumping any model postprocessor's version changes that hash for every FBX. The next run of the
    trial menu then rewrites every `.anim` and its `.meta` userData, `Animation/Birdy/Death.anim`
    included. The clips come out the same; only the userData changes.
  - The committed hashes are for `BirdyModelPostprocessor` v3, so the menu currently writes
    nothing.
- **The rotor never turns in the Editor.** `NpcRotor` runs in `LateUpdate`, so in Edit mode the
  rotor sits still at its rest pose on the head (as Report Birdy Poses measures it), and the fall
  is only seen in Play mode or through its `Tick`.
- **The controller rebuild is not byte-stable.** Setup Birdy NPC skips the rebuild when the
  inputs' hash (kept in the controller's `.meta` userData) matches, so an unchanged re-run has no
  diff. **Rebuild Birdy Animator** always rewrites the controller, giving every state new file IDs
  but the same states, parameters and transitions. Nothing references the states by file ID.
- **Adding or bumping `BirdyModelPostprocessor` reimports models once.** Unity treats a model
  postprocessor and its version as a dependency of every model import. So the first Editor to
  load a new one, or a new `GetVersion` (v3 here, for the rotor bone), reimports the project's FBX
  files once. It only changes Birdy.fbx.
- **Rest facing is captured in `OnEnable`.** A script reload mid-conversation makes the facing at
  that moment the new rest facing.
- **Single-NPC setup script.** The import, rig, clip and prefab steps are generic but live in
  `BirdyNpcSetup`. Extract them when a second NPC arrives.
