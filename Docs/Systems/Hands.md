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

**The chibi's hand can't close.** Measured on 2026-10-28 in the open scene:

- The Tripo auto-rig has 41 bones and **no finger bones**. `R_Hand` is a leaf bone, and the
  avatar maps 22 humanoid bones with no finger mappings. Newton uses the same skeleton.
- The hand is one skinned block on `R_Hand`: 516 vertices weighted to it at ≥ 0.5. In hand
  space at bind pose it runs 17 cm from the wrist. The palm is about 9 cm thick, the
  finger part about 4 cm, and the thumb sticks out forward and toward the palm.
- The clips are not the problem. All 24 clips on the chibi controller (locomotion, gesture and
  combo) carry 20 right-hand finger muscle curves (`RightHand.Index.1 Stretched` and so on),
  and the `UpperBody` mask on the Gesture and Attack layers passes `RightFingers` through.
  Those curves have no bones to drive.

So on the chibi the grab point goes in the centre of the hand's volume, and the hand mesh
itself encloses the handle: only the knob end shows on the little-finger side and the blade on
the thumb side. On a rig with finger bones the same code puts the grab point in front of the
palm and `FingerCurl` closes the fingers round it.

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
  - Chibi: (−0.002, 0.059, 0.031) m in `R_Hand` space, the middle of the 9 cm-thick palm, from
    325 triangles enclosing 471 cm³. Thresholds of 0.3 and 0.7 move it by under 0.7 cm.
- **Position with fingers** (`TrySolveClosedFist` → `FistCentre`): knuckle centre + palm ×
  (palm half-thickness + grip radius) − knuckles × (finger half-thickness + grip radius). The
  handle rests on the palm on one side and on the curled first finger segments on the other.
  Thicknesses come from the palm and proximal-finger skin.
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
turned toward the palm about the handle axis (the thumb about the knuckle axis), then blended
with the animated pose by the profile's weight. Each joint's axis is expressed in its own rest
frame, so the bends add up along the finger about one axis (`FingerCurlMath`). Building from
rest rather than adding to the clip means a clip that already curls can't push the fingers
through the palm.

It runs at `DefaultExecutionOrder(-75)`, after SwingTwist (-100) and before WeaponClearance (-50).
It only turns finger bones, which neither reads, and the grab point hangs off the hand bone,
so the blade and its markers never move with the curl.

The setup adds `FingerCurl` only when the avatar maps finger bones on that hand. It removes a
stale one otherwise and logs why.

### Hot reload

- `FingerCurl` rebuilds its chains in `OnEnable`.
- `HandGrip` serializes the held item and re-seats it in `OnEnable`.
- Nothing runtime-built is kept in a `readonly` field.

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

**Give the chibi a closing fist:** the rig needs finger bones. Two options:

- **A Blender pass** on the Tripo FBX: add finger chains, weight the hand's finger block to
  them, and map them in `ChibiSandboxSetup.BoneMap`. The existing Mixamo finger curves would
  then drive them too.
- **Generated joints**, the way `ClothSetup` generates skirt chains: proximal and middle joints
  across the finger block and a thumb joint, the hand vertices re-weighted onto them, and a
  mesh copy swapped into the renderer.
  - It would have to run after Setup Cloth, which replaces the renderer's mesh and bone list.

Either way, re-run Setup Weapon Grip. It then puts the grab point in front of the palm and adds
`FingerCurl`.

## 5. Known limitations

- **The chibi's hand stays open.** No finger bones (see §1). The grab point compensates by
  sitting inside the hand, but the fingers don't visibly wrap.
- **`FingerCurl` is untested on a real finger rig.** Its maths has EditMode tests, but no rig
  in the project has finger bones. Expect a tuning pass on the curl angles, especially the
  thumb, the first time one does.
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
- **The grip change moved the blade:**
  - BladeBase moved 3.9 cm toward the knuckles and 1 cm toward the little finger.
  - The blade now runs 0.78 m from the fist, not 0.85 m from the knob end.
  - Its direction turned 5° and its roll is unchanged.
  - WeaponClearance corrects every frame, so it still works, but its measured table in
    `WeaponClearance.md` predates this.
