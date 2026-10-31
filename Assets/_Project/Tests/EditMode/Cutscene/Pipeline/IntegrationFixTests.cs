using System.Linq;
using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Editor.Pipeline;
using Pickleball.Cutscene.Playback;
using Pickleball.Cutscene.Timeline.Plan;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>
    /// The integration fixes: gameplay indicators hidden and restored exactly, the Player blocked
    /// at its controller's resting height, MC's 180° turn across the S1_SH03 → S1_SH04 smash cut
    /// and back in S1_SH07.
    /// </summary>
    public class IntegrationFixTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        // ── GameplayVisuals ─────────────────────────────────────────────────────

        [Test]
        public void HideForcesRenderersOffAndRestorePutsBackTheExactFlags()
        {
            _root = new GameObject("Player");
            var a = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>();
            var b = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>();
            a.transform.SetParent(_root.transform);
            b.transform.SetParent(_root.transform);
            b.forceRenderingOff = true;   // already hidden by its owner
            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(_root.transform);
            light.cullingMask = 0x5;

            var visuals = new GameplayVisuals();
            visuals.Configure(new[] { a, b, null }, new[] { light });
            visuals.Hide();
            Assert.That(visuals.IsHidden, Is.True);
            Assert.That(a.forceRenderingOff, Is.True);
            Assert.That(b.forceRenderingOff, Is.True);
            Assert.That(light.cullingMask, Is.EqualTo(0));

            visuals.Restore();
            Assert.That(visuals.IsHidden, Is.False);
            Assert.That(a.forceRenderingOff, Is.False);
            Assert.That(b.forceRenderingOff, Is.True, "an item hidden before the cutscene stays hidden");
            Assert.That(light.cullingMask, Is.EqualTo(0x5));
        }

        [Test]
        public void OwnersKeepTheirOwnEnabledChangesMadeDuringTheCutscene()
        {
            _root = new GameObject("Player");
            var arc = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>();
            arc.transform.SetParent(_root.transform);
            arc.enabled = true;   // a dash arc still showing when the pickup fires

            var visuals = new GameplayVisuals();
            visuals.Configure(new[] { arc }, null);
            visuals.Hide();
            arc.enabled = false;   // its owner times it out mid-cutscene
            visuals.Restore();
            Assert.That(arc.enabled, Is.False, "restore must not re-show what the owner switched off");
            Assert.That(arc.forceRenderingOff, Is.False);
        }

        [Test]
        public void HideAndRestoreAreIdempotent()
        {
            _root = new GameObject("Player");
            var r = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>();
            r.transform.SetParent(_root.transform);
            var visuals = new GameplayVisuals();
            visuals.Configure(new[] { r }, null);
            visuals.Restore();   // not hidden: no-op
            Assert.That(r.forceRenderingOff, Is.False);
            visuals.Hide();
            visuals.Hide();      // second hide must not record "hidden" as the original state
            visuals.Restore();
            Assert.That(r.forceRenderingOff, Is.False);
        }

        [Test]
        public void TheHideListSkipsTheCharacterModel()
        {
            _root = new GameObject("Player");
            var model = new GameObject("Model");
            model.transform.SetParent(_root.transform);
            model.AddComponent<Animator>();
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(model.transform);
            var sword = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sword.transform.SetParent(body.transform);
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.transform.SetParent(_root.transform);
            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(ring.transform);

            var renderers = OpeningSceneBuilder.GameplayRenderers(_root.transform, out var lights);
            Assert.That(renderers, Is.EqualTo(new[] { ring.GetComponent<Renderer>() }));
            Assert.That(lights, Is.EqualTo(new[] { glow }));
        }

        // ── Rest height ─────────────────────────────────────────────────────────

        [Test]
        public void ThePlayerIsBlockedAtItsControllersRestingHeight()
        {
            _root = new GameObject("Player");
            var cc = _root.AddComponent<CharacterController>();
            cc.height = 2.535906f;
            cc.radius = 0.756582f;
            cc.center = new Vector3(0f, 1.267953f, 0f);
            cc.skinWidth = 0.08f;
            Assert.That(OpeningTimelineBuilder.RestLift(cc), Is.EqualTo(0.08f).Within(1e-4f));

            cc.center = new Vector3(0f, 1.0f, 0f);   // capsule bottom 0.268 below the root
            Assert.That(OpeningTimelineBuilder.RestLift(cc), Is.EqualTo(0.08f + 0.267953f).Within(1e-4f));

            Object.DestroyImmediate(cc);
            Assert.That(OpeningTimelineBuilder.RestLift(_root.transform), Is.EqualTo(0f), "stand-in actors stay on the ground");
        }

        // ── Scene 1 facing ──────────────────────────────────────────────────────

        [Test]
        public void McTurnsHalfwayRoundAtTheSmashCutAndBackNorthInTheCrane()
        {
            var shots = OpeningTimingPlan.Scene(1);
            var sitUp = shots.First(s => s.Id == "S1_SH03");
            Assert.That(sitUp.Actions.Select(a => a.Verb), Is.EqualTo(new[] { "sit up" }),
                "a standing clip inside the sit-up pops MC to his feet");

            var stand = shots.First(s => s.Id == "S1_SH04");
            var turned = stand.Marks.Single(m => m.Actor == PlanActors.MC);
            Assert.That(turned.Target.Frame, Is.EqualTo(CutsceneMarkerIds.MCStart));
            Assert.That(turned.Target.Yaw, Is.EqualTo(180f));
            Assert.That(turned.Beat, Is.EqualTo(0));
            Assert.That(turned.At, Is.EqualTo(0f));
            Assert.That(turned.MoveSeconds, Is.EqualTo(0f), "a cut, not a move");

            var crane = shots.First(s => s.Id == "S1_SH07");
            var back = crane.Marks.Single(m => m.Actor == PlanActors.MC);
            Assert.That(back.Target.Frame, Is.EqualTo(CutsceneMarkerIds.MCStart));
            Assert.That(back.Target.Yaw, Is.EqualTo(0f));
            Assert.That(back.Target.Face, Is.Null);
            // Review round 3: a slow turn across the crane (1.0–5.8 s), so the 180° reads over the shot.
            Assert.That(back.Beat, Is.EqualTo(1), "turns from 1.0 s");
            Assert.That(back.MoveSeconds, Is.EqualTo(4.8f).Within(1e-4f), "1.0–5.8 s");
        }
    }
}
