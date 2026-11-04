using System;
using System.Collections.Generic;

namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// The approved shot list (<c>Docs/Story/OpeningCutscenes_ShotList.md</c> §4) as data: every shot
    /// S1_SH01 … S6_SH16 with its duration beats, the MC and Birdy action verbs (§6 names), the
    /// blocking marks (§2 markers, offsets in H) and the look keys. <see cref="TimelineLayout"/>
    /// turns it into times; the timeline builder turns those into tracks.
    /// <para>
    /// Where the shot list gives intent rather than numbers (when inside a line a gesture lands,
    /// how far off-frame Birdy waits), the cue times here are the closest reading; they are the
    /// knobs to adjust after review. Every list is built fresh per call: no static state.
    /// </para>
    /// <para>
    /// Blocking tracks evaluate MC, then Birdy, then the propeller, so a mark may only depend on an
    /// actor evaluated before it: Birdy and the propeller may follow or face MC, but MC's marks use
    /// markers and yaws only (a test pins this). Otherwise the first frame of a timeline would read
    /// the other actor's pose from the previous scene.
    /// </para>
    /// </summary>
    public static class OpeningTimingPlan
    {
        /// <summary>Number of scenes.</summary>
        public const int SceneCount = 6;

        /// <summary>Scenes in block 1 (wake-up to the platforming). A fresh array per call.</summary>
        public static int[] BlockOne => new[] { 1, 2 };

        /// <summary>Scenes in block 2 (the propeller pickup to gameplay on the crest). A fresh array per call.</summary>
        public static int[] BlockTwo => new[] { 3, 4, 5, 6 };

        /// <summary>Birdy's hover: his head at MC.Feet + this many H (MC's eye line).</summary>
        public const float HoverHeadH = 0.80f;

        /// <summary>Silent tail after a punchline a cut follows (no cut within 0.5 s of a joke), s.</summary>
        public const float PunchlineTail = 0.3f;

        // Rotor speeds, degrees per second (PropellerConfig: idle 180, hover 1440, rev 2520).
        const float IdleSpin = 180f, HoverSpin = 1440f, RevSpin = 2520f, SputterSpin = 720f, SlipSpin = 900f;

        /// <summary>Scene 4's walk speed along the path, m/s (25 m over the 24.1 s scene).</summary>
        public const float PathWalkSpeed = 1.04f;

        /// <summary>The timeline asset name for a scene: <c>OC_S1</c>.</summary>
        public static string TimelineName(int scene) => "OC_S" + scene;

        /// <summary>The shots of one scene, in order.</summary>
        public static IReadOnlyList<ShotPlan> Scene(int scene)
        {
            switch (scene)
            {
                case 1: return Scene1();
                case 2: return Scene2();
                case 3: return Scene3();
                case 4: return Scene4();
                case 5: return Scene5();
                case 6: return Scene6();
                default: throw new ArgumentOutOfRangeException(nameof(scene), scene, "Scenes are 1..6.");
            }
        }

        const string MC = PlanActors.MC;
        const string B = PlanActors.Birdy;
        const string P = PlanActors.Propeller;
        const string AtMC = PlanActors.AtMC;
        const string AtB = PlanActors.AtBirdy;

        static ShotPlanBuilder Shot(string id, int scene, float lens, string subject, float distH, string framing) =>
            new ShotPlanBuilder(id, scene, lens, lens, subject, distH, framing);

        static ShotPlanBuilder Shot(string id, int scene, float lensStart, float lensEnd, string subject, float distH,
            string framing) =>
            new ShotPlanBuilder(id, scene, lensStart, lensEnd, subject, distH, framing);

        static PlanMark On(string frame, string face = null) => PlanMark.On(frame, face);

        static PlanMark At(string frame, float x, float y, float z, string face = null, bool hover = false) =>
            PlanMark.At(frame, x, y, z, face, hover);

        // ── Scene 1: the white void ─────────────────────────────────────────────

        static List<ShotPlan> Scene1()
        {
            var s = new List<ShotPlan>
            {
                Shot("S1_SH01", 1, 40f, PlanActors.AtMCHead, 2.6f,
                        "Top-down full body, spread-eagle; slow vertical push 2.6H → 2.0H; MC.Head near the top of frame.")
                    .Wordless(3.0f).Line("OC_S1_01")
                    .Act(MC, "lie still")
                    .Place(MC, On(CutsceneMarkerIds.MCStart))
                    // Birdy and his propeller wait far above the sky entry until scene 2 drops them in.
                    .Place(B, At(CutsceneMarkerIds.BirdySkyEntry, 0f, 25f, 0f))
                    .Place(P, On(PlanActors.AtBirdySocket))
                    .Key(LookChannels.LookBlend, 0f).Key(LookChannels.LinesProgress, 0f)
                    .Key(StateChannels.BirdyRotor, 1f).Key(StateChannels.BirdyRotorSpin, IdleSpin)
                    .Key(StateChannels.PropVisible, 0f).Key(StateChannels.McEyes, 1f)
                    .Build(),
                Shot("S1_SH02", 1, 25f, PlanActors.AtMCHead, 0.55f,
                        "Top-down ECU on both eyes, eye line across the middle third.")
                    .Hold(0.8f).Line("OC_S1_02")
                    .Act(MC, "eyes open")
                    // The slow blink-open lands inside the 0.8 s hold.
                    .Key(StateChannels.McEyes, 0f, 0, 0.5f)
                    .Build(),
                Shot("S1_SH03", 1, 40f, PlanActors.AtMCHead, 1.6f,
                        "Low side-on dolly beside the hip; ends a seated mid-shot at eye level.")
                    .Line("OC_S1_03")
                    // No "look around" here: MC_LookAround is a standing clip and popped him to his
                    // feet mid-line. MC_SitUp's last second already turns his head aside.
                    .Act(MC, "sit up")
                    .Build(),
                Shot("S1_SH04", 1, 55f, PlanActors.AtMCHead, 2.6f,
                        "Low-angle full figure against white, feet in the bottom 15%; shake burst on \"Isekai!\".")
                    .Line("OC_S1_04").Wordless(0.6f)
                    // Opens on his feet (the stand cheated across the smash cut); the pump's overhead
                    // peak lands on "Isekai!", about 0.9 s into the line.
                    .Act(MC, "idle stand").Act(MC, "fist pump", 0, 0.7f)
                    // MC_SitUp ends facing the actor's −Z (his feet); the stand is cheated across the
                    // smash cut, so the actor turns 180° here to keep him facing south. He turns
                    // back north in S1_SH07.
                    .Place(MC, PlanMark.Turned(CutsceneMarkerIds.MCStart, 180f))
                    .Build(),
                Shot("S1_SH05", 1, 40f, PlanActors.AtMCHead, 1.0f,
                        "OTS medium behind MC's right shoulder: outstretched hand and empty white beyond.")
                    .Line("OC_S1_05").Hold(1.0f).Line("OC_S1_06").Hold(PunchlineTail)
                    .Act(MC, "reach out")
                    .Build(),
                Shot("S1_SH06", 1, 30f, PlanActors.AtMCHead, 1.5f,
                        "MCU head and shoulders, white behind; near-lens glance on \"Hard mode.\"")
                    .Line("OC_S1_07").Hold(0.8f)
                    .Act(MC, "lower arm").Act(MC, "glance to lens", 0, 1.3f)
                    .Build(),
                Shot("S1_SH07", 1, 40f, 50f, PlanActors.AtMCHead, 3.4f,
                        "Crane back, up and 180° round MC to a wide establishing shot from behind; the void resolves into the shrine.")
                    .Wordless(1.0f).Wordless(3.0f).Wordless(2.0f)
                    .Act(MC, "look around")
                    // A slow wondering turn to face +Z (north, MC_Start's forward) over 1.0–5.8 s, so S2_SH01
                    // matches: with the camera craning back and up, his turn is the 180° orbit, spread over
                    // the shot. A 1.5 s turn at 4.0 s squeezed it into ~0.6 s on screen (review round 3).
                    .Move(MC, On(CutsceneMarkerIds.MCStart), 4.8f, MarkEase.EaseInOut, 1)
                    // 1.0–4.0 s the construction lines draw; 4.0–6.0 s the colour floods.
                    // Drawn by 4.0 s (progress = ResolveConfig.drawEnd 0.6), then faded to the permanent
                    // faint lines while the colour floods (4.0-6.0 s): no empty white gap.
                    .Key(LookChannels.LinesProgress, 0f, 1).Key(LookChannels.LinesProgress, 0.6f, 2)
                    .Key(LookChannels.LinesProgress, 1f, 3)
                    .Key(LookChannels.LookBlend, 0f, 2).Key(LookChannels.LookBlend, 1f, 3)
                    .Build(),
            };
            return s;
        }

        // ── Scene 2: the lost propeller ─────────────────────────────────────────

        static List<ShotPlan> Scene2()
        {
            return new List<ShotPlan>
            {
                Shot("S2_SH01", 2, 50f, PlanActors.AtMCHead, 4.0f,
                        "Wide from S1_SH07's end; tilt up 35° to Birdy tumbling across the sky; the propeller pops off.")
                    .Wordless(1.0f).Wordless(1.2f).Wordless(1.0f)
                    .Act(MC, "look up", 0, 0.3f).Act(B, "crash tumble")
                    .Place(MC, On(CutsceneMarkerIds.MCStart))
                    .Place(B, At(CutsceneMarkerIds.BirdySkyEntry, 0f, 25f, 0f))
                    .Place(P, On(PlanActors.AtBirdySocket))
                    .Place(B, On(CutsceneMarkerIds.BirdySkyEntry), 1)
                    .Move(B, On(CutsceneMarkerIds.PropSeparation), 1.4f, MarkEase.Linear, 1)
                    .Place(P, On(CutsceneMarkerIds.PropSeparation), 2, 0.2f)
                    // Lands on the ledge's front lip tilted 60 degrees toward MC_Start, so it shows above
                    // the edge from the meadow, Ledge_View and MC_Start (lying flat it was out of sight).
                    .Move(P, PlanMark.At(CutsceneMarkerIds.PropellerLedge, 0f, 0.2f, 0f, CutsceneMarkerIds.MCStart).Tilted(60f),
                        3.0f, MarkEase.EaseOut, 2, 0.2f)
                    .Key(StateChannels.BirdyRotor, 1f).Key(StateChannels.BirdyRotorSpin, SputterSpin).Key(StateChannels.PropVisible, 0f)
                    .Key(StateChannels.BirdyRotor, 0f, 2, 0.2f).Key(StateChannels.PropVisible, 1f, 2, 0.2f)
                    .Key(StateChannels.PropSpin, RevSpin, 2, 0.2f)
                    .Move(B, On(CutsceneMarkerIds.BirdyLand, AtMC), 1.8f, MarkEase.EaseIn, 2, 0.2f)
                    .Key(LookChannels.LookBlend, 1f).Key(LookChannels.LinesProgress, 1f)
                    .Build(),
                Shot("S2_SH02", 2, 40f, PlanActors.AtPropeller, 3.0f,
                        "NO-CHAR: track the propeller's arc onto the ledge; settle on Ledge_View with the Greek-key edge and top step stubs in frame.")
                    .Wordless(2.2f).Hold(1.0f)
                    .Key(StateChannels.PropSpin, 0f, 1)
                    .Build(),
                Shot("S2_SH03", 2, 45f, PlanActors.AtBirdyHead, 4.0f,
                        "Wide from behind MC's left shoulder; Birdy circling at Birdy_Land midground (frac ≈ 0.08).")
                    .Wordless(1.4f).Line("OC_S2_01").Line("OC_S2_02")
                    .Act(B, "crash tumble and bounce, scramble up").Act(B, "panic circling", 0, 1.4f)
                    .Act(MC, "flinch", 0, 0.1f).Act(MC, "idle stand", 0, 1.0f)
                    .Build(),
                Shot("S2_SH04", 2, 30f, PlanActors.AtMCHead, 1.3f,
                        "CU head and collar; Birdy's circling soft in the background left.")
                    .Line("OC_S2_03").Hold(PunchlineTail)
                    .Act(MC, "look off")
                    .Build(),
                Shot("S2_SH05", 2, 75f, PlanActors.AtBirdyHead, 1.3f,
                        "Wide-lens close on Birdy (NOT a TCU, frac 0.28, cap 0.30): he lunges in from frame left.")
                    .Wordless(0.2f).Line("OC_S2_04")
                    .Act(B, "close lean-in")
                    // Birdy_Lean = MC.Feet + 0.45·H along MC's forward. MC stands on MC_Start facing
                    // +Z here, so the mark is written on the marker: it must not follow MC's later turn.
                    // From the east, so with the camera on MC's left he lunges toward the lens.
                    .Place(B, At(CutsceneMarkerIds.MCStart, 0.6f, 0f, 0.45f, AtMC))
                    .Move(B, At(CutsceneMarkerIds.MCStart, 0f, 0f, 0.45f, AtMC), 0.25f, MarkEase.EaseOut)
                    .Build(),
                Shot("S2_SH06", 2, 40f, PlanActors.AtBirdyHead, 1.6f,
                        "High-angle OTS behind MC's right shoulder; Birdy pleads from below (frac ≤ 0.25).")
                    .Line("OC_S2_05")
                    .Act(B, "plead").Act(MC, "idle stand")
                    .Build(),
                Shot("S2_SH07", 2, 35f, PlanActors.AtMCHead, 1.1f,
                        "Low-angle MCU from Birdy's eye height.")
                    .Line("OC_S2_06")
                    .Act(MC, "sigh shrug")
                    .Build(),
                Shot("S2_SH08", 2, 40f, PlanActors.AtMCHead, 2.2f,
                        "Side-on medium-wide two-shot in profile, MC left, Birdy right.")
                    .Line("OC_S2_07").Line("OC_S2_08").Hold(0.5f)
                    .Act(B, "point up").Act(MC, "look up").Act(MC, "flat stare", 1)
                    .Build(),
                Shot("S2_SH09", 2, 40f, PlanActors.AtBirdyHead, 2.5f,
                        "Medium-full on Birdy at eye level, room for the full wingspan (frac ≈ 0.17).")
                    .Line("OC_S2_09", 0.8f)
                    .Act(B, "eager bounce").Act(B, "tiny wing spread", 0, 1.9f).Act(B, "sad droop", 0, 4.6f)
                    .Act(MC, "idle stand")
                    // A step further from MC (cheated at the cut) so his back and sword stay off Birdy.
                    .Place(B, At(CutsceneMarkerIds.MCStart, 0f, 0f, 0.95f, AtMC))
                    .Build(),
                Shot("S2_SH10", 2, 40f, PlanActors.AtMCHead, 1.4f,
                        "MCU front three-quarter, then crane over the right shoulder: MC bottom-left, the propeller on its ledge upper-right.")
                    .Line("OC_S2_10").Hold(0.6f).Wordless(3.0f).Hold(1.5f)
                    .Act(MC, "sigh shrug").Act(MC, "turn", 2).Act(MC, "idle stand", 2, 1.0f)
                    // Birdy hops clear (to MC_Start's west, MC's left) before MC turns, and stays clear
                    // into gameplay: on the east he sat in the path of the crane over MC's right shoulder.
                    .Move(B, At(CutsceneMarkerIds.MCStart, -1.1f, 0f, 0.6f, AtMC), 0.6f, MarkEase.EaseInOut, 1)
                    .Move(MC, On(CutsceneMarkerIds.MCStart, CutsceneMarkerIds.PropellerLedge), 1.0f, MarkEase.EaseInOut, 2)
                    .Build(),
            };
        }

        // ── Scene 3: "You're the one" ───────────────────────────────────────────

        static List<ShotPlan> Scene3()
        {
            const string handover = CutsceneMarkerIds.MCHandover;
            return new List<ShotPlan>
            {
                Shot("S3_SH01", 3, 40f, PlanActors.AtMCHead, 1.9f,
                        "Side-on medium two-shot; MC holds the propeller down to Birdy, who snatches it.")
                    .Wordless(1.2f)
                    .Act(MC, "hold out").Act(B, "snatch", 0, 0.6f)
                    .Place(MC, On(handover))
                    .Place(B, At(handover, 0f, 0f, 0.65f, AtMC))
                    .Place(P, On(PlanActors.AtMCHandR))
                    .Move(P, On(PlanActors.AtBirdyHandR), 0.15f, MarkEase.EaseInOut, 0, 0.75f)
                    .Key(LookChannels.LookBlend, 1f).Key(LookChannels.LinesProgress, 1f)
                    .Key(StateChannels.BirdyRotor, 0f).Key(StateChannels.PropVisible, 1f).Key(StateChannels.PropSpin, 0f)
                    .Build(),
                Shot("S3_SH02", 3, 30f, PlanActors.AtBirdySocket, 0.6f,
                        "INSERT: the propeller and helmet top; Birdy's eyes out of frame (not a TCU).")
                    .Wordless(0.9f)
                    .Act(B, "propeller click")
                    .Place(P, On(PlanActors.AtBirdySocket), 0, 0.4f)
                    .Key(StateChannels.BirdyRotor, 1f, 0, 0.4f).Key(StateChannels.BirdyRotorSpin, IdleSpin, 0, 0.4f)
                    .Key(StateChannels.PropVisible, 0f, 0, 0.4f).Key(StateChannels.BirdyRotorSpin, HoverSpin, 0, 0.5f)
                    .Build(),
                Shot("S3_SH03", 3, 40f, PlanActors.AtBirdyHead, 2.0f,
                        "Medium-full Birdy, pedestalling up with him as he lifts to hover height (frac ≈ 0.15).")
                    .Wordless(0.8f).Line("OC_S3_01")
                    .Act(B, "hover wobble").Act(B, "overjoyed", 1).Act(MC, "idle stand")
                    .Move(B, At(handover, 0f, 0f, 0.65f, AtMC, true), 0.8f, MarkEase.EaseOut)
                    .Build(),
                Shot("S3_SH04", 3, 40f, PlanActors.AtBirdyHead, 2.0f,
                        "Orbit 90° round Birdy, medium (frac ≈ 0.15).")
                    .Line("OC_S3_02")
                    .Act(B, "proud loop")
                    .Build(),
                Shot("S3_SH05", 3, 35f, PlanActors.AtBirdyHead, 2.4f,
                        "Slow push on Birdy, medium → MCU (frac 0.13 → 0.20); MC out of frame.")
                    .Hold(0.8f).Line("OC_S3_03")
                    .Act(B, "stare").Act(MC, "turn away")
                    // He has turned away and taken a step off (0.9 H west), so Birdy's push keeps him out of frame.
                    .Place(MC, At(handover, 0f, 0f, -0.9f, CutsceneMarkerIds.PathStart))
                    .Build(),
                Shot("S3_SH06", 3, 30f, PlanActors.AtMCBlade, 0.5f,
                        "INSERT: the compass-blade as MC wears it, slow lateral track; glint on \"compass-blade\".")
                    .Line("OC_S3_04")
                    .Build(),
                Shot("S3_SH07", 3, 40f, PlanActors.AtMCHead, 1.2f,
                        "Low-angle hero medium on MC, sun rim behind; Birdy soft in the bottom-right foreground.")
                    .Line("OC_S3_05").Line("OC_S3_06")
                    .Act(MC, "turn").Act(MC, "confused head tilt", 1).Act(B, "awed")
                    // Birdy is already down at MC's left (north), hovering low and well clear of him: from
                    // the low camera in front of MC that is small, soft, bottom-right. Placed, not moved, so
                    // he never crosses the frame or MC.
                    .Place(B, At(handover, -0.6f, -0.1f, 0.35f, AtMC, true))
                    // MC_Handover faces +X, toward Birdy_Handover: turning back to the marker's forward faces him.
                    .Move(MC, On(handover), 0.6f)
                    .Build(),
                Shot("S3_SH08", 3, 35f, PlanActors.AtBirdyHead, 2.5f,
                        "Medium on Birdy at eye level (frac 0.16): the comic pause plays in a medium.")
                    .Line("OC_S3_07").Hold(0.8f).Line("OC_S3_08")
                    .Act(B, "excited flap").Act(B, "deflate", 1)
                    .Build(),
                Shot("S3_SH09", 3, 40f, PlanActors.AtMCHead, 2.0f,
                        "Side-on medium two-shot.")
                    .Line("OC_S3_09")
                    .Act(MC, "flat stare").Act(B, "hover idle")
                    .Build(),
                Shot("S3_SH10", 3, 40f, 36f, PlanActors.AtMCHead, 1.2f,
                        "Low-angle hero push and zoom on MC; Birdy in the left foreground; music swell.")
                    .Line("OC_S3_10")
                    .Act(B, "grand gesture").Act(MC, "idle stand")
                    // Frame-left foreground for the push-in (MC's right, south), still hovering. Placed at
                    // the cut: a move from S3_SH09's spot crossed in front of MC for the first 0.4 s.
                    .Place(B, At(handover, 0.6f, -0.1f, 0.35f, AtMC, true))
                    .Build(),
                Shot("S3_SH11", 3, 30f, PlanActors.AtMCHead, 4.5f,
                        "Flat symmetric full-figure wide, both small and centred, the ruined columns behind.")
                    .Hold(0.8f).Line("OC_S3_11").Hold(0.5f)
                    .Act(B, "losing steam").Act(MC, "flat stare")
                    .Place(B, At(handover, 0f, 0f, 0.65f, AtMC, true))
                    .Build(),
                Shot("S3_SH12", 3, 30f, PlanActors.AtMCHead, 1.25f,
                        "CU front three-quarter on the side away from Birdy.")
                    .Line("OC_S3_12").Hold(PunchlineTail)
                    .Act(MC, "resigned mutter")
                    .Build(),
                Shot("S3_SH13", 3, 40f, PlanActors.AtBirdyHead, 2.2f,
                        "Medium from slightly above (he looks small), booming down to eye level on his recovery (frac ≈ 0.15).")
                    .Line("OC_S3_13").Hold(0.8f).Line("OC_S3_14").Hold(0.5f)
                    // He is hovering here: "deflate" is the airborne droop (B_SadDroop stands).
                    .Act(B, "deflate").Act(B, "perky recovery", 2).Act(MC, "idle stand")
                    .Build(),
            };
        }

        // ── Scene 4: the path ───────────────────────────────────────────────────

        static List<ShotPlan> Scene4()
        {
            // The walk runs to Path_End, not Path_Track_B: with the real voices the scene outlasts the
            // A to B stretch by about 4 s, and MC would walk on the spot for the rest of it.
            const string a = CutsceneMarkerIds.PathTrackA, e = CutsceneMarkerIds.PathEnd;
            return new List<ShotPlan>
            {
                Shot("S4_SH01", 4, 35f, PlanActors.AtMCHead, 3.2f,
                        "Side-on tracking dolly abeam MC.Mid: medium-wide profile two-shot, walking left to right, trunks in the foreground.")
                    .Wordless(1.5f).Line("OC_S4_01").Line("OC_S4_02").Line("OC_S4_03").Hold(0.8f)
                    .Act(MC, "walk").Act(B, "putter hover")
                    .Place(MC, On(a, e))
                    .Walk(MC, On(e, e), PathWalkSpeed)
                    // Birdy putters on MC's far (west) side, ahead and across, at hover height. A little
                    // higher than usual: the ground ahead of MC rises as the path steepens to the crest.
                    .Place(B, At(AtMC, -0.5f, 0.09f, 0.55f, e, true))
                    .Place(P, On(PlanActors.AtBirdySocket))
                    .Key(LookChannels.LookBlend, 1f).Key(LookChannels.LinesProgress, 1f)
                    .Key(StateChannels.BirdyRotor, 1f).Key(StateChannels.BirdyRotorSpin, HoverSpin).Key(StateChannels.PropVisible, 0f)
                    .Build(),
                Shot("S4_SH02", 4, 40f, PlanActors.AtBirdyHead, 2.0f,
                        "Leading frontal track on Birdy (frac ≈ 0.19), MC walking in the background right.")
                    .Line("OC_S4_04").Line("OC_S4_05").Hold(0.5f)
                    .Act(B, "confused head tilt").Act(MC, "walk relaxed")
                    .Build(),
                Shot("S4_SH03", 4, 35f, PlanActors.AtMCHead, 3.2f,
                        "As S4_SH01; after \"Never mind.\" the camera stops and the walkers go on out of frame right.")
                    .Line("OC_S4_06").Line("OC_S4_07").Hold(0.6f).Line("OC_S4_08").Hold(1.0f).Wordless(1.0f)
                    .Act(MC, "walk").Act(B, "putter hover").Act(B, "confused head tilt", 1).Act(B, "putter hover", 3)
                    .Build(),
            };
        }

        // ── Scene 5: the Academy ────────────────────────────────────────────────

        static List<ShotPlan> Scene5()
        {
            const string crest = CutsceneMarkerIds.CrestView;
            return new List<ShotPlan>
            {
                Shot("S5_SH01", 5, 40f, 50f, PlanActors.AtMCHead, 1.4f,
                        "Crane up and forward over the crest: backs of heads → extreme wide over the Academy, gnomon in the upper third, black sun out of frame.")
                    .Wordless(1.0f).Wordless(5.0f).Line("OC_S5_01").Line("OC_S5_02")
                    .Act(MC, "walk").Act(MC, "stop", 1).Act(MC, "awed look", 1, 0.5f)
                    .Act(B, "putter hover").Act(B, "grand gesture", 2)
                    // The last 1.4 m to the stop mark (0.875 H at H = 1.6).
                    .Place(MC, At(crest, 0f, 0f, -0.875f))
                    .Move(MC, On(crest), 1.0f, MarkEase.EaseOut)
                    .Place(B, At(crest, 0.5f, 0f, -0.875f, null, true))
                    .Move(B, At(crest, 0.5f, 0f, 0f, null, true), 1.0f, MarkEase.EaseOut)
                    .Place(P, On(PlanActors.AtBirdySocket))
                    .Key(LookChannels.LookBlend, 1f).Key(LookChannels.LinesProgress, 1f)
                    .Key(StateChannels.BirdyRotor, 1f).Key(StateChannels.BirdyRotorSpin, HoverSpin).Key(StateChannels.PropVisible, 0f)
                    .Build(),
                Shot("S5_SH02", 5, 35f, PlanActors.AtBirdyHead, 2.5f,
                        "Single on Birdy, front three-quarter at eye level (frac 0.16), the Academy soft behind.")
                    .Line("OC_S5_03").Hold(0.5f)
                    .Act(B, "chuckle")
                    // Birdy has turned to face south for the chuckle (across the cut), so his single looks north with the
                    // Academy soft behind him (and the black sun well outside the frame).
                    .Place(B, new PlanMark(crest, new UnityEngine.Vector3(1.4f, 0f, 0f), null, 180f, true))
                    .Build(),
                Shot("S5_SH03", 5, 35f, PlanActors.AtMCHead, 2.4f,
                        "Static side-on two-shot, facing each other, the Academy behind; the black sun at least 5° outside the frame.")
                    .Hold(0.8f).Line("OC_S5_04").Line("OC_S5_05").LineInHold("OC_S5_06", 2.0f, 0.1f).Line("OC_S5_07").Hold(1.5f)
                    .Act(MC, "turn").Act(MC, "flat stare", 2)
                    .Act(B, "earnest nod", 2).Act(B, "beam", 4)
                    // Birdy hovers to MC's front-right (north-east) and MC turns 45° right to face him:
                    // the pair's axis runs NE–SW, so the side-on camera sits south-east, both faces in
                    // profile, the black sun (NNE) well off frame right and the Academy behind on the
                    // right (review round 4: on an east–west axis the sun forced the camera to look
                    // down on them and MC's hair hid his face).
                    .Move(MC, PlanMark.Turned(crest, 45f), 0.8f)
                    // Placed at the cut (not moved): the side-on camera hangs off the pair, and Birdy
                    // drifting in moved it 0.26 m in a frame with a taller MC. Static, as the list says.
                    .Place(B, At(crest, 0.45f, 0f, 0.45f, AtMC, true))
                    .Build(),
            };
        }

        // ── Scene 6: the world coming apart ────────────────────────────────────

        static List<ShotPlan> Scene6()
        {
            const string crest = CutsceneMarkerIds.CrestView;
            const string sun = CutsceneMarkerIds.BlackSun;
            return new List<ShotPlan>
            {
                Shot("S6_SH01", 6, 27f, PlanActors.AtMCHead, 0.6f,
                        "Close behind MC's right ear; rack focus from his eyes to the fraying rim; the black sun just above frame.")
                    .Wordless(1.0f).Wordless(2.0f).Line("OC_S6_01")
                    .Act(MC, "look off").Act(MC, "turn", 2)
                    .Place(MC, PlanMark.Turned(crest, 90f))
                    .Move(MC, On(crest, sun), 1.2f, MarkEase.EaseInOut, 2)
                    // Birdy waits out of frame to the east until he darts in (S6_SH03).
                    // Waits well behind both cameras (south of the crest) until S6_SH03's dart-in.
                    .Place(B, At(crest, 2.5f, 0f, -7f, AtMC, true))
                    .Place(P, On(PlanActors.AtBirdySocket))
                    .Key(LookChannels.LookBlend, 1f).Key(LookChannels.LinesProgress, 1f)
                    .Key(StateChannels.BirdyRotor, 1f).Key(StateChannels.BirdyRotorSpin, HoverSpin).Key(StateChannels.PropVisible, 0f)
                    .Build(),
                Shot("S6_SH02", 6, 35f, PlanActors.AtMCHead, 2.2f,
                        "Wide behind-right: MC small at lower left, the fraying rim across the middle, the black sun upper right.")
                    .Wordless(0.5f).Line("OC_S6_02").Hold(0.8f)
                    .Act(MC, "idle look")
                    .Build(),
                Shot("S6_SH03", 6, 35f, PlanActors.AtMCHead, 1.8f,
                        "Front-right three-quarter medium two-shot; Birdy darts in from frame right.")
                    .Line("OC_S6_03")
                    .Act(B, "zip in").Act(MC, "idle stand")
                    .Move(B, At(AtMC, 0.5f, 0f, 0f, AtMC, true), 0.3f, MarkEase.EaseOut)
                    .Build(),
                Shot("S6_SH04", 6, 40f, PlanActors.AtBirdyHead, 2.2f,
                        "Slow lateral truck beside MC's shoulder: Birdy foreground (frac ≈ 0.17), the rim and the black sun behind him.")
                    .Line("OC_S6_04").Line("OC_S6_05")
                    .Act(B, "tour-guide gesture")
                    .Move(B, At(AtMC, 0.3f, 0f, 0.9f, AtMC, true), 0.8f)
                    .Build(),
                Shot("S6_SH05", 6, 30f, PlanActors.AtBirdyHead, 1.2f,
                        "TCU rig on Birdy (frac 0.45 → 0.48), slow push: the first cheer slip.")
                    .Hold(1.0f).Line("OC_S6_06")
                    .Act(B, "cheer slip")
                    .Key(StateChannels.BirdyEyes, 1f, 0, 0.3f).Key(StateChannels.BirdyRotorSpin, SlipSpin, 0, 0.3f)
                    .Build(),
                Shot("S6_SH06", 6, 40f, PlanActors.AtBirdyHead, 2.2f,
                        "Tour-guide rig, then a 25° pan left that brings MC into frame left.")
                    .Line("OC_S6_07").Line("OC_S6_08")
                    .Act(B, "perky recovery")
                    .Key(StateChannels.BirdyEyes, 0f).Key(StateChannels.BirdyRotorSpin, HoverSpin).Act(B, "tour-guide gesture", 0, 1.0f).Act(B, "point at mc", 1)
                    .Build(),
                Shot("S6_SH07", 6, 30f, PlanActors.AtMCHead, 1.35f,
                        "MCU front three-quarter from Birdy's side, the olive slope backlit behind.")
                    .Line("OC_S6_09")
                    .Act(MC, "serious frown")
                    .Build(),
                Shot("S6_SH08", 6, 30f, PlanActors.AtBirdyHead, 1.2f,
                        "TCU rig on Birdy (frac 0.45), static: the second cheer slip.")
                    .Hold(1.0f).Line("OC_S6_10")
                    .Act(B, "cheer slip")
                    .Key(StateChannels.BirdyEyes, 1f, 0, 0.3f).Key(StateChannels.BirdyRotorSpin, SlipSpin, 0, 0.3f)
                    .Build(),
                Shot("S6_SH09", 6, 30f, PlanActors.AtMCHead, 1.35f,
                        "MCU on MC, the S6_SH07 setup.")
                    .Line("OC_S6_11")
                    .Act(MC, "serious frown")
                    .Key(StateChannels.BirdyEyes, 0f).Key(StateChannels.BirdyRotorSpin, HoverSpin)
                    .Build(),
                Shot("S6_SH10", 6, 30f, PlanActors.AtBirdyHead, 1.2f,
                        "TCU rig on Birdy (frac 0.45 → 0.48), slow push: the deepest cheer slip.")
                    .Hold(1.0f).Line("OC_S6_12")
                    .Act(B, "cheer slip")
                    .Key(StateChannels.BirdyEyes, 1f, 0, 0.3f).Key(StateChannels.BirdyRotorSpin, SlipSpin * 0.75f, 0, 0.3f)
                    .Build(),
                Shot("S6_SH11", 6, 35f, PlanActors.AtBirdyHead, 2.5f,
                        "Medium on Birdy at eye level (frac 0.16).")
                    .Line("OC_S6_13")
                    .Act(B, "perky recovery")
                    .Key(StateChannels.BirdyEyes, 0f).Key(StateChannels.BirdyRotorSpin, HoverSpin)
                    .Build(),
                Shot("S6_SH12", 6, 30f, PlanActors.AtMCBlade, 0.45f,
                        "INSERT: slow track along the compass-blade; Hand_R comes to rest on the hilt; glint on \"compass\".")
                    .Line("OC_S6_14").Line("OC_S6_15")
                    .Act(MC, "touch hilt")
                    .Build(),
                Shot("S6_SH13", 6, 30f, PlanActors.AtMCHead, 1.6f,
                        "MCU pushing to CU on MC from \"Somewhere else\".")
                    .Line("OC_S6_16").Line("OC_S6_17")
                    .Act(MC, "look off", 0, 1.5f).Act(MC, "confused head tilt", 1)
                    .Build(),
                Shot("S6_SH14", 6, 35f, PlanActors.AtMCHead, 1.8f,
                        "Two-shot from behind three-quarter, the rim and the black sun beyond.")
                    .Line("OC_S6_18")
                    .Act(B, "warm lean").Act(MC, "look off")
                    .Move(B, At(AtMC, 0.45f, 0f, 0.15f, AtMC, true), 0.6f)
                    .Build(),
                Shot("S6_SH15", 6, 30f, PlanActors.AtBirdyHead, 1.2f,
                        "TCU rig during the 1.0 s hold only, then a 0.5 s dolly out to a medium (frac 0.19) on \"And hey!\".")
                    .Hold(1.0f).Line("OC_S6_19")
                    .Act(B, "cheer slip").Act(B, "perky recovery", 1)
                    .Key(StateChannels.BirdyEyes, 1f, 0, 0.2f).Key(StateChannels.BirdyEyes, 0f, 1)
                    .Move(B, At(AtMC, 0.3f, 0f, 0.9f, AtMC, true), 0.5f, MarkEase.EaseOut, 1)
                    .Build(),
                Shot("S6_SH16", 6, 60f, PlanActors.AtMCHead, 4.0f,
                        "Push in from behind to the gameplay pose G; Birdy zips off to Zip_Path; MC alone facing the black sun.")
                    .Wordless(0.4f).Wordless(1.6f).Wordless(1.5f).Wordless(1.5f)
                    // MC ends on the gameplay idle itself (phase-aligned by the builder), so the first
                    // gameplay frame shows the same pose.
                    .Act(B, "hover idle").Act(B, "zip-off", 1).Act(MC, "idle stand")
                    .Key(StateChannels.BirdyRotorSpin, RevSpin)
                    .Place(B, At(AtMC, 0.5f, 0f, 0f, sun, true))
                    .Move(B, On(CutsceneMarkerIds.ZipPath, CutsceneMarkerIds.ZipPath), 1.6f, MarkEase.EaseIn, 1)
                    .Build(),
            };
        }
    }
}
