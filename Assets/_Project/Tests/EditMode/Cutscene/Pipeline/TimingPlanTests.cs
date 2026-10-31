using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Cutscene.Script;
using Pickleball.Cutscene.Timeline.Plan;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>The seeded shot-list plan and its layout: totals, shot counts, and retiming.</summary>
    public class TimingPlanTests
    {
        // Shot list §5, plus the director's 0.3 s tail after the three punchlines a cut followed
        // within 0.5 s (S1_SH05, S2_SH04, S3_SH12; OpeningTimingPlan.PunchlineTail). The table sums
        // rows rounded to 0.01 s, so the exact layout differs by up to 0.01 s; the tolerance allows that.
        static readonly double[] SceneTotals =
        {
            32.54 + OpeningTimingPlan.PunchlineTail, 50.69 + OpeningTimingPlan.PunchlineTail,
            57.10 + OpeningTimingPlan.PunchlineTail, 24.10, 27.33, 79.10,
        };
        static readonly int[] ShotCounts = { 7, 10, 13, 3, 3, 16 };
        const double Tolerance = 0.0101;

        [Test]
        public void PlaceholderTotalsMatchTheShotList([Range(1, 6)] int scene)
        {
            var layout = PipelineTestData.Layout(scene);
            Assert.That(layout.Duration, Is.EqualTo(SceneTotals[scene - 1]).Within(Tolerance));
            Assert.That(layout.Shots.Count, Is.EqualTo(ShotCounts[scene - 1]));
            Assert.That(layout.Shots[layout.Shots.Count - 1].End, Is.EqualTo(layout.Duration));
        }

        [Test]
        public void ShotIdsRunInOrder([Range(1, 6)] int scene)
        {
            var shots = OpeningTimingPlan.Scene(scene);
            for (int i = 0; i < shots.Count; i++)
            {
                Assert.That(shots[i].Id, Is.EqualTo($"S{scene}_SH{i + 1:00}"));
                Assert.That(shots[i].Scene, Is.EqualTo(scene));
                Assert.That(shots[i].Framing, Is.Not.Empty);
            }
        }

        [Test]
        public void EveryLineIsPlayedExactlyOnceInItsScene()
        {
            var script = PipelineTestData.Script();
            var played = new List<string>();
            for (int s = 1; s <= 6; s++)
                foreach (var shot in OpeningTimingPlan.Scene(s))
                    foreach (var beat in shot.Beats)
                        if (beat.LineId != null)
                        {
                            played.Add(beat.LineId);
                            Assert.That(script.Find(beat.LineId).Scene, Is.EqualTo(s), beat.LineId);
                        }
            Assert.That(played, Is.EquivalentTo(script.Lines.Select(l => l.Id)));
        }

        [Test]
        public void SomeShotDurationsMatchTheirRows()
        {
            Assert.That(PipelineTestData.Layout(1).FindShot("S1_SH05").Duration, Is.EqualTo(6.90 + OpeningTimingPlan.PunchlineTail).Within(0.006));
            Assert.That(PipelineTestData.Layout(2).FindShot("S2_SH10").Duration, Is.EqualTo(8.13).Within(0.006));
            Assert.That(PipelineTestData.Layout(5).FindShot("S5_SH03").Duration, Is.EqualTo(15.00).Within(0.006));
            Assert.That(PipelineTestData.Layout(6).FindShot("S6_SH16").Duration, Is.EqualTo(5.0).Within(1e-6));
        }

        [Test]
        public void TheEllipsisPlaysInsideTheLongPause()
        {
            var layout = PipelineTestData.Layout(5);
            var shot = layout.FindShot("S5_SH03");
            var line = layout.FindLine("OC_S5_06");
            var before = layout.FindLine("OC_S5_05");
            Assert.That(line.Start, Is.EqualTo(before.End + 0.1).Within(1e-6));
            Assert.That(line.Cards[0].Text, Is.EqualTo("..."));
            Assert.That(layout.FindLine("OC_S5_07").Start, Is.EqualTo(before.End + 2.0).Within(1e-6));
            Assert.That(shot.Duration, Is.EqualTo(15.0).Within(0.006));
        }

        [Test]
        public void AudioShiftsEveryLaterBeatAndNothingEarlier()
        {
            const string id = "OC_S3_05";
            var before = PipelineTestData.Layout(3);
            var after = PipelineTestData.Layout(3, new Dictionary<string, float> { { id, 5.0f } });

            int chars = PipelineTestData.Script().Find(id).DisplayText.Length;
            double shift = 5.0 - LineTiming.PlaceholderSpeech(chars);
            double changedAt = before.FindLine(id).Start;

            Assert.That(after.FindLine(id).Start, Is.EqualTo(changedAt).Within(1e-6), "the line itself starts where it did");
            Assert.That(after.FindLine(id).SpeechEnd - after.FindLine(id).Start, Is.EqualTo(5.0).Within(1e-6));
            for (int i = 0; i < before.Shots.Count; i++)
            {
                for (int b = 0; b < before.Shots[i].BeatStarts.Length; b++)
                {
                    double t0 = before.Shots[i].BeatStarts[b], t1 = after.Shots[i].BeatStarts[b];
                    double expected = t0 > changedAt + 1e-6 ? shift : 0.0;
                    Assert.That(t1 - t0, Is.EqualTo(expected).Within(1e-5), $"{before.Shots[i].Id} beat {b}");
                }
            }
            Assert.That(after.Duration - before.Duration, Is.EqualTo(shift).Within(1e-5));
            for (int i = 0; i < before.Actions.Count; i++)
            {
                double expected = before.Actions[i].Start > changedAt + 1e-6 ? shift : 0.0;
                Assert.That(after.Actions[i].Start - before.Actions[i].Start, Is.EqualTo(expected).Within(1e-5));
            }
        }

        [Test]
        public void MixedNullAndRealClipsRetimeAndNeverThrow([Range(1, 6)] int scene)
        {
            // Every other line of the scene gets a clip; the rest (and OC_S5_06 always) stay null.
            var script = PipelineTestData.Script();
            var audio = new Dictionary<string, float>();
            int n = 0;
            foreach (var line in script.Lines.Where(l => l.Scene == scene))
                if (n++ % 2 == 0 && line.Id != "OC_S5_06") audio[line.Id] = 2.0f + 0.1f * n;

            SceneLayout layout = null;
            Assert.DoesNotThrow(() => layout = PipelineTestData.Layout(scene, audio));

            double expected = PipelineTestData.Layout(scene).Duration;
            foreach (var kv in audio)
            {
                var spec = PipelineTestData.Lines()[kv.Key];
                // With audio, OC_S2_09's 0.8 s mid-line hold is inside the clip.
                float midHold = kv.Key == "OC_S2_09" ? 0.8f : 0f;
                expected += kv.Value - LineTiming.PlaceholderSpeech(spec.DisplayChars) - midHold;
            }
            Assert.That(layout.Duration, Is.EqualTo(expected).Within(1e-4));
            foreach (var line in layout.Lines)
            {
                Assert.That(line.Cards.Length, Is.GreaterThan(0));
                Assert.That(line.End, Is.GreaterThan(line.Start));
                bool hasAudio = audio.ContainsKey(line.Id);
                Assert.That(line.Line.HasAudio, Is.EqualTo(hasAudio), line.Id);
            }
        }

        [Test]
        public void ANullClipEverywhereIsThePlaceholderCut()
        {
            var nulls = PipelineTestData.Script().Lines.ToDictionary(l => l.Id, l => -1f);
            for (int s = 1; s <= 6; s++)
                Assert.That(PipelineTestData.Layout(s, nulls).Duration, Is.EqualTo(PipelineTestData.Layout(s).Duration));
        }

        [Test]
        public void MidLineHoldIsInsideTheClipWithAudio()
        {
            var layout = PipelineTestData.Layout(2, new Dictionary<string, float> { { "OC_S2_09", 7.0f } });
            var line = layout.FindLine("OC_S2_09");
            Assert.That(line.SpeechEnd - line.Start, Is.EqualTo(7.0).Within(1e-6));
            Assert.That(line.Cards[1].Start - line.Cards[0].Start, Is.EqualTo((7.0 - 0.8) * 42 / 92 + 0.8).Within(1e-4));
        }

        [Test]
        public void VerbsAreLowerCaseAndEveryActorIsKnown()
        {
            for (int s = 1; s <= 6; s++)
                foreach (var shot in OpeningTimingPlan.Scene(s))
                {
                    foreach (var a in shot.Actions)
                    {
                        Assert.That(a.Verb, Is.EqualTo(a.Verb.ToLowerInvariant()), shot.Id);
                        Assert.That(a.Actor, Is.EqualTo(PlanActors.MC).Or.EqualTo(PlanActors.Birdy), shot.Id);
                        Assert.That(a.Beat, Is.InRange(0, shot.Beats.Length), shot.Id);
                    }
                    foreach (var m in shot.Marks)
                        Assert.That(m.Beat, Is.InRange(0, shot.Beats.Length), shot.Id);
                }
        }

        [Test]
        public void ActionsCoverEachActorToTheSceneEnd([Range(1, 6)] int scene)
        {
            var layout = PipelineTestData.Layout(scene);
            foreach (var group in layout.Actions.GroupBy(a => a.Actor))
            {
                var spans = group.OrderBy(a => a.Start).ToList();
                for (int i = 1; i < spans.Count; i++)
                    Assert.That(spans[i].Start, Is.EqualTo(spans[i - 1].End).Within(1e-6), $"{group.Key} contiguous");
                Assert.That(spans.Last().End, Is.EqualTo(layout.Duration).Within(1e-6));
            }
        }

        [Test]
        public void MarksOnlyDependOnActorsEvaluatedBeforeThem()
        {
            // Blocking evaluates MC, then Birdy, then the propeller.
            var order = new[] { PlanActors.MC, PlanActors.Birdy, PlanActors.Propeller };
            for (int s = 1; s <= 6; s++)
                foreach (var shot in OpeningTimingPlan.Scene(s))
                    foreach (var m in shot.Marks)
                    {
                        int self = System.Array.IndexOf(order, m.Actor);
                        foreach (string frame in new[] { m.Target.Frame, m.Target.Face })
                        {
                            if (!PlanActors.TryParseActorRef(frame, out string other, out _)) continue;
                            Assert.That(System.Array.IndexOf(order, other), Is.LessThan(self),
                                $"{shot.Id}: {m.Actor}'s mark depends on {frame}");
                        }
                    }
        }

        [Test]
        public void EveryActorStartsEachSceneOnAMark([Range(1, 6)] int scene)
        {
            var layout = PipelineTestData.Layout(scene);
            foreach (string actor in new[] { PlanActors.MC, PlanActors.Birdy, PlanActors.Propeller })
            {
                var first = layout.Marks.FirstOrDefault(m => m.Cue.Actor == actor);
                Assert.That(first.Cue.Actor, Is.EqualTo(actor), $"scene {scene} places {actor}");
                Assert.That(first.Start, Is.EqualTo(0.0), $"scene {scene} places {actor} at the start");
            }
        }
    }
}
