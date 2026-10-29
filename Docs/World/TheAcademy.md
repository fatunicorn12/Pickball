# The Academy: first-world design (draft)

Draft, 28 October 2026. A proposal for the hub world the whole game takes place in, as the player
first meets it with nothing unlocked. It builds on `Docs/Story/StoryBrief.md`. It adds to §3
(Look and setting) and proposes answers to Open questions 4 (how the eras connect), 5 (the order)
and 6 (the stakes). Nothing here is established until you say so.

## 1. The idea: a world drawn with a compass and a straightedge

The game takes place in **the Margin**: the space around a proof where mathematicians write what
doesn't fit. Galois's notes already live there ("the margins are full"). Every idea that outlived
its thinker ends up in the Margin, and each Guardian keeps a corner of it shaped like their era.

At the centre stands **the Academy**, the oldest and most finished part, drawn with Euclid's two
tools: every wall a straight line, every dome a circle. The student's compass-blade is one of those
tools, which is why Birdy takes them for a Guardian.

**The Singularity is the part of the Margin where nothing is written.** It is not evil. It is
blank, and blank spreads. From the Academy it hangs low in the northern sky as a black sun, and the
Margin's edges fray toward it: stone becomes ink lines, ink lines become bare parchment, and
parchment becomes nothing. That fraying is the stakes, and the player sees it in the first minute.

**Time runs outward.** The Academy *is* Ancient Greece, and the first era. The other seven corners
float around it on a golden spiral in historical order, with Hypatia's nearest. Riemann's is the
farthest out and the closest to the black sun, which is why he has seen it. The world is a timeline
laid out as distance, which is the Spacetime idea at world scale. Each corner is reached through a
door in the Academy, and each door sits behind the ability of the Guardian before it.

## 2. Art direction

> A sunlit Greek academy drawn by a geometer: painterly and saturated, built only from shapes you
> could construct with a compass and a straightedge, and fraying into ink and blank paper at its
> edges.

### Pillars

1. **Constructed shapes.** Circles, straight lines, right angles, clean triangles. Faint gold
   *construction lines* (compass arcs, ruled lines, tick marks) hang in the air around important
   things, as if the scaffolding that drew the world was never erased. They double as guidance:
   a ruled line often points at a secret.
2. **Painterly, not photoreal.** Match the chibi cast: hand-painted textures, big soft shapes,
   low-frequency detail, soft shading. AI tools drift toward photoreal stone and grime, so prompt
   against both and reject results that have them. (Newton is the one off-style asset.)
3. **Chibi-scaled architecture.** Squat columns, chunky steps, oversized doors. Proportions are
   cheated toward the 1.6 m player, not real Greek ones.
4. **Order fades toward the void.** The Academy's floors are periodic tilings. Toward the north
   rim they turn into Penrose tilings, which look regular but never repeat (the Singularity's
   phase 1 in miniature), then crack along √2 diagonals, then dissolve into sketch lines.
5. **Golden hour, always.** Warm low sun from the south-west, long shadows that read form, and
   haze that separates near, middle and far layers. The black sun in the north is the only dark
   thing in the sky.

### Colour script

| Area | Main colours | Accent |
|---|---|---|
| Academy core | cream marble, terracotta roofs, saturated lawn green, cerulean sky | gold (trims, construction lines) |
| Era wings and doors | each Guardian's pair from StoryBrief §6 | gold |
| Newton's door (he has no pair yet) | *proposal:* apple red and brass | gold |
| The fraying rim, the Singularity | parchment white, ink black | none: colour drains out |

Colour is navigation: a player who has seen Hypatia's indigo and silver will recognise her door
across the map before they know her name.

### Readability language (gameplay first)

- **Standable ledges** carry a cream Greek-key trim on their top edge. Decorative ledges don't.
- **Tether anchors** are brass rings (`TetherAnchor` already glows when in range).
- **Ability gates** wear their Guardian's emblem and colour pair, so the player learns "that's a
  Pythagoras thing" long before they can use it:

  | Ability | Emblem on its gates |
  |---|---|
  | Angle Dash | blue-and-gold right triangle |
  | Orbital Tether | indigo-and-silver astrolabe ring |
  | Equation Swap | emerald-and-gold "=" balance |
  | Spiral Jump | mustard-and-green nautilus |
  | Gravity Anchor | red-and-brass apple |
  | Network Step | brown-and-teal node graph |
  | Symmetry Break | navy-and-red mosaic star |
  | Limit Approach | violet-and-silver dotted line closing on a point |

- **Lemma caches** (XP) glint gold. **The fraying rim** is where the world stops: white means
  out of bounds.

## 3. Map

North (+Z) faces the black sun. The player spawns in the courtyard facing north, so the first shot
is the lawn, the gnomon beyond the roofs, and the black sun on the horizon.

```
                       N (+Z)       ●  the black sun, low in the northern sky
                                    ↖  the seven era islands curve away on a spiral into haze
  ┌──────────────────────────────── THE OVERLOOK (fraying rim) ────────────────────────────────┐
  │  Surveyor's Gate [SB]            telescope        Zeno's Bridge [LA] ──▶     Orrery Chasm  │
  │  (Riemann's door)                                                            [OT]          │
  │                                                                              (Al-Khw. door)│
  │  Library wing ═══gap═══ [AD]           GNOMON PLAZA                                        │
  │  (Hypatia's door)                (sundial, eight plinths)                    Mirror Court  │
  │                                                                              [ES]          │
  │  AMPHITHEATRE              ┌──────────────────────────────┐                  (Fibonacci's  │
W │  (Pythagoras's fight)      │   STOA RING AND ROOFTOPS     │                   door)        │ E
  │                            │                              │                                │
  │  OLIVE TERRACES            │   COURTYARD (spawn, Birdy)   │                  Nautilus      │
  │  aqueduct (skate run)      │            ◯ the Well [GA]   │                  Tower [SJ]    │
  │        │                   └──────────────────────────────┘                  (Newton's     │
  │        ▼                              GYMNASIUM                               door, top)   │
  │  Delta Pylons [NS] ──▶ Galois's door                                                       │
  └────────────────────────────────────────── south rim ───────────────────────────────────────┘
     Beneath the island, reached down the Well: the Underside (hanging orchard, Euler's door)
```

`[AD]` Angle Dash · `[OT]` Orbital Tether · `[ES]` Equation Swap · `[SJ]` Spiral Jump ·
`[GA]` Gravity Anchor · `[NS]` Network Step · `[SB]` Symmetry Break · `[LA]` Limit Approach

**Scale.** Aim for a walkable core of about 150 × 130 m: roughly 20 seconds of sprinting
(7 m/s) from rim to rim. That is big enough for a skate run and small enough to learn by heart.
Build the core first; the wings start as façades.

## 4. Regions you can explore from the start

| Region | What it is | What it teaches | What you see from it |
|---|---|---|---|
| **The Courtyard** (spawn) | Established: the saturated green lawn ringed by Greek columns. Birdy, a round well, low walls. Over the gate, Plato's Academy motto in Greek: "Let no one ignorant of geometry enter." | Run, jump, talk. | The gnomon and the black sun to the north; the Well's daylight below. |
| **The Stoa rooftops** | The colonnade's terracotta roofs, reached from crates and broken column stubs. Birdy "saw your compass-blade glinting from the rooftops", so this is his territory. | The world has a second layer upward. | The whole hub: the best place to read the map. |
| **The Gymnasium** | The Academy's training yard (Plato's Academy really was a gymnasium grove). Training dummies; the TrainingStage could open from here. The skateboard is found here. | Combat basics; first pickup. | The Nautilus Tower close up. |
| **The Gnomon Plaza** | A round plaza paved as a giant sundial, with a tall right-triangle gnomon at its centre. Around it stand **eight empty plinths**. Each defeated Guardian's statue draws itself in, line by line. | Where you are, and how far you have come. | The tallest landmark; visible from everywhere. |
| **The Amphitheatre** | A half-bowl of stone seats cut into the western hill. **Pythagoras's fight happens here, in the hub**: he is Ancient Greece, and so is the Academy. No scene change for the tutorial boss. | The first boss. | The Library wing across its gap. |
| **The Olive Terraces and Aqueduct** | Terraces stepping down the west slope; an aqueduct channel runs downhill along them toward the south rim. | Downhill flow; later, the skate run. | The Delta Pylons below. |
| **The Overlook** | The north rim, where paving breaks into floating cubes and triangles that thin into sketch lines and then blank paper. A brass telescope (the Telescope prop exists) shows each era island up close. | The stakes. | All seven islands on their spiral, the black sun, and, looking over the edge, apple trees growing upside down beneath the island. |

## 5. What you can see but not reach

Every gate below is **visible from somewhere walkable on day one**. Besides the door it guards,
give each ability one or two optional pockets around the hub (a Lemma cache, a Bean, a lore page),
so every return trip pays.

### Two pickups inside the first world

Before the first boss, the player should already have done the loop of seeing a ledge, failing to
reach it, and coming back. Neither of these abilities is tied to a Guardian in the brief, so both
are proposals:

| Gate | Seen as | Opens with | Pays |
|---|---|---|---|
| **Birdy's Perch**, on the stoa bell tower | Birdy flutters up to a ledge about 7 m high, out of triple-jump reach. | **Birdy's feather** (the existing feather mid-air jump), which he gives you after a small rooftop errand. | A Lemma cache and the clearest view of the gates. |
| **The Aqueduct** | A long channel running down the terraces from a 5 m head. | **The skateboard**, from the Gymnasium. | A skate run down to the lowest terrace and the Delta. |

The skateboard can't actually gate anything yet: it tops out at 6.2 m/s, slower than the 7 m/s
sprint, so any gap it clears a sprint clears too. In the greybox the aqueduct is a skate run, not
a lock. To make the board a key, it needs something only it can do, such as a ramp launch or a
grind rail.

### The Guardian chain

| Gate | Where | Seen on day one as | Opens with | Leads to |
|---|---|---|---|---|
| — | Amphitheatre | open | — | **Pythagoras** (fought in the hub) |
| **The Broken Bridge** | West | An indigo-and-silver library façade across a gap too wide to jump, its bridge fallen, soot on the stone. | Angle Dash | **Hypatia's** door (Alexandria) |
| **The Orrery Chasm** | North-east | A giant brass orrery turning over a gorge, its planets sweeping past the edge. | Orbital Tether: latch onto a passing planet | **Al-Khwarizmi's** door (a tiled House of Wisdom arch) |
| **The Mirror Court** | East | A court halved by a still pool and a glass wall. The far half mirrors yours, and only its gate stands open. | Equation Swap: swap to your mirror point | **Fibonacci's** door (an overgrown garden gate; rabbits watch you) |
| **The Nautilus Tower** | South-east | A spiral tower whose ledges hide behind overhangs a straight jump can't round. | Spiral Jump | **Newton's** door: an observatory at the top |
| **The Well and the Underside** | The courtyard Well, and beneath the island | A grated well with daylight at the bottom: it goes right through the island. From the Overlook, upside-down apple trees under the island ("Down is wherever I say it is"). | Gravity Anchor: drop through and invert | **Euler's** door, among the bridges where the river pours off the island |
| **The Delta Pylons** | South | Stone pylons on islets in the river delta, joined by faint lines like a drawn graph. | Network Step | **Galois's** door: a barricaded Paris street under a sky stuck just before dawn |
| **The Surveyor's Gate** | North-west | A great door sealed by a perfectly symmetric mosaic. Smaller mosaic seals on side doors all over the hub hide optional pockets. | Symmetry Break | **Riemann's** door |
| **Zeno's Bridge** | The Overlook | A bridge out toward the black sun whose planks each run half the length of the last. Walk it and you never arrive. | Limit Approach | **The Singularity** |

This runs the roadmap's chronological order as a strict chain, which answers Open question 5 with
"fixed". To open it up, put two doors behind the same ability (for example, both Al-Khwarizmi's
and Fibonacci's behind Orbital Tether).

**The Well's other job:** Eratosthenes, Alexandria's librarian, measured the Earth with a well and
a gnomon's shadow. With the Gnomon Plaza and the Well both in the hub, "at noon the sun shines
straight down the Well" is a ready-made puzzle or secret.

### Pure vistas (never reachable from the hub)

- The black sun, always in the northern sky. It is the world's compass.
- The seven era islands on their spiral, hazier the farther out they are.
- Galois's door, the one place in the hub where the sky is pre-dawn blue.
- The fraying rim itself. *Optional:* it frays a little further after each Guardian falls.

## 6. The first ten minutes

1. Spawn facing north: the lawn, the gnomon, the black sun. Birdy runs his intro.
2. Up onto the stoa roofs by the crates. The whole hub is laid out below. Birdy flutters to his
   perch, out of reach (gate 1, seen).
3. Down to the Gnomon Plaza: eight empty plinths, one per Guardian.
4. North to the Overlook: the fraying rim, the telescope, the islands, the trees under the
   island. Zeno's Bridge is right there and goes nowhere.
5. West past the Broken Bridge (indigo, soot) to the Amphitheatre: Pythagoras.

On that walk the player has seen at least four gates, the stakes and the goal, without a single
line of exposition.

## 7. Building it

### Metrics (from `MovementConfig.asset` and `TetherAnchor`, 28 October 2026)

| Move | Height or reach | Field |
|---|---|---|
| Run, sprint | 4.2 m/s, 7 m/s | (Movement.md) |
| Single jump | 2.0 m | `singleJumpHeight` |
| Double jump | 2.8 m | `doubleJumpHeight` |
| Triple jump | 6.0 m, needs a run-up of 5 m/s or more; carries 11.2 m over 1.58 s (playtest R2) | `tripleJumpHeight` (Movement.md still says 4.5 m) |
| Side somersault | 4.2 m | `sideFlipHeight` |
| Spin jump | 4.0 m | `spinJumpHeight` |
| Rollout | 1.3 m | `rolloutHeight` |
| Feather mid-air jump (gated) | +2.0 m | `featherJumpHeight` |
| Dash (gated) | 14 m/s for 0.22 s, about 3 m of burst | `dashSpeed`, `dashDuration` |
| Tether (gated) | grabs anchors within 9 m; rope 4–14 m | `TetherAnchor.maxRange`, `tetherMin/MaxLength` |

Rules of thumb:

- **Main route:** steps of 2 m or less. Use 2.8 m for "try a bit harder", and 4–6 m only for
  optional skill challenges.
- **A gate must beat the best combination the player has at that point, with margin.** With the
  base kit that is 6 m (triple jump); with the feather it is about 8 m (triple, then feather). Keep
  gated ledges at least 1.5 m above it. Abilities stack, so recheck every gate when a new one
  arrives or a tunable changes. A cheap guard: keep gate heights on a ScriptableObject and add an
  EditMode test that checks each one against `MovementConfig`.
- **Horizontal gaps** depend on air time, which these heights don't give. Measure them in a
  **metrics gym** before greyboxing: a strip of ledges at 1, 2, 2.8, 4, 5, 6, 7 and 8 m and gaps
  from 2 to 12 m, in TrainingStage or its own scene. Note what each move and each unlock clears.
- **The triple jump needs a runway.** A perch with no run-up can't be triple-jumped from. That
  works as a gate, but it's fragile; prefer height.

### Pipeline

1. **Paper:** this document.
2. **Metrics gym:** as above. Half a day, and it saves weeks.
3. **Greybox in Unity:** the whole core in grey boxes with ProBuilder, the real chibi and the real
   numbers. Gates are coloured boxes, Birdy is Birdy. Play it, move things, play it again, until
   the first ten minutes are fun with no art at all.
4. **Look-dev with AI:** paint over greybox screenshots with an image model. Export the greybox as
   GLB into Marble's **Chisel**, which keeps your layout and styles it from a prompt, and walk the
   result to judge the mood. Throw away freely.
5. **Production:** a modular kit (columns, arches, stairs, wall runs, roofs) built to the greybox's
   dimensions in Blender or ProBuilder; props and statues from Tripo through the existing pipeline;
   painterly materials. **Collision stays the greybox**, so it is exact. The far islands, the sky
   and the black sun come from Marble as a panorama or a collider-less backdrop mesh.
6. **Light and polish:** the golden-hour sun, haze layers, construction-line VFX, and the
   fraying-rim shader.

**Repo notes.**
- CodingStandards §4 has setup menus create every scene object. Hand-built level geometry
  doesn't fit that rule, so make the exception explicit: the geometry lives in its own scene
  (`AcademyHub.unity`) or prefabs that no setup menu rebuilds. Everything with references
  (player, gates, pickups, Birdy) is still placed by a `Setup Academy Hub` menu. Setup Chibi
  Sandbox wipes what later menus add, so keep the hub out of its reach.
- ProBuilder (`com.unity.probuilder`) isn't installed. Adding it recompiles the shared Editor, so
  ask the agent-manager session first.

### Why not generate the whole playable world

The current academy is a World Labs (Marble) export, and the cleanup commits (`d9f8f05`,
`a6f45d5`, `a6395f4`) record what that cost:

- lighting baked into the texture, which needed a custom shader;
- 22,424 triangles of black specks;
- 1,728 floating shards;
- a collider that sat a median 23 cm above the visible grass, so it was rejected;
- one 565k-triangle mesh, with a 180 MB GLB and an 8K texture.

The deeper problem is that a generated world can't be edited the way a level must be. You can't
raise a ledge by 50 cm to sit above the triple jump, open a door, turn an orrery or seal a mosaic.
Tencent's HY-World 2.0 (open source since April 2026, mesh and Gaussian-splat export) produces the
same kind of output, so switching generators changes the vendor, not the problem. Use these tools
where their weaknesses don't matter: the look, and the far distance.

| Job | Tool | Why |
|---|---|---|
| Find the look of each region | Any image model, fed greybox screenshots | Fastest way to find a style; free to throw away |
| See your layout as a styled place | Marble Chisel (greybox GLB plus a style prompt) | Keeps your structure and adds the look |
| Skybox, far islands, black sun | Marble panorama or backdrop mesh, no collision | Baked light and specks are invisible at 200 m |
| Props, statues, hero pieces | Tripo (`Docs/Art/TripoAssets.md`) | Already proven here |
| Modular kit | Blender (Claude can drive it through the Blender MCP) or ProBuilder | Needs exact dimensions to sit on the greybox |
| The ground you play on | Built by hand | Exact heights, moving parts, collision you can trust |

### Prompt kit

Keep the style line identical across every region so the regions match.

- **Style line:** *stylised hand-painted game environment, anime-inspired painterly shading,
  saturated colours, chunky simplified shapes with softly rounded edges, warm late-afternoon
  sunlight, soft atmospheric haze, clean readable silhouettes.*
- **Avoid:** *photoreal stone, fine noise, grime, text, people.*

| Region | Prompt (add the style line) |
|---|---|
| Courtyard | A sunlit Greek academy courtyard on a floating island: bright green lawn, a ring of cream marble columns under terracotta roofs, a round stone well, faint gold compass arcs and ruled lines floating in the air. |
| Overlook | The broken northern edge of a floating Greek island: marble paving cracks into floating cubes and triangles that thin into ink sketch lines and blank parchment; a perfectly black sun with a thin white ring hangs low in the sky; distant floating islands curve away on a spiral into haze. |
| Gnomon Plaza | A round plaza paved as a giant sundial with gold hour lines, a tall right-triangle gnomon at its centre, eight empty marble plinths in an octagon around it. |
| Broken Bridge | Across a chasm, a grand indigo-and-silver library façade with star-chart domes; its stone bridge has collapsed; faint soot stains; seen from a sunny Greek terrace. |

## 8. The greybox

**Tools ▸ Pickleball ▸ Build Academy Greybox** (`Scripts/Editor/AcademyGreyboxSetup.cs`) builds
`Scenes/AcademyGreybox.unity`. It copies ChibiSandbox and strips the academy model, Newton, the
pickups and the anchors, so the player, camera, HUD, dialogue and Birdy are wired exactly as there.
It then builds this map in grid-shaded boxes, so heights read off the surfaces. The scene is
regenerated on every run: change the numbers in the script, not the scene.

| Gate | Built as | Number |
|---|---|---|
| Birdy's Perch | The bell tower's top | 7 m above the 5 m roof (base kit 6 m, with the feather about 8 m) |
| Broken Bridge | A bridge stub, then the library's edge | 14 m gap (the triple jump carries 11.2 m) |
| Orrery Chasm | Three brass planets as tether anchors, 7 m apart | 26 m gap; tether range 9 m |
| Mirror Court | A glass wall and 12 m walls round the far half | — |
| Nautilus Tower | Spiral ledges to 8.75 m under a 24 m-wide overhang, more ledges to the observatory at 26.85 m | — |
| The Well | A grate | — |
| Delta Pylons | Four islets with pylons, joined by drawn graph edges | 16 m gaps |
| Surveyor's Gate, side doors | Doorways filled with a navy seal | — |
| Zeno's Bridge | Halving planks behind a see-through violet veil in a gateway, so the view north stays open | — |

- **Playing it.** Backspace respawns, and so does falling off. Shift+Backspace also takes every
  ability away. The top-right readout gives speed, height above the courtyard lawn, and each
  jump's rise, carry and air time.
- **Pickups.** Each Guardian's reward stands at that Guardian's door, using today's stand-ins: the
  Dash for Angle Dash and the Tether for Orbital Tether. The Gymnasium's debug shelf holds every
  pickup, for testing gates out of order.
- **Gates for unbuilt abilities.** Equation Swap, Spiral Jump, Network Step, Symmetry Break and
  Limit Approach don't exist yet, so their gates can't be opened. The orrery's planets don't
  orbit yet.

## 9. Questions this raises

1. **The name.** Is "the Margin" right, and does Birdy's "Guardian of Mathematics" mean the
   student is a new keeper of it?
2. **Does the hub change as Guardians fall?** The plinth statues are cheap to build. Further
   fraying at the rim costs more.
3. **Abilities that need hub versions.** Orbital Tether has to latch onto the orrery's moving
   planets, not just projectiles. Equation Swap needs mirror axes placed in the world, not only an
   arena centre. The Underside needs Gravity Anchor to hold inverted gravity while you walk. Each is
   work for the system's owner.
4. **Newton** paces the courtyard in the current build. In this layout he waits behind his
   observatory door, and his fight needs its own scene.
5. **Birdy's feather and the skateboard** become hub pickups here. Do you want them tied to
   Guardians instead?
6. **Strict chain or partly open?** See §5.
