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
  `Birdy_Normal.jpg`); clips in `Animation/Birdy/`; controller
  `Animation/Controllers/Birdy.controller`; prefab `Prefabs/Npc/Birdy.prefab`.
- **Setup:** `Scripts/Editor/BirdyNpcSetup.cs`: **Tools ▸ Pickleball ▸ Setup Birdy NPC**,
  **Rebuild Birdy Animator**, **Diagnostics ▸ Report Birdy Poses**; and
  `Scripts/Editor/BirdyModelPostprocessor.cs` (import-time skin fix, Birdy.fbx only).
- **Tests:** `Tests/EditMode/Npc/` (the `BirdyControllerTests` drive the shipped controller and
  prefab, category `Assets`), plus `Tests/EditMode/Combat/XpExemptionTests.cs`.

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

Birdy has 40 HP. The sword can kill it; it plays its death once (a stagger and a fall onto its
back) and stays down, the model is eased up a little over the fall so the body rests on the
ground (the rotor and tail, which stick out behind, still dip into it), its collider switches
off so the body is not an invisible pillar, and the Talkable stops offering a conversation
(`Talkable.CanTalk` checks the `Health` on the same object). Killing Birdy pays no XP
(`Health.grantsXp` is off on its prefab).

Birdy is 1.14 m tall in its idle, rotor included. The chibi measures 1.56 m in its bind pose,
so Birdy's rotor comes up to about the chibi's chin.

## 2. Architecture

```
DialogueDirector ──Notify…──▶ Talkable ──ConversationStarted / LineStarted / LineRevealed / ConversationEnded──▶ NpcAnimatorDriver ──▶ Animator (Birdy.controller)
                                                                                                                │      ├─▶ root yaw (faces Partner, then watches / returns to rest)
                                                                                                                │      └─▶ model height (death lift)
MeleeCombat ──IDamageable.TakeHit──▶ Health ──Died──▶ NpcAnimatorDriver (Death) · NpcBody (collider off) · Talkable.CanTalk = false
CombatXpSource ──skips Health.GrantsXp == false (Birdy)
LockOnTargeting ──FindObjectsByType<Health>──▶ Health (Birdy is lockable)
PlayerInteractor ──TalkableSet──▶ Talkable (walk-up talk)
```

Prefab layout (`Prefabs/Npc/Birdy.prefab`):

| Object | Components | Why |
|---|---|---|
| `Birdy` (root) | `CapsuleCollider`, `Health` (40 HP, no grey pool, `grantsXp` off), `Talkable` (focus = Head bone, registry = `TalkableSet`), `NpcAnimatorDriver`, `NpcBody` | Talkable and Health must share the root: `CanTalk` only checks a Health on its own object, and lock-on locks the Health's object. The capsule is solid because the blade sweep and lock-on's line of sight ignore triggers. |
| `BirdyModel` | `Animator` (avatar, `Birdy.controller`, root motion off, `CullUpdateTransforms`) | The model under the root, as for Newton, so it can be nudged (and lifted on death) without moving the collider. |

Runtime classes:

| Type | Kind | Job |
|---|---|---|
| `NpcAnimatorDriver` | MonoBehaviour, presentation | Subscribes to the Talkable and Health in `OnEnable`, unsubscribes in `OnDisable`. Switches the Animator to `UnscaledTime` on the first ConversationStarted and restores the saved mode on ConversationEnded **and** in `OnDisable`. Pushes the parameters every frame, rolls idle variants, turns the root, applies the death lift. Reads the clocks only in `Update`; `Tick(unscaledNow, unscaledDt, scaledDt)` is the test seam, and the event handlers use the clock it was last given. `OnEnable` re-syncs a running conversation, or one that ended while disabled. |
| `NpcBody` | MonoBehaviour, gameplay | Disables the blocking collider on `Health.Died`. |
| `NpcTalkTracker` | plain class | The conversation rules: talking, speaking (with the minimum gesture time), emote, dead (permanent, outranks everything). |
| `NpcFacing` | static | Yaw toward a point; exponential, capped, frame-rate independent step. |
| `NpcDeathLift` | static | How far to raise the model at a normalised time of the death clip: none before `deathLiftStart`, smoothstep to `deathGroundLift` at the end. |
| `IdleVariantPicker` | plain class | Main idle repeats with `mainIdleChance`, else a random fidget; fidgets return to the main idle. |
| `NpcClipRoles`, `NpcClipRole` | static, enum | File-prefix → role rules and the talk fallback chain. Only the setup menu calls them; they live in the runtime assembly so EditMode tests can reach them. |
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
     100% Head, and every other vertex with Head weight ≥ 0.9 too. Rotor drift relative to the
     head is now 0.0 cm in every state.
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

Clips are Mixamo FBX (*FBX for Unity, Without Skin, 30 fps*, like `Animation/Chibi/`) in
`Animation/Birdy/`, named by role prefix (case-insensitive):

| Prefix | Role | Loops |
|---|---|---|
| `Idle*` | idle; several become random variants, the first by name is the main idle | yes |
| `Talk_Talk*` | the Talk emote | yes |
| `Talk_Excited*` | the Excited emote | yes |
| `Talk_Thinking*` | the Thinking emote | yes |
| `Death*` | death, played once and held | no |

Import settings, written by the menu: Humanoid, avatar *Create From This Model* (the Mixamo
rig), one clip named after the file, Loop Time and Loop Pose on except for death, and root
rotation, height and XZ all **baked into the pose** (based upon *Original*, height based upon
*Feet*). Birdy never travels and its Animator has root motion off, so nothing drifts, and the
death lands on the ground instead of at standing height.

**Shipped clips.** Mixamo offered a primary and an alternative for each role. Both were rendered
on Birdy through the shipped controller (contact sheets at 0.1 / 0.3 / 0.5 / 0.7 / 0.9 of the
clip, plus soles, hand-to-head and hand-to-body distances), and only the winner was kept, saved
under the role name:

| File | Mixamo clip | Length | Why this one |
|---|---|---|---|
| `Idle.fbx` (main idle) | *Happy Idle* v1 | 2.0 s | A calm sway that loops cleanly. |
| `Idle_HappyV2.fbx` | *Happy Idle* v2 | 2.9 s | The alternative idle. It reads well as a fidget, so it is kept as a second variant rather than dropped. |
| `Idle_LookAround.fbx` | *Unarmed Idle Looking Ver. 2* | 7.8 s | The only look-around offered. |
| `Talk_Talk.fbx` | *Talking, General Conversation* | 10.3 s | Primary. Varied gestures over a long loop, so repeats aren't obvious. The alternative, *Asking A Question With Two Hands*, is one gesture and then stands still for half the loop. |
| `Talk_Excited.fbx` | *Excited, Super Excited* | 6.6 s | Primary. Big waves while staying grounded. The alternative, *Joyful Jump*, lifts the soles 0.35 m off the ground mid-line. |
| `Talk_Thinking.fbx` | *Thinking While Standing* | 4.2 s | Primary. A hand to the chin reads as thinking at a glance. The alternative, *Thoughtful Head Shake*, is a subtle shake that reads as idling. |
| `Death.fbx` | *Standing React Death Backward* | 3.6 s | Primary. Starts from a neutral stance, so the cut from idle or talk is clean, and falls onto the back. The alternative, *Falling Back Death*, starts fists-up (a fighting pose) and brings the hands to within 1 cm of the body axis, so the wings cut into the belly. |

Measured through the Animator with foot IK:
- **Soles** stay within −3 … +2 mm in every idle and talk state, so `ModelYOffset` (0.008 m)
  still holds.
- **Loops:** Loop Pose closes the seam on every looping clip.
- **Rigid parts:** rotor drift relative to the head is 0.0 cm in every state, and no non-wing
  part carries arm-chain weight, so the accessory rebinding holds under the real clips.

The rejected alternatives are not in the repo.

**Fallbacks.** A role with no clip in `Animation/Birdy/` uses `Animation/Birdy/Fallback/`. Every
role has a real clip now, so these are unused, but they stay as a safety net. The menu copies
them in when they are missing (copies, so their import settings can change without touching the
chibi's files):
- `Idle_ChibiIdle.fbx`, a copy of `Animation/Chibi/Idle.fbx`.
- `Death_SwordShieldDeath2.fbx`, a copy of `Animation/Chibi/SwordShield/sword and shield death (2).fbx`,
  a weapon clip whose first second is a fighting crouch.

There are no talk fallbacks: a talk emote with no clip of its own plays the Talk clip, and with
no Talk clip either, the main idle sped up (Talk ×1.2, Excited ×1.5) or slowed (Thinking ×0.8).
Only the first clip per talk emote and the first death clip are used.

## 3. How to tune

`ScriptableObjects/Npc/NpcAnimationConfig.asset` (seeded once; hand edits survive):

| Field | Unit | Feel |
|---|---|---|
| `turnRate` | 1/s | How fast the NPC turns to the player (6 ≈ settled in half a second) |
| `maxTurnSpeed` | °/s | Caps the start of a big turn so it pivots instead of snapping; 0 = no cap |
| `returnToRestFacing` | bool | Turn back to the placed facing once the player walks off |
| `restReturnDistance` | m | While the player stays within this after a conversation, the NPC keeps watching them |
| `restTurnRate` | 1/s | Speed of the turn back (slower than the turn in) |
| `deathGroundLift` | m | How far the model is eased up by the end of the death. **Re-tune per death clip** (Report Birdy Poses, "death sink") |
| `deathLiftStart` | normalised | Where in the death clip the lift starts; set to where the body goes down. Re-tune per death clip |
| `minGestureSeconds` | s (unscaled) | Shortest gesture per line, even if the line is skipped |
| `mainIdleChance` | 0–1 | Chance the main idle repeats instead of a fidget |
| `talkBlend`, `settleBlend`, `idleBlend`, `deathBlend` | s | Cross-fades baked into the controller |
| `idleExitTime` | normalised | Where in an idle loop the next variant may start |
| `fallbackTalkSpeed`, `fallbackExcitedSpeed`, `fallbackThinkingSpeed` | × | Idle playback speed in a talk state with no talk clip |

The blend, exit-time and fallback-speed fields are baked into the controller: run
**Rebuild Birdy Animator** after changing them. The rest are read live.

The shipped death lift is **0.08 m from t = 0.6**, tuned for *Standing React Death Backward* by
looking at its end pose rendered at 0, 0.05, 0.08, 0.10, 0.12, 0.15, 0.2 and 0.3 m.
- **Without a lift**, measured per mesh part through the Animator at the clip's end:

  | Part | Depth into the floor |
  |---|---|
  | rotor and tail (which stick out behind) | 34 cm |
  | lower body | 14 cm |
  | radio | 11 cm |
  | head | 9 cm |
  | helmet | 6 cm |
  | wings | 2–6 cm |

  The feet rest at the floor.
- **Lifting by the lowest point** (0.34 m) floats the whole bird in mid-air with its feet up,
  which is plainly worse.
- **At 0.08 m** the round lower body sits 6 cm into the floor and reads as resting on it, the head
  is at the floor, and the feet are raised about 9 cm, like legs kicked up after a fall onto the
  back. The rotor and tail stay 26 cm in: the rotor plane stands upright with its blades half in
  the floor.
- **At 0.10 m and above**, the radio box shows under the body and the shadow separates.
- **The body starts going down at t ≈ 0.6** (lower body −6 cm). A leg slam at t ≈ 0.8 still dips
  a foot 13 cm into the floor for a moment, with the lift half in.

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
   and `deathGroundLift` / `deathLiftStart` against a new death's "death sink" line.

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

- **Human clips on a bird.** The clips are Mixamo's, made for a thin human, so:
  - In *Thinking While Standing* the raised wing crosses the chest and passes close to the beak
    (hand 15 cm from the head bone), with a slight overlap.
  - The death can't be fully grounded (next point).
  - There are no beak, rotor-spin or wing-flap motions. The rotor is rigid to the head and never
    turns.
- **White slivers at the wing roots** when the wings fold. The harness tears are gone: the
  accessories no longer follow the arms (import step 4). What remains is the seam where each
  wing plank meets the body, at the front and back of the shoulder. The wings are flat planks
  hinged at the shoulder, so folding them opens that joint. Narrowing the fold (40°, tried at 25°)
  doesn't close it. It needs modelled geometry or blended weights across the seam (in Blender).
- **The tail follows the left leg.** `tripo_part_3` (the tail) is weighted 61% to `L_CalfTwist01`.
  It barely shows in the idles and talks. In the death's stagger (t ≈ 0.5–0.6, before the lift
  starts) the left leg steps back and pushes the tail 12–15 cm into the floor.
- **The death is only approximately grounded.** One lift for the whole model can't ground a body
  whose rotor and tail sink 34 cm while its feet stay at the floor (section 3). The shipped
  0.08 m leaves the rotor and tail 26 cm in, with the rotor blades standing upright out of the
  floor. Two things would fix it: a death that lands on the side or front, or tilting the rotor
  flat to the ground once Birdy is down.
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
- **The controller rebuild is not byte-stable.** Setup Birdy NPC skips the rebuild when the
  inputs' hash (kept in the controller's `.meta` userData) matches, so an unchanged re-run has no
  diff. **Rebuild Birdy Animator** always rewrites the controller, giving every state new file IDs
  but the same states, parameters and transitions. Nothing references the states by file ID.
- **Adding `BirdyModelPostprocessor` reimports models once.** Unity treats a new model
  postprocessor as a dependency of model imports, so the first Editor to load it reimports the
  project's FBX files once. It only changes Birdy.fbx.
- **Rest facing is captured in `OnEnable`.** A script reload mid-conversation makes the facing at
  that moment the new rest facing.
- **Single-NPC setup script.** The import, rig, clip and prefab steps are generic but live in
  `BirdyNpcSetup`. Extract them when a second NPC arrives.
