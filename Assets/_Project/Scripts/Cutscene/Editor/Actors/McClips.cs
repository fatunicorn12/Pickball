using System;
using System.Collections.Generic;
using UnityEngine;
using static Pickleball.Cutscene.Editor.Actors.PoseKit;
using M = Pickleball.Cutscene.Editor.Actors.HumanMuscles;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// MC's hand-keyed cutscene clips (shot list §6, MC table), authored in muscle space on the
    /// chibi and grounded on it. Blocking quality: clear silhouettes and readable beats, not
    /// polish. Variants that are a base pose plus a head offset say so in their note.
    /// </summary>
    public static class McClips
    {
        private const string Idle = CutsceneActorPaths.ChibiClipDir + "/Idle.fbx";
        private const string Walking = CutsceneActorPaths.ChibiClipDir + "/Walking.fbx";
        private const float IdleHold = 1f; // a calm frame of the Mixamo idle

        private static PoseClipSpec Spec(string name, string verb, float duration, bool loop, string note,
            string baseClip = Idle, float baseTime = IdleHold)
        {
            return new PoseClipSpec
            {
                Name = name,
                ModelPath = CutsceneActorPaths.ChibiModel,
                Folder = CutsceneActorPaths.McClipDir,
                Verbs = new[] { verb },
                BaseClip = baseClip,
                BaseTime = baseTime,
                Duration = duration,
                Loop = loop,
                Note = note,
            };
        }

        /// <summary>Spread-eagle on the back, eyes-closed pose (head toward the actor's +Z).</summary>
        private static PoseKey Lying(float t)
        {
            return K(t).Rot(OnBack)
                .S(M.Arm(true, 0.62f, 0f, 0.92f)).S(M.Arm(false, 0.62f, 0f, 0.92f))
                .S(M.OpenHand(true, 0.2f)).S(M.OpenHand(false, 0.2f))
                // Legs wide (In-Out 1.0, was 0.55) so they show either side of the cloak in the
                // top-down S1_SH01: at 0.55 the cloak covered them and he read as standing.
                .S(M.Leg(true, 0.55f, 1f, 0.95f, 0.3f)).S(M.Leg(false, 0.55f, 1f, 0.95f, 0.3f))
                .S(M.Torso(0f)).Look(0f, -0.12f, 0.05f);
        }

        private static void Breathe(float t, PoseFrame f, float period, float amount)
        {
            float s = Mathf.Sin(t * Mathf.PI * 2f / period);
            f.Add("Chest Front-Back", -amount * s);
            f.Add("Left Shoulder Down-Up", amount * 0.5f * s);
            f.Add("Right Shoulder Down-Up", amount * 0.5f * s);
        }

        /// <summary>Every MC clip spec.</summary>
        public static List<PoseClipSpec> All()
        {
            var list = new List<PoseClipSpec>();

            // ---- Scene 1: the void ----------------------------------------------------------
            var lie = Spec("MC_LieStill", "lie still", 4f, true,
                "On his back, spread-eagle, head toward the actor's +Z; a slow breath. Eyes closed via EyeSwap 'Closed'.");
            lie.K(Lying(0f), Lying(4f));
            lie.Procedural = (t, f) => Breathe(t, f, 4f, 0.06f);
            list.Add(lie);

            var eyes = Spec("MC_EyesOpen", "eyes open", 2f, false,
                "Still lying; a small head roll to centre and a finger twitch as the eyes open (EyeSwap 'Closed' → original).");
            eyes.K(Lying(0f),
                Lying(0.6f),
                Lying(1.4f).Look(0.08f, 0.05f, 0f).S(M.Fist(true, 0.4f)),
                Lying(2f).Look(0.08f, 0.05f, 0f).S(M.Fist(true, 0.25f)));
            eyes.Procedural = (t, f) => Breathe(t, f, 4f, 0.05f);
            list.Add(eyes);

            var sit = Spec("MC_SitUp", "sit up", 3.2f, false,
                "Slow sit-up from lying: props on the elbows, then sits upright, legs out, hands on the ground. " +
                "Ends facing the actor's −Z (toward his feet); yaw the actor 180° at the cut to the stand.");
            Quaternion half = Quaternion.Slerp(OnBack, FacingBack, 0.45f);
            sit.K(Lying(0f),
                Lying(0.4f),
                K(1.4f).Rot(half).Bend(0.25f).S(M.Arm(true, -0.25f, 0.55f, 0.55f)).S(M.Arm(false, -0.25f, 0.55f, 0.55f))
                    .S(M.Leg(true, -0.2f, 0.3f, 0.9f)).S(M.Leg(false, -0.2f, 0.3f, 0.9f)).Look(0.1f, 0f),
                K(2.5f).Rot(FacingBack).Bend(0.15f).S(M.Arm(true, -0.45f, 0.35f, 1f)).S(M.Arm(false, -0.45f, 0.35f, 1f))
                    .S(M.Leg(true, -0.55f, 0.2f, 0.75f)).S(M.Leg(false, -0.55f, 0.2f, 0.75f)).Look(0f, 0f),
                K(3.2f).Rot(FacingBack).Bend(0.15f).S(M.Arm(true, -0.45f, 0.35f, 1f)).S(M.Arm(false, -0.45f, 0.35f, 1f))
                    .S(M.Leg(true, -0.55f, 0.2f, 0.75f)).S(M.Leg(false, -0.55f, 0.2f, 0.75f)).Look(0.05f, 0.3f));
            list.Add(sit);

            var pump = Spec("MC_FistPump", "fist pump", 1.8f, false,
                "Opens near the top of a spring-up, right fist punched overhead; lands, yanks the fist down to the chest, holds.");
            pump.Ground = 1f;
            pump.K(K(0f).Stand(0.05f).S(M.Arm(true, 1f, -0.1f, 0.95f, 0f, 1f)).S(M.Arm(false, -0.15f, 0.2f, 0.8f))
                    .Fists().Look(0.3f, 0f).O(0f, 0.22f, 0f),
                K(0.15f).Stand(0.05f).S(M.Arm(true, 1f, -0.15f, 1f, 0f, 1f)).S(M.Arm(false, -0.1f, 0.25f, 0.8f))
                    .Fists().Look(0.35f, 0f).O(0f, 0.28f, 0f),
                K(0.45f).Crouch(0.45f).S(M.Arm(true, 0.75f, -0.2f, 0.4f, 0f, 0.6f)).S(M.Arm(false, -0.3f, 0.3f, 0.8f))
                    .Fists().Look(0.1f, 0f).O(0f, 0f, 0f),
                K(0.7f).Crouch(0.3f).Bend(0.12f).S(M.Arm(true, 0.15f, -0.45f, -0.35f, 0f, 0f)).S(M.Arm(false, -0.35f, 0.25f, 0.8f))
                    .Fists().Look(0.05f, 0f),
                K(1.1f).Crouch(0.1f).Bend(0.05f).S(M.Arm(true, 0.15f, -0.4f, -0.35f)).S(M.Arm(false, -0.4f, 0.15f, 0.85f))
                    .Fists().Look(0.15f, 0f),
                K(1.8f).Crouch(0.05f).Bend(0.05f).S(M.Arm(true, 0.15f, -0.4f, -0.35f)).S(M.Arm(false, -0.4f, 0.15f, 0.85f))
                    .Fists().Look(0.15f, 0f));
            list.Add(pump);

            // The LEFT (free) hand reaches (review round 2): the sword in his right hand pointed up
            // into the over-the-shoulder frame.
            // Shoulder height, 45° out to his left and forward (Down-Up 0.42, Front-Back -0.35): the
            // old -0.68 swung the short chibi arm across in front of his face, where the over-the-
            // shoulder camera only saw it beside his head (review round 3).
            PoseKey Reach(float t, float poke, float tilt = 0f) => K(t).Bend(0.08f)
                .S(M.Arm(false, 0.26f, -0.35f + 0.12f * poke, 1f - 0.55f * poke)).S(M.Point(false))
                .S(M.Arm(true, -0.45f, 0.15f, 0.85f)).Look(0.05f, 0.1f * tilt, 0.4f * tilt);
            var reach = Spec("MC_ReachOut", "reach out", 7f, false,
                "Left (free) arm rises to shoulder height and out on \"Status screen\" (1.5–2.2 s); pokes the air " +
                "on \"Status!\" (2.8 s) and \"Open status.\" (3.5 s); holds frozen through the 1.0 s hold; one small " +
                "poke and a head tilt on \"...Menu?\" (5.4–5.8 s), then holds.");
            reach.K(K(0f).ArmsDown().Look(0f, 0f), K(1.5f).ArmsDown().Look(0f, 0f), Reach(2.2f, 0f),
                Reach(2.65f, 0f), Reach(2.78f, 1f), Reach(2.95f, 0f),
                Reach(3.35f, 0f), Reach(3.48f, 1f), Reach(3.65f, 0f),
                Reach(5.3f, 0f), Reach(5.45f, 0.5f), Reach(5.6f, 0f, 0.6f), Reach(5.85f, 0f, 1f), Reach(7f, 0f, 1f));
            list.Add(reach);

            var lower = Spec("MC_LowerArm", "lower arm", 1f, false, "From the reach-out hold back down to the sides.");
            lower.K(Reach(0f, 0f, 1f), K(0.8f).ArmsDown().S(M.Fist(true, 0.2f)).Look(0f, 0f), K(1f).ArmsDown().S(M.Fist(true, 0.2f)).Look(0f, 0f));
            list.Add(lower);

            var glance = Spec("MC_GlanceToLens", "glance to lens", 1.5f, false,
                "Head turns 25° to his right and a touch down, then holds flat (aim the actor so his right is the lens).");
            glance.K(K(0f).Look(0f, 0f), K(0.35f).Look(-0.06f, -0.38f), K(1.5f).Look(-0.06f, -0.38f));
            list.Add(glance);

            var turn = Spec("MC_Turn", "turn", 1f, true,
                "In-place stepping loop for a turn; the yaw itself comes from the timeline rotating the actor.");
            turn.K(K(0f).Stand(), K(0.25f).S(M.Leg(true, 0.3f, 0.1f, 0.55f)).S(M.Leg(false, 0.6f, 0.05f, 1f)).Bend(0f, -0.15f),
                K(0.5f).Stand(), K(0.75f).S(M.Leg(false, 0.3f, 0.1f, 0.55f)).S(M.Leg(true, 0.6f, 0.05f, 1f)).Bend(0f, -0.15f),
                K(1f).Stand());
            list.Add(turn);

            var lookUp = Spec("MC_LookUp", "look up", 1.2f, false, "Idle pose with the head tipped well up and the chest lifted; holds.");
            lookUp.K(K(0f).Look(0f, 0f), K(0.6f).Look(0.85f, 0f).Bend(-0.08f), K(1.2f).Look(0.85f, 0f).Bend(-0.08f));
            list.Add(lookUp);

            var flinch = Spec("MC_Flinch", "flinch", 1.4f, false,
                "Snaps both forearms up in front of his face, leans back with bent knees, peeks, recovers.");
            PoseKey Guard(float t) => K(t).Crouch(0.3f).Bend(-0.12f).S(M.Arm(true, 0.35f, -0.75f, -0.35f))
                .S(M.Arm(false, 0.35f, -0.75f, -0.35f)).Fists(0.6f).Look(-0.25f, 0.2f);
            flinch.K(K(0f).ArmsDown().Stand().Look(0f, 0f), Guard(0.15f), Guard(0.75f),
                K(1.4f).ArmsDown().Stand().Look(0.1f, 0f));
            list.Add(flinch);

            var lookOff = Spec("MC_LookOff", "look off", 1.2f, false, "Turns head and shoulders to his left, eyes on the horizon; holds.");
            lookOff.K(K(0f).Look(0f, 0f), K(0.6f).Look(0.05f, 0.75f).Bend(0f, -0.15f), K(1.2f).Look(0.05f, 0.75f).Bend(0f, -0.15f));
            list.Add(lookOff);

            var shrug = Spec("MC_SighShrug", "sigh shrug", 2f, false,
                "Breath in with shoulders up and palms turned out, then a long drop: shoulders and head sag.");
            PoseKey ShrugArms(PoseKey k, float up) => k.S(M.Arm(true, -0.3f, -0.1f, 0.35f, 0.6f, up)).S(M.Arm(false, -0.3f, -0.1f, 0.35f, 0.6f, up))
                .S(M.OpenHand(true)).S(M.OpenHand(false));
            shrug.K(K(0f).ArmsDown().Look(0f, 0f),
                ShrugArms(K(0.5f), 0.9f).Look(0.12f, 0f, 0.25f),
                ShrugArms(K(0.8f), 0.9f).Look(0.12f, 0f, 0.25f),
                K(1.4f).ArmsDown().Look(-0.2f, 0f, 0.1f).Bend(0.06f),
                K(2f).ArmsDown().Look(-0.2f, 0f, 0.1f).Bend(0.06f));
            list.Add(shrug);

            var stare = Spec("MC_FlatStare", "flat stare", Idle8(), true,
                "The breathing idle with the head held dead level and the chin a touch down: deadpan. Base idle + head offset.",
                Idle, -1f);
            stare.K(K(0f).Look(-0.06f, 0f, 0f));
            list.Add(stare);

            var hold = Spec("MC_HoldOut", "hold out", 1.2f, false,
                "Right forearm forward, palm up, offering what's in Hand_R; holds.");
            PoseKey Offer(float t) => K(t).S(M.Arm(true, 0.0f, -0.55f, 0.45f, -0.6f)).S(M.OpenHand(true, 0.1f))
                .S(M.Arm(false, -0.45f, 0.1f, 0.85f)).Look(-0.2f, 0f);
            hold.K(K(0f).ArmsDown(), Offer(0.5f), Offer(1.2f));
            list.Add(hold);

            var away = Spec("MC_TurnAway", "turn away", 1.2f, false,
                "Turns his back on the camera (a 180° body turn baked into the pose, the actor doesn't rotate) and holds, arms folded-ish.");
            away.K(K(0f).ArmsDown().T(0f, 0f, 0f),
                K(0.35f).S(M.Leg(true, 0.35f, 0.1f, 0.6f)).T(0f, 70f, 0f),
                K(0.7f).Stand().T(0f, 160f, 0f).S(M.Arm(true, -0.2f, -0.4f, -0.2f)).S(M.Arm(false, -0.2f, -0.4f, -0.2f)),
                K(1.2f).Stand().T(0f, 180f, 0f).S(M.Arm(true, -0.2f, -0.4f, -0.2f)).S(M.Arm(false, -0.2f, -0.4f, -0.2f)).Look(-0.1f, 0f));
            list.Add(away);

            var tilt = Spec("MC_ConfusedTilt", "confused head tilt", 1f, false, "Head cocks to his right with a small lean; holds. Base idle + head offset.");
            tilt.K(K(0f).Look(0f, 0f), K(0.35f).Look(0.05f, 0.1f, 0.55f), K(1f).Look(0.05f, 0.1f, 0.55f));
            list.Add(tilt);

            var mutter = Spec("MC_ResignedMutter", "resigned mutter", 2f, false,
                "Eye-roll (head swings up and round), then looks aside and down, shoulders drop.");
            mutter.K(K(0f).Look(0f, 0f), K(0.45f).Look(0.45f, 0.35f, -0.2f), K(1.1f).Look(-0.25f, -0.5f).Bend(0.05f),
                K(2f).Look(-0.25f, -0.5f).Bend(0.05f));
            list.Add(mutter);

            var relaxed = Spec("MC_WalkRelaxed", "walk relaxed", WalkLength(), true,
                "The Mixamo walk (in place) with both hands laced behind the head, elbows out.", Walking, -1f);
            relaxed.K(K(0f).S(M.Arm(true, 0.85f, 0.35f, -0.9f, 0f, 0.35f)).S(M.Arm(false, 0.85f, 0.35f, -0.9f, 0f, 0.35f))
                .S(M.OpenHand(true, 0f)).S(M.OpenHand(false, 0f)));
            relaxed.Ground = 0f;
            relaxed.SymmetricLegs = true;
            list.Add(relaxed);

            var stop = Spec("MC_Stop", "stop", 0.9f, false, "A walk settling to a stand: weight rocks forward then back to idle.");
            stop.K(K(0f).S(M.Leg(true, 0.35f, 0.05f, 0.95f)).S(M.Leg(false, 0.8f, 0.05f, 1f)).Bend(0.1f),
                K(0.4f).Stand().Bend(0.04f), K(0.9f).Stand());
            list.Add(stop);

            var awe = Spec("MC_AwedLook", "awed look", 2f, false,
                "Base idle + head offset: chin up, chest open, hands drift slightly out from the body.");
            awe.K(K(0f).Look(0f, 0f), K(1f).Look(0.38f, 0f).Bend(-0.06f).ArmsDown(0.12f).S(M.OpenHand(true, 0.3f)).S(M.OpenHand(false, 0.3f)),
                K(2f).Look(0.4f, 0f).Bend(-0.06f).ArmsDown(0.12f).S(M.OpenHand(true, 0.3f)).S(M.OpenHand(false, 0.3f)));
            list.Add(awe);

            var frown = Spec("MC_SeriousFrown", "serious frown", 1.2f, false,
                "Base idle + head offset: chin down, shoulders squared, fists half-closed.");
            frown.K(K(0f).Look(0f, 0f), K(0.5f).Look(-0.18f, 0f).Fists(0.6f).S("Left Shoulder Front-Back", 0.3f).S("Right Shoulder Front-Back", 0.3f),
                K(1.2f).Look(-0.18f, 0f).Fists(0.6f).S("Left Shoulder Front-Back", 0.3f).S("Right Shoulder Front-Back", 0.3f));
            list.Add(frown);

            var hilt = Spec("MC_TouchHilt", "touch hilt", 1.6f, false,
                "Right hand (holding the blade) comes up in front of his waist and the left hand closes over it; he looks down at it.");
            PoseKey Hilt(float t) => K(t).S(M.Arm(true, -0.2f, -0.35f, 0.35f)).S(M.Arm(false, -0.15f, -0.55f, 0.2f, 0.3f))
                .S(M.Fist(true, 0.8f)).S(M.Fist(false, 0.6f)).Look(-0.35f, 0.1f).Bend(0.05f);
            hilt.K(K(0f).ArmsDown().Look(0f, 0f), Hilt(0.6f), Hilt(1.6f));
            list.Add(hilt);

            var idleLook = Spec("MC_IdleLook", "idle look", Idle8(), true,
                "The breathing idle with the gaze raised to the horizon. Base idle + head offset.", Idle, -1f);
            idleLook.K(K(0f).Look(0.15f, 0f));
            list.Add(idleLook);

            return list;
        }

        private static float Idle8() => ClipLength(Idle, 8.333f);

        private static float WalkLength() => ClipLength(Walking, 1.1f);

        private static float ClipLength(string path, float fallback)
        {
            AnimationClip c = PoseClipBaker.LoadClip(path);
            return c != null ? (float)Math.Round(c.length, 4) : fallback;
        }
    }
}
