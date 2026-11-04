# Cutscene sets (opening cutscenes, workstream D)

The cutscene-only environments for the opening sequence (`Docs/Story/OpeningCutscenes_ShotList.md`),
built as prefabs by **Tools ▸ Pickleball ▸ Cutscene ▸ 2 Build Sets**
(`Scripts/Cutscene/Editor/Sets/CutsceneSetBuilder.cs`). Painterly, chibi-scaled, golden hour from
the south-west, realtime lighting only.

## 1. What the menu builds

| Prefab (`Prefabs/Cutscene/Sets/`) | Contents | Markers |
|---|---|---|
| `Void` | `CutsceneLook` (the look blend), the white dome (r 40 m) and ground disc round MC_Start | none |
| `OuterShrine` | Meadow and shrine lobe, lawn circle, marble kerb, a broken ring of 9 columns, three amphorae, the broken propylon (5 m ledge, three 1.25 m steps, a broken second pier, a fallen lintel), two olive trees, `ConstructionLineResolve` | Shrine_Center, MC_Start, Birdy_SkyEntry, Prop_Separation, Birdy_Land, Propeller_Ledge, Ledge_View, MC_Handover, Play_Spawn, Ledge_Step_1..3 |
| `OlivePath` | The hill's middle band, the stone path, olive trees (foreground trunks for scene 4's parallax, background rows), an underside rock | Path_Start, Path_Track_A, Path_Track_B, Cam_Track_S4_Start, Cam_Track_S4_End, Path_End |
| `SouthCrest` | The crest band on the plateau's south rim, the last of the path, the way down east along the rim | Crest_View, Zip_Path |
| `AcademyVista` | Markers only, at the greybox's real positions | Gnomon_Aim, Rim_Gaze, BlackSun |

Every prefab's root is at the world origin with its content at world coordinates: the scene
builder instantiates them at identity into its copy of AcademyGreybox. Every marker id in
`CutsceneMarkerIds` is provided by exactly one set.

Assets: meshes in `Art/Cutscene/Sets/Meshes` (refilled in place each run, so GUIDs hold),
materials in `Art/Cutscene/Sets/Materials`, the Greek-key texture in `Art/Cutscene/Sets/Textures`,
looks and the resolve config in `ScriptableObjects/Cutscene/Sets`. Shaders in `Shaders/Cutscene`.

Running the menu twice gives the same result. `GoldenHourLook` is rewritten every run (it must
equal gameplay lighting); `VoidLook` and `ResolveConfig` are created once and then left alone, so
tuning survives a rebuild. The prefabs are rebuilt in a preview scene, so the open scene is
untouched. No lightmap or GI static flags are set anywhere (a test checks this).

## 2. The south island and why it moved

The greybox's **Delta Pylons** (`AcademyGreyboxSetup.BuildDelta`: four islets at y −8 between
z −104 and −60) lie under the shot list's proposed meadow. A meadow at y 0 there would roof over
them, and a player could drop from it onto islets 2 and 3 and skip the Network Step gate. So the
island is fitted around them:

- The **shrine lobe** (a circle of r 7.5 round (0, −96)) sits in the 16 m gap between islet 2
  (x ≤ −8) and islet 3 (x ≥ 8).
- The **slope** spans x −17.5..7.5, z −90..−50.45; the **crest band** spans x −17.5..26.5,
  z −56.5..−50.45. Nothing is built at z > −50.45, so the plateau and its south parapet
  (z −50.4..−50) are untouched.
- Every edge except the north (plateau) edge has a low dry-stone wall and an invisible 14 m
  **fence** collider, so nobody drops onto the islets.
- From the plateau's south rim the hill hides islets 2 and 3; the Delta is still seen from the
  crest itself, the west terraces and the rim west of x −17.5.

Shape (`SouthIslandTerrain`): a flat meadow (y 0) round the shrine; the olive hill rising north
along the path from z −85 (monotone curve, no overshoot; about 18–20° on the walked stretch,
up to ~29° past Path_Track_B) to a level crest at **y 11**; the hill flanks drop up to 35 % away
from the path and fade out east of x 5; a ridge falls east along the rim from y 11 at x 2 to
**0.9 m at x 26**, level with the parapet top. That ridge is the walk down: at x ≈ 24.5 the
ground is 1.0 m, so the player steps onto the parapet and drops 0.9 m to the Gymnasium lawn,
clear of the SealWall (x 11..17). Raycast walk check: max slope 29.5° (controller limit 50°).

The north edge is a rock cliff (strata-banded skirt with a grassy lip) rising up to 11 m behind
the Gymnasium's parapet. It reads as a plain wall from the Gymnasium (it faces away from the
sun); a later art pass should break it up.

## 3. Changes from the shot list (§2)

| Marker | Shot list | Built | Why |
|---|---|---|---|
| Shrine_Center, MC_Start | (−4, 0, −100) | (0, 0, −96) | Fit the lobe between Delta islets 2 and 3 |
| Birdy_SkyEntry | (4, 14, −88) | (−7, 14, −82) | North-west of the shrine (review round 1): S2_SH01's tilt up to Birdy then looks away from the black sun |
| Prop_Separation | (1, 9, −92) | (−3, 9, −88) | On his way down from the north-west |
| Birdy_Land | (0, 0, −95) | (2.5, 0, −91.67) | 5 m from MC_Start at 30°, as the text says ("≈ 5 m NNE"); the shot list's coordinates were 6.4 m away, in the column ring |
| Propeller_Ledge | (−13, 5, −88) | (−8, 5, −84.9) | Moved with the shrine; the pier is centred at (−9, −84) and the marker sits in its south-east corner (0.6 m from the south face, 0.75 m from the east), because from below the pier's own edge hides the middle of its top. From there a 0.35 m propeller clears the edge from MC_Start, Ledge_View and the meadow |
| Ledge_View | (−6, 2.2, −96) | (−2, 2.2, −92) | Moved with the shrine |
| MC_Handover | (−11, 0, −91) | (−5.5, 0, −88) | Clear of the pier (review round 1: at (−7, −87) MC read as pressed into the wall) |
| Ledge_Step_1..3 | (owned by level design) | (−13.75, 1.25, −86.5), (−11.75, 2.5, −86.5), (−9.75, 3.75, −86.5) | 2 × 2 m blocks along the pier's south face, 1.25 m rises |
| Play_Spawn | (not given) | (0, 0.05, −96), facing the ledge | Where S2_SH10 hands over |
| Path_Start | (0, 0, −88) | (0, 0, −88) | Unchanged |
| Path_Track_A | (0, 1, −84) | (0, 0.05, −84) | The hill starts at z −85, so A is at its foot |
| Path_Track_B | (−3, 8, −60) | (−3, 7.80, −60) | y from the ground |
| Path_End | (−4, 9.5, −56) | (−4, 9.84, −56) | y from the ground (crest raised) |
| Cam_Track_S4_Start/End | 3.2·H east, 0.75·H up | (5.08, 1.25, −83.36), (1.08, 11.1, −55.36) | Abeam Path_Track_A and Path_End, perpendicular to A→B. The end moved out from abeam Path_Track_B when the voiced scene outran that stretch |
| Crest_View | (−4, 10, −53) | (−4, 11, −53) | +1 m so the eye line (12.45 m) clears the north stoa roof to the Overlook's parapet |
| Zip_Path | (8, 2, −30) | (8, 2, −30) | Unchanged |
| Gnomon_Aim | (0, 8, 36) | (0, 9, 36) | The gnomon rises 16 m from the 1 m plaza; its middle is y 9 |
| Rim_Gaze | (30, 8, 64) | (20, 8, 66) | From Crest_View the old line ran through Birdy's bell tower (x 14.5..19, 12 m tall) |
| BlackSun | (183, 130, 411) | (183.03, 130, 411.09) | Computed from the greybox constants (bearing 24°, 450 m, 130 m); 21.9° / 13.4° from Crest_View |

## 4. The look (scene 1 → 2)

`CutsceneLook` (on `Void`) has one float, **`blend`**: 0 = the white void, 1 = golden hour. It
blends RenderSettings fog (colour, mode, density, start, end), trilight ambient, and the sun's
intensity, colour and shadow strength between the `VoidLook` and `GoldenHourLook` profiles, and
publishes the colour flood. Timeline animates the serialized `blend` field (an Animation track
bound to the Void object); it is applied in LateUpdate when it changes and on
OnDidApplyAnimationProperties (Timeline preview). No per-frame allocation.

- **Void:** white exponential fog (density 0.06), white trilight ambient with a slightly darker
  ground (MC's soft top fill), sun intensity 0, shadow strength 0.
- **Pure white (integration fix, 30 October):** the gameplay post stack tonemaps (Neutral, from
  the pipeline asset's volume profile), which turned a 1.0 white into light grey (about 218/255),
  and the World Look volume's white balance and vignette warmed and darkened it. The void's white
  is now HDR: `ResolveConfig.voidWhite` (6.5) reaches the dome, the disc and the painterly
  white-out through the `_CutsceneVoidWhite` global. A `VoidPost` global volume on the Void
  prefab (`VoidPostFX.asset`: bloom 0, white balance 0, vignette 0, priority 100) is weighted
  1 − blend by `CutsceneLook` and disabled at blend 1, so gameplay post is unchanged after the
  flood. It measures 254/255 neutral in Play mode. A camera rendering into an LDR RenderTexture
  clamps before the tonemapper and still shows grey; the framing validator's contact sheets now
  render HDR.
- **GoldenHour:** exactly `AcademyGreyboxSetup.ApplyLighting` plus the saved sun (shadow strength
  1, fog density 0.01). A test pins every value.
- Fog modes can't be mixed, so while blending the void's exponential fog thins (log-space) toward
  an equivalent density (0.00035) and switches to the linear golden-hour fog exactly at blend 1.
- The sun is the wired Light, or `RenderSettings.sun` if none is wired (the greybox sets it).
- Prefab default: blend **1** (gameplay look), so an edited scene isn't saved white. The scene-1
  timeline must key blend 0 at its start.
- Disabling the component removes the void (flood strength 0) but leaves fog, ambient and sun as
  last applied; set blend 1 first.

**The colour flood** (`VoidFlood`, `Shaders/Cutscene/CutsceneVoid.hlsl`): CutsceneLook sets
global shader values (centre MC_Start, radius, front width, strength, rise weight). Every set
material (`Pickball/Cutscene/Painterly`) renders pure white beyond the flood radius, with a gold
front; the dome and disc (`Pickball/Cutscene/Void Surface`) are opaque white beyond it and clear
inside. So the shrine is hidden in the void without toggling anything, and reveals radially from
MC's feet, columns from the base up (rise weight 0.5), the dome from the horizon up (rise weight
1.5). The radius is driven by `blend` (−front at 0, `floodMaxRadius` 80 m at 1, along
blend^1.5). At blend 1 the strength is 0 and the dome and disc renderers are switched off. With no
CutsceneLook in a scene the globals are 0 and the painterly shader is an ordinary lit shader.

## 5. The construction lines

`ConstructionLineResolve` (on `OuterShrine/ConstructionLines`) has one float, **`progress`**:
0 = nothing drawn, `drawEnd` (0.6) = everything drawn, 1 = faded to the faint permanent lines
(alpha 0.22). With the shot list's timing, animate progress 0→1 over S1_SH07's 1.0–6.0 s and
blend 0→1 over 4.0–6.0 s. 55 strokes in four groups, each with its own window on `ResolveConfig`:

| Group | Strokes | Window (drawing phase) |
|---|---|---|
| Arcs | the compass's radius line, the lawn circle, the column-ring circle | 0–0.45 |
| Columns | two ruled lines up each shaft (overshooting the top by 0.6 m) and one across its top, 9 columns clockwise from north | 0.2–0.8 |
| Ticks | 24 ticks round the ring | 0.3–0.85 |
| Hill | one curve up the hill's west flank, along the crest and down the ridge | 0.5–1.0 |

The lines are one static mesh (camera-facing ribbons, `Shaders/Cutscene/ConstructionLine.shader`),
never widened below about a pixel. Each vertex carries its stroke index and how far along the
stroke it is; the component writes a preallocated array of per-stroke drawn fractions and the
brightness to a MaterialPropertyBlock each frame. Rebuilt in OnEnable, so a script reload is safe.
Unlit and unfogged; they draw over the void (the dome sorts before them).

## 6. Notes for cameras (E) and the scene builder (A)

- **Dome:** radius 40 m round MC_Start (0, 0, −96). Every scene-1 camera is within 6 m. Anything
  more than 40 m away is white until the flood passes; the dome draws its inside, so keep scene-1
  cameras inside it.
- **Void disc** at y −0.05 (just under the meadow), r 40 m: it hides the Delta below.
- **Scene 4 trunks:** foreground trunks stand 1.5 m from the camera line (3.62 m east of the path)
  at path distances 2, 6.5, 11 and 16 m from Path_Track_A. With the voiced timing and the 35° lens,
  none crosses frame during S4_SH01's hold (8.57–9.67 s) or from OC_S4_07 to the end
  (19.39–28.15 s), with the dolly abeam MC walking from A at t = 0 (1.0 m/s along the ground). A
  trunk is in frame for ±1.15 m of dolly travel. `WalkLengthTests` fails if new voice takes put a
  trunk in those spans; move `SetLayout.TrunkStations` if it does. From 16 m to the camera's stop
  there is no foreground trunk.
- **Crest sightlines** (checked against `GreyboxReference` boxes in tests and by raycast in the
  greybox): from Crest_View's eye (y 12.45) the gnomon, Rim_Gaze, the black sun and the Overlook's
  north parapet are visible over the stoa. The Overlook's paving itself (y 0) stays hidden behind
  the north stoa roof at eye level; S5_SH01's crane top (y ≈ 17.4) sees it.
- **Greybox signs** (FaceCamera TMP: "GYMNASIUM", region labels) are in the copied greybox. Menu 4
  now switches all 46 of them off in the generated OpeningPlayable scene only.
- **Propeller pickup:** the ledge top is a flat 3.5 × 3 m at y 5; put the trigger at Propeller_Ledge.
- **Walk down:** from Crest_View east along the descent path to x ≈ 24.5, onto the parapet, down to
  the Gymnasium lawn.

## 7. Known limitations

- Re-running the menu gives byte-identical prefabs and assets (checked: two runs, empty
  `git status`). Keep sibling names unique in the builder: duplicate names reorder the prefab's
  file IDs between runs.
- The fence is an invisible collider at the island's edge; the visible cue is the knee-high rim
  wall (not on the cliff tops' full height).
- The trunk spacing is tied to placeholder line timings (see §6).
- **The fraying rim** (`FrayingRim`, a vertex-coloured mesh in `AcademyVista`, no colliders;
  rebuilt in review round 3) is the Academy's own paving coming apart off the plateau's north
  edge (z ≈ 60, y 0). It runs in three zones at increasing distance and height. Its centre line
  drifts east with distance toward the black sun.
  1. **Marble, z 61–90.** 3 m cream slabs continue the paving grid. Gaps open up, then the slabs
     lift, tilt and break into floating cubes and tetrahedra of shrinking size. It keeps clear of
     the greybox's narrow north pier at x ≈ 0.
  2. **Ink, z 88–118, y 4–20.** The same shapes as unfilled black outlines (cube and tetrahedron
     edges), then only hatching.
  3. **Parchment, z 116–160, y 10–40.** Staggered blank sheets fade from cream to white.

  The golden-hour haze layers the far zones. A still from Crest_View is at
  `Builds/Cutscenes/frames/S6_CrestView_FrayingRim.png` (local, not committed).
- The void dome and disc draw at queue 3500/3501 and the lines at 3600, so the greybox's floating
  TMP signs (queue 3000) are hidden by the void. In golden hour those signs are still visible from
  the shrine and the crest; hide them in the cutscene scene.
- The scene-1 frames were checked by rendering, not with the real MC: a capsule stood in.

## 8. Verification (Editor, 29 October 2026)

In a temporary copy of AcademyGreybox with all five prefabs placed at identity
(`SetVerification.Sightlines()` / `Walk()` raycasts, plus rendered frames):

- Clear lines of sight from Crest_View's eye to Gnomon_Aim, Rim_Gaze, the black sun and the
  Overlook's north parapet at x −25, −10, 10, 25; from MC_Start to the propeller on the ledge, the
  ledge's trim and Birdy_Land; from Ledge_View to the propeller and step 3's front edge; from the
  meadow (0, −82) to the propeller and the trim. Ledge_View → step 2's front edge only meets the
  invisible fence (no visual block).
- Walk Play_Spawn → ledge: flat meadow, then four 1.25 m single jumps (steps 1–3, ledge).
- Walk shrine → path → crest → descent → parapet → Gymnasium lawn: no jump needed, max 29.5°.
