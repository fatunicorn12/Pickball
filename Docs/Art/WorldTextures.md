# World textures

How the playable world gets its painted look without any UV work, and how to try a new texture.
The art direction is in [TheAcademy.md](../World/TheAcademy.md) §2. This is a look test: nothing
here changes `AcademyGreybox` or any shipped scene yet.

## How it works

The greybox gives each surface type one material, so changing six materials repaints most of the
map. `Pickball/World Painterly` (`Art/World/Painted/WorldPainterly.shader`) makes that work on
scaled boxes:

- **World-space projection.** Textures are placed by world position, in metres, so they never
  stretch and they line up across neighbouring boxes.
- **Soft colour patches.** Large tints, also from world position, hide the repeat at a distance.
- **Colour only.** It takes one colour map, with no normal or roughness maps. Light and shadow
  come from the same soft shading as `Pickball/Cutscene/Painterly`.
- **Cost.** A flat, axis-aligned face samples the texture once. Curved or slanted surfaces sample
  two or three times where the projections blend.

| Greybox material | Painted material | Texture | Metres per repeat |
|---|---|---|---|
| `GreyboxLawn` | `Painted_Lawn` | `Lawn.png` | 8 |
| `GreyboxPaving` | `Painted_Paving` | `Paving.png` | 4 |
| `GreyboxStone` | `Painted_Marble` | `Marble.png` | 6 |
| `GreyboxRoof` | `Painted_Roof` | none, flat colour | n/a |
| `GreyboxRock` | `Painted_Rock` | none, flat colour | n/a |
| `GreyboxSand` | `Painted_Sand` | none, flat colour | n/a |

The table's starting values are in `Scripts/Editor/WorldPaint/WorldPaintCatalog.cs`.

## See it

- **Tools ▸ Pickleball ▸ Trials ▸ Build Courtyard Texture Test** builds
  `Scenes/Trials/CourtyardTextureTest.unity`, a copy of AcademyGreybox with the materials swapped
  and the floating signs hidden. Press Play in it to walk around.
- **Tools ▸ Pickleball ▸ Trials ▸ Render Courtyard Shots** renders the open scene from four fixed
  views into `Logs/WorldPaint/current/`, so textures can be compared shot for shot.

The scene is rebuilt on every run, so its GUID changes and hand edits are lost. Keep it out of
`EditorBuildSettings`. It copies AcademyGreybox, so rebuild it when the greybox changes.

Headless, in a worktree whose Editor is closed. Leave out `-nographics`, because this renders:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.5f2\Editor\Unity.exe" -batchmode -projectPath <worktree> `
  -executeMethod Pickleball.EditorTools.WorldPaint.CourtyardTextureTestSetup.Batch -logFile <worktree>\Logs\worldpaint-build.log
```

That also writes `Logs/WorldPaint/after/` and `before/`, and exits 1 if the build or a render fails.

## Try an AI texture

The three PNGs in `Art/World/Painted/Textures/` are procedural placeholders. Replace one with a file
of the same name and every material using it updates, with no rebuild. Its `.meta` keeps the import
settings.

Ask for a **seamless, tileable, top-down or straight-on** colour texture at 1024×1024 or larger,
with **no lighting baked in**. Put the shared style line first, so the textures match each other:

> *Stylised hand-painted game texture, seamless and tileable, flat even lighting, visible soft
> brush strokes, low-frequency detail, big simple shapes, saturated colours, anime-inspired.*
> Avoid: *photoreal, fine noise, grime, cracks, dirt, baked shadows, perspective, text.*

| Texture | Add to the style line | Scale to match |
|---|---|---|
| `Lawn` | Bright saturated lawn green grass, large soft painted clumps, gentle variation | 8 m across, so clumps about 0.5 m wide |
| `Paving` | Cream sandstone flagstones in offset rows, softly rounded corners, warm tan joints | 4 m across: 4 rows of 3 stones |
| `Marble` | Cream marble blocks in offset courses, faint gold-grey veins, thin soft joints | 6 m across: 4 courses of 2 blocks |

**What to reject:**
- Photoreal stone or grime. Image models drift this way, and the art direction rules it out.
- Baked shadows or a lit side. The shader lights the surface, so baked light gets shaded twice.
- Horizontal courses that aren't level. `Paving` and `Marble` lean on the straight rows.

**To judge one,** put it in place, render the shots and compare with the last set. A texture can
look fine alone and still be too busy or too repetitive at game scale.

If a texture's scale is wrong, change `_TileSize` on its `Painted_*` material rather than resizing
the image. Tuned material values survive a rebuild.

Before shipping an AI texture, check that the plan it was generated on allows commercial use, and
record that the way `Docs/Art/TripoAssets.md` does for models.

## Known limitations

- **Columns:** they share `GreyboxStone` with the walls, so they pick up the wall's block joints,
  including a vertical joint no drum should have. They need their own material with drums only.
- **Moving things:** the projection is world space, so a texture slides across anything that moves
  (the orrery, doors). Those need their own material.
- **Cutscenes:** this shader ignores the cutscene void flood that `Pickball/Cutscene/Painterly`
  responds to. It would need `ApplyCutsceneVoid` before it appears in a flooded shot.
- **Exact patterns:** the Penrose floors, sundial lines and construction lines aren't textures.
  Build them as shader work or decals, since image models won't get the geometry right.
- **Not measured:** the shader hasn't been profiled on a phone.
