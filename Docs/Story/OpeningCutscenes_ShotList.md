# Opening Cutscenes: Shot List

Draft, 29 October 2026. The director's shot list for the six opening scenes, from waking in the
white void to the hand-off into gameplay at the south crest.

- **Purpose:** a camera agent builds Cinemachine 3 shots on a Timeline straight from the tables
  below; an animation agent maps the action verbs to clips; a reviewer checks recordings against
  the framing and timing columns.
- **Source script (the source of truth for every line):** `Docs/Story/OpeningCutscenes.md`.
- **Also read:** `Docs/Story/StoryBrief.md` (characters, tone), `Docs/World/TheAcademy.md`
  (art direction, map, the black sun).
- **Status:** every position, offset and timing here is a proposal until the sets are greyboxed.

## 1. Conventions

### Line ids and durations

- **Line id** = `OC_S<scene>_<nn>`, numbered in script order within each scene. Every voiced line
  in a `text` block has exactly one id. 65 lines in all.
- **Display text** = the raw line with every `[tag]` removed (mid-line tags too), whitespace
  collapsed, trimmed. `...` is kept as written.
- **Placeholder duration** = max(1.5 s, chars / 15) + 0.3 s, where chars = length of the display
  text. The +0.3 s is the inter-line gap: nothing else is added between consecutive lines.
- **Subtitle cards:** at most 2 lines × 42 characters per card, wrapped at word boundaries. A line
  that cannot wrap that way (every line over 84 chars, plus OC_S3_08) is split on sentence
  boundaries into cards a/b under the same line id (see §3).
- **Holds** are silent timeline gaps placed *before* the line's audio clip, never baked into it.
  Trim any lead-in silence that ElevenLabs generates for a `[pause]` tag.

  | Tag | Hold |
  |---|---|
  | `[pause]` (comic, any scene) | 0.8 s |
  | `[pause]` on Birdy's scene-6 lines (his cheer slips) | 1.0 s |
  | `[beat]` | 0.6 s |
  | `[long pause]` | 2.0 s static hold |
  | mid-line `[pause]` (OC_S2_09) | 0.8 s between subtitle cards a and b |

- **Shot duration** = covered lines' placeholder durations + holds + wordless action time; each
  table states the breakdown. When real audio lands, re-time each shot as audio + the same holds
  + the same action time.
- **Never cut on a punchline** (the cut never lands in the 0.5 s after a joke's last word) except
  where the direction says so: the smash cut to "Fuck yeah. Isekai!" and the cut on "What's a
  taxes?".

### Anchor and marker vocabulary

- **Character anchors** (on each stand-in slot): `MC.Head`, `MC.Eyes`, `MC.Hand_R`, `MC.Feet`;
  `Birdy.Head`, `Birdy.Eyes`, `Birdy.Hand_R` (wing tip), `Birdy.Feet`, `Birdy.PropellerSocket`.
- **Proposed extra anchor:** `MC.Blade`, the compass-blade's guard, wherever the blade is worn.
- **Derived points:** `MC.Mid` = lerp(MC.Feet, MC.Head, 0.5). `Pair.Mid` = midpoint of MC.Head and
  Birdy.Head. `Pair.Side` = the horizontal direction perpendicular to the MC→Birdy line, on the
  side the shot names (default: the side facing the south-west sun).
- **Sizes** (reported by the slots, never hard-coded): **H** = MC height (nominal 1.6 m), **h** =
  Birdy height (nominal 1.0 m: his rotor comes up to MC's chin, per StoryBrief §5 and Npc.md),
  **HB** = Birdy's head-box height (nominal 0.55h ≈ 0.55 m; Birdy is mostly head). Every Birdy
  distance is in HB, so the framings hold at any real size.
- **Offsets** are written `A + (x, y, z)·H` in character A's local frame: x = A's right, y = up,
  z = A's forward. Example: `MC.Head + (0.35, 0.1, −0.55)·H` is behind MC's right shoulder. Birdy
  distances are written in HB: `Birdy.Head + 10·HB front` = 10 HB along Birdy's forward.
- **Birdy's hover height** (propeller working): Birdy.Head at MC.Feet + 0.80·H (about MC's eye line),
  0.5·H to MC's right unless the shot says otherwise.
- **Aim** = the Cinemachine Rotation Composer target, with its screen position `(sx, sy)` (0..1 from
  bottom-left; (0.5, 0.5) is centre).

### Lens convention

Lens is vertical FOV in degrees, with the equivalent focal length on a 24 mm-high sensor
(f = 12 / tan(vFOV/2)): 75° = 15.6 mm, 60° = 20.8 mm, 55° = 23.1 mm, 50° = 25.7 mm,
45° = 29.0 mm, 40° = 33.0 mm, 35° = 38.1 mm, 30° = 44.8 mm, 27° = 50.0 mm, 25° = 54.1 mm.
Composition is for 16:9; wider phones gain width only (vertical FOV is fixed).

### Safe frame and the automated check

- **Safe frame** = the central 90% × 90% of the 16:9 frame.
- Each shot names its **subject anchor(s)**; that anchor stays inside the safe frame for the whole
  shot. The automated check verifies `<subject>.Head`.
- Shots marked **INSERT** or **NO-CHAR** frame an object; they name the object anchor instead and
  are exempt from the Head check.

### Birdy tight close-up rule (TCU)

- `frac` = HB / (2 · d · tan(vFOV/2)), where d is the distance from the camera to Birdy.Head along
  the camera's forward. It is HB's share of the frame height.
- **TCU** = frac ≥ 0.40 **and** Birdy.Eyes inside the frame.
- **Banned band:** 0.30 ≤ frac < 0.40, so every Birdy shot reads clearly as either a TCU or not.
  The only exception is the 0.5 s pull-out in S6_SH15.
- **Allowed TCUs:** S6_SH05, S6_SH08, S6_SH10, and the first 1.0 s of S6_SH15 (Birdy's scene-6
  `[pause]` lines). Every other Birdy framing has frac < 0.30. Each Birdy shot below states its
  frac.
- An INSERT that keeps Birdy.Eyes out of frame (S3_SH02) is not a TCU, whatever its frac.
- **Check:** compute frac per frame; fail if frac ≥ 0.30 outside the allowed shots.

### Camera types (Cinemachine 3 terms)

| Type | Build |
|---|---|
| Locked | `CinemachineCamera` with no Body; fixed position; Rotation Composer or fixed rotation |
| Dolly / track | `CinemachineSplineDolly` on a spline |
| Crane | `CinemachineSplineDolly` on a spline with a vertical component |
| Handheld-ish | `CinemachineBasicMultiChannelPerlin` (6D shake / handheld profile), amplitude stated |
| Orbit | `CinemachineOrbitalFollow`, animated horizontal axis |
| Tracking | `CinemachineFollow` (offset in the target's frame) + Rotation Composer |
| Rack focus | URP Volume Depth of Field (Bokeh); focus distance animated, or `CinemachineAutoFocus` with an animated target. Focal length matches the lens. |

### Cut and blend notation

| Notation | Meaning |
|---|---|
| CUT | hard cut, 0 s |
| SMASH | hard cut landing on the line's first audio frame, with an audio hit |
| JUMP | hard cut with a time and place skip |
| BLEND n s | Cinemachine blend, EaseInOut, n seconds |
| MATCH | the next camera starts on this shot's exact end pose (an invisible cut) |
| VFX | a visual-effect transition, described in the shot |
| → GAMEPLAY | hand-off to the gameplay camera and player input |

## 2. Sets and markers

**Staging (proposal):** the script's opening "courtyard" cannot be the Academy courtyard, because
scene 5 reveals the Academy from a crest. So the opening is staged at the **Outer Shrine**: a small
ruined shrine (a lawn circle, a few broken columns) in a meadow **south** of the Academy plateau.
An olive path climbs north from it to a crest on the plateau's south rim. From the crest you look
north over the whole Academy, with the gnomon at the centre and the black sun beyond.

Coordinates: X east, Z north, Y up, metres; the Academy lawn is y = 0. All positions are
**proposals** to verify in greybox, especially the crest's sightline over the stoa roofs to the
north rim.

> **Built positions differ.** The sets build moved several markers to fit the greybox: the shrine
> sits clear of the Delta Pylons, the ledge is at the pier corner you can see from below, and the
> crest is 1 m higher to clear the stoa roof. MC_Start is now (0, 0, −96) and Propeller_Ledge is
> (−8, 5, −84.9). `Docs/Art/CutsceneSets.md` has the full table. Shots always use the markers, so
> the numbers below are history.

| Set (prefab) | Where it sits | Markers (proposed world position) |
|---|---|---|
| **OuterShrine** (has a *Void* state for scene 1 and a *Day* state) | Meadow south of the plateau, centred ≈ 50 m south of the south rim, y = 0 | `Shrine_Center` (−4, 0, −100). `MC_Start` (−4, 0, −100): MC lies on his back, head toward +Z, and stands facing +Z. `Birdy_SkyEntry` (4, 14, −88): Birdy enters the frame here. `Prop_Separation` (1, 9, −92): the propeller pops off. `Birdy_Land` (0, 0, −95): Birdy bounces, ≈ 5 m north-north-east of MC_Start. `Propeller_Ledge` (−13, 5, −88): the top of a broken propylon, 5 m up, about 15 m north-north-west of MC_Start, with the cream Greek-key trim of a standable ledge, reached by a chain of steps of 2 m or less (`Ledge_Step_1..3`, owned by level design). Visible from MC_Start and from the meadow. `Ledge_View` (−6, 2.2, −96): where S2_SH02 settles, the player's-eye view of the ledge. `MC_Handover` (−11, 0, −91), at the ledge foot, facing +X. `Birdy_Handover` = MC_Handover + 0.65·H along MC's forward. `Path_Start` (0, 0, −88) |
| **OlivePath** | Winds north from the shrine up the slope to the crest, ≈ 40 m long, between olive trees | `Path_Start` (0, 0, −88). `Path_Track_A` (0, 1, −84) to `Path_Track_B` (−3, 8, −60): the 25 m stretch scene 4 walks, a spline. `Cam_Track_S4`: a spline parallel to that stretch, 3.2·H to the walkers' right (east) and 0.75·H above the path. Olive trunks stand between Cam_Track_S4 and the path every 4–6 m, 1–2 m from the camera, for foreground parallax. `Path_End` (−4, 9.5, −56) |
| **SouthCrest** | A grassy rise on the plateau's south rim, y ≈ 10; its north side falls to the Academy lawn | `Crest_View` (−4, 10, −53): MC's stop mark, facing +Z. MC later turns to face the black sun, a bearing of ≈ 22° east of north from here. `Birdy_Crest` = Birdy's hover mark at MC's right shoulder. `Birdy_Guide` = MC.Feet + (0.3, 0, 0.9)·H, at hover height, facing MC. `Zip_Path` (8, 2, −30): down the crest's north side into the Academy, a bearing of ≈ 27° from Crest_View |
| **Academy vista** (existing world, not a new set) | The plateau, x −36..40, z −50..50 | `Gnomon_Aim` (0, 8, 36): the gnomon's mid-height in Gnomon Plaza. `Rim_Gaze` (30, 8, 64): the fraying shards above the Overlook near Zeno's Bridge, a bearing of ≈ 16° from Crest_View. `BlackSun` (183, 130, 411): the sky object's centre, ≈ 500 m from Crest_View at ≈ 13.5° elevation |

Light: golden hour throughout scenes 2–6; the key is from the south-west (azimuth 225°,
elevation ≈ 15°), warm, with long shadows and layered haze. Scene 4 adds dappled olive shade.

## 3. Line index

Hold = the silent gap placed before the line (or inside it, for OC_S2_09). Card timings are
proportional to characters.

| Id | Speaker | Raw text (with tags) | Display text | Chars | Dur (s) | Hold | Subtitle cards | Shot |
|---|---|---|---|---|---|---|---|---|
| OC_S1_01 | MC | [groggy] ...Okay. White void. I'm dead. Probably dead. | ...Okay. White void. I'm dead. Probably dead. | 45 | 3.30 | — | 1 | S1_SH01 |
| OC_S1_02 | MC | [pause] Wait. | Wait. | 5 | 1.80 | 0.8 | 1 | S1_SH02 |
| OC_S1_03 | MC | [slowly, then building] Wait wait wait. White void. No body pain. Weird calm feeling. | Wait wait wait. White void. No body pain. Weird calm feeling. | 61 | 4.37 | — | 1 | S1_SH03 |
| OC_S1_04 | MC | [excited, shouting] Fuck yeah. Isekai! | Fuck yeah. Isekai! | 18 | 1.80 | — | 1 | S1_SH04 |
| OC_S1_05 | MC | [eager] Okay. Okay okay okay. Status screen. Status! Open status. | Okay. Okay okay okay. Status screen. Status! Open status. | 57 | 4.10 | — | 1 | S1_SH05 |
| OC_S1_06 | MC | [pause] ...Menu? | ...Menu? | 8 | 1.80 | 1.0 (the "Status!" hold) | 1 | S1_SH05 |
| OC_S1_07 | MC | [flat] Cool. No menu. Hard mode. Love that for me. | Cool. No menu. Hard mode. Love that for me. | 43 | 3.17 | — | 1 | S1_SH06 |
| OC_S2_01 | BIRDY | [panicked] No no no no no. Where'd it go? Where did it go?! | No no no no no. Where'd it go? Where did it go?! | 48 | 3.50 | — | 1 | S2_SH03 |
| OC_S2_02 | BIRDY | [panicked] My propeller! It was right on my head! It's always on my head! | My propeller! It was right on my head! It's always on my head! | 62 | 4.43 | — | 1 | S2_SH03 |
| OC_S2_03 | MC | [quietly, to himself] ...What is this guy on about? | ...What is this guy on about? | 29 | 2.23 | — | 1 | S2_SH04 |
| OC_S2_04 | BIRDY | [spotting MC, suddenly very close] You! Hi! Sorry. Hi. Emergency. | You! Hi! Sorry. Hi. Emergency. | 30 | 2.30 | — | 1 | S2_SH05 |
| OC_S2_05 | BIRDY | [pleading] Can you help me find my propeller? Please? I can't fly without it. I can barely stand without it. | Can you help me find my propeller? Please? I can't fly without it. I can barely stand without it. | 97 | 6.77 | — | **a** (0.00–2.83 s): "Can you help me find my propeller? Please?" (42) / **b** (2.83 s–end): "I can't fly without it. I can barely stand without it." (54) | S2_SH06 |
| OC_S2_06 | MC | [sighs] Okay, okay. I'll help. Where'd you last have it? | Okay, okay. I'll help. Where'd you last have it? | 48 | 3.50 | — | 1 | S2_SH07 |
| OC_S2_07 | BIRDY | [certain] The sky! I had it in the sky! | The sky! I had it in the sky! | 29 | 2.23 | — | 1 | S2_SH08 |
| OC_S2_08 | MC | [flat] That's... not helpful. | That's... not helpful. | 22 | 1.80 | — | 1 | S2_SH08 |
| OC_S2_09 | BIRDY | [eager] It's round! It spins! It's about this big! [pause] I can't show you how big. My wings are very small. | It's round! It spins! It's about this big! I can't show you how big. My wings are very small. | 93 | 6.50 | 0.8 mid-line, between a and b | **a** (0.00–2.83 s): "It's round! It spins! It's about this big!" (42), stays up through the 0.8 s hold / **b** (3.63 s–end): "I can't show you how big. My wings are very small." (50) | S2_SH09 |
| OC_S2_10 | MC | [sighs] Great. First quest. Fetch a bird his hat. | Great. First quest. Fetch a bird his hat. | 41 | 3.03 | — | 1 | S2_SH10 |
| OC_S3_01 | BIRDY | [gasps, overjoyed] My propeller! You found it! Oh, thank you, thank you, thank you! | My propeller! You found it! Oh, thank you, thank you, thank you! | 64 | 4.57 | — | 1 | S3_SH03 |
| OC_S3_02 | BIRDY | [proud] And we're back, baby. One perfect circle, spinning very, very fast. | And we're back, baby. One perfect circle, spinning very, very fast. | 67 | 4.77 | — | 1 | S3_SH04 |
| OC_S3_03 | BIRDY | [pause, staring at MC's back] Wait. | Wait. | 5 | 1.80 | 0.8 | 1 | S3_SH05 |
| OC_S3_04 | BIRDY | [slowly] Wait wait wait. That sword. That's a compass-blade. | Wait wait wait. That sword. That's a compass-blade. | 51 | 3.70 | — | 1 | S3_SH06 |
| OC_S3_05 | BIRDY | [awed whisper] You're... you're the one. Aren't you? | You're... you're the one. Aren't you? | 37 | 2.77 | — | 1 | S3_SH07 |
| OC_S3_06 | MC | [confused] The one what? | The one what? | 13 | 1.80 | — | 1 | S3_SH07 |
| OC_S3_07 | BIRDY | [excited] The Guardian! I've been looking everywhere for you! | The Guardian! I've been looking everywhere for you! | 51 | 3.70 | — | 1 | S3_SH08 |
| OC_S3_08 | BIRDY | [pause] Well, not everywhere. I crashed. But before I crashed, I was definitely looking. | Well, not everywhere. I crashed. But before I crashed, I was definitely looking. | 80 | 5.63 | 0.8 | **a** (0.00–2.25 s): "Well, not everywhere. I crashed." (32) / **b** (2.25 s–end): "But before I crashed, I was definitely looking." (47). Split although ≤ 84 chars: no word break fits 2 × 42. | S3_SH08 |
| OC_S3_09 | MC | I have no idea what you're talking about. | I have no idea what you're talking about. | 41 | 3.03 | — | 1 | S3_SH09 |
| OC_S3_10 | BIRDY | [committed] The prophecy! The ancient texts! The... | The prophecy! The ancient texts! The... | 39 | 2.90 | — | 1 | S3_SH10 |
| OC_S3_11 | BIRDY | [pause, losing steam] ...the thing a guy told me about once. | ...the thing a guy told me about once. | 38 | 2.83 | 0.8 | 1 | S3_SH11 |
| OC_S3_12 | MC | [resigned, under his breath] Is this seriously my fucking companion for this world? | Is this seriously my fucking companion for this world? | 54 | 3.90 | — | 1 | S3_SH12 |
| OC_S3_13 | BIRDY | [small, a little hurt] ...I can hear you. I have ears. They're just inside the helmet. | ...I can hear you. I have ears. They're just inside the helmet. | 63 | 4.50 | — | 1 | S3_SH13 |
| OC_S3_14 | BIRDY | [pause, then bouncing back] But that's okay! First impressions are just rough drafts! | But that's okay! First impressions are just rough drafts! | 57 | 4.10 | 0.8 | 1 | S3_SH13 |
| OC_S4_01 | MC | [casual] Okay. Rules. Do I need to eat here? Sleep? | Okay. Rules. Do I need to eat here? Sleep? | 42 | 3.10 | — | 1 | S4_SH01 |
| OC_S4_02 | BIRDY | [cheerful] Yes and yes! | Yes and yes! | 12 | 1.80 | — | 1 | S4_SH01 |
| OC_S4_03 | MC | Taxes? | Taxes? | 6 | 1.80 | — | 1 | S4_SH01 |
| OC_S4_04 | BIRDY | [pause] What's a taxes? | What's a taxes? | 15 | 1.80 | 0.8 (held in S4_SH01; the cut lands on the line) | 1 | S4_SH02 |
| OC_S4_05 | MC | [content] God, I love it here. | God, I love it here. | 20 | 1.80 | — | 1 | S4_SH02 |
| OC_S4_06 | MC | [casual, testing the waters] So like... do I even need to worry about STDs in here? | So like... do I even need to worry about STDs in here? | 54 | 3.90 | — | 1 | S4_SH03 |
| OC_S4_07 | BIRDY | [genuinely confused] S-T-D? Is that like... a bird thing? | S-T-D? Is that like... a bird thing? | 36 | 2.70 | — | 1 | S4_SH03 |
| OC_S4_08 | MC | [beat] Never mind. | Never mind. | 11 | 1.80 | 0.6 | 1 | S4_SH03 |
| OC_S5_01 | BIRDY | [grand, gesturing] And here we are! The Academy! | And here we are! The Academy! | 29 | 2.23 | — | 1 | S5_SH01 |
| OC_S5_02 | MC | [awed] Whoa. It's huge. | Whoa. It's huge. | 16 | 1.80 | — | 1 | S5_SH01 |
| OC_S5_03 | BIRDY | [chuckles] That's what she said. | That's what she said. | 21 | 1.80 | — | 1 | S5_SH02 |
| OC_S5_04 | MC | [pause, turning to him] Wait. You didn't know what an STD was five minutes ago. | Wait. You didn't know what an STD was five minutes ago. | 55 | 3.97 | 0.8 | 1 | S5_SH03 |
| OC_S5_05 | BIRDY | [completely earnest] Oh! No, I have lots of sex. I just... I got them all. | Oh! No, I have lots of sex. I just... I got them all. | 53 | 3.83 | — | 1 | S5_SH03 |
| OC_S5_06 | MC | [long pause] ... | ... | 3 | 1.80 | 2.0 static (the line plays inside the hold, see S5_SH03) | 1 ("...") | S5_SH03 |
| OC_S5_07 | BIRDY | [cheerfully] I don't think they're a problem though! | I don't think they're a problem though! | 39 | 2.90 | — | 1 | S5_SH03 |
| OC_S6_01 | MC | [quiet, serious] Hey. What's happening over there? | Hey. What's happening over there? | 33 | 2.50 | — | 1 | S6_SH01 |
| OC_S6_02 | MC | Why is it... coming apart like that? | Why is it... coming apart like that? | 36 | 2.70 | — | 1 | S6_SH02 |
| OC_S6_03 | BIRDY | [bright, a little too quickly] Oh! That. Yeah. That's the bad part. | Oh! That. Yeah. That's the bad part. | 36 | 2.70 | — | 1 | S6_SH03 |
| OC_S6_04 | BIRDY | [upbeat, like a tour guide] Okay! So. This world has Guardians. Eight of them. | Okay! So. This world has Guardians. Eight of them. | 50 | 3.63 | — | 1 | S6_SH04 |
| OC_S6_05 | BIRDY | The greatest minds that ever lived. Each one keeps a big idea safe. Angles. Stars. Gravity. That kind of thing. | The greatest minds that ever lived. Each one keeps a big idea safe. Angles. Stars. Gravity. That kind of thing. | 111 | 7.70 | — | **a** (0.00–4.51 s): "The greatest minds that ever lived. Each one keeps a big idea safe." (67) / **b** (4.51 s–end): "Angles. Stars. Gravity. That kind of thing." (43) | S6_SH04 |
| OC_S6_06 | BIRDY | [pause] But lately, math hasn't been mathing. | But lately, math hasn't been mathing. | 37 | 2.77 | 1.0 | 1 | S6_SH05 |
| OC_S6_07 | BIRDY | [cheerful again] They've all been corrupted by something. And the world's been coming apart at the edges. | They've all been corrupted by something. And the world's been coming apart at the edges. | 88 | 6.17 | — | **a** (0.00–2.70 s): "They've all been corrupted by something." (40) / **b** (2.70 s–end): "And the world's been coming apart at the edges." (47) | S6_SH06 |
| OC_S6_08 | BIRDY | [excited] So you, my friend, are going to set them straight! | So you, my friend, are going to set them straight! | 50 | 3.63 | — | 1 | S6_SH06 |
| OC_S6_09 | MC | But how did this even happen? Why are they corrupted? | But how did this even happen? Why are they corrupted? | 53 | 3.83 | — | 1 | S6_SH07 |
| OC_S6_10 | BIRDY | [pause] I'm... not really sure. It's just been like this for a while. | I'm... not really sure. It's just been like this for a while. | 61 | 4.37 | 1.0 | 1 | S6_SH08 |
| OC_S6_11 | MC | How long's a while? | How long's a while? | 19 | 1.80 | — | 1 | S6_SH09 |
| OC_S6_12 | BIRDY | [pause, softer] Long enough that people stopped asking. | Long enough that people stopped asking. | 39 | 2.90 | 1.0 | 1 | S6_SH10 |
| OC_S6_13 | BIRDY | [bright] But that's why you're here! | But that's why you're here! | 27 | 2.10 | — | 1 | S6_SH11 |
| OC_S6_14 | MC | Why me, though? | Why me, though? | 15 | 1.80 | — | 1 | S6_SH12 |
| OC_S6_15 | BIRDY | That compass you wield. Only navigators carry those. | That compass you wield. Only navigators carry those. | 52 | 3.77 | — | 1 | S6_SH12 |
| OC_S6_16 | BIRDY | [gently] You were a navigator. Somewhere else. Some other world. | You were a navigator. Somewhere else. Some other world. | 55 | 3.97 | — | 1 | S6_SH13 |
| OC_S6_17 | MC | [confused] Huh? | Huh? | 4 | 1.80 | — | 1 | S6_SH13 |
| OC_S6_18 | BIRDY | [warm] You'll figure it out as you go. Just know that this world needs you. | You'll figure it out as you go. Just know that this world needs you. | 68 | 4.83 | — | 1 | S6_SH14 |
| OC_S6_19 | BIRDY | [pause, then excited] And hey! We're gonna do this together! | And hey! We're gonna do this together! | 38 | 2.83 | 1.0 | 1 | S6_SH15 |

Four lines exceed 84 characters and split: OC_S2_05, OC_S2_09, OC_S6_05 and OC_S6_07. Card a of
OC_S6_05 (67 chars) wraps to two lines at "lived. / Each".

## 4. Scenes

### Scene 1: The white void (OuterShrine, Void state)

- **Look:** pure white, shadowless. Ambient flat white, skybox and fog colour #FFFFFF, dense
  exponential fog so there is no horizon and no ground line. Directional light off, shadows off,
  MC lit by flat ambient plus a soft top fill. MC lies at `MC_Start`; the shrine geometry is hidden
  in the Void state.
- **Sound:** total silence until OC_S1_01; no room tone, no music. A short whoosh-hit on the smash
  cut (S1_SH04). The transition (S1_SH07) brings a rising shimmer, pen and compass scratching as
  the lines draw, then birdsong, breeze and a warm pad as the colour floods.
- **Continuity:** scene 1 runs continuously into scene 2 (S1_SH07 ends on the pose S2_SH01 starts
  from).

| Shot | Lines | Camera | Lens | Movement (start → end) | Framing (subject in safe frame) | Action | Out | Dur (s) |
|---|---|---|---|---|---|---|---|---|
| S1_SH01 | OC_S1_01 | Locked, slow push (vertical dolly) | 40° (33 mm) | Straight down from `MC.Mid + 2.6·H up` → `MC.Mid + 2.0·H up`, ease-in-out over the whole shot. The frame's up vector is MC's forward, so his head is at the top of the frame. | Top-down full body, spread-eagle, MC.Mid at (0.5, 0.5); MC.Head ends at about (0.5, 0.84). Subject: **MC.Head** | MC: lie still (eyes closed, spread-eagle), groggy lip-sync | CUT | 3.0 wordless (white silence, push) + 3.30 = **6.30** |
| S1_SH02 | OC_S1_02 | Locked extreme close-up, top-down | 25° (54 mm) | `MC.Eyes + 0.55·H` along the face normal (straight up); a 3% micro push is allowed | ECU on both eyes, the eye line across the middle third, MC.Eyes at (0.5, 0.5). Subject: **MC.Head** (the head centre stays in frame) | MC: eyes open (a slow blink-open during the 0.8 s hold), pupils dart on "Wait." | CUT | 0.8 hold + 1.80 = **2.60** |
| S1_SH03 | OC_S1_03 | Dolly (side-on, low) | 40° (33 mm) | Starts beside MC's hip, `MC_Start + 1.6·H to MC's right, 0.25·H up`, aimed at MC.Head. As he sits up, it dollies in to 1.2·H and pedestals up to MC.Head height, ease-in. Aim MC.Head at (0.45, 0.65). | Medium; at the end, a seated mid-shot at eye level. Subject: **MC.Head** | MC: sit up (slow, 2.0 s, from "Wait wait wait"), look around (head left and right on "White void. No body pain."), hands to chest, building, on "Weird calm feeling." | SMASH | **4.37** |
| S1_SH04 | OC_S1_04 | Low-angle wide, locked, with a handheld shake burst | 55° (23 mm) | `MC.Feet + (0.3, 0.12, 2.6)·H` (in front, near the ground), tilted up about 20°, aim MC.Head at (0.5, 0.62). Perlin 6D shake: amplitude 0 → 1.2 (frequency 2.0) over 0.1 s at "Isekai!", held 0.4 s, decaying to 0 over 0.6 s. | Low-angle full figure against white, feet in the bottom 15%. Subject: **MC.Head** | MC: fist pump. The stand-up is cheated across the smash cut, so the shot opens with MC already on his feet at the top of a spring-up; the pump hits on "Isekai!" with a knee pop. | CUT | 1.80 + 0.6 shake decay = **2.40** |
| S1_SH05 | OC_S1_05, OC_S1_06 | Over-the-shoulder, locked, light handheld (amplitude 0.2) | 40° (33 mm) | `MC.Head + (−0.6, 0, −0.9)·H` (behind and wide of his **left** shoulder, review round 3); aim the empty air ahead and left of the hand at (0.2, 0.5), so the hand sits near frame centre and his head is at the right. **Changed in review round 2:** he reaches with his free left hand, because the sword in his right hand pointed up into the over-the-shoulder frame. | OTS medium: MC's left cheek and shoulder at frame right, outstretched Hand_L at about (0.45, 0.5), empty white beyond. Subject: **MC.Head** (x ≤ 0.9) | MC: reach out with the left hand (arm extends on "Status screen"), poke air twice ("Status!", "Open status."), then freeze, arm out. **Hold 1.0 s on the hand and the empty air** (this is also OC_S1_06's `[pause]`). Then one small poke and a head tilt on "...Menu?" | CUT | 4.10 + 1.0 hold + 1.80 = **6.90** |
| S1_SH06 | OC_S1_07 | Locked MCU | 30° (45 mm) | `MC.Head + (0.1, 0, 1.5)·H` (in front); aim MC.Eyes at (0.5, 0.6). On "Hard mode." MC looks at a point 0.1·H left of the lens, about 4° off the lens axis: almost into camera. | MCU, head and shoulders, white behind. Subject: **MC.Head** | MC: lower arm (drops the hand on "Cool. No menu."), glance to lens (head and eyes turn to just beside camera on "Hard mode."), flat stare held to the end | VFX (into S1_SH07, same camera) | 3.17 + 0.8 deadpan hold = **3.97** |
| S1_SH07 | — (wordless) | Crane + orbit (pull back and up) | 40° → 50° (33 → 26 mm) | Starts on S1_SH06's end pose. Over 6.0 s it cranes back, up and 180° around MC, ending at `MC.Feet + (0.3, 1.5, −3.4)·H` (behind him, looking north over the shrine toward the olive slope), aim MC.Head at (0.5, 0.4). Ease-in-out. | MCU → wide establishing shot: MC centred from behind, the shrine's columns and lawn around him, the olive hill beyond. Propeller_Ledge may be in frame but is not emphasised. Subject: **MC.Head** | MC: look around in wonder (slow head turns), turning slowly to face +Z (north) over 1.0–5.8 s. **Changed in review round 3:** the turn spans the crane, so the 180° reads over the shot; at 4.0–5.5 s it squeezed into ~0.6 s on screen. **VFX:** 0.0–1.0 s the white swells (exposure +1 stop, bloom up). 1.0–4.0 s gold construction lines draw the shrine: compass arcs sweep the lawn circle, ruled lines rise into column shafts, tick marks pace out the ring, and one ruled curve draws the olive hill's profile. 4.0–6.0 s colour floods outward from MC's feet (a radial lawn-green wipe), the sky turns cerulean, the south-west sun fades in (shadow strength 0 → 1), and the fog thins to golden-hour haze; the lines fade to the faint permanent construction lines | MATCH (into S2_SH01) | **6.0** wordless |

**Scene 1 total: 7 shots, 32.54 s.**

### Scene 2: The lost propeller (OuterShrine, Day state)

- **Look:** golden hour, key from the south-west, long shadows, the shrine's faint gold
  construction lines.
- **Sound:** a light ambient Academy theme comes in under the colour flood. The **sputter** (a
  sputtering whine) starts off-screen at S2_SH01 t = 0 as a 3D source at Birdy_SkyEntry. Then a pitch
  drop, a *pop* when the propeller detaches (S2_SH01 ≈ 2.4 s), a *thud* off-screen in S2_SH02 at
  1.0 s (Birdy's first impact), a *clink* and spin-down when the propeller lands on the ledge, and a
  smaller bounce thump in S2_SH03.
- **Gameplay hand-off (end of scene 2):** S2_SH10 ends on MC with Propeller_Ledge in frame, then
  BLEND 1.5 s to the gameplay camera. Player input is enabled at the start of the blend. The
  blend is not counted in the scene time.

| Shot | Lines | Camera | Lens | Movement (start → end) | Framing (subject in safe frame) | Action | Out | Dur (s) |
|---|---|---|---|---|---|---|---|---|
| S2_SH01 | — (wordless) | Locked tilt (pan-tilt head) | 50° (26 mm) | Fixed at S1_SH07's end position. 0.0–1.0 s hold while the sputter starts off-screen. 1.0–2.2 s tilt up 35° (ease-in-out) from MC.Head toward Birdy; the Rotation Composer picks up Birdy.Head once he is in frame. 2.2–3.2 s Birdy tumbles across the sky; the propeller pops off at Prop_Separation and spins away to screen left. | Wide → sky. Subject: **MC.Head** for 0–1.6 s, then **Birdy.Head** (in frame from 1.6 s) | MC: look up (head snaps up at 0.3 s). Birdy: crash tumble (airborne, flailing). Propeller: separate | CUT | 1.0 + 1.2 + 1.0 = **3.2** wordless |
| S2_SH02 | — (wordless) | Tracking the propeller (NO-CHAR) | 40° (33 mm) | Follows `Prop.Root` from Prop_Separation along its arc to Propeller_Ledge, from the meadow side, Follow damping 0.4 s; settles at `Ledge_View`, aiming Propeller_Ledge at (0.5, 0.55). | The propeller lands on the ledge. The ledge's Greek-key top edge and at least the top two step stubs are in frame, so the player reads it as reachable. Subject: **Prop.Root** | Propeller: arc, land, spin down (0.6 s), settle, glint. An off-screen thud at 1.0 s. | CUT | 2.2 flight and landing + 1.0 hold on the ledge = **3.2** wordless |
| S2_SH03 | OC_S2_01, OC_S2_02 | Locked wide, handheld (amplitude 0.3) | 45° (29 mm) | `MC.Feet + (−0.5, 0.75, −1.0)·H` (behind MC's left shoulder); aim Birdy_Land at (0.5, 0.4) | Wide: MC in the right foreground from behind, Birdy midground, circling within a 1.5 m radius of Birdy_Land (keep him in the safe frame). Birdy frac ≈ 0.08. Subject: **Birdy.Head** (MC.Head also in frame) | Birdy: crash tumble and bounce (0–1.0 s), scramble up (1.0–1.4 s), panic circling with helmet patting (from 1.4 s). MC: flinch at the bounce, then watch. | CUT | 1.4 wordless + 3.50 + 4.43 = **9.33** |
| S2_SH04 | OC_S2_03 | Locked close single | 30° (45 mm) | `MC.Head + (−0.15, 0, 1.3)·H`; aim MC.Eyes at (0.45, 0.6) | CU, head and collar; Birdy's circling soft in the background left. Subject: **MC.Head** | MC: look off (at Birdy), deadpan, small head shake | CUT | **2.23** |
| S2_SH05 | OC_S2_04 | Locked wide-lens "close" (**not a TCU**) | 75° (15.6 mm) | `Birdy.Head + 2.33·HB front, +0.2·HB up` (the camera stands between MC and Birdy at Birdy's head height); aim Birdy.Eyes at (0.5, 0.52). Birdy starts out of frame left and lunges to his mark `Birdy_Lean` = MC.Feet + 0.45·H along MC's forward in 0.25 s. | Wide-lens medium close-up: **frac 0.28 (hard cap 0.30)**; Birdy.Eyes in frame; the 15.6 mm lens bulges his face and beak toward the lens, which sells "suddenly very close" without crossing the TCU line. Subject: **Birdy.Head** | Birdy: close lean-in (lunge, then hold with eyes wide), a wing wave on each "Hi!" | CUT | 0.2 lead-in + 2.30 = **2.50** |
| S2_SH06 | OC_S2_05 | Over-the-shoulder, high angle, locked (5% push over the shot) | 40° (33 mm) | `MC.Head + (0.35, 0.12, −0.5)·H` (behind MC's right shoulder); aim Birdy.Head at (0.55, 0.42) | MC's shoulder and cheek soft at left; Birdy in a medium shot from above, looking up. frac ≤ 0.25. Subject: **Birdy.Head** | Birdy: plead (wings clasped, little hops), a wobble on "barely stand". MC: listen | CUT | **6.77** (cards a/b) |
| S2_SH07 | OC_S2_06 | Low-angle MCU, locked (from Birdy's eye height) | 35° (38 mm) | `MC.Feet + (0.2, 0.45, 1.1)·H`; aim MC.Head at (0.5, 0.6) | Low-angle MCU. Subject: **MC.Head** | MC: sigh shrug (shoulders drop), look down at Birdy | CUT | **3.50** |
| S2_SH08 | OC_S2_07, OC_S2_08 | Locked side-on two-shot | 40° (33 mm) | `midpoint(MC.Feet, Birdy.Feet) + 2.2·H along Pair.Side, 0.5·H up`; aim midpoint(MC.Head, Birdy.Head) at (0.5, 0.5) | Medium-wide two-shot in profile, MC left, Birdy right. Subject: **MC.Head + Birdy.Head** | Birdy: point up (wing to the sky, triumphant, held). MC: look up at the sky, look back down, flat stare on "not helpful" | CUT | 2.23 + 1.80 + 0.5 hold (the flat line lands) = **4.53** |
| S2_SH09 | OC_S2_09 | Locked medium on Birdy, eye level | 40° (33 mm) | `Birdy.Head + 8·HB front`, at Birdy's eye level; aim Birdy.Head at (0.5, 0.55). Framing leaves room for the full wingspan inside 80% of the frame width. | Medium-full shot of Birdy, frac ≈ 0.17. Subject: **Birdy.Head** | Birdy: eager bounce ("round!", a wingtip twirl on "spins!"), tiny wing spread on "about this big!", held through the 0.8 s pause while he looks down at his own wings, then a small sad droop on "My wings are very small." | CUT | 6.50 + 0.8 mid-line hold = **7.30** |
| S2_SH10 | OC_S2_10 | Locked, then crane up to the ledge | 40° (33 mm) | 0–3.63 s locked: `MC.Head + (0.3, 0.05, 1.4)·H` (front three-quarter), aim MC.Head at (0.45, 0.6). From 3.63 s: crane 3.0 s (ease-in-out) on a spline up and over MC's right shoulder to `MC.Head + (0.35, 0.55, −1.2)·H`, aiming Propeller_Ledge at (0.62, 0.62), with MC.Head held at about (0.25, 0.2). Hold 1.5 s. | MCU → over-the-shoulder wide with MC bottom-left and the propeller glinting on its ledge upper-right: the player and the goal in one frame. Subject: **MC.Head** throughout | MC: sigh, then (after the line plus 0.6 s) turn to face Propeller_Ledge (body and head), idle. Birdy (soft at the frame edge): sad droop, then a hopeful look | BLEND 1.5 s → GAMEPLAY | 3.03 + 0.6 hold (the punchline lands) + 3.0 crane + 1.5 ledge hold = **8.13** |

**Scene 2 total: 10 shots, 50.69 s** (plus the 1.5 s gameplay blend, not counted).

### Scene 3: "You're the one" (OuterShrine, at the ledge foot)

- **Starts on pickup:** when the player picks up the propeller on Propeller_Ledge, input turns off
  and the Timeline CUTs to S3_SH01. MC is placed at MC_Handover with the propeller attached to
  MC.Hand_R, and Birdy at Birdy_Handover. Scenes 3–6 then play back to back.
- **Look:** golden hour as in scene 2. MC faces +X at MC_Handover, so the south-west sun rims him
  from behind-left in the hero shots.
- **Sound:** a *click* when the propeller seats (S3_SH02 at 0.4 s), a whir spin-up (0.5–0.9 s), then
  Birdy's rotor loop for as long as he hovers (through scene 6). A mock-heroic music swell under
  S3_SH10 that cuts dead on the cut to the flat wide (S3_SH11).

| Shot | Lines | Camera | Lens | Movement (start → end) | Framing (subject in safe frame) | Action | Out | Dur (s) |
|---|---|---|---|---|---|---|---|---|
| S3_SH01 | — (wordless) | Locked side-on two-shot | 40° (33 mm) | `midpoint(MC.Feet, Birdy.Feet) + 1.9·H along Pair.Side, 0.45·H up`; aim midpoint(MC.Hand_R, Birdy.Head) at (0.5, 0.45) | Medium two-shot. Subject: **MC.Head + Birdy.Head** | MC: hold out (the propeller in Hand_R, arm extended down to Birdy). Birdy: snatch (a hop, grabbing with both wings, 0.6–0.9 s) | CUT | **1.2** wordless |
| S3_SH02 | — (wordless) | INSERT, locked close-up | 30° (45 mm) | `Birdy.PropellerSocket + 0.9·HB`, 45° above Birdy's forward; aim PropellerSocket at (0.5, 0.45). The frame's bottom edge stays above Birdy.Eyes. | INSERT: the propeller and the helmet top. Not a TCU (Eyes out of frame). Subject: **Birdy.PropellerSocket** | Birdy: propeller click (seats it at 0.4 s; the blades spin up 0.5–0.9 s) | CUT | **0.9** wordless |
| S3_SH03 | OC_S3_01 | Tracking (pedestals up with Birdy) | 40° (33 mm) | Starts at `Birdy.Head + 9·HB front`, at Birdy's head height; pedestals up with Birdy as he rises to hover height in 0.8 s (damping 0.25); aim Birdy.Head at (0.5, 0.55) | Medium-full Birdy, frac ≈ 0.15; MC's hand and side at the frame edge. Subject: **Birdy.Head** | Birdy: hover wobble (lifts off over 0.8 s, wobbles ±0.1·h sideways, settles), overjoyed (hugs his helmet, flaps). MC: watch | CUT | 0.8 wordless lift + 4.57 = **5.37** |
| S3_SH04 | OC_S3_02 | Orbit around Birdy | 40° (33 mm) | `CinemachineOrbitalFollow` on Birdy.Head, radius 9·HB, height 0; horizontal axis 0° → +90° over the shot (ease-in-out), starting from S3_SH03's angle | Medium Birdy, frac ≈ 0.15. Subject: **Birdy.Head** | Birdy: proud (wings on hips), then a proud loop (a small 360° hover lap of radius 0.5·h on "spinning very, very fast") | CUT | **4.77** |
| S3_SH05 | OC_S3_03 | Slow push (dolly) | 35° (38 mm) | `Birdy.Head + 12·HB front` → `8·HB` over the shot, ease-in; aim Birdy.Eyes at (0.5, 0.55). MC is out of frame. | Medium → medium close-up, frac 0.13 → 0.20 (under the cap). Subject: **Birdy.Head** | MC: turn away (already turned toward Path_Start when the shot opens, his back to Birdy). Birdy: stare (freezes mid-hover, eyes lock on MC's back during the 0.8 s hold), head tilt on "Wait." | CUT | 0.8 hold + 1.80 = **2.60** |
| S3_SH06 | OC_S3_04 | INSERT, slow lateral track | 30° (45 mm) | `MC.Blade + 0.5·H` behind-right; tracks 0.15·H along the blade over the shot; aim MC.Blade at (0.5, 0.5). Glint VFX (a specular sweep) plus a "ting" on "compass-blade" (about 2.6 s). | INSERT: the compass-blade as MC wears it. Subject: **MC.Blade** | MC: idle (back to camera). Birdy: off-screen (voice only) | CUT | **3.70** |
| S3_SH07 | OC_S3_05, OC_S3_06 | Low-angle hero, locked (3% push over the shot) | 40° (33 mm) | `MC.Feet + (−0.3, 0.32, 1.2)·H` in MC's frame once he has turned (in front, at Birdy's height), tilted up about 20°; aim MC.Head at (0.5, 0.64). The sun is behind MC as a rim light; Birdy's helmet and propeller are soft in the bottom-right foreground. | Low-angle hero medium shot of MC; faint construction-line arcs behind him. Subject: **MC.Head** | MC: turn (turns round to face Birdy on the first "You're...", cloak swinging, landing in a hero pose), then a confused head tilt on "The one what?". Birdy (foreground): awed | CUT | 2.77 + 1.80 = **4.57** |
| S3_SH08 | OC_S3_07, OC_S3_08 | Locked medium on Birdy, eye level | 35° (38 mm) | `Birdy.Head + 10·HB front`; aim Birdy.Head at (0.5, 0.55) | Medium, frac 0.16. This comic `[pause]` plays in a medium, **not** a close-up. Subject: **Birdy.Head** | Birdy: excited flap (a big wing spread on "The Guardian!"); during the 0.8 s hold, freeze and deflate slightly (eyes slide sideways); a sheepish shrug on "Well, not everywhere. I crashed."; perks up on "definitely looking" | CUT | 3.70 + 0.8 hold + 5.63 = **10.13** |
| S3_SH09 | OC_S3_09 | Locked side-on two-shot | 40° (33 mm) | `midpoint(MC.Feet, Birdy.Feet) + 2.0·H along Pair.Side, 0.6·H up`; aim midpoint(MC.Head, Birdy.Head) at (0.5, 0.5) | Medium two-shot. Subject: **MC.Head + Birdy.Head** | MC: flat stare, head shake. Birdy: hover, eager nods | CUT | **3.03** |
| S3_SH10 | OC_S3_10 | Low-angle hero with a push | 40° → 36° (33 → 37 mm) | Starts on S3_SH07's pose; dollies 0.15·H toward MC over the shot while the lens zooms 40° → 36°; aim MC.Head at (0.5, 0.64). Birdy hovers in the left foreground. | Low-angle hero medium shot, music swell. Subject: **MC.Head** | Birdy: grand gesture (wings sweep toward MC), committed. MC: stands, unimpressed | CUT (on the first frame of OC_S3_11's 0.8 s hold: the cut is the undercut) | **2.90** |
| S3_SH11 | OC_S3_11 | Locked flat wide (eye level, symmetric) | 30° (45 mm) | `midpoint(MC.Feet, Birdy.Feet) + 4.5·H along Pair.Side, 0.55·H up`; aim midpoint(MC.Head, Birdy.Head) at (0.5, 0.45); level horizon, no tilt | Flat full-figure wide, both small and centred, the ruined columns behind. Subject: **MC.Head + Birdy.Head** | Birdy: losing steam (wings droop mid-gesture during the 0.8 s hold; he sinks 0.1·h). MC: flat stare | CUT | 0.8 hold + 2.83 + 0.5 hold = **4.13** |
| S3_SH12 | OC_S3_12 | Locked close single | 30° (45 mm) | `MC.Head + (0.2, 0, 1.25)·H` (front three-quarter, on the side away from Birdy); aim MC.Eyes at (0.45, 0.6) | CU; Birdy soft in the background or out of frame. Subject: **MC.Head** | MC: resigned mutter (eyes roll aside, looks off away from Birdy, mouth tight) | CUT | **3.90** |
| S3_SH13 | OC_S3_13, OC_S3_14 | Crane (boom down), single | 40° (33 mm) | 0–5.3 s: `Birdy.Head + 9·HB front, +4·HB up` (about 24° down), aim Birdy.Head at (0.5, 0.5). At the start of OC_S3_14's audio (5.3 s), boom down to Birdy's eye level over 0.8 s (ease-out) in sync with his recovery. | A medium shot from slightly above (he looks small) → a medium at eye level. frac ≈ 0.15. Subject: **Birdy.Head** | Birdy: sad droop (sinks 0.15·h, rotor slows, looks down) through OC_S3_13 and the 0.8 s hold; perky recovery (bounces up, rotor revs) on "But that's okay!" | JUMP (to scene 4) | 4.50 + 0.8 hold + 4.10 + 0.5 tail = **9.90** |

**Scene 3 total: 13 shots, 57.10 s.**

### Scene 4: The path (OlivePath)

- **Look:** golden hour through olive leaves; dappled light on the stone path. The camera is on
  the walkers' east side looking west, so they walk screen left to right (north) and the
  south-west sun backlights them. Add a soft fill so faces read.
- **Walk:** MC and Birdy move along the Path spline from Path_Track_A at **1.04 m/s** (25 m over
  the 24.1 s scene). MC walks; Birdy putters on MC's far (west) side at hover height, 0.55·H ahead
  of MC and 0.5·H across, so he shows in the gap in front of MC's face.
- **Occluders:** no foreground olive trunk may cross MC.Head or Birdy.Head during a hold or
  during OC_S4_04, S4_05, S4_07 or S4_08 (the set dresser spaces the trunks to suit).
- **Sound:** footsteps on stone, cicadas, rustling leaves, Birdy's rotor; a light walking cue.
- **Cuts:** this is the one long side-on tracking shot, broken only by the cut the direction allows
  on "What's a taxes?" (S4_SH02). S4_SH01 and S4_SH03 are the same rig, so it reads as one shot.

| Shot | Lines | Camera | Lens | Movement (start → end) | Framing (subject in safe frame) | Action | Out | Dur (s) |
|---|---|---|---|---|---|---|---|---|
| S4_SH01 | OC_S4_01, OC_S4_02, OC_S4_03 (and OC_S4_04's hold) | Tracking dolly, side-on | 35° (38 mm) | `CinemachineSplineDolly` on Cam_Track_S4, its position driven to stay abeam of MC.Mid; aim midpoint(MC.Head, Birdy.Head) at (0.45, 0.55), leaving lead room to screen right | Medium-wide two-shot in profile, full figures; foreground trunks, the walkers and the background olive rows give three layers of parallax. Subject: **MC.Head + Birdy.Head** | MC: walk (casual), counts on his fingers on "Rules.", glances at Birdy on "Taxes?". Birdy: putter hover (bobbing alongside), nods on "Yes and yes!", then a blank stare during the 0.8 s hold (still bobbing) | CUT (on the first audio frame of "What's a taxes?", as directed) | 1.5 wordless walk-in + 3.10 + 1.80 + 1.80 + 0.8 hold = **9.00** |
| S4_SH02 | OC_S4_04, OC_S4_05 | Tracking, leading (frontal, moving backward) | 40° (33 mm) | `CinemachineFollow` on Birdy.Head, offset `6·HB` along Birdy's forward (the camera leads, travelling backward at walk speed); aim Birdy.Head at (0.4, 0.55) | Medium on Birdy (frac ≈ 0.19) with MC walking in the background right. Subject: **Birdy.Head** (MC.Head also in frame) | Birdy: confused head tilt, putter hover. MC: walk relaxed (hands behind his head), content smile on "God, I love it here." | CUT | 1.80 + 1.80 + 0.5 hold = **4.10** |
| S4_SH03 | OC_S4_06, OC_S4_07, OC_S4_08 | Tracking dolly, side-on (the S4_SH01 rig, which kept moving) | 35° (38 mm) | As S4_SH01. After "Never mind." the camera stops and the walkers go on toward frame right. | As S4_SH01. Subject: **MC.Head + Birdy.Head** | MC: walk, a casual glance at Birdy on "So like...". Birdy: confused head tilt (stops bobbing mid-air on "S-T-D?"). In the 0.6 s beat, MC looks at Birdy, then forward. "Never mind.": MC walks on and Birdy putters after him. | JUMP (to scene 5) | 3.90 + 2.70 + 0.6 beat + 1.80 + 1.0 hold + 1.0 walk-on = **11.00** |

**Scene 4 total: 3 shots, 24.10 s.**

### Scene 5: The Academy (SouthCrest, looking north over the plateau)

- **Look:** the golden-hour showpiece: cream marble, terracotta roofs, saturated lawn, gold
  construction lines over the Gnomon Plaza, haze between the near, middle and far layers.
- **Black sun:** out of frame for the whole scene (at least 5° outside the frame edge), so the
  scene-6 gaze reveals it. Reviewer check: BlackSun's screen position is outside the viewport in
  every S5 frame.
- **Sound:** the music swells on the crane reveal and peaks at the top of the crane (6.0 s). It
  sits low under the dialogue and fades out at the end of S5_SH03 (see scene 6).

| Shot | Lines | Camera | Lens | Movement (start → end) | Framing (subject in safe frame) | Action | Out | Dur (s) |
|---|---|---|---|---|---|---|---|---|
| S5_SH01 | OC_S5_01, OC_S5_02 | Crane (spline: up and forward over the crest) | 40° → 50° (33 → 26 mm) | Starts at `MC.Feet + (0.15, 0.55, −1.4)·H` (behind and low, at Birdy's height), aim Pair.Mid at (0.5, 0.55). 0–1.0 s: they walk the last 1.4 m to Crest_View and stop; the camera follows. 1.0–6.0 s: the crane rises and pushes forward to `MC.Feet + (0, 4.0, −4.0)·H` with pitch −20°, yaw toward Gnomon_Aim (aim Gnomon_Aim at (0.5, 0.79)); the lens widens 40° → 50°, ease-in-out. Static from 6.0 s while the lines play. | Backs of heads → an extreme wide over the whole Academy, the gnomon centred horizontally in the upper third, the Overlook hazy near the top. MC.Head stays at about (0.5, 0.13), with Birdy beside him, as small silhouettes. Black sun out of frame. Subject: **MC.Head** | MC: walk, stop, awed look (head lifts). Birdy: putter hover, stop, grand gesture (a wing sweep across the vista) on OC_S5_01 | CUT | 1.0 walk + 5.0 crane + 2.23 + 1.80 = **10.03** |
| S5_SH02 | OC_S5_03 | Locked single on Birdy | 35° (38 mm) | `Birdy.Head + 10·HB`, 30° off his forward (front three-quarter), at eye level; aim Birdy.Head at (0.5, 0.55); the Academy soft behind | Medium, frac 0.16. Subject: **Birdy.Head** | Birdy: chuckle (shoulders shaking, wing over beak) | CUT | 1.80 + 0.5 hold (the joke lands) = **2.30** |
| S5_SH03 | OC_S5_04, OC_S5_05, OC_S5_06, OC_S5_07 | Locked two-shot, **static throughout** | 35° (38 mm) | `Pair.Mid + 2.2·H along Pair.Side` (the south side), at MC.Eyes height; aim Pair.Mid at (0.5, 0.55); yaw chosen so the black sun stays at least 5° outside the frame. **Review round 4:** Birdy is placed to MC's front-right (north-east) at the cut and MC turns 45° to him, so the pair's axis runs NE–SW and the camera sits south-east: both faces in profile, the sun well off frame right, the Academy behind on the right | Medium two-shot in profile, facing each other, the Academy behind. Subject: **MC.Head + Birdy.Head** | MC: turn (head and body, from the vista to Birdy, during the 0.8 s hold), a suspicious squint on "Wait."; then a flat stare, frozen through the long pause. Birdy: earnest nod; beam (a held smile, rotor whirring) through OC_S5_07 and the final hold. During the 2.0 s static hold only blinks and the rotor move; **OC_S5_06 ("...") plays inside that hold**, starting 0.1 s in. | CUT | 0.8 hold + 3.97 + 3.83 + 2.0 static hold (contains OC_S5_06) + 2.90 + 1.5 hold ("MC stares at him. Birdy beams back.") = **15.00** |

**Scene 5 total: 3 shots, 27.33 s.**

### Scene 6: The world coming apart (SouthCrest, looking north at the rim and the black sun)

- **Look:** the same golden hour, but the frame now looks at the black sun and the fraying rim:
  marble breaking into floating shapes, then ink lines, then blank parchment. Use DoF only where
  the shot says.
- **Music:** **all music drops out** at the start of S6_SH01 (a 0.5 s fade to silence). What is left:
  wind on the crest, Birdy's rotor loop, and a faint paper rustle and ink crackle from the north.
  No music until S6_SH16, where a sparse motif enters at 2.0 s and crossfades into the gameplay
  music.
- **Birdy TCUs:** S6_SH05, S6_SH08 and S6_SH10 are TCUs, as is the first 1.0 s of S6_SH15. They
  all use one setup, the **TCU rig**: `Birdy.Head + 4.15·HB` along Birdy's forward (toward MC),
  at Birdy's eye level, 10° off his eyeline; vFOV 30° (45 mm); DoF focus Birdy.Eyes, f/2.8; aim
  Birdy.Eyes at (0.5, 0.58); **frac 0.45**. These are the only Birdy face close-ups in the
  sequence.
- **End of scene 6 = gameplay hand-off (BLEND 2.0 s, lead decision in review round 2).** The
  orbit camera rests at a 16° pitch and can't frame the black sun from behind MC; matching its
  pose cropped the sun at the final frame. So S6_SH16 now ends behind MC with the whole sun
  upper-centre, then BLENDs 2.0 s (ease-in-out, the same mechanism as S2's hand-off) into the live
  orbit camera. Player input is enabled at the start of the blend; the blend is not counted in
  the scene time. The validator checks that S6_SH16's last frame shows the whole sun and that the
  blend's end target, the orbit camera's resting pose G, frames MC.Head with a clear view.
  The original MATCH spec, kept for reference: the **gameplay pose G** is defined as follows:
  - Target T = MC.Feet + 1.35 m up (0.84·H for H = 1.6). Read the real offset from the gameplay
    camera's config, not from this document.
  - Yaw = MC's forward, which faces BlackSun (a bearing of ≈ 21.9° east of north from Crest_View).
  - Pitch = 8° looking down.
  - Distance = 4.0 m back from T along the view axis.
  - vFOV = 60°.

  The final frame must equal G within 1 cm and 0.1°, with an identical FOV, and the gameplay
  orbit camera must start at that same yaw and pitch. In G the black sun sits upper-centre, at
  about (0.5, 0.84), above MC's head.

| Shot | Lines | Camera | Lens | Movement (start → end) | Framing (subject in safe frame) | Action | Out | Dur (s) |
|---|---|---|---|---|---|---|---|---|
| S6_SH01 | OC_S6_01 | Rack focus, locked (2% micro push) | 27° (50 mm), DoF f/2.0 | `MC.Head + (0.3, 0.06, −0.45)·H` (close behind his right ear); aim Rim_Gaze at (0.62, 0.48). 0–1.0 s focus on MC.Eyes. **1.0–3.0 s focus pull MC.Eyes → Rim_Gaze (2.0 s, ease-in-out).** The black sun is kept just above the top edge (it is revealed in S6_SH02). | MC's head and shoulder in the left third, foreground; the fraying rim far off. Subject: **MC.Head** (x 0.2–0.35) | MC: look off north (a slow head turn, the smile fading), then turn his body to face BlackSun | CUT | 1.0 + 2.0 rack + 2.50 = **5.50** |
| S6_SH02 | OC_S6_02 | Locked wide | 35° (38 mm) | `MC.Feet + (0.5, 0.9, −2.0)·H` (behind-right, MC facing BlackSun); aim Rim_Gaze at (0.5, 0.35) | Wide: MC small at lower left from behind (MC.Head about (0.3, 0.28)); the fraying rim across the middle of the frame (shapes → ink lines → blank); the black sun at about (0.6, 0.75). Subject: **MC.Head** | MC: stand, stare at the sun | CUT | 0.5 + 2.70 + 0.8 hold = **4.00** |
| S6_SH03 | OC_S6_03 | Locked medium two-shot | 35° (38 mm) | `MC.Head + (0.9, −0.05, 1.6)·H` (front-right three-quarter); aim Pair.Mid at (0.5, 0.55). Birdy darts in from frame right to his hover mark in 0.3 s. | Medium two-shot. Subject: **MC.Head**, plus **Birdy.Head** from 0.3 s | Birdy: zip in (a short dart, "a little too quickly"), a bright wave-off on "Oh! That." MC: look at Birdy | CUT | **2.70** |
| S6_SH04 | OC_S6_04, OC_S6_05 | Tracking (slow lateral truck): the tour guide | 40° (33 mm), DoF f/4 on Birdy.Eyes | Birdy moves to Birdy_Guide (in front of MC, facing him, the world behind). The camera starts at `MC.Head + (0.55, −0.05, −0.25)·H` (beside MC's right shoulder; MC out of frame or a sliver at the left edge) and trucks right 0.4·H over the shot; aim Birdy.Head at (0.38, 0.55) | Birdy in the foreground (a medium, frac ≈ 0.17, ≤ 0.20) with the world behind him to the right: the Academy's north edge, the fraying rim, the black sun. Subject: **Birdy.Head** | Birdy: tour-guide gesture (a presenting wing sweep on "This world has Guardians"), counts to eight on his wings, points three ways on "Angles. Stars. Gravity." | CUT | 3.63 + 7.70 = **11.33** |
| S6_SH05 | OC_S6_06 | **TCU rig** (Birdy TCU 1), push 4.15 → 3.9·HB | 30° (45 mm) | TCU rig; a slow push over the shot | **TCU**, frac 0.45 → 0.48. Subject: **Birdy.Head** | Birdy: cheer slip (in the 1.0 s hold the smile falters, his eyes drop, the rotor hum dips in pitch); a quiet line | CUT | 1.0 hold + 2.77 = **3.77** |
| S6_SH06 | OC_S6_07, OC_S6_08 | Tracking, the tour-guide rig, then a pan to MC | 40° (33 mm) | Starts on S6_SH04's end pose. On OC_S6_08 Birdy spins to point at MC, and the camera pans left 25° over 1.0 s so MC.Head enters frame left. | Medium on Birdy (frac ≈ 0.17) → medium two-shot. Subject: **Birdy.Head**, plus **MC.Head** from 1.0 s into OC_S6_08 | Birdy: perky recovery (snaps back to bright), a tour-guide wave toward the rim on "coming apart at the edges", then points at MC | CUT | 6.17 + 3.63 = **9.80** |
| S6_SH07 | OC_S6_09 | Locked MCU on MC | 30° (45 mm) | `MC.Head + (−0.25, 0, 1.35)·H` (front three-quarter, from Birdy's side); aim MC.Eyes at (0.45, 0.6). The background is the olive slope, backlit. | MCU. Subject: **MC.Head** | MC: serious frown, looking at Birdy | CUT | **3.83** |
| S6_SH08 | OC_S6_10 | **TCU rig** (Birdy TCU 2), static | 30° (45 mm) | TCU rig | **TCU**, frac 0.45. Subject: **Birdy.Head** | Birdy: cheer slip (in the 1.0 s hold his eyes slide away; a tiny shrug) | CUT | 1.0 hold + 4.37 = **5.37** |
| S6_SH09 | OC_S6_11 | Locked MCU on MC (the S6_SH07 setup) | 30° (45 mm) | As S6_SH07 | MCU. Subject: **MC.Head** | MC: looking at Birdy, quiet | CUT | **1.80** |
| S6_SH10 | OC_S6_12 | **TCU rig** (Birdy TCU 3), push 4.15 → 3.9·HB | 30° (45 mm) | TCU rig; a slow push over the shot | **TCU**, frac 0.45 → 0.48. Subject: **Birdy.Head** | Birdy: cheer slip, the deepest (looks down and away, the rotor slows, he sinks 0.05·h); speaks softer | CUT | 1.0 hold + 2.90 = **3.90** |
| S6_SH11 | OC_S6_13 | Locked medium on Birdy | 35° (38 mm) | `Birdy.Head + 10·HB front`, at eye level; aim Birdy.Head at (0.5, 0.55) | Medium, frac 0.16. Subject: **Birdy.Head** | Birdy: perky recovery (pops up, wings out) | CUT | **2.10** |
| S6_SH12 | OC_S6_14, OC_S6_15 | INSERT, slow track along the compass-blade | 30° (45 mm) | `MC.Blade + 0.45·H` from the side; tracks 0.2·H from the tip toward the guard over the shot; aim MC.Blade at (0.5, 0.5). MC.Hand_R enters frame during OC_S6_14 and rests on the hilt. A glint sweep on "compass" (about 2.0 s). | INSERT: the compass-blade and Hand_R. Subject: **MC.Blade** | MC: touch hilt (Hand_R to the hilt; he looks down, off-screen) | CUT | 1.80 + 3.77 = **5.57** |
| S6_SH13 | OC_S6_16, OC_S6_17 | Slow push on MC | 30° (45 mm) | `MC.Head + (0.15, 0, 1.6)·H` (MCU), static until "Somewhere else" (about 1.5 s into OC_S6_16), then a slow push to `1.05·H` (CU) by the end of OC_S6_17, ease-in; aim MC.Eyes at (0.5, 0.6) | MCU → CU. Subject: **MC.Head** | MC: listen, then look off (his eyes unfocus on "Somewhere else"), a confused frown on "Huh?" | CUT | 3.97 + 1.80 = **5.77** |
| S6_SH14 | OC_S6_18 | Locked two-shot from behind three-quarter | 35° (38 mm) | `Pair.Mid + 1.8·H behind, 0.6·H to the right, 0.1·H up` (behind relative to MC's facing); aim Pair.Mid at (0.45, 0.45); the rim and the black sun beyond | Medium two-shot from behind three-quarter, the world ahead of them. Subject: **MC.Head + Birdy.Head** | Birdy: warm lean (leans in, wing on MC's shoulder). MC: looks back to the black sun | CUT | **4.83** |
| S6_SH15 | OC_S6_19 | **TCU rig** (Birdy TCU 4) → dolly out | 30° (45 mm) | 0–1.0 s (the hold): the TCU rig, static. At the line's first audio frame ("And hey!"), dolly back along the same axis from 4.15·HB to 10·HB in 0.5 s (ease-out), then hold. | **TCU during the hold only** (frac 0.45); it crosses the banned band in transit (≤ 0.5 s, the allowed exception) and ends on a medium, frac 0.19. Subject: **Birdy.Head** | Birdy: cheer slip (the last, brief) in the hold, then a perky recovery bursting out on "And hey!" (wings wide) as the camera pulls out | CUT | 1.0 hold + 2.83 = **3.83** |
| S6_SH16 | — (wordless) | Tracking push-in, behind MC toward the black sun | 60° (20.8 mm) throughout | Starts behind MC on the line to the sun, `MC.Feet + (0, 1.0, −4.6)·H` turned toward BlackSun, Birdy at MC's right shoulder. 0.0–0.4 s: the rotor revs. 0.4–2.0 s: Birdy zips off down the path to Zip_Path (up and right in frame, dwindling, with a rotor doppler). 0.5–3.5 s: the camera pushes in to `(0, 0.9, −3.0)·H` (ease-in-out), aim BlackSun at (0.5, 0.8). 3.5–5.0 s: static. MC's root does not move after 3.5 s. | MC from behind, alone; the whole black sun upper-centre above his head. Subject: **MC.Head** | Birdy: zip-off. MC: idle look (stands facing the black sun, cloak fluttering, one breath) | BLEND 2.0 s → GAMEPLAY (lead decision, review round 2: the orbit camera's 16° resting pitch can't frame the sun; input enabled at the start of the blend) | 0.4 + 1.6 + 1.5 + 1.5 = **5.0** wordless |

**Scene 6 total: 16 shots, 79.10 s.**

## 5. Summary

| Scene | Shots | Placeholder total (s) | Voiced lines (sum of line durations, s) | Hand-off |
|---|---|---|---|---|
| 1 The white void | 7 | 32.54 | 20.34 (7 lines) | continuous into scene 2 (MATCH) |
| 2 The lost propeller | 10 | 50.69 | 36.29 (10 lines) | → GAMEPLAY (BLEND 1.5 s, not counted) |
| 3 You're the one | 13 | 57.10 | 50.00 (14 lines) | starts on the propeller pickup |
| 4 The path | 3 | 24.10 | 18.70 (8 lines) | — |
| 5 The Academy | 3 | 27.33 | 18.33 (7 lines; OC_S5_06's 1.80 s overlaps its hold) | — |
| 6 The world coming apart | 16 | 79.10 | 65.80 (19 lines) | → GAMEPLAY (BLEND 2.0 s into the orbit camera) |
| **Whole sequence** | **52** | **270.86 (4:30.9)** | 209.46 (65 lines) | |

Blocks the player sits through: scenes 1–2 run 83.23 s (1:23.2) before the platforming; scenes 3–6
run 187.63 s (3:07.6) back to back after the pickup.

## 6. Animation needs

Verbs are clip names for the animation agent. Lip-sync (or beak-sync) is needed on every voiced
line and is not listed.

### MC

| Action | Shots |
|---|---|
| lie still (eyes closed, spread-eagle) | S1_SH01 |
| eyes open | S1_SH02 |
| sit up (slow) | S1_SH03 |
| look around | S1_SH03, S1_SH07 |
| fist pump (opens at the top of a spring-up) | S1_SH04 |
| reach out (arm extended, with "poke air" additive and a hold) | S1_SH05 |
| lower arm | S1_SH06 |
| glance to lens (flat stare) | S1_SH06 |
| turn (in place, up to 180°) | S1_SH07, S2_SH10, S3_SH07, S5_SH03, S6_SH01 |
| look up | S2_SH01, S2_SH08 |
| flinch | S2_SH03 |
| look off | S2_SH04, S3_SH12, S6_SH01, S6_SH13 |
| sigh shrug | S2_SH07, S2_SH10 |
| flat stare (deadpan, held) | S2_SH08, S3_SH09, S3_SH11, S5_SH03 |
| hold out (an object in Hand_R) | S3_SH01 |
| turn away (pose held, back to camera) | S3_SH05, S3_SH06 |
| confused head tilt | S3_SH07, S6_SH13 |
| resigned mutter (eye roll, look aside) | S3_SH12 |
| walk (casual) | S4_SH01, S4_SH03, S5_SH01 |
| walk relaxed (hands behind head) | S4_SH02 |
| stop | S5_SH01 |
| awed look | S5_SH01 |
| serious frown | S6_SH07, S6_SH09 |
| touch hilt | S6_SH12 |
| idle stand / idle look (breathing, cloak flutter) | S2_SH03, S2_SH10, S6_SH02, S6_SH16 |

### Birdy

| Action | Shots |
|---|---|
| crash tumble (airborne) | S2_SH01 |
| crash tumble and bounce, scramble up | S2_SH03 |
| panic circling (patting helmet) | S2_SH03 |
| close lean-in (lunge and hold) | S2_SH05 |
| plead (wings clasped, hops, wobble) | S2_SH06 |
| point up | S2_SH08 |
| eager bounce | S2_SH09 |
| tiny wing spread (held) | S2_SH09 |
| sad droop | S2_SH09 (small), S3_SH13, S2_SH10 |
| snatch | S3_SH01 |
| propeller click | S3_SH02 |
| hover wobble (lift-off) | S3_SH03 |
| overjoyed (hug helmet, flap) | S3_SH03 |
| proud loop (360° hover lap) | S3_SH04 |
| stare (freeze mid-hover) | S3_SH05 |
| awed (foreground hover) | S3_SH07 |
| excited flap | S3_SH08 |
| deflate / losing steam | S3_SH08 (small), S3_SH11 |
| grand gesture | S3_SH10, S5_SH01 |
| perky recovery | S3_SH13, S6_SH06, S6_SH11, S6_SH15 |
| putter hover (flying alongside a walker) | S4_SH01, S4_SH02, S4_SH03, S5_SH01 |
| confused head tilt | S4_SH02, S4_SH03 |
| chuckle | S5_SH02 |
| earnest nod | S5_SH03 |
| beam (held smile) | S5_SH03 |
| zip in (short fast dart) | S6_SH03 |
| tour-guide gesture (present, count, point) | S6_SH04, S6_SH06 |
| cheer slip (facial: smile falters, eyes drop; the rotor pitch dips with it) | S6_SH05, S6_SH08, S6_SH10, S6_SH15 |
| point at MC | S6_SH06 |
| warm lean (wing on MC's shoulder) | S6_SH14 |
| zip-off | S6_SH16 |
| hover idle (rotor loop) | every shot from S3_SH03 on when no other clip plays |

### Props

| Action | Shots |
|---|---|
| propeller: separate, fly (arc), land, spin down, glint | S2_SH01, S2_SH02 |
| propeller: seat (click), spin up, spin loop | S3_SH02 onward |
| compass-blade: glint sweep (VFX) | S3_SH06, S6_SH12 |

## 7. Assumptions

1. The opening "courtyard" is the Outer Shrine, a ruined lawn-and-columns shrine in a meadow south of the plateau (y 0). An olive path climbs north to a crest on the south rim (y ≈ 10). All coordinates are proposals; check the crest's sightline in greybox.
2. H = 1.6 m, Birdy h ≈ 1.0 m (rotor at MC's chin), HB = 0.55h. Birdy hovers with his Head at about MC's eye line (0.80·H). Both are read from the slots at runtime.
3. Holds are timeline gaps, not baked audio: [pause] 0.8 s, [beat] 0.6 s, [long pause] 2.0 s, and 1.0 s on Birdy's scene-6 [pause] lines. The formula's +0.3 s is the only gap between lines.
4. On the propeller pickup, MC cuts to MC_Handover at the ledge foot, because Birdy can't fly up to him.
5. The compass-blade needs one extra anchor, MC.Blade.
6. Superseded in review round 2: the final hand-off is a 2.0 s blend from S6_SH16 (the sun in frame) into the orbit camera at its own resting pitch (16°).
7. Compositions are for 16:9 (wider phones gain width only); the safe frame is the central 90%.

## 8. Conflicts for the user

1. **Birdy close-ups.** Rule: a tight close-up is HB ≥ 40% of the frame height with his eyes in frame, and 30–40% is banned. Only the scene-6 [pause] shots reach 40%. Scene 2's "suddenly very close" is a 15.6 mm wide-lens shot at 28%, where the distortion sells it; scene 3's [pause] beats are mediums at 20% or less.
2. **OC_S6_19:** the close-up holds during the 1.0 s pause only, then a 0.5 s dolly-out on "And hey!" (the mask goes back on).
3. **The black sun in scene 5:** the lead's crest view includes it. I keep it out of frame until scene 6's rack focus so the gaze pays off. The alternative is to let it sit soft at the top of S5_SH01.
4. **Scene 4** is "one long tracking shot", but the direction allows a cut on "What's a taxes?". So it is 3 shots on one rig (side, then frontal on Birdy, then side).
5. **The 1 s "Status!" hold** and the [pause] before "...Menu?" are merged into one 1.0 s hold.
6. **OC_S5_06 ("...")** plays inside the 2.0 s static hold instead of adding 1.80 s.
7. **Birdy's size (resolved by the lead):** chin height, as StoryBrief and the in-game model have it. Framings are in HB, so they scale; the "looking down at Birdy" beats (S2_SH06, S2_SH07, S3_SH01, S3_SH13) are gentler than first drafted.
8. **The "Why me" insert** carries both MC's question and Birdy's answer (OC_S6_14 and OC_S6_15).
