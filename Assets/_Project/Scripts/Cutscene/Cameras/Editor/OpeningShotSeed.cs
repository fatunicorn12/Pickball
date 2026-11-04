using System.Collections.Generic;
using Pickleball.Cutscene.Timeline.Plan;
using UnityEngine;
using static Pickleball.Cutscene.Cameras.Editor.ShotSpecBuilder;
using B = Pickleball.Cutscene.Cameras.RigBasis;
using F = Pickleball.Cutscene.Cameras.RigFrame;
using M = Pickleball.Cutscene.CutsceneMarkerIds;
using E = Pickleball.Cutscene.Cameras.EaseKind;
using T = Pickleball.Cutscene.Cameras.TimeRef;

namespace Pickleball.Cutscene.Cameras.Editor
{
    /// <summary>
    /// The 52 shots of <c>Docs/Story/OpeningCutscenes_ShotList.md</c> §4 as camera data, seeded into
    /// the shot table by the cameras setup menu. One entry per table row, in order; times are beat
    /// references into <see cref="OpeningTimingPlan"/>'s duration breakdown (its beat index comments
    /// are next to each shot), so re-timed audio moves the camera moves with it.
    /// <para>Where a row could not be built as written with the built sets and blocking, the entry
    /// carries a note saying what changed and why; those notes are also the report's deviations.</para>
    /// </summary>
    public static class OpeningShotSeed
    {
        /// <summary>Bump when the seed changes: the setup menu then reseeds the table (tuning is lost).</summary>
        public const int Version = 47;

        /// <summary>Degrees off Birdy's forward for medium shots on his face while MC stands in front of
        /// him: the shot list's straight-on axis runs through MC.</summary>
        public const float OffAxis = -50f;

        /// <summary>The TCU rig's angle off Birdy's eyeline. The shot list says 10°; at 4.15·HB that puts
        /// the lens inside MC, who stands 0.9·H in front of Birdy_Guide.</summary>
        public const float TcuYaw = -40f;

        /// <summary>Every shot, in order.</summary>
        public static List<ShotRigSpec> Build()
        {
            var list = new List<ShotRigSpec>(52);
            Scene1(list);
            Scene2(list);
            Scene3(list);
            Scene4(list);
            Scene5(list);
            Scene6(list);
            HideBlackSun(list);
            return list;
        }

        /// <summary>
        /// Degrees the black sun's centre stays outside the frame before the scene-6 gaze: its rim
        /// ring's ~7.2° radius plus 3° (the validator wants the rim ≥ 2° out).
        /// </summary>
        public const float SunMargin = 10.5f;

        /// <summary>
        /// The black sun's first on-screen appearance is S6_SH02, after S6_SH01's gaze. Every shot
        /// from S1_SH07 (the void resolves) to S5_SH03 without its own avoid rule tilts (or pans)
        /// just enough to keep it <see cref="SunMargin"/>° outside the frame. The void hides it in
        /// S1_SH01–S1_SH06.
        /// </summary>
        static void HideBlackSun(List<ShotRigSpec> list)
        {
            foreach (var s in list)
            {
                if (s == null || s.shotId.StartsWith("S6_")) continue;
                if (s.shotId.StartsWith("S1_") && string.CompareOrdinal(s.shotId, "S1_SH07") < 0) continue;
                if (!string.IsNullOrEmpty(s.avoidMarker) && s.avoidMarker != M.BlackSun) continue;
                s.avoidMarker = M.BlackSun;
                // Scene 5 keeps the rim at least 5° out (shot list), not 2°.
                s.avoidMargin = Mathf.Max(s.avoidMargin, s.shotId.StartsWith("S5_") ? SunMargin + 3f : SunMargin);
            }
        }

        static ShotSpecBuilder Shot(string id, float lens) => new ShotSpecBuilder(id, lens, lens);
        static ShotSpecBuilder Shot(string id, float lensA, float lensB) => new ShotSpecBuilder(id, lensA, lensB);
        static T S(float at = 0f) => T.Start(at);
        static T End(float at = 0f) => T.End(at);
        static T Bt(int beat, float at = 0f) => T.Beat(beat, at);

        // ── Scene 1: the white void ─────────────────────────────────────────────

        static void Scene1(List<ShotRigSpec> l)
        {
            // Beats [W3.0, OC_S1_01]
            l.Add(Shot("S1_SH01", 40f)
                .Key(S(), H(B.MCMid, F.World, 0f, 2.6f, 0f))
                .Key(End(), H(B.MCMid, F.World, 0f, 2.0f, 0f), E.InOut)
                .TopDown(At(B.MCMid))
                .Subject(B.MCHead).Build());

            // Beats [H0.8, OC_S1_02]
            l.Add(Shot("S1_SH02", 25f)
                .Key(S(), H(B.MCEyes, F.World, 0f, 0.55f, 0f))
                .Key(End(), H(B.MCEyes, F.World, 0f, 0.55f * 0.97f, 0f), E.InOut)
                .TopDown(At(B.MCEyes))
                // An eye ECU frames the eyes, not the head centre (above them by the hair on MC, by more
                // on a swapped-in model).
                .Subject(B.MCEyes).Build());

            // Beats [OC_S1_03]
            l.Add(Shot("S1_SH03", 40f)
                // Low beside his legs on the side he sits up toward (MC_SitUp ends facing the root's
                // -Z), rising to a front three-quarter at eye level so his face is seen.
                .Key(S(), InMarker(H(B.Marker, F.Marker, -1.4f, 0.25f, -0.6f), M.MCStart).WithMarker(M.MCStart))
                .Key(End(), Level(H(B.MCHead, F.MC, -0.85f, 0f, -0.85f), B.MCHead), E.In)
                .Aim(At(B.MCHead), 0.45f, 0.65f)
                .Subject(B.MCHead).Build());

            // Beats [OC_S1_04, W0.6]. "Isekai!" lands ~0.9 s into the line.
            // 1.3·H, not the table's 2.6·H: at 2.6·H on the 55° lens MC filled a third of the frame
            // height with his feet at 0.3. At 1.3·H the full figure fills it, feet in the bottom 15%.
            l.Add(Shot("S1_SH04", 55f)
                .Locked(H(B.MCFeet, F.MC, 0.2f, 0.04f, 1.25f))
                // Near the ground and tilted up ~20°: a real low angle.
                .Aim(At(B.MCHead), 0.5f, 0.7f)
                .Noise(NoiseKind.Shake6D, 0f, 2f)
                .Burst(Bt(0, 0.9f), 0.1f, 0.4f, 0.6f, 1.2f)
                .Subject(B.MCHead).Build());

            // Beats [OC_S1_05, H1.0, OC_S1_06]
            l.Add(Shot("S1_SH05", 40f)
                // Over his LEFT shoulder: he reaches with the free left hand (the sword is in his right),
                // into the empty white ahead of it.
                // Wide of the shoulder (review round 3: from right behind, his head hid the hand):
                // the hand reaches toward frame left/centre into the white, his cheek at frame right.
                .Locked(H(B.MCHead, F.MC, -0.6f, 0.0f, -0.9f))
                .Aim(H(B.MCHead, F.MC, -0.7f, -0.05f, 1.3f), 0.2f, 0.5f)
                .Noise(NoiseKind.Handheld, 0.2f)
                .Subject(B.MCHead).Build());

            // Beats [OC_S1_07, H0.8]
            l.Add(Shot("S1_SH06", 30f)
                .Locked(H(B.MCHead, F.MC, 0.1f, 0f, 1.5f))
                .Aim(At(B.MCEyes), 0.5f, 0.6f)
                .Subject(B.MCHead).Build());

            // Beats [W1.0, W3.0, W2.0]. Starts on S1_SH06's pose, cranes back and up, ending behind MC.
            // MC stands facing south (−Z) from S1_SH04 (the sit-up ends facing his feet) and turns
            // north at 4.0–5.5 s, so the 180° is his turn: the camera stays south of him and works
            // in MC_Start's fixed frame (+Z north) after the first key, swinging out to his right.
            l.Add(Shot("S1_SH07", 40f, 50f)
                .Key(S(), H(B.MCHead, F.MC, 0.1f, 0f, 1.5f))
                // One steady crane back, up and east (monotonic keys): the old west-then-east swing put
                // the whole sideways move into the last 2 s.
                .Key(S(2.0f), InMarker(H(B.MCFeet, F.Marker, 0.3f, 0.8f, -2.2f), M.MCStart))
                .Key(S(4.0f), InMarker(H(B.MCFeet, F.Marker, 1.2f, 1.2f, -2.7f), M.MCStart))
                // Ends behind MC and off to the east, looking north-north-west over the shrine to the
                // olive slope: the black sun (north-north-east) stays well off frame right without any
                // avoid correction (a tilt had the shot end looking steeply down).
                .Key(End(), InMarker(H(B.MCFeet, F.Marker, 2.0f, 1.5f, -3.0f), M.MCStart))
                .Crane(E.InOut)
                .Aim(S(), At(B.MCEyes), 0.5f, 0.6f)
                .Aim(End(), At(B.MCHead), 0.5f, 0.4f, E.InOut)
                .Subject(B.MCHead)
                .Note("Lens 40°→50° as the table says, so it pops from S1_SH06's 30° at the VFX transition.").Build());
        }

        // ── Scene 2: the lost propeller ─────────────────────────────────────────

        static void Scene2(List<ShotRigSpec> l)
        {
            // Beats [W1.0, W1.2, W1.0]. Fixed at S1_SH07's end position; tilt up to Birdy 1.0–2.2 s.
            l.Add(Shot("S2_SH01", 50f)
                .Locked(InMarker(H(B.MCFeet, F.Marker, 2.0f, 1.5f, -3.0f), M.MCStart))
                // Pan (not tilt) off the black sun: a tilt pushed Birdy, high in the north-east, out of the bottom.
                .Avoid(M.BlackSun, SunMargin, true)
                .Aim(S(), At(B.MCHead), 0.5f, 0.4f)
                .Aim(Bt(1), At(B.MCHead), 0.5f, 0.4f, E.Linear)
                .Aim(S(1.6f), At(B.MCHead), 0.5f, 0.1f, E.In)
                .Aim(Bt(2), At(B.BirdyHead), 0.5f, 0.55f, E.InOut)
                .Subject(B.MCHead, S(), S(1.6f))
                .Subject(B.BirdyHead, S(1.6f), End())
                .Note("The tilt: 1.0–1.6 s the composer slides MC.Head to the bottom of frame (the camera tilts up), then hands its aim to Birdy.Head by 2.2 s; the angle follows where Birdy is.").Build());

            // Beats [W2.2, H1.0]. NO-CHAR: follow the propeller (damping 0.4); the offset is
            // Ledge_View − Propeller_Ledge, so the camera lands on Ledge_View as the prop lands.
            l.Add(Shot("S2_SH02", 40f)
                .Follow(B.PropRoot, new Vector3(0.4f, 0.4f, 0.4f), M.PropellerLedge)
                .Key(S(), Marker(M.LedgeView))
                .Aim(At(B.PropRoot), 0.5f, 0.55f)
                .Insert(B.PropRoot).Build());

            // Beats [W1.4, OC_S2_01, OC_S2_02]
            l.Add(Shot("S2_SH03", 45f)
                .Locked(Toward(H(B.MCFeet, F.Toward, -0.5f, 1.25f, -1.8f), M.BirdyLand))
                .Aim(Marker(M.BirdyLand), 0.5f, 0.4f)
                .Noise(NoiseKind.Handheld, 0.3f)
                .Cap(0.28f)
                .Subject(B.BirdyHead).Subject(B.MCHead)
                .Note("\"Behind MC's left shoulder\" is taken relative to MC's line to Birdy_Land, not his facing: from his facing, MC's body covers Birdy_Land (30° to his right).").Build());

            // Beats [OC_S2_03]
            l.Add(Shot("S2_SH04", 30f)
                .Locked(H(B.MCHead, F.MC, -0.15f, 0f, 1.3f))
                .Aim(At(B.MCEyes), 0.45f, 0.6f)
                .Subject(B.MCHead).Build());

            // Beats [W0.2, OC_S2_04]. Locked on Birdy_Lean (MC_Start + 0.45·H north), not on Birdy, so he lunges in.
            var lean = InMarker(new RigPoint { basis = B.Marker, marker = M.MCStart, h = new Vector3(0f, 0f, 0.45f) }, M.MCStart);
            var leanCam = lean;
            // On MC's left (his free hand), not his right: from the right his sword blade crossed
            // Birdy's beak and chest (review round 3).
            // Far enough that Birdy reads about 0.28 of the frame on screen once he lands. A frac cap
            // used to set this distance, but it backed the camera away as he lunged, a visible jolt
            // in a shot that is meant to be locked.
            const float leanDistance = 3.15f;
            leanCam.hb = new Vector3(-Mathf.Sin(75f * Mathf.Deg2Rad) * leanDistance, 0.2f, -Mathf.Cos(75f * Mathf.Deg2Rad) * leanDistance);
            leanCam.level = B.BirdyHead;
            var leanEyes = lean;
            leanEyes.level = B.BirdyEyes;
            l.Add(Shot("S2_SH05", 75f)
                .Locked(leanCam)
                .Aim(leanEyes, 0.5f, 0.52f)
                .Subject(B.BirdyHead, S(0.25f), End())
                .Note("Camera 2.33·HB from Birdy_Lean's head point but 75° east of Birdy's forward: straight on, 2.33·HB is more than the 0.45·H between Birdy and MC and puts the lens inside MC.").Build());

            // Beats [OC_S2_05]
            l.Add(Shot("S2_SH06", 40f)
                // Over his LEFT shoulder: the sword in his right hand hid Birdy.
                .Locked(H(B.MCHead, F.MC, -0.5f, 0.15f, -0.5f))
                .Push(0.05f)
                .Aim(At(B.BirdyHead), 0.55f, 0.42f)
                .Cap(0.25f)
                .Subject(B.BirdyHead)
                .Note("0.5·H right of MC.Head instead of 0.35·H: with the real actors MC's hair covered Birdy from 0.35·H.").Build());

            // Beats [OC_S2_06]
            l.Add(Shot("S2_SH07", 35f)
                .Locked(H(B.MCFeet, F.MC, 0.2f, 0.45f, 1.1f))
                .Aim(At(B.MCHead), 0.5f, 0.6f)
                .Cap(0.28f)
                .Subject(B.MCHead).Build());

            // Beats [OC_S2_07, OC_S2_08, H0.5]
            l.Add(Shot("S2_SH08", 40f)
                .Locked(Side(H(B.FeetMid, F.PairSide, 0f, 0.5f, 2.2f), SideRule.MCLeft))
                .Aim(At(B.PairMid), 0.5f, 0.5f)
                .Cap(0.28f)
                .Subject(B.MCHead).Subject(B.BirdyHead).Build());

            // Beats [OC_S2_09 (mid-line hold)]
            l.Add(Shot("S2_SH09", 40f)
                // 50° off Birdy's forward on MC's left (free-hand) side: on the right his back and
                // sword blade lay across Birdy (review round 3).
                .Locked(Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 8f), -OffAxis), B.BirdyEyes))
                .Aim(At(B.BirdyHead), 0.5f, 0.55f)
                .Cap(0.28f)
                .Subject(B.BirdyHead)
                .Note("50° off Birdy's forward: he faces MC, so the straight-on axis looks through MC's back.").Build());

            // Beats [OC_S2_10, H0.6, W3.0, H1.5]. Locked, then a 3 s crane over MC's right shoulder.
            l.Add(Shot("S2_SH10", 40f)
                .Key(Bt(2), H(B.MCHead, F.MC, 0.3f, 0.05f, 1.0f))
                // Up and over the right shoulder, well clear of his head (review round 3: at 0.35 H up it
                // skimmed past the back of his hair).
                .Key(Bt(2, 1.5f), H(B.MCHead, F.MC, 1.25f, 0.7f, -0.7f))
                // The end key hangs off MC's feet (0.87 H = the head anchor + 0.2 H): his last second
                // eases into the gameplay controller, and in edit mode (no controller) a head-based key
                // dropped 0.7 m there.
                .Key(Bt(3), H(B.MCFeet, F.MC, 0.45f, 0.87f, -2.4f))
                .Crane(E.InOut)
                .Aim(S(), At(B.MCHead), 0.45f, 0.6f)
                .Aim(Bt(2), At(B.MCHead), 0.45f, 0.6f, E.Linear)
                .Aim(Bt(2, 1.5f), At(B.MCHead), 0.32f, 0.5f, E.InOut)
                // Stay on MC a little longer, already settling low-left, then hand the aim to the
                // ledge: a direct head-to-ledge swing dipped MC into the bottom-left corner.
                .Aim(Bt(2, 2.2f), At(B.MCHead), 0.42f, 0.38f, E.InOut)
                .Aim(Bt(3), Marker(M.PropellerLedge), 0.62f, 0.66f, E.InOut)
                .Cap(0.28f)
                .Subject(B.MCHead)
                .Note("Crane ends at MC.Head + (0.35, 0.3, -2.9)·H, not (0.35, 0.55, -1.2)·H: from 1.2·H behind, MC.Head and the 5 m ledge are 40° apart and can't share a 40° frame; from 2.9·H MC sits bottom-left and the ledge upper-right as the framing column wants.").Build());
        }

        // ── Scene 3: "You're the one" ───────────────────────────────────────────

        static void Scene3(List<ShotRigSpec> l)
        {
            // Beats [W1.2]
            l.Add(Shot("S3_SH01", 40f)
                // The north side of the pair at the ledge foot (SideRule.MCRight here): shooting south keeps the black sun
                // behind the camera (SunSide looked north-east, straight at it).
                .Locked(Side(H(B.FeetMid, F.PairSide, 0f, 0.5f, 2.4f), SideRule.MCRight))
                .Aim(At(B.PairMid), 0.5f, 0.5f)
                .Cap(0.28f)
                .Subject(B.MCHead).Subject(B.BirdyHead).Build());

            // Beats [W0.9]. INSERT; Birdy's eyes stay below the frame.
            // 60° above his forward (not 45°) and the socket low in frame, so his eyes stay below the
            // bottom edge now that HB is his real head.
            var socketCam = Yaw(HB(B.BirdySocket, F.Birdy, 0f, 1.8f * 0.866f, 1.8f * 0.5f), 60f);
            l.Add(Shot("S3_SH02", 30f)
                .Locked(socketCam)
                .Aim(At(B.BirdySocket), 0.5f, 0.3f)
                .Insert(B.BirdySocket)
                .Note("45° above and 60° round from Birdy's front: straight on, the lens sat against MC's head; 60° still keeps his eyes below the frame.").Build());

            // Beats [W0.8, OC_S3_01]. Pedestals up with Birdy (Follow, damping 0.25).
            var front9 = Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 9f), OffAxis);
            l.Add(Shot("S3_SH03", 40f)
                .Follow(B.BirdyHead, new Vector3(0.25f, 0.25f, 0.25f))
                .Key(S(), front9)
                .Aim(At(B.BirdyHead), 0.5f, 0.55f)
                .Subject(B.BirdyHead)
                .Note("50° off Birdy's forward (MC stands on the straight-on axis).").Build());

            // Beats [OC_S3_02]. Orbit 90° from S3_SH03's angle.
            l.Add(Shot("S3_SH04", 40f)
                .Orbit(B.BirdyHead, -90f, S(), End(), E.InOut)
                .Key(S(), front9)
                .Aim(At(B.BirdyHead), 0.5f, 0.55f)
                .Subject(B.BirdyHead)
                .Note("Orbits 90° the other way (away from MC), so MC never passes between the lens and Birdy.").Build());

            // Beats [H0.8, OC_S3_03]. Push 12·HB → 8·HB.
            l.Add(Shot("S3_SH05", 35f)
                // 75° off his eyeline: MC (a step further off, back turned) stays out of the frame.
                .Key(S(), Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 12f), 75f), B.BirdyEyes))
                .Key(End(), Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 8f), 75f), B.BirdyEyes), E.In)
                .Aim(At(B.BirdyEyes), 0.5f, 0.55f)
                .Cap(0.22f)
                .Subject(B.BirdyHead)
                .Note("50° off-axis; MC (0.65·H in front of Birdy) is in the frame's side, not out of it: at 8–12·HB nothing keeps him out.").Build());

            // Beats [OC_S3_04]. INSERT, lateral track 0.15·H.
            l.Add(Shot("S3_SH06", 30f)
                .Key(S(), H(B.MCBlade, F.MC, -0.55f, 0.25f, 0.2f))
                .Key(End(), H(B.MCBlade, F.MC, -0.5f, 0.25f, 0.32f), E.InOut)
                .Aim(H(B.MCBlade, F.MC, -0.1f, 0.13f, 0.13f), 0.5f, 0.5f)
                .Insert(B.MCBlade)
                .Note("Behind-right of MC's BODY and above his right shoulder, looking down the blade that rests on it (guard at his chest, tip up over the shoulder). MC_TurnAway bakes a 180° body turn while the actor root keeps facing Path_Start (toward Birdy), so the body's behind-right is the root's front-left: offsets (−x, +z) in the root frame, and Birdy is behind the camera. Aimed at mid-blade, not the guard, which his hair hides from here.").Build());

            // Beats [OC_S3_05, OC_S3_06]. In MC_Handover's frame (MC's frame once he has turned).
            // x -0.45 (not -0.3): Birdy, 0.65 H in front of MC, sits in the bottom-right foreground
            // instead of over MC's torso.
            // On the marker, not MC's feet: MC steps onto MC_Handover in S3_SH07's first 0.6 s and a
            // feet-based camera rode along at up to 0.27 m a frame (review round 3, jump check (i)).
            var hero = InMarker(H(B.Marker, F.Marker, -0.45f, 0.32f, 1.2f), M.MCHandover).WithMarker(M.MCHandover);
            l.Add(Shot("S3_SH07", 40f)
                .Locked(hero)
                .Push(0.03f)
                .Aim(At(B.MCHead), 0.5f, 0.64f)
                .Cap(0.28f)
                .Subject(B.MCHead).Build());

            // Beats [OC_S3_07, H0.8, OC_S3_08]
            l.Add(Shot("S3_SH08", 35f)
                .Locked(Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 10f), OffAxis), B.BirdyEyes))
                .Aim(At(B.BirdyHead), 0.5f, 0.55f)
                .Cap(0.22f)
                .Subject(B.BirdyHead)
                .Note("50° off Birdy's forward (MC stands on the straight-on axis).").Build());

            // Beats [OC_S3_09]
            l.Add(Shot("S3_SH09", 40f)
                .Locked(Side(H(B.FeetMid, F.PairSide, 0f, 0.6f, 2.0f), SideRule.MCRight))
                .Aim(At(B.PairMid), 0.5f, 0.5f)
                .Cap(0.28f)
                .Subject(B.MCHead).Subject(B.BirdyHead).Build());

            // Beats [OC_S3_10]. From S3_SH07's pose, dolly 0.15·H in, zoom 40° → 36°.
            l.Add(Shot("S3_SH10", 40f, 36f)
                .Key(S(), hero)
                .Key(End(), InMarker(H(B.MCFeet, F.Marker, -0.45f, 0.32f, 1.05f), M.MCHandover), E.InOut)
                .Aim(At(B.MCHead), 0.5f, 0.64f)
                .Cap(0.28f)
                .Subject(B.MCHead).Build());

            // Beats [H0.8, OC_S3_11, H0.5]
            l.Add(Shot("S3_SH11", 30f)
                .Locked(Side(H(B.FeetMid, F.PairSide, 0f, 0.55f, 4.5f), SideRule.MCRight))
                .Aim(At(B.PairMid), 0.5f, 0.45f)
                .Subject(B.MCHead).Subject(B.BirdyHead).Build());

            // Beats [OC_S3_12]. Front three-quarter on his LEFT: MC_ResignedMutter looks off to his left, away from Birdy.
            l.Add(Shot("S3_SH12", 30f)
                .Locked(H(B.MCHead, F.MC, -0.9f, 0f, 0.6f))
                .Aim(At(B.MCEyes), 0.45f, 0.6f)
                .Cap(0.28f)
                .Subject(B.MCHead)
                .Note("(0.7, 0, 1.05)·H instead of (0.2, 0, 1.25)·H: Birdy hovers 0.65·H in front of MC, so the near-frontal line put his head over MC's face.").Build());

            // Beats [OC_S3_13, H0.8, OC_S3_14, H0.5]. Boom down to eye level at OC_S3_14 over 0.8 s.
            l.Add(Shot("S3_SH13", 40f)
                .Key(S(), Yaw(HB(B.BirdyHead, F.Birdy, 0f, 4f, 9f), OffAxis))
                .Key(Bt(2), Yaw(HB(B.BirdyHead, F.Birdy, 0f, 4f, 9f), OffAxis))
                .Key(Bt(2, 0.8f), Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 9f), OffAxis), B.BirdyEyes))
                .Crane(E.Out)
                .Aim(At(B.BirdyHead), 0.5f, 0.5f)
                .Cap(0.22f)
                .Subject(B.BirdyHead)
                .Note("50° off Birdy's forward (MC stands on the straight-on axis).").Build());
        }

        // ── Scene 4: the path ───────────────────────────────────────────────────

        static void Scene4(List<ShotRigSpec> l)
        {
            // Beats [W1.5, OC_S4_01, OC_S4_02, OC_S4_03, H0.8]
            l.Add(Shot("S4_SH01", 35f)
                .Track(M.CamTrackS4Start, M.CamTrackS4End, End(1000f), OpeningTimingPlan.PathWalkSpeed, M.PathTrackA, M.PathEnd)
                .Aim(At(B.PairMid), 0.45f, 0.55f)
                .Subject(B.MCHead).Subject(B.BirdyHead).Build());

            // Beats [OC_S4_04, OC_S4_05, H0.5]. Leading frontal track 6·HB ahead of Birdy.
            l.Add(Shot("S4_SH02", 40f)
                .Follow(B.BirdyHead, Vector3.zero)
                .Key(S(), HB(B.BirdyHead, F.Birdy, 0f, 0f, 7.5f))
                .Aim(At(B.BirdyHead), 0.4f, 0.55f)
                .Subject(B.BirdyHead).Subject(B.MCHead)
                .Note("MC reads screen-left of Birdy, not right: Birdy putters on MC's west side and the camera looks back south.").Build());

            // Beats [OC_S4_06, OC_S4_07, H0.6, OC_S4_08, H1.0, W1.0]. Stops after "Never mind.".
            l.Add(Shot("S4_SH03", 35f)
                .Track(M.CamTrackS4Start, M.CamTrackS4End, Bt(4), OpeningTimingPlan.PathWalkSpeed, M.PathTrackA, M.PathEnd)
                .Aim(At(B.PairMid), 0.45f, 0.55f)
                .Subject(B.MCHead).Subject(B.BirdyHead).Build());
        }

        // ── Scene 5: the Academy ────────────────────────────────────────────────

        static void Scene5(List<ShotRigSpec> l)
        {
            // Beats [W1.0, W5.0, OC_S5_01, OC_S5_02]. Follows the walk-in, cranes 1.0–6.0 s.
            l.Add(Shot("S5_SH01", 40f, 50f)
                .Key(Bt(1), H(B.MCFeet, F.MC, 0.15f, 0.55f, -1.4f))
                .Key(Bt(1, 2.5f), H(B.MCFeet, F.MC, 0.08f, 2.3f, -2.9f))
                // Ends south-east of the pair looking north-west over them, so the black sun (NNE)
                // stays out of frame on the right without a pan.
                .Key(Bt(2), H(B.MCFeet, F.MC, 1.8f, 4.0f, -3.6f))
                .Crane(E.InOut)
                .Aim(S(), At(B.PairMid), 0.5f, 0.55f)
                .Aim(Bt(1), At(B.PairMid), 0.5f, 0.55f, E.Linear)
                // Ends on the pair, centred low (MC.Head about (0.5, 0.27): the shot list's 0.13 is under the subtitle card), the Academy spread beyond them
                // (review round 3: the sun pan pushed them into the bottom-right corner).
                .Aim(Bt(2), At(B.PairMid), 0.5f, 0.25f, E.InOut)
                .Zoom(Bt(1), Bt(2), E.InOut)
                .Cap(0.25f)
                .Avoid(M.BlackSun, 5f, true)
                .Subject(B.MCHead)
                .Note("Black sun kept ≥ 5° out of frame by panning when it would show; the end aims at the pair, not the gnomon, so they stay centred (review round 3).").Build());

            // Beats [OC_S5_03, H0.5]
            l.Add(Shot("S5_SH02", 35f)
                // Birdy turns to face south for this shot; 30° to his right puts the camera south-south-east
                // looking north-north-west: the Academy behind him, the black sun far off frame right.
                .Locked(Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 10f), -15f), B.BirdyEyes))
                .Aim(At(B.BirdyHead), 0.5f, 0.55f)
                .Cap(0.22f)
                .Avoid(M.BlackSun, 5f, true)
                .Subject(B.BirdyHead)
                .Note("Front three-quarter as written; Birdy faces north there, so the background is the slope, not the Academy.").Build());

            // Beats [H0.8, OC_S5_04, OC_S5_05, OC_S5_06 in hold, OC_S5_07, H1.5]
            l.Add(Shot("S5_SH03", 35f)
                // A true profile from the south (no yaw), raised 0.5 H and looking down on them: the
                // Academy, below the crest, fills the background and the black sun (13° up) stays above
                // the frame. Level with MC's eyes and yawed -20° it saw MC from behind.
                // Review round 4: from 0.5 H up, MC's hair hid his face (his deadpan through the long
                // pause never read). Near eye level now: a true side-on two-shot, both faces in
                // profile; the sun avoid tilts down just enough to keep it out.
                .Locked(Side(H(B.PairMid, F.PairSide, 0f, 0.08f, 2.2f), SideRule.South))
                .Aim(At(B.PairMid), 0.5f, 0.55f)
                .Cap(0.28f)
                .Avoid(M.BlackSun, 5f, true)
                .Subject(B.MCHead).Subject(B.BirdyHead)
                .Note("The pair faces along NE–SW, so the side-on camera is south-east of them and the black sun (NNE) stays well off frame right; a pan correction remains as a guard.").Build());
        }

        // ── Scene 6: the world coming apart ────────────────────────────────────

        static void Scene6(List<ShotRigSpec> l)
        {
            const string sun = M.BlackSun;
            var tcu = Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 4.15f), TcuYaw), B.BirdyEyes);
            var tcuPushed = Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 3.9f), TcuYaw), B.BirdyEyes);
            var tcuOut = Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 10f), TcuYaw), B.BirdyEyes);
            const string tcuNote = "TCU rig 40° off Birdy's eyeline, not 10°: at 4.15·HB the 10° rig puts the lens inside MC.";

            // Beats [W1.0, W2.0, OC_S6_01]. Rack focus MC.Eyes → Rim_Gaze over 1.0–3.0 s.
            // (0.33, −0.62) rather than the table's (0.3, −0.45): that put MC.Head at x 0.13; the
            // shot list wants it in the left third at x 0.2–0.35. Further back, the lens also clears his hair.
            l.Add(Shot("S6_SH01", 27f)
                .Locked(Toward(H(B.MCHead, F.Toward, 0.33f, 0.06f, -0.62f), sun))
                .Push(0.02f)
                .Aim(Marker(M.RimGaze), 0.62f, 0.48f)
                .Dof(2.0f)
                // "The black sun is kept just above the top edge" (rim included): revealed in S6_SH02.
                .Avoid(M.BlackSun, 8f)
                .Focus(S(), At(B.MCEyes))
                .Focus(Bt(1), At(B.MCEyes))
                .Focus(Bt(2), Marker(M.RimGaze), E.InOut)
                .Subject(B.MCHead)
                .Note("Behind the right ear of MC's final facing (toward the black sun), not his live facing: he turns to the sun during the shot.").Build());

            // Beats [W0.5, OC_S6_02, H0.8]
            l.Add(Shot("S6_SH02", 35f)
                // Further back than the table's (0.5, 0.9, -2.0): that made MC large and centred.
                // MC small at lower left, about (0.3, 0.28), clear of the subtitle cards (review
                // round 3: at (0.9, 1.4, -4.5) he sat bottom-centre under the card).
                .Locked(Toward(H(B.MCFeet, F.Toward, 2.2f, 1.15f, -4.2f), sun))
                .Aim(Marker(M.RimGaze), 0.55f, 0.38f)
                .Subject(B.MCHead).Build());

            // Beats [OC_S6_03]. Aimed at the pair's final midpoint so the frame stays locked as Birdy darts in.
            l.Add(Shot("S6_SH03", 35f)
                .Locked(Toward(H(B.MCHead, F.Toward, 0.9f, -0.05f, 1.6f), sun))
                .Aim(Toward(H(B.MCHead, F.Toward, 0.25f, -0.025f, 0f), sun), 0.5f, 0.55f)
                .Cap(0.28f)
                .Subject(B.MCHead)
                .Subject(B.BirdyHead, S(0.3f), End()).Build());

            // Beats [OC_S6_04, OC_S6_05]. Truck right 0.4·H; DoF f/4 on Birdy.Eyes.
            l.Add(Shot("S6_SH04", 40f)
                .Key(S(), Toward(H(B.MCHead, F.Toward, 0.55f, -0.05f, -0.25f), sun))
                .Key(End(), Toward(H(B.MCHead, F.Toward, 0.95f, -0.05f, -0.25f), sun), E.InOut)
                .Aim(At(B.BirdyHead), 0.38f, 0.55f)
                .Cap(0.20f)
                .Dof(4f)
                .Focus(S(), At(B.BirdyEyes))
                .Subject(B.BirdyHead)
                .Note("Capped at frac 0.20 (the table's ≤ 0.20): from beside MC's shoulder Birdy_Guide is only ~1.2·H away, frac ≈ 0.4 at nominal sizes, so the camera backs off along the line to Birdy.").Build());

            // Beats [H1.0, OC_S6_06]. TCU 1, push 4.15 → 3.9·HB.
            l.Add(Shot("S6_SH05", 30f)
                .Key(S(), tcu)
                .Key(End(), tcuPushed, E.InOut)
                .Aim(At(B.BirdyEyes), 0.5f, 0.58f)
                .Dof(2.8f).Focus(S(), At(B.BirdyEyes))
                .AllowTcu(S(), End())
                .Subject(B.BirdyHead).Note(tcuNote).Build());

            // Beats [OC_S6_07, OC_S6_08]. The tour-guide rig, then a pan left that brings MC in.
            l.Add(Shot("S6_SH06", 40f)
                .Locked(Toward(H(B.MCHead, F.Toward, 0.95f, -0.05f, -0.25f), sun))
                .Aim(S(), At(B.BirdyHead), 0.38f, 0.55f)
                .Aim(Bt(1), At(B.BirdyHead), 0.38f, 0.55f, E.Linear)
                .Aim(Bt(1, 1.0f), At(B.PairMid), 0.5f, 0.55f, E.InOut)
                .Cap(0.20f)
                .Subject(B.BirdyHead)
                .Subject(B.MCHead, Bt(1, 1.0f), End())
                .Note("The pan is the composer's aim moving from Birdy to Pair.Mid over 1.0 s, so its angle is whatever brings MC in (the table's 25° didn't, from this rig).").Build());

            // Beats [OC_S6_09]
            var mcu = Toward(H(B.MCHead, F.Toward, -0.2f, 0f, 1.0f), sun);
            l.Add(Shot("S6_SH07", 30f)
                .Locked(mcu)
                .Aim(At(B.MCEyes), 0.45f, 0.6f)
                .Cap(0.28f)
                .Subject(B.MCHead).Build());

            // Beats [H1.0, OC_S6_10]. TCU 2, static.
            l.Add(Shot("S6_SH08", 30f)
                .Locked(tcu)
                .Aim(At(B.BirdyEyes), 0.5f, 0.58f)
                .Dof(2.8f).Focus(S(), At(B.BirdyEyes))
                .AllowTcu(S(), End())
                .Subject(B.BirdyHead).Note(tcuNote).Build());

            // Beats [OC_S6_11]. The S6_SH07 setup.
            l.Add(Shot("S6_SH09", 30f)
                .Locked(mcu)
                .Aim(At(B.MCEyes), 0.45f, 0.6f)
                .Cap(0.28f)
                .Subject(B.MCHead).Build());

            // Beats [H1.0, OC_S6_12]. TCU 3, push.
            l.Add(Shot("S6_SH10", 30f)
                .Key(S(), tcu)
                .Key(End(), tcuPushed, E.InOut)
                .Aim(At(B.BirdyEyes), 0.5f, 0.58f)
                .Dof(2.8f).Focus(S(), At(B.BirdyEyes))
                .AllowTcu(S(), End())
                .Subject(B.BirdyHead).Note(tcuNote).Build());

            // Beats [OC_S6_13]
            l.Add(Shot("S6_SH11", 35f)
                // +70°, a step closer, Birdy right of centre: the shared −40° put MC's hair across the
                // foreground, and at 10 HB, centred, a lock of it still edged into frame right.
                .Locked(Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 8.5f), 70f), B.BirdyEyes))
                .Aim(At(B.BirdyHead), 0.6f, 0.55f)
                .Cap(0.22f)
                .Subject(B.BirdyHead)
                .Note("50° off Birdy's forward (MC stands on the straight-on axis).").Build());

            // Beats [OC_S6_14, OC_S6_15]. INSERT, track 0.2·H along the blade.
            l.Add(Shot("S6_SH12", 30f)
                .Key(S(), H(B.MCBlade, F.MC, 0.55f, -0.1f, 0f))
                .Key(End(), H(B.MCBlade, F.MC, 0.55f, 0.1f, 0f), E.InOut)
                .Aim(At(B.MCBlade), 0.5f, 0.5f)
                .Insert(B.MCBlade)
                .Note("0.55·H from the blade instead of 0.45·H: at 0.45·H the lens grazed MC's sleeve.").Build());

            // Beats [OC_S6_16, OC_S6_17]. Static, then from 1.5 s into OC_S6_16 push 1.6·H → 1.05·H.
            l.Add(Shot("S6_SH13", 30f)
                // Front-left of MC (Birdy hovers front-right): MCU, then a slow push to CU. No Birdy
                // cap: with Birdy 1.6 m off MC it held the camera back at 3.1 m, a static medium
                // (review round 3; the same in edit mode and Play).
                .Key(S(), Toward(H(B.MCHead, F.Toward, -0.75f, 0f, 1.3f), sun))
                .Key(Bt(0, 1.0f), Toward(H(B.MCHead, F.Toward, -0.75f, 0f, 1.3f), sun))
                .Key(End(), Toward(H(B.MCHead, F.Toward, -0.42f, 0f, 0.72f), sun), E.InOut)
                .Aim(At(B.MCEyes), 0.5f, 0.6f)
                .Subject(B.MCHead).Build());

            // Beats [OC_S6_18]
            l.Add(Shot("S6_SH14", 35f)
                .Locked(Toward(H(B.PairMid, F.Toward, 0.6f, 0.1f, -1.8f), sun))
                .Aim(At(B.PairMid), 0.45f, 0.45f)
                .Cap(0.28f)
                .Subject(B.MCHead).Subject(B.BirdyHead).Build());

            // Beats [H1.0, OC_S6_19]. TCU in the hold, then a 0.5 s dolly back to 10·HB.
            // 60° off his eyeline (not the shared 40°): at 40° MC's hair filled the left third.
            var tcuWide = Level(Yaw(HB(B.BirdyHead, F.Birdy, 0f, 0f, 4.15f), -60f), B.BirdyEyes);
            l.Add(Shot("S6_SH15", 30f)
                .Key(S(), tcuWide)
                .Key(Bt(1), tcuWide)
                .Key(Bt(1, 0.5f), tcuOut, E.InOut)
                .Aim(At(B.BirdyEyes), 0.5f, 0.58f)
                .Dof(2.8f).Focus(S(), At(B.BirdyEyes))
                .AllowTcu(S(), Bt(1, 0.5f))
                .Subject(B.BirdyHead).Note(tcuNote).Build());

            // Beats [W0.4, W1.6, W1.5, W1.5]. Push in 0.5–3.5 s from 1.6 × the distance to pose G.
            // Lead decision (review round 2): end behind MC with the black sun fully in frame,
            // upper-centre above his head, then BLEND 2.0 s into the live orbit camera. The orbit
            // camera's resting pitch (16° down) can't frame the sun, so a MATCH at pose G cropped it.
            l.Add(Shot("S6_SH16", 60f)
                .Key(S(), Toward(H(B.MCFeet, F.Toward, 0f, 1.0f, -4.6f), sun))
                .Key(Bt(1, 0.1f), Toward(H(B.MCFeet, F.Toward, 0f, 1.0f, -4.6f), sun))
                .Key(Bt(3), Toward(H(B.MCFeet, F.Toward, 0f, 0.9f, -3.0f), sun), E.InOut)
                .Aim(Marker(M.BlackSun), 0.5f, 0.8f)
                .Subject(B.MCHead)
                .Note("Ends behind MC, the black sun upper-centre above his head; CutsceneCameraHandback blends 2.0 s into the orbit camera.").Build());
        }
    }

    /// <summary>Small point helpers the seed uses.</summary>
    static class RigPointSeedExtensions
    {
        /// <summary>Sets the marker a Marker basis names.</summary>
        public static RigPoint WithMarker(this RigPoint p, string id)
        {
            p.marker = id;
            return p;
        }
    }
}
