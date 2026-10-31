using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Cutscene.Playback;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Tests.Cutscene.Playback
{
    /// <summary>
    /// A timeline is finished only when its time reaches the end or the director stops after it was
    /// seen playing. A director that hasn't started, or was paused, is still playing: on phones an
    /// app can start paused or unfocused, and in the Editor an unfocused Play mode skipped scene 1.
    /// </summary>
    public class TimelineCompletionTests
    {
        [Test]
        public void NotPlayingOnTheFirstTickIsStillPlaying()
        {
            var c = new TimelineCompletion();
            c.Begin();
            Assert.IsFalse(c.Tick(false, 0.0, 32.8));
            Assert.IsTrue(c.NeedsResume(false, false), "the player nudges it to play");
            Assert.IsFalse(c.Tick(false, 0.0, 32.8), "however many ticks it takes");
        }

        [Test]
        public void PausedMidTimelineIsStillPlaying()
        {
            var c = new TimelineCompletion();
            c.Begin();
            Assert.IsFalse(c.Tick(true, 10.0, 32.8));
            Assert.IsFalse(c.Tick(false, 10.0, 32.8), "app paused or focus lost");
            Assert.IsFalse(c.IsOver(10.0, 32.8));
            Assert.IsTrue(c.NeedsResume(false, false), "resumed where it was");
        }

        [Test]
        public void TimeAtTheDurationIsFinished()
        {
            var c = new TimelineCompletion();
            c.Begin();
            Assert.IsTrue(c.Tick(true, 32.8, 32.8));
            Assert.IsTrue(c.Tick(false, 32.8, 32.8), "even if the director already stopped");
        }

        [Test]
        public void StoppedAfterPlayingIsFinished()
        {
            var c = new TimelineCompletion();
            c.Begin();
            c.Tick(true, 30.0, 32.8);
            c.OnStopped();
            // With wrap mode None the director resets its time when it stops at the end.
            Assert.IsTrue(c.Tick(false, 0.0, 32.8));
            Assert.IsFalse(c.NeedsResume(false, true));
        }

        [Test]
        public void StoppedBeforePlayingDoesNotCount()
        {
            var c = new TimelineCompletion();
            c.Begin();
            c.OnStopped(); // e.g. the previous timeline's stop, or a stop before the graph started
            Assert.IsFalse(c.Tick(false, 0.0, 32.8));
        }

        [Test]
        public void BeginForgetsThePreviousTimeline()
        {
            var c = new TimelineCompletion();
            c.Begin();
            c.Tick(true, 30.0, 32.8);
            c.OnStopped();
            c.Begin();
            Assert.IsFalse(c.SeenPlaying);
            Assert.IsFalse(c.Tick(false, 0.0, 51.0));
        }

        [Test]
        public void TheSkipPathIsUnchanged()
        {
            var go = new GameObject("CutscenePlayerTest");
            var timelines = new List<PlayableAsset>
            {
                ScriptableObject.CreateInstance<TimelineAsset>(),
                ScriptableObject.CreateInstance<TimelineAsset>(),
            };
            try
            {
                go.AddComponent<PlayableDirector>();
                var player = go.AddComponent<CutscenePlayer>();
                var director = go.GetComponent<PlayableDirector>();
                var so = new UnityEditor.SerializedObject(player);
                so.FindProperty("director").objectReferenceValue = director;
                so.ApplyModifiedPropertiesWithoutUndo();
                string finishedId = null;
                bool? skipped = null;
                player.Finished += (id, s) => { finishedId = id; skipped = s; };

                Assert.IsTrue(player.Play("OC_Test", timelines));
                Assert.IsTrue(player.IsPlaying);
                player.Skip();
                Assert.IsFalse(player.IsPlaying);
                Assert.AreEqual("OC_Test", finishedId);
                Assert.AreEqual(true, skipped);
            }
            finally
            {
                Object.DestroyImmediate(go);
                foreach (var t in timelines) Object.DestroyImmediate(t);
            }
        }
    }
}
