# Weapon Clearance

Keeps the player's sword out of their own head in every animation.

Code: `Assets/_Project/Scripts/Sandbox/Weapon/` (`WeaponClearance`, `ClearanceSolver`,
`WeaponClearanceProfile`). Setup: `Editor/WeaponClearanceSetup.cs`. Tests:
`Tests/EditMode/Weapon/ClearanceSolverTests.cs`.

## 1. Purpose and feel

The player's animations are Mixamo clips authored on an adult-proportioned body. The chibi's
head is about 0.7 m across with hair included, so a 0.85 m blade held where those clips hold it
cuts through the hair.

Measured on 2026-10-26 by sampling every clip on the controller 48 times. The test is exact
against the head mesh: the gap is the distance from the blade's surface (centre line minus
2 cm) to the nearest head vertex. Negative means the blade is inside the hair.

| Clip | Off: frames in hair, min gap | On: frames in hair, min gap | Max arm turn |
|---|---|---|---|
| idle | 0, 4.8 cm | 0, 9.0 cm | 11° |
| walk | 12, −0.5 cm | 0, 7.1 cm | 12° |
| run | **48 of 48**, −1.9 cm | 0, 9.4 cm | 32° |
| crouch walk | 4, 0.0 cm | 0, 1.8 cm | 18° |
| jump / running jump | 6 / 3, −2.0 cm | 0 / 0, 4.0 cm | 22° / 42° |
| stop | 0, 6.7 cm | 0, 11.5 cm | 16° |
| front flip / flip air | 12 / 11, −1.9 cm | **3 / 3**, −1.8 cm | 63° / 70° |
| tethered | 0, 1.9 cm | 0, 2.7 cm | 4° |
| throw (Gesture layer) | 15, −2.0 cm | 0, 3.4 cm | 18° |
| Attack1 / 2 / 3 / 4 | 4 / 6 / 8 / 8 | **1** / 0 / **5** / 0 | 70° / 36° / 57° / 51° |
| AirAttack | 22, −1.7 cm | **1**, −0.1 cm | 48° |
| fall, land, dash, skate, crouch idle | 0 | 0 | 0° |

Even the sampled idle comes within 5 cm of the hair. In Play mode it gets closer: the lock-on
head turn, the idle/walk blend and the Attack layer fading out all move the head or the arm
after the clip, and a live 10 s idle came within 2 cm of the head sphere. Because the clearance
pass runs after all of those, it covers them too.

With clearance on, the arm lifts just enough to carry the blade past the head, then lets go
again as soon as the clip clears by itself. When nothing is in the way, the correction is
exactly zero, so clips that were already fine are left untouched. The remaining frames in bold
are those where the hand itself is in the hair (see Known limitations).

## 2. Architecture

```
Animator (base, Gesture, Attack layers)
  └─ OnAnimatorIK: ChibiFootIK (feet, head look-at)
LateUpdate, in execution order:
  -100  SwingTwist         restores the swings' hip turn (moves the arm)
   -50  WeaponClearance    bends shoulder + wrist so the blade clears the head
     0  BladeTrail, SpringCloth, MeleeCombat sweep (read BladeBase / BladeTip)
```

- **`ClearanceSolver`** (pure static maths): the smallest turn about the grip that takes a
  straight blade out of a sphere. A blade long enough to reach the tangent point clears at
  `asin(R / D)`; a shorter one only needs its tip on the surface. See the class summary.
- **`WeaponClearance`** (component on `WeaponSocket`): each frame, finds the worst volume,
  splits the turn between the shoulder (`RightUpperArm`) and the wrist (`RightHand`) about one
  world axis, and repeats for `iterations` passes, because turning the shoulder also moves the
  grip. It only moves bones. It raises no events and owns no gameplay state, and nothing
  depends on it. `LastCorrection` reports the turn applied this frame, for debugging.
- **`WeaponClearanceProfile`** (ScriptableObject,
  `ScriptableObjects/Weapon/ChibiWeaponClearance.asset`): the spheres to avoid, the blade's
  half-thickness and margin, the shoulder/wrist split, the per-frame cap and the pass count.

Wiring: `WeaponSetup.AttachToPlayer` rebuilds the socket and then calls
`WeaponClearanceSetup.Attach`. You can also run it alone with
**Tools ▸ Pickleball ▸ Setup Weapon Clearance**.

## 3. How to tune

Open `ChibiWeaponClearance.asset`. Select the WeaponSocket in Play mode to see the gizmos: the
solid sphere is the volume, the faint one adds the pad, and the blade line turns red while the
arm is being corrected.

Shipped values: head sphere centre (−0.008, 0.262, 0.028) in head space, radius 0.373 m (the
99th percentile of head vertices, fitted by the setup), `bladeRadius` 0.02, `margin` 0.06,
`shoulderShare` 0.5, `maxCorrection` 75°, `iterations` 3.

- **Blade still brushes the hair:** raise `margin` (or the head volume's `radius`).
- **Arm looks pushed around in a swing:** lower `margin`. If the wrist kinks, raise
  `shoulderShare`.
- **Wrist looks broken:** raise `shoulderShare` toward 0.7.
- **Correction looks too strong in the flip tuck:** lower `maxCorrection`.

## 4. How to extend

- **Keep the blade out of another body part:** add a volume (bone + local centre + radius).
  To fit a radius, select the bone in Play mode and size the sphere gizmo to the silhouette.
- **New weapon:** nothing to do as long as it has `BladeBase` / `BladeTip` children on the
  socket.
- **New character on another rig:** delete or duplicate the profile, then run Setup Weapon
  Clearance. The head sphere is fitted from that character's skinned mesh.
- **Left-handed wielder:** untick `rightHanded`.

## 5. Known limitations

- Volumes are spheres. The chibi head is close to one (0.69 × 0.57 × 0.68 m), but a long or
  flat part such as a torso would need several spheres.
- The blade is treated as a straight segment from grip to tip. A wide crossguard or a second
  compass leg is covered only by `bladeRadius`.
- When the hand itself is inside the hair (3 frames of the front-flip tuck, 5 of Attack3's
  wind-up, 1 each of Attack1 and AirAttack, at 48 samples per clip), turning about the grip
  can't clear the blade. The push stays finite and capped, so those frames improve but may still
  overlap by up to 2 cm. A fix would be a shoulder-only step that moves the hand out radially
  before the blade is turned.
- Correcting the arm can, in principle, push the blade into the torso or cloak. No volume
  guards the body yet, because the measured clips didn't need one.
