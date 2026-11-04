using System.Linq;
using NUnit.Framework;
using Pickleball.Cutscene.Editor.Sets;
using Pickleball.Cutscene.Timeline;
using UnityEditor;
using UnityEngine.Timeline;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>
    /// Scene 4's walk has to last the whole scene: the walk clip keeps looping, so a walk shorter
    /// than the scene leaves MC walking on the spot. Its foreground trunks also have to stay out of
    /// the comic holds. New voice takes can break either, and these catch it on the built timeline.
    /// </summary>
    public class WalkLengthTests
    {
        const string TimelinePath = "Assets/_Project/Timeline/Cutscene/OC_S4.playable";

        [Test]
        public void McIsStillWalkingWhenSceneFourEnds()
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null) Assert.Ignore(TimelinePath + " is not built.");

            var marks = timeline.GetOutputTracks().OfType<ActorMarkTrack>().First(t => t.name.StartsWith("MC"));
            var last = marks.GetClips().OrderBy(c => c.start).Last();
            Assert.That(last.displayName, Does.Not.StartWith("hold"),
                $"MC stops at {last.start:0.00} s and walks on the spot until the scene ends at {timeline.duration:0.00} s.");
            Assert.AreEqual(timeline.duration, last.end, 1e-3);
        }

        [Test]
        public void NoTrunkCrossesFrameDuringTheHoldsOrTheLastLines()
        {
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null) Assert.Ignore(TimelinePath + " is not built.");

            var cards = timeline.GetOutputTracks().OfType<SubtitleTrack>().SelectMany(t => t.GetClips()).ToList();
            var shots = timeline.GetOutputTracks().OfType<ShotTrack>().SelectMany(t => t.GetClips()).ToList();
            double holdStart = cards.Where(c => c.displayName.StartsWith("OC_S4_03")).Max(c => c.end);
            double holdEnd = shots.Single(c => c.displayName == "S4_SH01").end;
            double lastLines = cards.Where(c => c.displayName.StartsWith("OC_S4_07")).Min(c => c.start);

            const double walkSpeedAlongTrack = 1.0, inFrameMetres = 1.15;
            foreach (float station in SetLayout.TrunkStations)
            {
                double from = (station - inFrameMetres) / walkSpeedAlongTrack;
                double to = (station + inFrameMetres) / walkSpeedAlongTrack;
                Assert.IsFalse(from < holdEnd && to > holdStart,
                    $"The trunk at {station} m is in frame {from:0.00}-{to:0.00} s, across the hold at {holdStart:0.00}-{holdEnd:0.00} s.");
                Assert.Less(to, lastLines,
                    $"The trunk at {station} m is in frame until {to:0.00} s, into the last lines from {lastLines:0.00} s.");
            }
        }
    }
}
