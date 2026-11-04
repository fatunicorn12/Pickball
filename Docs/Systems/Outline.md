# Screen Outline

## 1. Purpose and feel

Thick black cartoon lines around everything opaque, from one shader, with no per-model work. Any
new model or primitive gets lines automatically, and the whole game shares one look.

## 2. Architecture

| Piece | Job |
|---|---|
| `Shaders/Outline/ScreenOutline.shader` | Finds edges from depth and normals and draws the line |
| `Scripts/Rendering/OutlineRenderPass.cs` | URP render graph pass that runs the shader before transparents |
| `Scripts/Rendering/OutlineSettings.cs` | The look, as an asset: `ScriptableObjects/Rendering/OutlineSettings.asset` |
| `Scripts/Rendering/OutlineMath.cs` | Width and fade sums, tested in `Tests/Rendering/` |
| `Scripts/Rendering/OutlineRendererFeature.cs` | Adds the pass to game cameras, in `PC_Renderer` and `Mobile_Renderer` |
| `Scripts/Editor/Outline/` | Menus: **Tools ▸ Pickleball ▸ Setup Outline** and **Render Outline Shots** |

`Pickleball.Rendering` is its own assembly because `Pickleball.Runtime` has no reference to URP.

**How an edge is found.** Each pixel compares itself with a ring of eight neighbours. It's on an edge
if a neighbour is sky while it isn't (or the reverse), if a neighbour is off its surface's plane by
more than the depth threshold, or if the surface turns more sharply than the normal threshold. The
gap is measured from both surfaces' planes. Measuring from one alone splits the line in two wherever
a surface turns side-on near a silhouette.

**Why before transparents.** Fire, pulses, glows and text draw over the lines and get none of their
own, because they aren't in the depth texture. The cost is that a transparent glow also lightens
any lines behind it.

**Why a renderer feature.** Both project renderers are shared by every scene, so one feature on each
outlines the whole game, including scenes and models added later. Game cameras get it; Scene view,
asset previews and overlay cameras don't.

## 3. How to tune

Edit `OutlineSettings.asset`, live in Play mode if you like. Changes made in Play mode stay after you
stop. To turn the outline off at one quality level, untick its feature on that renderer asset.

| Field | Default | Change it when |
|---|---|---|
| Width At 1080p | 6 | Chosen by eye in the trial scene. 10 clogs hair and turns small details into blobs; 5 starts to look thin |
| Depth Threshold | 0.25 m | Lower to ink smaller steps; raise if bumpy surfaces fill with lines |
| Depth Threshold Per Metre | 0.02 | Raise if distant objects fill with lines |
| Normal Threshold | 0.9 | Lower to ink softer creases; above 1, the 90° edges of boxes lose their lines |
| Fade Start / End | 45 / 110 m | Lines keep their width with distance, so far detail needs fading |

## 4. How to extend

**New models and scenes** need nothing. A custom opaque shader needs a `DepthNormals` pass (copy the
one in `Art/World/WorldUnlitShadowed.shader`). Without one the object gets no lines, and lines from
whatever is behind it draw across it.

**To opt a surface out of the outline**, write alpha above 0.5 in its shader's `DepthNormals` pass.
It then counts as sky: it gets no lines of its own, and whatever stands in front of it still gets a
silhouette. `Shaders/Cutscene/Painterly.shader` uses this so the white void doesn't reveal the hidden
shrine, and its lines arrive with the colour flood.

**A new renderer** needs adding to `OutlineSetup.RendererPaths`, and to the cases in
`OutlineRolloutTests`, before running **Setup Outline**.

## 5. Known limitations

- **Generated world mesh.** The mesh in ChibiSandbox has torn edges and floating flaps of geometry.
  These are real shapes, so they get lines that no threshold can remove. Clean geometry (greybox,
  training stage) looks far better. Options are to clean the mesh or to add a per-object opt-out,
  which doesn't exist yet.
- **Dark backgrounds.** Black lines hardly show against the HypatiaFight arena.
- **Stray marks on characters.** Sharp folds can draw short marks, such as one on the chibi's cloak
  below the scarf.
- **Very thick lines.** Above about 8 px the eight samples start to leave a rough rim on fine shapes.
- **Mobile cost.** The outline is on at Mobile quality too, and its speed on a phone hasn't been
  measured (see Performance).
- **Cutscene void.** The gameplay greybox stays rendered under the void's dome, and it doesn't use
  `Painterly`, so a few faint pale lines from it can show through in the void shots, up close. Hiding
  the greybox during the void would remove them.

## 6. Performance

GPU time of the outline pass, measured headless with Unity's GPU recorders on an RTX 5070 Ti with
Direct3D 12, at PC quality in a copy of ChibiSandbox. Medians over 100 frames, repeated with matching
results.

| Resolution | Outline | SSAO, for comparison | Share of a 60 fps frame |
|---|---|---|---|
| 1920×1080 | 0.17 ms | 0.35–0.38 ms | 1.0% |
| 2560×1440 | 0.29 ms | 0.68 ms | 1.8% |
| 3840×2160 | 0.65 ms | 1.80 ms | 3.9% |

- Inside the open Editor at 1080p the same pass took 0.22 ms.
- At PC quality the depth-and-normals prepass takes the same time with the outline on or off, because
  SSAO already needs it.
- At Mobile quality the prepass runs only when the outline is on, so **every scene pays for it on a
  phone**. It took 0.08 ms at 1080p on this GPU, which says little about a phone.
- The outline's own GPU timer returned nothing at Mobile quality, for a reason not yet known. So the
  Mobile cost was measured as whole-frame time (render plus waiting for the GPU) with the outline on and
  off, at 1080p: about 0.85 ms to 1.03 ms, so 0.17–0.19 ms added, or around 20%. The same measure at PC
  quality adds about 0.13 ms, or around 9%. Use these to compare the two pipelines, not as phone timings.

The pass reads 18 textures per pixel across the screen, 9 each of depth and normals, so its cost follows
resolution and hardly depends on the scene or on line width.

**Checked on real frames**, with both pipelines, in Play mode: HypatiaFight (rings, moons, pulse, floor
fire, particles, both phases), the opening cutscene to the hand-off (void, reveal, faces, subtitles,
HUD), TrainingStage (cloth, sword trail, all five spells) and ChibiSandbox (Newton's fight). These were
sampled frames, so they rule out wrong edges, not line shimmer.

**Not tested:** speed on a real phone or on a weaker PC GPU, and how the lines look in continuous motion.
The Mobile checks swapped in `Mobile_RPAsset` and its renderer on this PC, so the rest of the Mobile
quality level didn't apply.
