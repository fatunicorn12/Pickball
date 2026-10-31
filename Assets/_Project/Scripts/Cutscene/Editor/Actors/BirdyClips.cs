using System.Collections.Generic;
using UnityEngine;
using static Pickleball.Cutscene.Editor.Actors.PoseKit;
using M = Pickleball.Cutscene.Editor.Actors.HumanMuscles;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Birdy's hand-keyed cutscene clips (shot list §6, Birdy table), authored in muscle space
    /// and baked on Birdy's own avatar: his wings are the humanoid arms, so every gesture is an
    /// arm gesture. Hover clips are not grounded: the timeline sets his flying height (Head at
    /// about MC's eye line); the clip only adds the bob. Standing clips are grounded.
    /// </summary>
    public static class BirdyClips
    {
        private const string Idle = CutsceneActorPaths.BirdyClipDir + "/Idle.fbx";
        private const float IdleHold = 0.5f;

        private static PoseClipSpec Spec(string name, string verb, float duration, bool loop, bool hover, string note)
        {
            return new PoseClipSpec
            {
                Name = name,
                ModelPath = CutsceneActorPaths.BirdyModel,
                Folder = CutsceneActorPaths.BirdyCutsceneClipDir,
                Verbs = new[] { verb },
                BaseClip = Idle,
                BaseTime = IdleHold,
                Duration = duration,
                Loop = loop,
                Ground = hover ? 0f : 1f,
                Note = note,
            };
        }

        private static PoseKey Wings(this PoseKey k, float downUp, float frontBack, float forearm, float shoulderUp = 0f)
        {
            return k.S(M.Arm(true, downUp, frontBack, forearm, 0f, shoulderUp)).S(M.Arm(false, downUp, frontBack, forearm, 0f, shoulderUp));
        }

        private static PoseKey HoverLegs(this PoseKey k, float tuck = 0f)
        {
            return k.S(M.Leg(true, 0.3f - tuck * 0.6f, 0.08f, 0.5f - tuck * 0.5f, -0.4f))
                .S(M.Leg(false, 0.3f - tuck * 0.6f, 0.08f, 0.5f - tuck * 0.5f, -0.4f));
        }

        /// <summary>
        /// The flying pose: legs dangling, wings relaxed (Down-Up −0.45: on Birdy's avatar 0 is only ~20°
        /// below horizontal; lowered, a
        /// little back, elbows soft), a slight forward pitch. Review round 3: the old wings-out
        /// (Down-Up 0.3) read as a T-pose in every hover shot and drowned the gestures, which set the
        /// gesturing wing on top of this.
        /// </summary>
        private static PoseKey Hover(float t, float pitch = 8f)
        {
            return K(t).HoverLegs().Wings(-0.45f, 0.25f, 0.8f).Look(0f, 0f).T(pitch, 0f, 0f).G(0f);
        }

        private static float Sin(float t, float period, float phase = 0f) => Mathf.Sin((t / period + phase) * Mathf.PI * 2f);

        private static void Bob(PoseFrame f, float t, float period, float height)
        {
            f.Offset += new Vector3(0f, height * Sin(t, period), 0f);
        }

        private static void Paddle(PoseFrame f, float t, float period, float amount)
        {
            float s = Sin(t, period);
            f.Add("Left Arm Down-Up", amount * s);
            f.Add("Right Arm Down-Up", amount * s);
        }

        /// <summary>Every Birdy clip spec.</summary>
        public static List<PoseClipSpec> All()
        {
            var list = new List<PoseClipSpec>();

            // ---- Scene 2: the crash ---------------------------------------------------------
            var tumble = Spec("B_CrashTumble", "crash tumble", 0.9f, true, true,
                "Airborne somersault loop: one forward flip per loop, wings and legs flailing out of phase.");
            tumble.K(K(0f).HoverLegs(0.3f).Wings(0.5f, -0.2f, 0.7f).G(0f), K(0.9f).HoverLegs(0.3f).Wings(0.5f, -0.2f, 0.7f).G(0f));
            tumble.Procedural = (t, f) =>
            {
                f.Turn += new Vector3(360f * t / 0.9f, 0f, 15f * Sin(t, 0.45f));
                f.Add("Left Arm Down-Up", 0.5f * Sin(t, 0.3f));
                f.Add("Right Arm Down-Up", 0.5f * Sin(t, 0.3f, 0.5f));
                f.Add("Left Upper Leg Front-Back", 0.4f * Sin(t, 0.45f));
                f.Add("Right Upper Leg Front-Back", 0.4f * Sin(t, 0.45f, 0.5f));
            };
            list.Add(tumble);

            var panic = Spec("B_PanicCircling", "panic circling", 0.8f, true, false,
                "Hopping in place with both wing tips patting the top of his helmet, wobbling. The circle path is the timeline's.");
            panic.K(K(0f).Wings(0.9f, -0.45f, -0.55f, 0.6f).Look(0.1f, 0f), K(0.8f).Wings(0.9f, -0.45f, -0.55f, 0.6f).Look(0.1f, 0f));
            panic.Procedural = (t, f) =>
            {
                f.Offset += new Vector3(0f, 0.07f * Mathf.Abs(Sin(t, 0.8f)), 0f);
                f.Turn += new Vector3(0f, 0f, 10f * Sin(t, 0.8f));
                f.Add("Left Forearm Stretch", 0.25f * Sin(t, 0.2f));
                f.Add("Right Forearm Stretch", 0.25f * Sin(t, 0.2f, 0.5f));
                f.Add("Left Upper Leg Front-Back", -0.3f * Mathf.Max(0f, Sin(t, 0.8f)));
                f.Add("Right Upper Leg Front-Back", -0.3f * Mathf.Max(0f, Sin(t, 0.8f, 0.5f)));
            };
            list.Add(panic);

            var lean = Spec("B_CloseLeanIn", "close lean-in", 1.4f, false, false,
                "Lunges toward the lens (weight forward, wings swept back, head up into camera) and holds there.");
            PoseKey Lunge(float t) => K(t).Bend(0.35f).Wings(-0.1f, 0.6f, 0.9f).Look(0.35f, 0f).O(0f, 0f, 0.12f)
                .S(M.Leg(true, 0.35f, 0.05f, 0.8f)).S(M.Leg(false, 0.75f, 0.05f, 1f));
            lean.K(K(0f), Lunge(0.3f), Lunge(1.4f));
            list.Add(lean);

            var plead = Spec("B_Plead", "plead", 1f, true, false,
                "Wing tips clasped together in front of his chest, little hops, a side-to-side wobble and a hopeful head tilt.");
            plead.K(K(0f).Wings(0.1f, -0.85f, 0.3f).Look(0.15f, 0f, 0.3f), K(1f).Wings(0.1f, -0.85f, 0.3f).Look(0.15f, 0f, 0.3f));
            plead.Procedural = (t, f) =>
            {
                f.Offset += new Vector3(0f, 0.04f * Mathf.Abs(Sin(t, 1f)), 0f);
                f.Turn += new Vector3(0f, 0f, 7f * Sin(t, 1f));
            };
            list.Add(plead);

            var pointUp = Spec("B_PointUp", "point up", 1f, false, false,
                "Right wing shoots straight up (at the propeller on the ledge), left wing on his hip, looking up.");
            PoseKey Up(float t) => K(t).S(M.Arm(true, 1f, -0.2f, 1f, 0f, 1f)).S(M.Arm(false, -0.3f, 0.3f, 0.2f)).Look(0.4f, -0.1f);
            pointUp.K(K(0f), Up(0.3f), Up(1f));
            list.Add(pointUp);

            var eager = Spec("B_EagerBounce", "eager bounce", 0.6f, true, false,
                "Bouncing on his toes, wings fluttering at his sides.");
            eager.K(K(0f).Wings(-0.35f, 0.1f, 0.9f).Look(0.15f, 0f), K(0.6f).Wings(-0.35f, 0.1f, 0.9f).Look(0.15f, 0f));
            eager.Procedural = (t, f) =>
            {
                f.Offset += new Vector3(0f, 0.06f * Mathf.Abs(Sin(t, 1.2f)), 0f);
                f.Add("Left Arm Down-Up", 0.15f * Sin(t, 0.15f));
                f.Add("Right Arm Down-Up", 0.15f * Sin(t, 0.15f));
            };
            list.Add(eager);

            var spread = Spec("B_TinyWingSpread", "tiny wing spread", 1f, false, false,
                "Wings open just a little and stay open, chest puffed, chin up: 'ta-da', small.");
            PoseKey Spread(float t) => K(t).Wings(0.22f, 0.12f, 0.95f).Bend(-0.08f).Look(0.15f, 0f);
            spread.K(K(0f), Spread(0.35f), Spread(1f));
            list.Add(spread);

            var droop = Spec("B_SadDroop", "sad droop", 1.2f, false, false,
                "Head hangs, wings hang limp, shoulders and knees sag; holds. Standing (on the lawn).");
            PoseKey Droop(float t) => K(t).Wings(-1f, 0.1f, 0.9f).Bend(0.2f).Look(-0.55f, 0f).Crouch(0.15f)
                .S("Left Shoulder Down-Up", -0.6f).S("Right Shoulder Down-Up", -0.6f);
            droop.K(K(0f), Droop(0.6f), Droop(1.2f));
            list.Add(droop);

            // ---- Scene 3: the propeller ------------------------------------------------------
            var snatch = Spec("B_Snatch", "snatch", 0.8f, false, false,
                "Both wings dart forward to grab, then pull the propeller in to his chest.");
            snatch.K(K(0f), K(0.15f).Wings(0.3f, -0.85f, 0.95f).Bend(0.2f), K(0.4f).Wings(0.1f, -0.6f, -0.3f).Bend(0.05f).Look(-0.15f, 0f),
                K(0.8f).Wings(0.1f, -0.6f, -0.3f).Bend(0.05f).Look(-0.15f, 0f));
            list.Add(snatch);

            var click = Spec("B_PropellerClick", "propeller click", 1.2f, false, false,
                "Wing tips up to the top of his helmet, a push down (knees dip, the click), a bounce, wings out.");
            click.K(K(0f), K(0.3f).Wings(1f, -0.3f, -0.2f, 1f).Look(0.1f, 0f),
                K(0.5f).Wings(0.95f, -0.3f, -0.3f, 0.8f).Crouch(0.3f).Look(-0.2f, 0f),
                K(0.7f).Wings(0.6f, 0f, 0.6f).Look(0.2f, 0f).O(0f, 0.05f, 0f),
                K(1.2f).Wings(-0.3f, 0.15f, 0.9f).Look(0.15f, 0f));
            list.Add(click);

            var wobble = Spec("B_HoverWobble", "hover wobble", 1.2f, true, true,
                "Unsteady lift-off: hovering with his body rolling and pitching, wings see-sawing for balance.");
            wobble.K(Hover(0f), Hover(1.2f));
            wobble.Procedural = (t, f) =>
            {
                f.Turn += new Vector3(6f * Sin(t, 0.6f), 0f, 12f * Sin(t, 1.2f));
                f.Add("Left Arm Down-Up", 0.25f * Sin(t, 1.2f));
                f.Add("Right Arm Down-Up", -0.25f * Sin(t, 1.2f));
                Bob(f, t, 0.6f, 0.04f);
            };
            list.Add(wobble);

            var joy = Spec("B_Overjoyed", "overjoyed", 1.2f, true, true,
                "Hugs his helmet with both wings, then flings them out flapping; legs kick.");
            joy.K(Hover(0f).Wings(0.85f, -0.6f, -0.7f, 0.5f), Hover(0.5f).Wings(0.85f, -0.6f, -0.7f, 0.5f),
                Hover(0.7f).Wings(0.6f, 0f, 1f), Hover(1.1f).Wings(0.6f, 0f, 1f), Hover(1.2f).Wings(0.85f, -0.6f, -0.7f, 0.5f));
            joy.Procedural = (t, f) =>
            {
                float flap = t > 0.55f && t < 1.15f ? 0.35f * Sin(t, 0.2f) : 0f;
                f.Add("Left Arm Down-Up", flap);
                f.Add("Right Arm Down-Up", flap);
                f.Add("Left Upper Leg Front-Back", 0.3f * Sin(t, 0.3f));
                f.Add("Right Upper Leg Front-Back", 0.3f * Sin(t, 0.3f, 0.5f));
                Bob(f, t, 0.6f, 0.03f);
            };
            list.Add(joy);

            var loopLap = Spec("B_ProudLoop", "proud loop", 1f, true, true,
                "Banked into a turn (the 360° lap is the timeline's path): chest out, wings swept back, chin up.");
            // Swept back and down (review round 4: at Down-Up 0.3 the "swept back" wings were straight out).
            loopLap.K(Hover(0f).T(10f, 0f, -20f).Wings(-0.2f, 0.55f, 0.95f).Look(0.15f, -0.2f),
                Hover(1f).T(10f, 0f, -20f).Wings(-0.2f, 0.55f, 0.95f).Look(0.15f, -0.2f));
            loopLap.Procedural = (t, f) => Bob(f, t, 1f, 0.02f);
            list.Add(loopLap);

            var stare = Spec("B_Stare", "stare", 1f, false, true,
                "Freezes mid-hover: wings stiff and straight out, legs straight down, head locked forward.");
            PoseKey Freeze(float t) => K(t).Wings(0.5f, 0f, 1f).S(M.Leg(true, 0.6f, 0.05f, 1f, -0.5f)).S(M.Leg(false, 0.6f, 0.05f, 1f, -0.5f))
                .Look(0f, 0f).T(0f, 0f, 0f).G(0f);
            stare.K(Hover(0f), Freeze(0.15f), Freeze(1f));
            list.Add(stare);

            var awed = Spec("B_Awed", "awed", 2f, true, true,
                "Hovering in the foreground looking up and out: head up, wings eased back, a slow bob.");
            awed.K(Hover(0f).Look(0.35f, 0f).Wings(-0.25f, 0.35f, 0.95f), Hover(2f).Look(0.35f, 0f).Wings(-0.25f, 0.35f, 0.95f));
            awed.Procedural = (t, f) => Bob(f, t, 2f, 0.025f);
            list.Add(awed);

            var flap = Spec("B_ExcitedFlap", "excited flap", 0.6f, true, true,
                "Hovering with fast, big wing flaps and kicking feet.");
            flap.K(Hover(0f).Wings(0.35f, 0f, 0.9f).Look(0.15f, 0f), Hover(0.6f).Wings(0.35f, 0f, 0.9f).Look(0.15f, 0f));
            flap.Procedural = (t, f) =>
            {
                f.Add("Left Arm Down-Up", 0.45f * Sin(t, 0.2f));
                f.Add("Right Arm Down-Up", 0.45f * Sin(t, 0.2f));
                f.Add("Left Upper Leg Front-Back", 0.25f * Sin(t, 0.3f));
                f.Add("Right Upper Leg Front-Back", 0.25f * Sin(t, 0.3f, 0.5f));
                Bob(f, t, 0.3f, 0.02f);
            };
            list.Add(flap);

            var deflate = Spec("B_Deflate", "deflate", 1.5f, false, true,
                "Losing steam in the air: head and wings drop, he sinks a little and tips forward; holds.");
            deflate.Verbs = new[] { "deflate", "losing steam" };
            PoseKey Sag(float t) => Hover(t, 18f).Wings(-0.4f, 0.15f, 0.9f).Look(-0.45f, 0f).O(0f, -0.08f, 0f).HoverLegs(-0.2f);
            deflate.K(Hover(0f), Sag(0.9f), Sag(1.5f));
            list.Add(deflate);

            var grand = Spec("B_GrandGesture", "grand gesture", 1.5f, false, true,
                "Right wing sweeps from across his chest out and up to the side, presenting; chest out, looking where he presents.");
            PoseKey Present(float t) => Hover(t).S(M.Arm(true, 0.65f, 0.15f, 1f, 0f, 0.4f))
                .Look(0.15f, -0.35f).Bend(-0.08f);
            grand.K(Hover(0f).S(M.Arm(true, 0.2f, -0.8f, 0.4f)), Present(0.6f), Present(1.5f));
            grand.Procedural = (t, f) => Bob(f, t, 1.5f, 0.02f);
            list.Add(grand);

            var perky = Spec("B_PerkyRecovery", "perky recovery", 0.8f, false, true,
                "Snaps out of a droop: head up, wings flick out, a little hop up in the air.");
            // The flick is brief; the clip settles back to the relaxed hover wings, because a one-shot
            // clip holds its last frame for the rest of its span (review round 4: it held the wings
            // straight out through S6_SH06 and S6_SH15).
            perky.K(Hover(0f).Wings(-0.6f, 0.1f, 0.9f).Look(-0.35f, 0f),
                Hover(0.25f).Wings(0.4f, 0f, 1f).Look(0.25f, 0f).O(0f, 0.06f, 0f),
                Hover(0.8f).Look(0.15f, 0f).O(0f, 0.02f, 0f));
            list.Add(perky);

            // ---- Scenes 4–6 --------------------------------------------------------------------
            var putter = Spec("B_PutterHover", "putter hover", 1.6f, true, true,
                "Flying alongside a walker: pitched forward, gentle bob, wings paddling.");
            putter.K(Hover(0f, 12f), Hover(1.6f, 12f));
            putter.Procedural = (t, f) =>
            {
                Bob(f, t, 0.8f, 0.03f);
                Paddle(f, t, 0.4f, 0.1f);
            };
            list.Add(putter);

            var tilt = Spec("B_ConfusedTilt", "confused head tilt", 1f, false, true,
                "Hovering, head cocked hard to one side. Hover base + head offset.");
            tilt.K(Hover(0f), Hover(0.35f).Look(0.05f, 0.1f, 0.6f), Hover(1f).Look(0.05f, 0.1f, 0.6f));
            tilt.Procedural = (t, f) => Bob(f, t, 1f, 0.02f);
            list.Add(tilt);

            var chuckle = Spec("B_Chuckle", "chuckle", 1.2f, false, true,
                "A wing tip to his beak, head back a touch, shoulders bouncing with the laugh.");
            chuckle.K(Hover(0f), Hover(0.25f).S(M.Arm(true, 0.35f, -0.8f, -0.45f)).Look(0.15f, 0f, -0.15f),
                Hover(1.2f).S(M.Arm(true, 0.35f, -0.8f, -0.45f)).Look(0.15f, 0f, -0.15f));
            chuckle.Procedural = (t, f) =>
            {
                float b = t > 0.25f ? 0.25f * Mathf.Abs(Sin(t, 0.4f)) : 0f;
                f.Add("Left Shoulder Down-Up", b);
                f.Add("Right Shoulder Down-Up", b);
                f.Offset += new Vector3(0f, 0.015f * Mathf.Abs(Sin(t, 0.4f)), 0f);
            };
            list.Add(chuckle);

            var nod = Spec("B_EarnestNod", "earnest nod", 1.2f, false, true,
                "Two deliberate nods while hovering. Hover base + head offset.");
            nod.K(Hover(0f), Hover(0.25f).Look(-0.4f, 0f), Hover(0.5f).Look(0.05f, 0f), Hover(0.75f).Look(-0.4f, 0f),
                Hover(1f).Look(0.05f, 0f), Hover(1.2f).Look(0.05f, 0f));
            list.Add(nod);

            var beam = Spec("B_Beam", "beam", 1.5f, false, true,
                "Held smile: chest out, chin up and a little tilted, wings eased open.");
            PoseKey Beam(float t) => Hover(t).Wings(-0.15f, 0.35f, 0.9f).Look(0.2f, 0f, 0.2f).Bend(-0.08f);
            beam.K(Hover(0f), Beam(0.4f), Beam(1.5f));
            beam.Procedural = (t, f) => Bob(f, t, 1.5f, 0.02f);
            list.Add(beam);

            PoseKey Dart(float t) => K(t).Wings(0.15f, 0.8f, 1f).S(M.Leg(true, 1f, 0.05f, 1f, -0.6f)).S(M.Leg(false, 1f, 0.05f, 1f, -0.6f))
                .Look(0.45f, 0f).T(40f, 0f, 0f).G(0f);
            var zipIn = Spec("B_ZipIn", "zip in", 0.8f, false, true,
                "Arrives in a dart (pitched forward, wings swept back, legs trailing), brakes upright with wings forward, settles to hover.");
            zipIn.K(Dart(0f), Dart(0.3f), Hover(0.55f, -15f).Wings(0.5f, -0.35f, 0.9f).HoverLegs(0.4f), Hover(0.8f));
            list.Add(zipIn);

            var tour = Spec("B_TourGuide", "tour-guide gesture", 3f, false, true,
                "Present (right wing sweeps out, palm up), count (left wing forward, right tip taps it twice), point (right wing out ahead).");
            tour.K(Hover(0f),
                Hover(0.5f).S(M.Arm(true, 0.45f, 0.05f, 0.95f, 0f, 0.3f)).Look(0.1f, -0.3f),
                Hover(0.9f).S(M.Arm(true, 0.45f, 0.05f, 0.95f, 0f, 0.3f)).Look(0.1f, -0.3f),
                Hover(1.2f).S(M.Arm(false, 0.2f, -0.6f, 0.4f)).S(M.Arm(true, 0.25f, -0.7f, 0.2f)).Look(-0.15f, 0.1f),
                Hover(1.4f).S(M.Arm(false, 0.2f, -0.6f, 0.4f)).S(M.Arm(true, 0.2f, -0.75f, 0.35f)).Look(-0.15f, 0.1f),
                Hover(1.6f).S(M.Arm(false, 0.2f, -0.6f, 0.4f)).S(M.Arm(true, 0.25f, -0.7f, 0.2f)).Look(-0.15f, 0.1f),
                Hover(1.8f).S(M.Arm(false, 0.2f, -0.6f, 0.4f)).S(M.Arm(true, 0.2f, -0.75f, 0.35f)).Look(-0.15f, 0.1f),
                Hover(2.3f).S(M.Arm(true, 0.4f, -0.6f, 1f)).S(M.Point(true)).Look(0.1f, -0.15f),
                Hover(3f).S(M.Arm(true, 0.4f, -0.6f, 1f)).S(M.Point(true)).Look(0.1f, -0.15f));
            tour.Procedural = (t, f) => Bob(f, t, 1.5f, 0.02f);
            list.Add(tour);

            var slip = Spec("B_CheerSlip", "cheer slip", 1.5f, false, true,
                "From a beam, the cheer slips: head drops, wings sag, he sinks a few centimetres; holds. " +
                "Pair with EyeSwap 'Drop' and a CutsceneRotor spin dip.");
            PoseKey Slipped(float t) => Hover(t).Wings(-0.7f, 0.1f, 0.9f).Look(-0.3f, 0f).Bend(0.08f).O(0f, -0.04f, 0f);
            slip.K(Beam(0f), Beam(0.3f), Slipped(0.9f), Slipped(1.5f));
            list.Add(slip);

            var pointMc = Spec("B_PointAtMC", "point at mc", 1f, false, true,
                "Right wing jabs straight out ahead at whoever he faces (face him toward MC).");
            PoseKey AtMc(float t) => Hover(t).S(M.Arm(true, 0.3f, -0.75f, 1f, 0f, 0.2f)).Look(0f, 0f).Bend(0.05f);
            pointMc.K(Hover(0f), AtMc(0.25f), AtMc(1f));
            list.Add(pointMc);

            var warm = Spec("B_WarmLean", "warm lean", 1.5f, false, true,
                "Leans to his left with the left wing raised out and up (onto MC's shoulder when he hovers at MC's right).");
            PoseKey Warm(float t) => Hover(t).T(8f, 0f, 14f).S(M.Arm(false, 0.75f, -0.15f, 0.75f, 0f, 0.4f)).Look(0.05f, 0.25f, -0.2f);
            warm.K(Hover(0f), Warm(0.6f), Warm(1.5f));
            list.Add(warm);

            var zipOff = Spec("B_ZipOff", "zip-off", 1f, false, true,
                "A wind-up (sits back, wings forward), then shoots off: dart pose, wings swept back, legs trailing; holds the dart.");
            zipOff.K(Hover(0f), Hover(0.25f, -12f).Wings(0.45f, -0.4f, 0.9f).HoverLegs(0.5f), Dart(0.45f), Dart(1f));
            list.Add(zipOff);

            var hover = Spec("B_HoverIdle", "hover idle", 1.6f, true, true,
                "Hovering in place: bob and a slow wing paddle. The default whenever no other clip plays from S3_SH03 on.");
            hover.K(Hover(0f), Hover(1.6f));
            hover.Procedural = (t, f) =>
            {
                Bob(f, t, 1.6f, 0.03f);
                Paddle(f, t, 0.8f, 0.08f);
                // A gentle flap, two per bob.
                f.Add("Left Arm Down-Up", 0.1f * Sin(t, 0.8f));
                f.Add("Right Arm Down-Up", 0.1f * Sin(t, 0.8f));
            };
            list.Add(hover);

            return list;
        }
    }
}
