# NPCs (Birdy)

Talking characters that stand in the world, turn to face the player during a conversation,
gesture each line, and can be locked on to and cut down like anything else with a `Health`.
The first one is **Birdy**, a little helicopter bird in the academy courtyard who tips the player
off about Newton.

- **Code:** `Assets/_Project/Scripts/Sandbox/Npc/` (namespace `Pickleball.Sandbox.Npc`).
- **Contract it consumes:** `Scripts/Sandbox/Dialogue/` (`Talkable`, `DialogueDefinition`,
  `DialogueLine`, `DialogueEmote`, `ConversationEndReason`, `TalkableSet`). The dialogue director,
  camera, prompt and UI are a separate system; NPC code never talks to them.
- **Data:** `ScriptableObjects/Npc/NpcAnimationConfig.asset`,
  `ScriptableObjects/Dialogue/Birdy_Intro.asset`.
- **Art:** `Art/Characters/Birdy/` (`Birdy.fbx`, `Birdy.mat`, `Birdy_BaseColor.jpg`,
  `Birdy_Normal.jpg`); clips in `Animation/Birdy/`; controller
  `Animation/Controllers/Birdy.controller`; prefab `Prefabs/Npc/Birdy.prefab`.
- **Setup:** `Scripts/Editor/BirdyNpcSetup.cs`: **Tools ▸ Pickleball ▸ Setup Birdy NPC**,
  **Rebuild Birdy Animator**, **Diagnostics ▸ Report Birdy Poses**.
- **Tests:** `Tests/EditMode/Npc/`.

## 1. Purpose and player-facing feel

Birdy stands near the player's spawn in ChibiSandbox, idling. Walk up (or lock on) and talk:
the game pauses, Birdy turns to face you (on unscaled time, easing in, capped so a 180° turn is
a pivot rather than a snap), and gestures each line with the line's emote: ordinary **Talk**,
big **Excited** delivery, or slow **Thinking**. When a line has finished typing, the gesture
settles back to a listening idle. A line skipped with E still gestures for a moment
(`minGestureSeconds`), so skipping never reads as Birdy freezing. After the conversation,
Birdy waits a couple of seconds and drifts back to the way it was facing.

Birdy has 40 HP. The sword can kill it; it plays its death once and stays down, its collider
switches off so the body is not an invisible pillar, and the Talkable stops offering a
conversation (`Talkable.CanTalk` checks the `Health` on the same object).

Birdy is about 1.15 m tall with its rotor, against the chibi's 1.70 m: small, but tall enough
for the dialogue camera to frame both heads.

## 2. Architecture

```
DialogueDirector ──Notify…──▶ Talkable ──ConversationStarted / LineStarted / LineRevealed / ConversationEnded──▶ NpcAnimatorDriver ──▶ Animator (Birdy.controller)
                                                                                                                │      └─▶ root yaw (faces Partner)
MeleeCombat ──IDamageable.TakeHit──▶ Health ──Died──▶ NpcAnimatorDriver (Death) · NpcBody (collider off) · Talkable.CanTalk = false
LockOnTargeting ──FindObjectsByType<Health>──▶ Health (Birdy is lockable)
```

Prefab layout (`Prefabs/Npc/Birdy.prefab`):

| Object | Components | Why |
|---|---|---|
| `Birdy` (root) | `CapsuleCollider`, `Health`, `Talkable`, `NpcAnimatorDriver`, `NpcBody` | Talkable and Health must share the root: `CanTalk` only checks a Health on its own object, and lock-on locks the Health's object. The capsule is solid because the blade sweep and lock-on's line of sight ignore triggers. |
| `BirdyModel` | `Animator` (avatar, `Birdy.controller`, root motion off, `CullUpdateTransforms`) | The model under the root, as for Newton, so it can be nudged without moving the collider. |

Runtime classes:

| Type | Kind | Job |
|---|---|---|
| `NpcAnimatorDriver` | MonoBehaviour, presentation | Subscribes to the Talkable and Health in `OnEnable`, unsubscribes in `OnDisable`. Switches the Animator to `UnscaledTime` on the first ConversationStarted and restores the saved mode on ConversationEnded **and** in `OnDisable`. Pushes the parameters every frame, rolls idle variants, turns the root. `Tick(unscaledNow, unscaledDt, scaledNow, scaledDt)` is the test seam. |
| `NpcBody` | MonoBehaviour, gameplay | Disables the blocking collider on `Health.Died`. |
| `NpcTalkTracker` | plain class | The conversation rules: talking, speaking (with the minimum gesture time), emote, dead (permanent, outranks everything). |
| `NpcFacing` | static | Yaw toward a point; exponential, capped, frame-rate independent step. |
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
| `TalkIdle` | the main idle | from idles on Talking; from talk states when Speaking drops | → `Idle` when not Talking |
| `Talk_Talk`, `Talk_Excited`, `Talk_Thinking` | the emote's clip, else `Talk_Talk`'s, else the main idle at `fallback…Speed` | Any State on Speaking && Emote == e && !Dead (no self-transition) | → `TalkIdle` when !Speaking; → `Idle` when !Talking |
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
4. **Rig.** `globalScale` is solved so the model is `TargetHeight` (1.15 m) tall (native height
   0.954 m, so about 1.205). Humanoid, *Create From This Model*, with the shared Tripo bone map
   (`ChibiSandboxSetup.SetupRigPublic`, the same map as the chibi and Newton):

   | Human | Tripo bone |
   |---|---|
   | Hips | `Hip` |
   | Spine / Chest / UpperChest | `Waist` / `Spine01` / `Spine02` |
   | Neck / Head | `NeckTwist01` / `Head` |
   | Shoulder / UpperArm / LowerArm / Hand | `L/R_Clavicle` / `L/R_Upperarm` / `L/R_Forearm` / `L/R_Hand` |
   | UpperLeg / LowerLeg / Foot / Toes | `L/R_Thigh` / `L/R_Calf` / `L/R_Foot` / `L/R_ToeBase` |

   Twist bones (`*Twist01/02`), `Pelvis`, `NeckTwist02` and `Root` stay unmapped. The wings are
   the arm chain: the wing meshes are skinned to `L/R_Upperarm…Hand` and their twist bones.
5. **T-pose.** Tripo binds the arms (wings) about 13° below horizontal. Unity treats the avatar's
   reference pose as the T-pose, which would add that droop to every gesture, so each upper arm
   and forearm in the stored reference skeleton is rotated to point straight out (Unity's
   *Enforce T-Pose*, arms only). The mesh bind pose is untouched. A re-run finds them straight
   and writes nothing.
6. **Clips** (next section), **controller** (rewritten in place, GUID kept; skipped when its
   inputs are unchanged, see section 5), **dialogue** and **config** (seeded once).
7. **Prefab**, edited in place with `LoadPrefabContents`, so component file IDs survive.
8. **Placement** (ChibiSandbox only; any other open scene only gets the prefab updated). One
   instance named `Birdy` under the `AcademyNpcs` root, on flat ground 3.5–8 m from the player's
   spawn, in view of the spawn, at least 8 m from Newton, 2–2.5 m from pickups and tether anchors,
   3 m from training dummies, facing the spawn. An existing instance is left where it is. The
   scene is marked dirty, never saved.

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

A role with no clip in `Animation/Birdy/` uses `Animation/Birdy/Fallback/`. The menu copies these
in when they are missing (copies, so their import settings can change without touching the
chibi's files). They were chosen by sampling candidates on Birdy's rig:
- `Idle_ChibiIdle.fbx`, a copy of `Animation/Chibi/Idle.fbx`. The arms hang at the sides, so the
  wings fold down along the body (about 80° below horizontal), tips about 16 cm from the chest axis.
- `Death_SwordShieldDeath2.fbx`, a copy of `Animation/Chibi/SwordShield/sword and shield death (2).fbx`.
  It is a 3.9 s collapse that starts from standing and ends lying forward. Rejected: Mixamo
  *Falling Flat Impact* from the local inbox, which starts mid-fall (Birdy would pop 0.8 m up
  before landing); and *sword and shield death*, whose end pose sinks 0.37 m against 0.21 m.

There are no talk fallbacks, so the talk states play the main idle sped up (Talk ×1.2,
Excited ×1.5) or slowed (Thinking ×0.8) until real talk clips land. Only the first clip per
talk emote and the first death clip are used.

## 3. How to tune

`ScriptableObjects/Npc/NpcAnimationConfig.asset` (seeded once; hand edits survive):

| Field | Unit | Feel |
|---|---|---|
| `turnRate` | 1/s | How fast the NPC turns to the player (6 ≈ settled in half a second) |
| `maxTurnSpeed` | °/s | Caps the start of a big turn so it pivots instead of snapping; 0 = no cap |
| `returnToRestFacing` | bool | Turn back to the placed facing after talking |
| `restReturnDelay` | s (game time) | Pause before turning back |
| `restTurnRate` | 1/s | Speed of the turn back (slower than the turn in) |
| `minGestureSeconds` | s (unscaled) | Shortest gesture per line, even if the line is skipped |
| `mainIdleChance` | 0–1 | Chance the main idle repeats instead of a fidget |
| `talkBlend`, `settleBlend`, `idleBlend`, `deathBlend` | s | Cross-fades baked into the controller |
| `idleExitTime` | normalised | Where in an idle loop the next variant may start |
| `fallbackTalkSpeed`, `fallbackExcitedSpeed`, `fallbackThinkingSpeed` | × | Idle playback speed in a talk state with no talk clip |

The blend, exit-time and fallback-speed fields are baked into the controller: run
**Rebuild Birdy Animator** after changing them. The rest are read live.

In `BirdyNpcSetup` (code constants, since they define the asset): `TargetHeight` (1.15 m),
`MaxHealth` (40), `ModelYOffset` (0.045 m: lifts the fallback idle's soles, which sank 4.6 cm,
onto the ground), and the placement clearances. Change them and re-run **Setup Birdy NPC**. The
prefab's own Health and capsule values are rewritten on every run. When real clips land, run
**Diagnostics ▸ Report Birdy Poses** and re-check `ModelYOffset` against the idle's "soles" value.

Content: edit `ScriptableObjects/Dialogue/Birdy_Intro.asset` directly. It is seeded once and
never overwritten.

## 4. How to extend

**Add clips to Birdy (the Mixamo scout's path):**
1. Download from Mixamo as *FBX for Unity*, *Without Skin*, 30 fps.
2. Save into `Assets/_Project/Animation/Birdy/` as `Idle_<Name>.fbx`, `Talk_Talk_<Name>.fbx`,
   `Talk_Excited_<Name>.fbx`, `Talk_Thinking_<Name>.fbx` or `Death_<Name>.fbx`.
3. Run **Tools ▸ Pickleball ▸ Rebuild Birdy Animator** (or Setup Birdy NPC). A role that now has
   a real clip stops using its fallback. Check with **Diagnostics ▸ Report Birdy Poses**.

**Add another NPC in 7 steps:**
1. Put its model in `Art/Characters/<Name>/` (a Tripo humanoid takes the same bone map).
2. Copy `BirdyNpcSetup.cs` to `<Name>NpcSetup.cs`; change the paths, `InstanceName`,
   `TargetHeight`, `MaxHealth` and the menu names. (When a third NPC arrives, lift the shared
   steps into a helper instead of copying again.)
3. Put its clips in `Animation/<Name>/` with the role prefixes above; add fallbacks if wanted.
4. Write its dialogue: let the menu seed `ScriptableObjects/Dialogue/<Name>_<Topic>.asset`, then
   edit the asset.
5. Reuse `NpcAnimationConfig.asset`, or create a second config for a different temperament and
   point the menu at it.
6. Choose where it stands: change the placement candidates, or skip placement and drop the prefab
   by hand.
7. Run **Setup Dialogue** first (it creates `TalkableSet.asset`), then the new menu.

No runtime code changes are needed: `NpcAnimatorDriver`, `NpcBody` and the controller layout are
generic.

**A new emote** (a new `DialogueEmote` value): the controller grows a `Talk_<Emote>` state on the
next rebuild. Add a `NpcClipRole` and a prefix in `NpcClipRoles` so it can have its own clip;
until then it falls back to Talk.

## 5. Known limitations and open questions

- **Not play-tested.** Everything here compiled and passed EditMode tests in a headless worktree,
  and the setup ran headless against ChibiSandbox. Nothing has been looked at in the Editor or
  in Play mode: how Mixamo gestures read on the wings, whether the idle's arm swing clips the
  wings into the body, and where the death lands.
- **Killing Birdy pays XP.** `CombatXpSource` pays any `Health`: 40 HP × 1.5 plus the 250
  defeat bonus is 310 Lemmas, which jumps a new player to level 3. Excluding friendly NPCs needs
  a change in the combat system (`CombatXpSource`), which this system doesn't own.
- **Birdy is a lock-on target in the Newton fight.** `LockOnTargeting` takes any live `Health`,
  so Tab can pick Birdy, and when Newton dies the lock hands over to Birdy if Birdy is on screen.
  Placement keeps Birdy 8 m or more from Newton, behind the spawn's view relative to him, which
  lowers the chance but doesn't remove it.
- **No hit reaction.** A sword hit only shows the sword's own flash and hitstop. No flinch clip,
  no damage numbers, no health bar. The boss bar and `DamageNumbers` are wired by Setup Newton
  Fight to exactly Newton and the player, and rewritten on every run of that menu, so adding
  Birdy there would vanish. They would also frame a friendly NPC as an enemy.
- **One clip per talk emote and one death.** Extra `Talk_*` or `Death*` clips are ignored, not
  randomised.
- **Mixamo deaths sink into the floor on Birdy.** The clips are made for a thin humanoid. Birdy's
  big head and its rotor, which points sideways when lying down, end up to 0.21 m below the ground
  with the fallback death. A clip `heightOffset` would lift the standing start too, so it is left
  alone. A bird-specific death (or a lift ramped in over the fall) would fix it.
- **The controller rebuild is not byte-stable.** Setup Birdy NPC skips the rebuild when the
  inputs' hash (kept in the controller's `.meta` userData) matches, so an unchanged re-run has no
  diff. **Rebuild Birdy Animator** always rewrites the controller, giving every state new file IDs
  but the same states, parameters and transitions. Nothing references the states by file ID.
- **Where Birdy lands in ChibiSandbox has only been computed, not looked at.** Headless, the
  placement chose (3.87, -1.48, 4.86): 8.1 m from the spawn, 50° off the starting camera's view,
  on ground about 0.9 m below the spawn, 3 m from `TetherAnchor_3`. Closer spots were too near
  the tether anchors or pickups, or not flat. If it reads badly, move the instance by hand: a
  re-run keeps an existing instance where it is.
- **Rest facing is captured in `OnEnable`.** A script reload mid-conversation makes the facing at
  that moment the new rest facing.
- **Single-NPC setup script.** The import, rig, clip and prefab steps are generic but live in
  `BirdyNpcSetup`. Extract them when a second NPC arrives.
