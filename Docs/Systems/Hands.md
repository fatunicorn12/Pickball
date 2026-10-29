# Hands (grab point, held items, finger curl)

How a character holds things: where the grab point on the hand is, how an item seats in
it, and how the fingers close round it.

Code: `Assets/_Project/Scripts/Sandbox/Hands/` (`HandGrip`, `HeldItem`, `FingerCurl`,
`HandGripProfile`, and the pure maths in `GripFrameSolver`, `GripSeat`, `FingerCurlMath`,
`GripFrame`, `HandAxes`). Setup: `Editor/HandGripSetup.cs`, called from `Editor/WeaponSetup.cs`.
Tests: `Tests/EditMode/Hands/`.

## 1. Purpose and feel

The sword should look held: the handle in the fist, the blade coming out between thumb and
index finger, and the fingers wrapped round it through locomotion and every combo swing.

Before this, the weapon socket was a hand-typed "first guess" offset, (0, 0.02, 0.04) and
Euler(0, 90, 100) on `R_Hand`. That put the grip 2 cm from the wrist, and the compass was held
by its very end, so the blade came out of the wrist and its 8 cm knob stuck out of the hand.
Now both sides are measured: the hand from the rig and the weapon from its mesh.

**The chibi's fingers.** Tripo's auto-rig gave the chibi 41 bones and none in the fingers, but
the hands are modelled with separate fingers (a fingerless glove: four fingers apart past the
knuckles, and a thumb). A Blender pass, `Tools/Blender/chibi_add_fingers.py`, adds three bones
per digit on both hands (`R_Thumb1..3`, `R_Index1..3`, `R_Middle1..3`, `R_Ring1..3`,
`R_Pinky1..3`, and the same with `L_`) and skins the fingers to them. `ChibiSandboxSetup` maps
them on the avatar. Measured on 2026-10-28:

- Each finger has three edge loops past the glove: where it leaves the glove, mid-finger, and
  the tip cap. The joints sit between loops (the knuckle at the palm's front edge), so every
  loop moves rigidly with one bone and only the faces between loops stretch.
- The palm is thick for the fingers: 2.6 cm from the knuckle line to the palm surface under
  the knuckles, against fingers about 8 cm long (Unity units). Curled, they wrap the front and
  the underside of a handle held against the palm.
- The thumb points mostly along the fingers at rest, about 30° toward the thumb side and 25°
  toward the palm. That is not the Mixamo T-pose thumb (along the handle), which is why the
  thumb has its own bend axis and a swing (see "How the fingers close").
- All 24 clips on the chibi controller (locomotion, gesture and combo) carry 20 finger muscle
  curves per hand, and the `UpperBody` mask on the Gesture and Attack layers passes
  `RightFingers` through, so the clips drive the fingers whenever the fist isn't closed.

Newton's Tripo rig still has no finger bones. There the grab point goes in the centre of the
hand's volume, and the hand mesh itself encloses the handle.

## 2. Architecture

```
Editor time (Setup Weapon / Setup Weapon Grip)
  HandGripSetup.TrySolve   bind-pose skin + bones ──▶ GripFrameSolver ──▶ WeaponSocket local pose
  WeaponSetup.PrepareHeldItem  compass mesh ──▶ GripSeat.HandleLength ──▶ GripAnchor on the compass
  HandGrip.Hold(compass)   seats the GripAnchor on the WeaponSocket; BladeBase/BladeTip follow

Runtime
  HandGrip      OnEnable: re-seats the held item. Hold / Release raise HoldChanged.
  LateUpdate -75  FingerCurl  (rigs with finger bones only) reads HandGrip.IsHolding,
                               poses the finger bones from rest + curl, blends by weight
```

Scene layout on the chibi:

```
R_Hand
  WeaponSocket      HandGrip, WeaponClearance   ← the grab point
    WeaponVisual    HeldItem                    ← the compass, seated by its anchor
      GripAnchor                                ← middle of the compass's handle
    BladeBase                                   ← centre of the fist
    BladeTip                                    ← far end of the compass
```

The grab point keeps the name `WeaponSocket` because `WeaponClearanceSetup` (it looks for
`WeaponSocket/BladeBase` and `BladeTip` as direct children of the hand) and the Tripo
contact sheet (the sword renderer's parent must be `WeaponSocket`) find it by name.

### Axis convention (`GripFrame`)

Every grab point and every `HeldItem` grip anchor uses the same frame, so any item seats in any
hand by lining the two up:

- **origin:** the centre of the fist, where the middle of the handle sits;
- **+Y:** along the handle, out of the thumb side of the fist (the blade direction);
- **+Z:** the way the knuckles point, so a blade's edge leads with the knuckles and its flat
  faces the palm;
- **+X:** Y × Z. That is out of the back of a right hand and out of the palm of a left hand,
  because a mirror can't be a rotation.

### How the grab point is computed (`GripFrameSolver`)

All inputs are in the hand bone's space at bind pose, taken from the skinned mesh's bind
poses, so the scene's current pose doesn't matter (the same approach as the clearance setup's
head fit).

- **Hand axes** (`TryHandAxes`): the knuckle direction is kept exactly; the thumb side is made
  perpendicular to it; the palm is the cross product, ordered by handedness.
  - With finger joints: knuckles = wrist → mean of the proximal joints; thumb side = index
    proximal − little proximal.
  - Without (Tripo): knuckles = the forearm's direction at bind (elbow → wrist); thumb side =
    the way the character faces, the humanoid T-pose convention (thumbs forward, palms down).
- **Position without fingers** (`TrySolveOpenHand`): the centroid of the volume enclosed by the
  hand's triangles (every corner weighted ≥ 0.5 to the hand), by summing signed tetrahedra
  from the wrist. The hand is open at the wrist, but with the reference on the wrist the
  missing cap adds nothing. Volume rather than a vertex average, because a low-poly mesh puts
  its vertices at detail (fingertips), not bulk (the palm).
  - Chibi before its fingers: (−0.002, 0.059, 0.031) m in `R_Hand` space, the middle of the
    9 cm-thick palm, from 325 triangles enclosing 471 cm³. Newton still uses this path.
- **Position with fingers** (`TrySolveClosedFist` → `FistCentre`): knuckle centre + palm ×
  (palm half-thickness + grip radius) − knuckles × (finger half-thickness + grip radius). The
  handle rests on the palm on one side and on the curled first finger segments on the other.
  Thicknesses come from the palm and proximal-finger skin.
  - The palm's depth is measured only on the strip the handle lies across, from the knuckle
    line back by a finger half-thickness and the handle's width (`PalmUnderHandle`). The
    deepest point of the whole palm is the heel of the hand by the wrist: on the chibi 4.2 cm
    past the knuckles where the palm under them is 2.6 cm. Measuring it put the handle 1.6 cm
    too far out, below the reach of the curled fingers.
  - Chibi: (0.033, 0.049, 0.018) m in `R_Hand` space, from 4 knuckle joints, 161 palm and 91
    finger vertices.
- **Tilt** (`Compose`): optional turn of the handle about +X toward the knuckles, from the
  profile.

### How an item seats (`HeldItem`, `GripSeat`)

`HeldItem.gripAnchor` is a child transform on the item, in the same convention. `HandGrip.Hold`
parents the item to the grab point and sets its local pose so the anchor coincides with the
grab point (`GripSeat.ItemPose`). Scale is never touched.

For the compass, `WeaponSetup.PrepareHeldItem` builds the anchor from the mesh:

- the long axis and the held end (`GripFarEnd`), as before;
- **the handle** (`GripSeat.HandleLength`): the mesh is cut into slices of 2% of its length
  from the held end, and the handle ends at the first slice wider than 1.5 × the widest handle
  slice so far. Compass: 0.136 m (the knob and neck, up to the hinge disc), so the fist closes
  0.068 m in from the end;
- **the roll:** the wider of the two girth axes is the edge (+Z). On the compass that is the
  plane its legs splay in, the same roll as before.

### How the fingers close (`FingerCurl`)

A post-Animator pass, not a fist pose on a masked layer:

- the clips already drive the fingers on three layers, and a fist layer would fight them for
  the fingers depending on layer order;
- it would mean editing the controller, which other setups rebuild;
- it would only work on Humanoid rigs.

Each finger joint is set to its **rest** rotation (from the avatar's skeleton description)
turned toward the palm about the handle axis, then blended with the animated pose by the
profile's weight. Each joint's axis is expressed in its own rest frame, so the bends add up
along the finger about one axis (`FingerCurlMath`). Building from rest rather than adding to the
clip means a clip that already curls can't push the fingers through the palm.

The thumb is different, because rigs disagree on where it points at rest:

- **Bend** (`ThumbFlexAxis`): about the axis across the thumb's own rest direction, in the
  palm's plane. For a Mixamo-style thumb, which points along the handle, that is the knuckle
  axis. The chibi's points along its fingers, and turned about the knuckle axis it only
  rolled about its own length.
- **Swing** (`ThumbSwingAxis`, `Opposed`): the thumb's first joint also turns about the palm's
  normal, toward the little finger's knuckle, by the profile's `thumbSwing`. That lays it across
  the curled fingers, under the handle, instead of hanging beside the index finger.
- The thumb's direction comes from the avatar's rest positions (first joint to second).

It runs at `DefaultExecutionOrder(-75)`, after SwingTwist (-100) and before WeaponClearance (-50).
It only turns finger bones, which neither reads, and the grab point hangs off the hand bone,
so the blade and its markers never move with the curl.

The setup adds `FingerCurl` only when the avatar maps finger bones on that hand. It removes a
stale one otherwise and logs why.

### Hot reload

- `FingerCurl` rebuilds its chains in `OnEnable`.
- `HandGrip` serializes the held item and re-seats it in `OnEnable`.
- Nothing runtime-built is kept in a `readonly` field.

### Prefab instances

In every saved scene the Chibi is an instance of `Chibi.prefab`, so the socket and the compass
belong to the prefab. **Setup Weapon Grip** works in place and never reparents or destroys
anything:

- It measures the compass in its own space through the transform matrices instead of moving it
  to the world origin.
- `HandGrip.Hold` only reparents an item that isn't already on the grab point.
- It records every changed transform and component as an instance override.
- `HandGrip`, `HeldItem` and the `GripAnchor` child become added-component and added-object
  overrides.
- A stale `FingerCurl` that the prefab owns is switched off rather than destroyed.

Re-running it gives the same pose and adds nothing new. That still holds after `Chibi.prefab`
is rebuilt with these objects inside it.

The first version moved the compass to the world origin to measure it. Unity refuses to
reparent a prefab's own object from an instance: it logs "Setting the parent of a transform
which resides in a Prefab instance is not possible" and leaves the object where it was.

Full **Setup Weapon** also works on the instance in Unity 6000.3. It destroys the old socket and
BladeTrail, which Unity records as removed-object overrides, and builds new ones as added
objects. That leaves the scene further from `Chibi.prefab`, so prefer Setup Weapon Grip for a
re-fit.

## 3. How to tune

The asset is `ScriptableObjects/Hands/HandGripProfile.asset`. Setup creates it with defaults
when it's missing and never overwrites it.

- **`handleTilt`** (°, default 0): leans the blade toward the knuckles. A real sword grip
  crosses the palm at roughly 15-20°.
  - It is baked into the grab point, so run **Tools ▸ Pickleball ▸ Setup Weapon Grip** after
    changing it.
  - To explore it in Play mode, rotate `WeaponSocket` about its local X, then copy the angle in.
- **`gripRadius`** (m, default 0.015): the handle radius a closing fist wraps. It only moves the
  grab point on rigs with fingers. Baked, like `handleTilt`.
- **`fingerCurl` / `thumbCurl`** (° per joint): live in Play mode.
  - Too high pushes the fingertips into the palm; too low leaves a claw.
  - Only matters on rigs with finger bones.
- **`thumbSwing`** (°): how far the thumb swings across the palm before it bends. Live in Play
  mode. 0 leaves it beside the index finger.
- **The chibi's values:** `fingerCurl` (85, 100, 60), `thumbCurl` (0, 15, 20), `thumbSwing` 80,
  with the default `gripRadius` 0.015 (the compass's neck and knob are 1.0-1.85 cm in radius
  where the fist closes). More thumb bend than that points the thumb straight down out of the
  fist.
- **Without Unity:** `chibi_add_fingers.py --preview DIR` renders both hands closed round a
  handle with the same maths and any `--curl`, `--thumb-curl`, `--thumb-swing`, as a contact
  sheet per hand. That is how the values above were found.
- **`curlWeight`** (0-1): how fully the fist replaces the clips' finger poses.
- **`closeRate`** (1/s): how fast the fist closes on a grab.

To nudge the compass in the hand, move its `GripAnchor` (Scene view), then use **HandGrip ▸ ⋮ ▸
Re-seat Held Item**. A re-run of Setup Weapon or Setup Weapon Grip re-measures and overwrites
the anchor.

## 4. How to extend

**Hold another prop:**

1. Add `HeldItem` to the prop and a child `GripAnchor` at the middle of its handle: +Y toward
   the business end, +Z toward the edge that should lead.
2. Call `handGrip.Hold(item)`. `Release(newParent)` lets go and keeps the world pose.
3. Listen to `HandGrip.HoldChanged` for anything that cares (VFX, UI).

`FingerCurl` closes the hand by itself. The blade markers (`BladeBase` / `BladeTip`) are the
sword's, not the hand's: a prop that needs them must bring its own.

**Another character:**

- Call `HandGripSetup.TrySolve` and `HandGripSetup.Attach` for its hand bone, from its setup
  script.
- Left hands work: pass `rightHand: false`.

**Re-rig the chibi's fingers** (to move a joint or change the skinning):

1. Change the constants at the top of `Tools/Blender/chibi_add_fingers.py`. Check with
   `--preview` (no `--out`) until the renders look right.
2. Run it on the finger-less FBX from git, writing `chibi_unity.fbx` (the script's docstring
   has the command). It refuses an FBX that already has fingers.
3. In Unity, re-apply the rig: `ChibiSandboxSetup.SetupRigPublic(importer, 1.7)` on the FBX,
   or Setup Chibi Sandbox (the full chain). It re-captures the avatar's reference skeleton if
   the model has bones it lacks, and maps the fingers.
4. Regenerate the cloth inside `Chibi.prefab` (`ClothSetup.Apply` on the prefab contents).
   `Apply` first rebuilds the renderer's bone list from the model, so the new bones land in
   the right slots.
5. Run Tools ▸ Pickleball ▸ Setup Weapon Grip. It puts the grab point in front of the palm and
   adds `FingerCurl`.

**Fingers for another Tripo character** (Newton): the script's measurements are generic, but
its constants were checked on the chibi only. Point it at the other FBX and read the printed
joints and the preview before trusting it. The bone names are the ones `FingerBoneMap` expects.

## 5. Known limitations

- **Short fingers, thick palm.** The chibi's curled fingers wrap the front and underside of
  the handle but can't reach all the way round to the palm, and the thumb's tip hangs a little
  below the fist. Fine at game-camera distance; a close-up would want longer fingers in the
  mesh.
- **Low-poly fingers.** Three edge loops per finger, so a bend is a crease between two rigid
  segments, not a rounded knuckle.
- **The left hand has fingers too**, but nothing is held in it, so it has no `FingerCurl` and
  its fingers only follow the clips. Its knuckle line slopes more than the right's, so an item
  held in it would sit tilted about 15°.
- **The thumb side on finger-less rigs comes from the T-pose convention** (thumbs forward at
  bind). A rig bound in another pose would hold the blade sideways; a rig with fingers gets it
  from the knuckles.
- **The handle measurement is a heuristic:**
  - A weapon that never widens near its held end is gripped at the end.
  - A heavy pommel counts as handle, which moves the grip toward the pommel.
- **Baked, not live:** the grab point's tilt and grip radius need Setup Weapon Grip after a
  change.
- **The grab point is named `WeaponSocket`** for the two name lookups above. A neutral name
  (`GrabPoint`) would need `WeaponClearanceSetup` and `TripoContactSheet` to look it up by
  component instead.
- **The grip changes moved the blade:**
  - The first grab point moved BladeBase 3.9 cm toward the knuckles and 1 cm toward the little
    finger. The blade then ran 0.78 m from the fist, not 0.85 m from the knob end, and its
    direction turned 5° with the roll unchanged.
  - The closed fist moved the grab point again, 3.9 cm from the centre of the hand to in front
    of the palm, and BladeBase with it.
  - WeaponClearance corrects every frame, so it still works, but its measured table in
    `WeaponClearance.md` predates both moves.
