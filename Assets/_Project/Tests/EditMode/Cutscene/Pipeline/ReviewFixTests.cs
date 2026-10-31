using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Editor.Pipeline;
using Pickleball.Cutscene.Playback;
using Pickleball.Cutscene.Timeline;
using Pickleball.Cutscene.Timeline.Plan;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>
    /// Review round 1 (R-cutscene-review-1): Birdy's rotor and the loose propeller follow the
    /// story, the prop lands tilted where it can be seen, the hand-back scenes are the blocks'
    /// last scenes, gameplay behaviours come back exactly, and a mark's tilt reaches the pose.
    /// </summary>
    public class ReviewFixTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        /// <summary>The value a state channel holds at a scene time (the last key at or before it), or null.</summary>
        static float? StateAt(int scene, string channel, double time)
        {
            var layout = PipelineTestData.Layout(scene);
            float? v = null;
            double best = double.NegativeInfinity;
            foreach (var k in layout.Keys)
                if (k.Channel == channel && k.Time <= time + 1e-6 && k.Time >= best)
                {
                    best = k.Time;
                    v = k.Value;
                }
            return v;
        }

        static double ShotStart(int scene, string shotId) => PipelineTestData.Layout(scene).FindShot(shotId).Start;

        [Test]
        public void BirdyLosesHisRotorAtTheSeparationAndGetsItBackAtTheClick()
        {
            double separation = ShotStart(2, "S2_SH01") + 2.4;
            Assert.That(StateAt(2, StateChannels.BirdyRotor, separation - 0.1), Is.EqualTo(1f), "on his head until it pops off");
            Assert.That(StateAt(2, StateChannels.BirdyRotor, separation + 0.01), Is.EqualTo(0f));
            Assert.That(StateAt(2, StateChannels.PropVisible, separation + 0.01), Is.EqualTo(1f), "the loose prop flies");
            Assert.That(StateAt(2, StateChannels.BirdyRotor, PipelineTestData.Layout(2).Duration), Is.EqualTo(0f),
                "bare helmet through the whole of scene 2");

            double click = ShotStart(3, "S3_SH02") + 0.4;
            Assert.That(StateAt(3, StateChannels.BirdyRotor, 0.0), Is.EqualTo(0f), "bare helmet when MC hands it over");
            Assert.That(StateAt(3, StateChannels.BirdyRotor, click + 0.01), Is.EqualTo(1f));
            Assert.That(StateAt(3, StateChannels.PropVisible, click + 0.01), Is.EqualTo(0f), "seated: only the skinned rotor shows");
            foreach (int scene in new[] { 4, 5, 6 })
                Assert.That(StateAt(scene, StateChannels.BirdyRotor, 0.0), Is.EqualTo(1f), $"scene {scene} opens with him flying");
        }

        [Test]
        public void ThePropellerLandsTiltedTowardTheShrine()
        {
            var sh01 = OpeningTimingPlan.Scene(2).First(s => s.Id == "S2_SH01");
            var landing = sh01.Marks.Last(m => m.Actor == PlanActors.Propeller);
            Assert.That(landing.Target.Frame, Is.EqualTo(CutsceneMarkerIds.PropellerLedge));
            Assert.That(landing.Target.Face, Is.EqualTo(CutsceneMarkerIds.MCStart));
            Assert.That(landing.Target.Pitch, Is.GreaterThan(30f), "lying flat it hid behind the ledge's edge");
            Assert.That(landing.Target.OffsetH.y, Is.GreaterThan(0f));
        }

        [Test]
        public void MarkPitchTiltsThePoseAboutItsRight()
        {
            _root = new GameObject("Frame");
            var b = new ActorMarkBehaviour { FromFrame = _root.transform, ToFrame = _root.transform, FromPitch = 60f, ToPitch = 60f };
            Assert.That(b.TryEvaluate(1f, out var pose), Is.True);
            Vector3 up = pose.rotation * Vector3.up;
            Assert.That(Vector3.Angle(up, Vector3.up), Is.EqualTo(60f).Within(0.01f));
            Assert.That(up.z, Is.GreaterThan(0f), "+ tips the top toward the forward");
            Assert.That(new PlanMark("X", Vector3.zero, null, 0f, false).Tilted(25f).Pitch, Is.EqualTo(25f));
        }

        [Test]
        public void TheLastSceneOfEachBlockHandsBack()
        {
            var handsBack = Enumerable.Range(1, OpeningTimingPlan.SceneCount).Where(OpeningTimelineBuilder.HandsBack).ToArray();
            Assert.That(handsBack, Is.EqualTo(new[] { 2, 6 }));
        }

        [Test]
        public void GameplayBehavioursAreDisabledAndRestoredExactly()
        {
            _root = new GameObject("Player");
            var on = _root.AddComponent<BoxCollider>();
            var off = new GameObject("Off").AddComponent<Light>();
            off.transform.SetParent(_root.transform);
            off.enabled = false;
            var visuals = new GameplayVisuals();
            visuals.Configure(null, null);
            visuals.ConfigureBehaviours(new Behaviour[] { off });
            visuals.Hide();
            Assert.That(off.enabled, Is.False);
            visuals.Restore();
            Assert.That(off.enabled, Is.False, "a behaviour that was off stays off");
            Assert.That(on.enabled, Is.True);
        }

        [Test]
        public void PunchlinesGetATailBeforeTheirCut()
        {
            foreach (var (scene, shot) in new List<(int, string)> { (1, "S1_SH05"), (2, "S2_SH04"), (3, "S3_SH12") })
            {
                var plan = OpeningTimingPlan.Scene(scene).First(s => s.Id == shot);
                var last = plan.Beats[plan.Beats.Length - 1];
                Assert.That(last.Kind, Is.EqualTo(BeatKind.Hold), shot);
                Assert.That(last.Seconds, Is.EqualTo(OpeningTimingPlan.PunchlineTail).Within(1e-5f), shot);
            }
        }
    }
}
