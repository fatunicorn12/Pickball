# Tripo assets

How models generated in Tripo AI become game-ready prefabs, what has been imported so far, and how
to add the next batch. The art plan (ids, budgets, prompts) lives with the generator, in the
session scratchpad's `tripo/asset-plan.md`; the spend ledger is `tripo/ledger.md` beside it.

## Licence

Every asset here was generated on the user's own Tripo account on the **Professional (Pro)
plan**, with each generation set to **Private**. The Pro plan grants commercial use and private
models; the free plan would make models public, CC BY 4.0 and non-commercial. The ledger records
the plan, its expiry and every spend. Keep generating on a paid plan, and keep the ledger, so the
rights stay documented.

## Pipeline

```
Tripo Studio ─export FBX (Blender preset)─▶ downloads/<id>/<name>[_vN].zip   (untrusted)
  ─extract.py (python -I)─▶ extracted/<id>/<zip stem>/                       (fresh folder per zip)
  ─Tools ▸ Pickleball ▸ Tripo ▸ Import Tripo Assets…─▶ Art/Tripo + Prefabs/Tripo
  ─Tools ▸ Pickleball ▸ Tripo ▸ Render Contact Sheets─▶ Logs/TripoRenders/*.png  (judge by eye)
  ─EditMode tests (TripoAssetTests)─▶ budget, material, size, pivot, collider, references
```

### 1. Extract (outside Unity)

Downloads are untrusted. Each zip is opened with Python's `zipfile` under `python -I`, nothing in
it is run, and anything other than `.fbx`, `.jpg`, `.jpeg`, `.png` or `.tga` rejects the whole
zip. Absolute paths, `..` and symlinks are rejected too. Each zip goes into its own new, empty
folder named after the zip, so a retry (`_v2`) never mixes with an older extraction. The
extractor and a file-signature check are in the scratchpad's `tripo-import-tools/`:

```powershell
python -I extract.py <downloads> <extracted> [ID | ID=exact.zip ...]
python -I magic.py <extracted>
```

With several zips per id, the highest `_vN` wins. Use `ID=exact.zip` to pick one by name, for
example the Blender-preset export of a rig over its Mixamo-preset twin.

### 2. Import (Unity)

`Scripts/Editor/Tripo/TripoAssetImporter.cs`. From the menu it asks for the extracted folder;
Cancel re-processes what is already imported. Headless, in a worktree whose Editor is closed:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.5f2\Editor\Unity.exe" -batchmode -nographics -quit `
  -projectPath <worktree> -executeMethod Pickleball.EditorTools.Tripo.TripoAssetImporter.Batch `
  -tripoSource <extracted> -logFile <worktree>\Logs\tripo-import.log
```

It exits 1 if any row reports an error and writes a table to `Logs/TripoImport.md`. Per catalog
row it:

1. **Stages** the newest extraction of `<extracted>/<id>/`: the FBX becomes
   `Art/Tripo/<Category>/<Name>/<Name>.fbx` and the colour map `<Name>_BaseColor.jpg` (a
   `*normal*` image becomes `<Name>_Normal.jpg`). Files are copied only when their bytes differ.
   More than one colour texture (a multi-part export) is refused: merge the parts first.
2. **Textures:** mip maps on, Read/Write off, the row's size cap, Compressed on desktop, ASTC 6×6
   overrides on Android and iOS. An optional colour grade (saturation, gain) is baked in at import
   by `TripoTexturePostprocessor`; the JPG in the repo stays as Tripo exported it.
3. **Model:** Read/Write off, imported normals, tangents only when there is a normal map, no
   animation, cameras, lights or colliders, axis conversion baked. The scale factor is fitted
   so the model measures the row's target size along the row's axis. Humanoid rows import as a
   Unity Humanoid built from the model's own skeleton; Tripo's Mixamo rig preset auto-maps.
4. **Material:** one URP Lit material per asset (`<Name>.mat`), and every material slot of the
   FBX is remapped to it, so code that instantiates the FBX directly gets it too.
5. **Prefab** `Prefabs/Tripo/<Name>.prefab`: the model under a `Model` child whose offset puts
   the origin at the row's pivot (props bottom-centre on the ground, projectiles and effects at
   the bounds centre) and whose rotation is the row's facing fix. Shadows on or off per row. A
   box collider on the root only for solid props.
6. **Budget:** triangles over the plan's budget are a warning, over the hard cap an error.

Every step compares before it writes, and prefabs are updated in place, so running it twice
leaves git clean. It never opens or saves a scene.

### 3. Judge

`Render Contact Sheets` (or `-executeMethod Pickleball.EditorTools.Tripo.TripoContactSheet.Batch
-tripoRenderDir <dir>`, with a GPU, so without `-nographics`) writes per row: three views next
to the chibi player for scale plus a close-up. Humanoid rows also get a pose sheet: a line-up
with the chibi and Newton, then Newton's idle, walk and cast clips retargeted onto the character
(add more with `-tripoPreviewClips a.fbx,b.fbx`), with close-ups of the shoulders and hands and of
the hem and feet. The lowest vertex of each pose is logged, so "feet on the ground" is a number.
From the menu it refuses to run over unsaved scene changes and restores the open scenes after.

Look at every sheet. Send anything with an unreadable silhouette, texture smears, broken geometry
or a wrong facing back to the generator rather than patching it here.

### 4. Validate

`Tests/EditMode/Validation/TripoAssetTests.cs` (category `Assets`, read-only) checks every row:
triangles under the cap, URP Lit with a base map, one material, size within tolerance, pivot,
collider and shadow mode as the row says, no missing scripts or references, Read/Write off,
texture caps, mobile overrides and, for Humanoid rows, a valid human avatar.
`Tests/EditMode/Art/TripoFitTests.cs` covers the fitting maths and the colour grade.

## The catalog

`Art/Tripo/TripoAssetCatalog.asset` (`TripoAssetCatalog`, one `TripoAssetSpec` per row) drives
the import and the tests. It lives in the runtime assembly only so the tests can read it; nothing
references it at runtime. Row fields, all with tooltips:

| Field | Meaning |
|---|---|
| Id, Asset Name, Category | Plan id (also the download folder), PascalCase name, folder under `Art/Tripo` |
| Rig | None, or Humanoid for rigged characters |
| Source | Which extraction was staged (written by the import) |
| Size Axis, Target Size, Size Tolerance | What "1.6 m" means (Height, Largest Horizontal, Largest Extent), the metres, and the test's slack |
| Pivot, Model Rotation | Bottom-centre or centre; a facing fix in degrees |
| Triangle Budget, Triangle Cap | Plan target (warning) and hard cap (error, test failure) |
| Texture Max Size | 2048 characters, 1024 props, 512 small props and projectiles, 0 geometry-only |
| Texture Saturation, Texture Gain | Colour grade baked into the base map |
| Base Color, Smoothness, Metallic, Emission | The URP Lit material |
| Collider Shape, Cast Shadows | Box for solid props, None for anything code moves; shadows off for effect cores |

**Seeded once, then the asset wins.** Rows are seeded from `TripoCatalogSeed.cs`: the import
appends any seeded row whose id the catalog lacks and never changes an existing row, so Inspector
edits survive. To change a shipped row, edit the asset; to retire one, delete it from both.

## Adding a batch

1. Extract the new zips (above) into the same `extracted` folder.
2. Add one row per asset, either to `TripoCatalogSeed` as a new `IEnumerable` method listed in
   `All()` (preferred for agents: no YAML editing) or in the catalog's Inspector. Take the size,
   budget and texture cap from the plan and a pivot, collider and shadow mode from the table
   above.
3. Run Import Tripo Assets with the extracted folder. Read `Logs/TripoImport.md`.
4. Render contact sheets and look at them. Fix facing with Model Rotation and colour with the
   grade; send real defects back to the generator. A retry exported as `_v2` replaces the old
   one on the next import.
5. Run the EditMode suite.
6. Commit the new `Art/Tripo/**` and `Prefabs/Tripo/**` files, the catalog and any seed change.
   FBX and JPG files go through Git LFS (`.gitattributes`); check `git lfs ls-files` lists them.

Hooking an asset into the game (NewtonFightArt, the boss scripts, the Magic spells) is a separate
change owned by that system; this pipeline only produces prefabs.

## Imported so far

Measured by the import (prefab space, metres). "Unit" sizes are rescaled by game code.

| Id | Prefab | Triangles (budget / cap) | Size W × H × D | Texture | Source | Verdict |
|---|---|---|---|---|---|---|
| NF-01 | GravityWellCore | 1,822 (2,000 / 2,500) | 0.93 × 1.00 × 0.92 (unit; the well scales it to 3.6 m) | 1024 | v2 (free retry) | Good: glossy black sphere in brass armillary rings |
| NF-02 | FluxionPrism | 2,351 (2,500 / 3,000) | 0.44 × 0.38 × 0.50, apex +Z | 512 | v2 | Usable stopgap: square glass pyramid in a gear frame, but the glass texture is smeared; regenerate |
| NF-03 | Apple | 768 (800 / 800) | 0.54 × 0.60 × 0.49 | 512 | v2 | Good: red apple, brown stalk, leaf |
| NF-04 | InverseSquareOrb | 1,894 (2,000 / 2,000) | 1.00 × 0.55 × 1.00 (unit) | none | v3 | Good: clean tiny Saturn; gold Lit with emission |
| NF-05 | Telescope | 2,855 (3,000 / 5,000) | 1.32 × 1.60 × 0.74 | 1024 | v1 | Good: chunky brass spyglass on a tripod (no side eyepiece) |
| NF-06 | AppleCrate | 2,223 (2,500 / 5,000) | 0.90 × 0.96 × 0.89 | 1024 | v1 | Good |
| NF-07 | LeibnizBust | 2,776 (3,000 / 5,000) | 0.99 × 1.40 × 0.84, turned to face +Z | 1024 | v2, graded to marble (saturation 0.15, gain 1.25) | Good: smug wigged bust, cracked plinth |
| GD-01 | Pythagoras | 11,563 (10,000 / 15,000) | 2.15 (T-pose span) × 2.20 × 0.85 | 2048 | Blender-preset rig | Good: Humanoid, 52 bones mapped incl. fingers; retargets cleanly |

## Known limitations

- Prefab renderers on nested model instances carry overrides for the shadow mode and materials;
  re-running the import resets them, so tune those in the catalog, not on the prefab.
- Unity drops 3 degenerate polygons from Pythagoras on import (logged); nothing visible.
- Geometry-only FBXs (the orb) have no material slot to remap, so the raw FBX renders with
  Unity's default material; use the prefab, which has the asset's material.
- The colour grade is a global desaturate-and-lift. It can't recolour one part of a texture.
