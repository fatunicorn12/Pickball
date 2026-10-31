# Cutscene characters (opening cutscenes, workstream C)

The stand-ins, clips, faces, mouth flap and propeller prop for the opening cutscenes
(`Docs/Story/OpeningCutscenes_ShotList.md`). Everything here is built by two setup menus and
is swappable: shots bind to a `CutsceneActor` slot and its named anchors, never to a model.

- **Code:** `Scripts/Cutscene/Actors/` (runtime, `Pickleball.Cutscene.Actors`) and
  `Scripts/Cutscene/Editor/Actors/` (builders, `Pickleball.Cutscene.Editor.Actors`).
- **Assets:** `Prefabs/Cutscene/Actors/{MC_Actor,Birdy_Actor,Propeller}.prefab`,
  `ScriptableObjects/Cutscene/Actors/` (action libraries and configs), `Animation/Cutscene/{MC,Birdy}/`,
  `Art/Cutscene/Characters/` (propeller mesh, eye variants).
- **Blender:** `Tools/Blender/cutscene_extract_propeller.py`, `Tools/Blender/cutscene_paint_eyes.py`
  (run once, outputs committed; the menus never call Blender).
- **Tests:** `Tests/EditMode/Cutscene/Actors/` (26 cases; prefab, library and root-motion suites are category `Assets`).

## Menus (run order)

| Menu | What it does |
|---|---|
| **Tools ▸ Pickleball ▸ Cutscene ▸ 1 Build Actors** (`CutsceneActorSetup.BuildActors`) | Importer settings (propeller scale = Birdy's, material → `Birdy.mat`; eye variants copy the originals' texture settings; Mixamo copy → Humanoid, root baked); seeds `PropellerConfig`, `MC_MouthFlap`, `Birdy_MouthFlap` once; bakes every hand-keyed clip; builds the three prefabs (edited in place with `LoadPrefabContents`, GUIDs kept); fills both action libraries. ~4 s. |
| **… ▸ 4b Rig Scene Actors** (`CutsceneActorSetup.RigSceneActors`) | In the open scene: the gameplay Player (found by its `MovementMotor`) gets a `CutsceneActor` (id `MC`), anchors, `Voice`, `MouthFlap`, `EyeSwap`; every other `CutsceneActor` (placed `MC_Actor` / `Birdy_Actor`) is re-rigged. Marks the scene dirty, never saves. |

Both were run twice in a row: the second run of menu 1 left no file diff, and 4b's second run
gave identical anchors and sizes.

## The slot (`ActorRigger`)

Model-agnostic; works for any Humanoid.

| Anchor | Parent | Where |
|---|---|---|
| `Head` | head bone | centre of the bounding box of skin weighted ≥ 0.5 to the head bone (hair included) |
| `Eyes` | head bone | on the face surface, at eye height = head-bone height × a per-model ratio (chibi 1.089, Birdy 1.172, else 1.10), front-most head skin within ±2.5 cm of the centre line |
| `Hand_R` | right hand | with finger bones: midway wrist → middle-finger knuckle (MC's palm); without: the skin point furthest along forearm → hand (Birdy's wing tip) |
| `Feet` | actor root | on the root's ground, offset like the feet midpoint is from the hips at bind (the blocking mark; it doesn't follow a lying body) |
| `PropellerSocket` | head bone | the `Rotor` bone's rest pose (mast foot, +Y = spin axis); only when the model has a `Rotor` bone |
| `Blade` (MC only) | `R_Hand/WeaponSocket` | the compass-blade's guard: the grip anchor + its +Y × the handle's held-end distance (WeaponSetup names, read-only). On the standalone `MC_Actor` there is no blade, so `Blade` sits at `Hand_R` |

- **Rest pose from bind data.** Every point is read from the skinned meshes' bind poses, not the
  current bone pose, so a Player saved mid-animation measures like the asset. Sizes are relative
  to the bind pose's lowest skin point (the sandbox Player's cloth mesh has its bind frame 0.64 m
  off its root; root-relative heights were wrong by that much).
- **Facing** comes from the shoulders at bind (a raw Tripo guardian FBX faces −Z; every humanoid
  clip turns the body to the actor's +Z anyway).
- **Measured:** MC H **1.697 m**, HB **0.574 m** (head centre 1.409, eyes 1.252: the hair puts the
  head centre 16 cm above the eyes, so aim MC close-ups at `Eyes`). Birdy h **1.112 m**,
  HB **0.280 m**: since review round 1, HB is the height of the skin weighted to the head bone (the
  visible head); the old rule (twice the head centre's distance to the model top) counted the
  propeller mast and gave 0.529 m. The gameplay Player measures exactly like `MC_Actor`.
- **Voice:** a `Voice` child with a 2D AudioSource (`spatialBlend 0`, no doppler, priority 64,
  not playing on awake). Cutscene dialogue must read the same in a 15 m wide shot as in a
  close-up, and hard cuts would make a 3D voice jump in level and pan.

`MC_Actor`: root (`CutsceneActor`, `MouthFlap`) → `ChibiModel` (chibi_unity.fbx, Animator with the
chibi avatar, no controller, root motion off, Always Animate, `EyeSwap`) + `Voice` + `Feet`.
No cloak simulation on the standalone actor (`ClothSetup` writes shared mesh and config assets
this workstream doesn't own); the gameplay Player, which plays MC in every scene, has it.

`Birdy_Actor`: the same with `BirdyModel` (Birdy.fbx, at y 0.008 like `Birdy.prefab`'s model
offset; the FBX's material remap gives it `Birdy.mat`, so it matches `Birdy.prefab`'s look) and
`CutsceneRotor` on the model. Nothing from `Birdy.prefab`'s gameplay (Health, Talkable, NpcRotor)
is copied.

### Birdy's bones

40 transforms in Birdy.fbx plus `Rotor` (added under `Head` on import by
`BirdyModelPostprocessor`). The 22 humanoid-mapped ones:

| Human | Bone | Human | Bone |
|---|---|---|---|
| Hips | `Hip` | Spine / Chest / UpperChest | `Waist` / `Spine01` / `Spine02` |
| Neck / Head | `NeckTwist01` / `Head` | L/R Shoulder | `L_Clavicle` / `R_Clavicle` |
| L/R UpperArm | `L_Upperarm` / `R_Upperarm` | L/R LowerArm | `L_Forearm` / `R_Forearm` |
| L/R Hand | `L_Hand` / `R_Hand` | L/R UpperLeg | `L_Thigh` / `R_Thigh` |
| L/R LowerLeg | `L_Calf` / `R_Calf` | L/R Foot | `L_Foot` / `R_Foot` |
| L/R Toes | `L_ToeBase` / `R_ToeBase` | | |

Unmapped: `Root`, `Pelvis`, `NeckTwist02`, and the twist bones `L/R_ThighTwist01/02`,
`L/R_CalfTwist01/02`, `L/R_UpperarmTwist01/02`, `L/R_ForearmTwist01/02`. No jaw, eye or finger
bones (so no jaw for the mouth flap). The wings are the arm chain.

## The propeller

- `Tools/Blender/cutscene_extract_propeller.py` takes part `tripo_part_1` out of Birdy.fbx into
  `Art/Cutscene/Characters/Birdy/BirdyPropeller.fbx`, in the `Rotor` bone's own frame: the
  pivot is on the hub axis at the foot of the mast (the postprocessor's rule), +Y is the spin
  axis (normal of the blade disc, 16° off world up at bind), +Z Birdy's forward. Blade radius
  0.355 Blender units × Birdy's import scale 1.166 = 0.41 m (the postprocessor reports 0.42).
- `Propeller.prefab`: root (`PropellerProp`) → `Spin` (turns about +Y) → `Model`.
  `AttachTo(actor, PropellerSocket | HandR)` parents with an identity pose, so seated on Birdy it
  covers his skinned propeller exactly (checked by render). `SpinTo(°/s)`, `SpinDown()` (quadratic
  wind-down over `PropellerConfig.spinDownSeconds`), `Detach()`.
- `CutsceneRotor` (Birdy's own skinned propeller): `SetSpin(°/s)` eased by `spinResponse`;
  `SetAttached(false)` scales the `Rotor` bone to zero so only the loose prop shows. Rest pose is
  serialized by the menu. Both run in LateUpdate in Play mode only.
- `PropellerConfig`: idle 180 °/s, hover 1440, rev 2520, response 4/s, wind-down 1.6 s.

## Clips

**MC clips are all Humanoid; so are all of Birdy's (wings = arms). No Birdy verb failed to retarget.**

Sources, in the brief's order:
1. **Existing project clips:** `Animation/Chibi/Walking.fbx` (walk), `Animation/Chibi/Idle.fbx`
   (idle stand), `Animation/Birdy/Idle_LookAround.fbx` (MC's look around; a Mixamo humanoid).
2. **Mixamo inbox:** `Hard Landing.fbx` (Mid-Air Falling To A Hard Landing) copied to
   `Animation/Cutscene/Birdy/`, imported Humanoid with its own Mixamo avatar (*Copy From Other
   Avatar* can't apply: the bone names differ). It is Birdy's crash landing and scramble up.
   Nothing else in the inbox fit (Standing Up is from the stomach; MC lies on his back).

   **The libraries never point at these sources.** Each one is baked by menu 1 into a
   cutscene-owned **in-place copy** (`MC_Walk`, `MC_Idle`, `MC_LookAround`, `B_HardLanding`):
   the source's own pose curves played through, net XZ travel removed, root rotation, height and
   XZ baked into the pose (`CutsceneClipCatalog.InPlaceCopy`). The gameplay walk carries 1.15 m of
   root travel per cycle, which the Timeline Animation track applied on top of the marks track
   (the OC_S4 bug: MC's body ran twice as far as his marks). The sources and their `.meta` files
   are never written. `RootMotionTests` samples every library clip on its model and fails if the
   root moves.
3. **Hand-keyed:** 23 MC and 31 Birdy clips.

**Deviation: hand-keyed in Unity muscle space, not in Blender.** The specs
(`McClips.cs`, `BirdyClips.cs`) are keyed in Unity's humanoid muscle space and baked by
`PoseClipBaker` into `.anim` files. A muscle clip is Humanoid by construction: it plays on MC,
Birdy and any swapped-in humanoid with no retarget step, menu 1 regenerates it byte-identically,
and each pose stays readable as code ("right arm up, fist"). A Blender armature pass would have
needed a per-rig export and re-import for the same result. How it bakes:
- each frame (30 fps) it reads the base clip's own curves (`HumanClipCurves`), applies the keys
  (absolute muscles, deltas, body rotation, offset), runs an optional procedural layer (bob,
  flap, wobble), and pulls the lowest skin point onto y = 0 where the clip is grounded;
- root rotation, height and XZ are baked into the pose: the clip never moves the actor, the
  timeline does;
- sign conventions were measured on the chibi (`HumanMuscles`, `PoseKit`): head turn + is left,
  tilt + right, spine twist + right, arm front-back − forward. Bending the spine alone swings the
  legs (the body rotation is the centre-of-mass frame), so `Bend` counter-pitches the body by 55%.

### Verb → clip (both libraries; spelling = §6 first cell, parenthetical removed, split on " / ", lower case)

MC (`MC_ActionLibrary`, idle verb `idle stand`):

| Verb | Clip | s | Loop | Notes |
|---|---|---|---|---|
| lie still | `MC_LieStill` | 4.00 | loop | on his back, spread-eagle (legs In-Out 1.0, so they show beside the cloak top-down), head toward the actor's +Z; a breath. Pair with EyeSwap `Closed` |
| eyes open | `MC_EyesOpen` | 2.00 | hold | lying; small head roll and finger twitch; switch EyeSwap `Closed` → original |
| sit up | `MC_SitUp` | 3.20 | hold | ends sitting **facing the actor's −Z** (toward his feet): yaw the actor 180° at the cut to the stand |
| look around | `MC_LookAround` | 7.83 | loop | in-place copy of Birdy's `Idle_LookAround`, standing; the sit-up's end already looks aside |
| fist pump | `MC_FistPump` | 1.80 | hold | opens near the top of the spring (fist overhead, 0.28 m up), lands, yanks the fist to his chest |
| reach out | `MC_ReachOut` | 3.00 | hold | arm out, index pointing; "poke air" jabs at 1.4 s and 1.8 s; holds |
| lower arm | `MC_LowerArm` | 1.00 | hold | |
| glance to lens | `MC_GlanceToLens` | 1.50 | hold | head 25° to his right, a touch down: aim the actor so his right is the lens |
| turn | `MC_Turn` | 1.00 | loop | stepping in place; the yaw is the timeline's |
| look up | `MC_LookUp` | 1.20 | hold | |
| flinch | `MC_Flinch` | 1.40 | hold | forearms up over the face, recovers |
| look off | `MC_LookOff` | 1.20 | hold | to his left |
| sigh shrug | `MC_SighShrug` | 2.00 | hold | |
| flat stare | `MC_FlatStare` | 8.33 | loop | breathing idle + level head (base + head offset) |
| hold out | `MC_HoldOut` | 1.20 | hold | right palm up in front: seat props on `Hand_R` |
| turn away | `MC_TurnAway` | 1.20 | hold | 180° body turn baked in; the actor doesn't rotate |
| confused head tilt | `MC_ConfusedTilt` | 1.00 | hold | base + head offset |
| resigned mutter | `MC_ResignedMutter` | 2.00 | hold | eye-roll then look aside |
| walk | `MC_Walk` | 1.03 | loop | in-place copy of the gameplay walk |
| walk relaxed | `MC_WalkRelaxed` | 1.03 | loop | the walk, in place, hands behind the head |
| stop | `MC_Stop` | 0.90 | hold | |
| awed look | `MC_AwedLook` | 2.00 | hold | base idle + head offset (chin up, chest open) |
| serious frown | `MC_SeriousFrown` | 1.20 | hold | base idle + head offset (chin down, shoulders squared) |
| touch hilt | `MC_TouchHilt` | 1.60 | hold | right hand up at the waist, left closes over it, looks down |
| idle stand | `MC_Idle` | 8.33 | loop | in-place copy of the gameplay idle |
| idle look | `MC_IdleLook` | 8.33 | loop | idle + gaze to the horizon |

Birdy (`Birdy_ActionLibrary`, idle verb `hover idle`). Hover clips aren't grounded: put his
Head at about MC's eye line (shot list: MC.Feet + 0.80·H); the clip only bobs.

| Verb | Clip | s | Loop | Ground | Notes |
|---|---|---|---|---|---|
| crash tumble | `B_CrashTumble` | 0.90 | loop | air | one somersault per loop, flailing |
| crash tumble and bounce, scramble up | `B_HardLanding` | 2.50 | hold | yes | Mixamo; lands in a crouch and rises (no literal bounce: add a small hop on the actor if wanted). Alias `crash tumble and bounce` |
| panic circling | `B_PanicCircling` | 0.80 | loop | yes | hopping, wing tips patting the helmet; the circle is the timeline's path |
| close lean-in | `B_CloseLeanIn` | 1.40 | hold | yes | |
| plead | `B_Plead` | 1.00 | loop | yes | |
| point up | `B_PointUp` | 1.00 | hold | yes | |
| eager bounce | `B_EagerBounce` | 0.60 | loop | yes | |
| tiny wing spread | `B_TinyWingSpread` | 1.00 | hold | yes | |
| sad droop | `B_SadDroop` | 1.20 | hold | yes | standing; for the hovering S3_SH13 use `deflate` |
| snatch | `B_Snatch` | 0.80 | hold | yes | |
| propeller click | `B_PropellerClick` | 1.20 | hold | yes | |
| hover wobble | `B_HoverWobble` | 1.20 | loop | air | |
| overjoyed | `B_Overjoyed` | 1.20 | loop | air | |
| proud loop | `B_ProudLoop` | 1.00 | loop | air | banked; the lap is the timeline's path |
| stare | `B_Stare` | 1.00 | hold | air | |
| awed | `B_Awed` | 2.00 | loop | air | |
| excited flap | `B_ExcitedFlap` | 0.60 | loop | air | |
| deflate, losing steam | `B_Deflate` | 1.50 | hold | air | |
| grand gesture | `B_GrandGesture` | 1.50 | hold | air | |
| perky recovery | `B_PerkyRecovery` | 0.80 | hold | air | |
| putter hover | `B_PutterHover` | 1.60 | loop | air | |
| confused head tilt | `B_ConfusedTilt` | 1.00 | hold | air | hover base + head offset |
| chuckle | `B_Chuckle` | 1.20 | hold | air | |
| earnest nod | `B_EarnestNod` | 1.20 | hold | air | hover base + head offset |
| beam | `B_Beam` | 1.50 | hold | air | |
| zip in | `B_ZipIn` | 0.80 | hold | air | |
| tour-guide gesture | `B_TourGuide` | 3.00 | hold | air | present 0–0.9 s, count 1.2–1.8 s, point 2.3 s on |
| cheer slip | `B_CheerSlip` | 1.50 | hold | air | slips at 0.3–0.9 s; pair with EyeSwap `Drop` and a `CutsceneRotor.SetSpin` dip |
| point at mc | `B_PointAtMC` | 1.00 | hold | air | points straight ahead: face him at MC |
| warm lean | `B_WarmLean` | 1.50 | hold | air | left wing out and up (MC is on his left when he hovers at MC's right shoulder) |
| zip-off | `B_ZipOff` | 1.00 | hold | air | holds the dart; spin the rotor to `revSpin` |
| hover idle | `B_HoverIdle` | 1.60 | loop | air | the default from S3_SH03 on |

**Missing list: none** (`CutsceneClipCatalog.McMissing` / `BirdyMissing` are empty; the coverage
test parses §6 and fails on any verb that is neither mapped nor listed). Blend-in is 0.25 s for
hand-keyed clips, 0.3 s for the idles, 0.1 s for the crash landing. "Hold" clips have Loop Time
off: hold their last frame with the timeline clip's post-extrapolation.

Props (shot list §6 Props) are code-driven: `PropellerProp` / `CutsceneRotor` above; the blade
glint is VFX (not this workstream).

## Faces

Neither rig has blendshapes, a jaw or eye bones. No facial rig was added.

- **Head and neck pose** carry expression in every clip (the "base + head offset" variants above).
- **EyeSwap** (on the model, `ExecuteAlways`) swaps `_BaseMap` through a MaterialPropertyBlock
  (read back first, so the hurt flash's block survives; no material instances). Variant 0 is the
  original; animate the float `variant` or call `Show("Closed")` / `Show("Drop")`.
  - **MC `Closed`** (`Chibi_EyesClosed.jpg`): S1_SH02's eyes opening is `Closed` → 0.
  - **Birdy `Drop`** (`Birdy_EyesDrop.jpg`): lids lowered, sagging to the outer corners, for the cheer slip.
- **How they were painted** (`Tools/Blender/cutscene_paint_eyes.py`): Tripo atlases scatter the
  face over many tiny UV islands, so each front-facing triangle near an eye is rasterised in UV
  space and every texel is mapped back to its 3D point; the paint is decided in a front
  orthographic frame where the eye ellipses were read off renders. Texels more than 6 mm in front
  of the eye surface (a hair strand crossing MC's right eye) are left alone. Copies only; the
  originals are untouched.
- **Result:** both read clearly in close-up renders (closed lids with a lash flick on MC; heavy,
  sad lids on Birdy). Kept. Limits: the edits are flat colour (no painted texture), and Birdy's lid
  colour is a muted cream.
- **Mouth flap** (`MouthFlap` on the actor root, `MouthFlapMath`, `MouthFlapConfig`): each
  LateUpdate while the voice plays, `GetOutputData` into a buffer allocated in OnEnable, RMS,
  noise floor → full-open mapping, attack/release smoothing, then a nod about the actor's right
  axis on top of the Animator's pose (the head bone, since neither rig has a jaw; a rig with a
  humanoid Jaw gets the jaw). Undoes its own previous nod if nothing re-posed the bone. Inert
  while nothing plays. MC: 6° at full open, attack 30/s, release 12/s; Birdy: 9°, attack 40/s.

## Swapping a model

Verified on `MC_Actor` with the Tripo guardian **Euler** (`Art/Tripo/Characters/Euler/Euler.fbx`,
a Mixamo-rigged humanoid):
1. In the scene, unpack the `MC_Actor` instance, delete `ChibiModel`, drop the new humanoid FBX
   under the root (any name; it needs an Animator with a humanoid avatar).
2. Run **4b Rig Scene Actors**.

Result: anchors moved onto `mixamorig:Head` / `mixamorig:RightHand`, H 1.697 → **2.100 m**,
HB 0.574 → **0.748 m**, the mouth flap moved to `mixamorig:Head`, `EyeSwap` was removed (no
painted variant for Euler's texture), a second 4b run changed nothing, and `MC_FistPump` played on
him facing +Z. A raw Tripo FBX faces −Z at rest; the slot measures its facing from the shoulders,
and clips turn the body to +Z anyway. For the gameplay Player the swap is the same: replace the
model under the Player, run 4b.

## For the pipeline

- Library verbs are lower case and follow the spelling rule above (`CutsceneVerbs.FromCell`).
- No library clip moves the root (all are cutscene-owned with root motion baked into the pose; `RootMotionTests` checks it); move or rotate the actor.
- Timeline can drive `CutsceneRotor.targetSpin` / `attached`, `EyeSwap.variant` and
  `PropellerProp.targetSpin` as serialized fields, or call their methods from a playable.
- `MC_SitUp` ends facing the actor's −Z; `MC_TurnAway` bakes a 180° turn. The timeline places MC
  turned 180° on MC_Start at S1_SH04 and turns him back north at S1_SH07's 4.0–5.5 s.
  `MC_LookAround` (standing) is no longer played inside S1_SH03's sit-up.
- `Diagnostics`: `ActorPoseSheet.Render` renders contact sheets off-scene (preview scene), with
  anchor markers drawn on top.

## Known limitations

- Blocking quality: no contact IK, no finger animation beyond fist/point/open, no facial animation
  beyond the two texture variants.
- Birdy's head nod range is small, so `sad droop` reads mostly from the wings and body.
- `MC_LieStill`'s grounding uses the lowest skin point, which is the cloak, so the body rests a
  few centimetres up on it.
- `MC_Actor` has no cloak simulation (see above).
