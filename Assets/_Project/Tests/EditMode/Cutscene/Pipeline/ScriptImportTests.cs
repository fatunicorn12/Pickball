using System.Linq;
using NUnit.Framework;
using Pickleball.Cutscene.Script;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>Re-import keeps AudioClips by id and reports what changed. Uses in-memory assets only.</summary>
    public class ScriptImportTests
    {
        CutsceneScript _script;
        AudioClip _clip;

        [SetUp]
        public void Make()
        {
            _script = ScriptableObject.CreateInstance<CutsceneScript>();
            _script.hideFlags = HideFlags.DontSave;
            _clip = AudioClip.Create("OC_S3_07", 4410, 1, 44100, false);
            _clip.hideFlags = HideFlags.DontSave;
        }

        [TearDown]
        public void Clean()
        {
            Object.DestroyImmediate(_script);
            Object.DestroyImmediate(_clip);
        }

        [Test]
        public void FirstImportAddsEveryId()
        {
            var report = _script.Import(PipelineTestData.Script(), "x.md");
            Assert.That(report.Added.Count, Is.EqualTo(65));
            Assert.That(report.Removed.Count, Is.EqualTo(0));
            Assert.That(_script.LineCount, Is.EqualTo(65));
            Assert.That(_script.SceneCount, Is.EqualTo(6));
        }

        [Test]
        public void ReimportKeepsAudioById()
        {
            _script.Import(PipelineTestData.Script(), "x.md");
            Assert.That(_script.TryGetLine("OC_S3_07", out var line), Is.True);
            line.Audio = _clip;

            var report = _script.Import(PipelineTestData.Script(), "x.md");
            Assert.That(report.Added, Is.Empty);
            Assert.That(report.Removed, Is.Empty);
            Assert.That(report.AudioKept, Is.EqualTo(1));
            _script.TryGetLine("OC_S3_07", out line);
            Assert.That(line.Audio, Is.SameAs(_clip));
            Assert.That(line.AudioSeconds, Is.EqualTo(0.1f).Within(1e-4f));
        }

        [Test]
        public void ReimportReportsAddedAndRemovedIds()
        {
            const string v1 = "## Scene 1: A\n\n```text\nMC: One.\nMC: Two.\nMC: Three.\n```\n";
            const string v2 = "## Scene 1: A\n\n```text\nMC: One.\nMC: Two.\n```\n\n## Scene 2: B\n\n```text\nBIRDY: Hi.\n```\n";
            _script.Import(CutsceneScriptParser.Parse(v1), "x.md");
            _script.TryGetLine("OC_S1_01", out var first);
            first.Audio = _clip;
            var report = _script.Import(CutsceneScriptParser.Parse(v2), "x.md");
            Assert.That(report.Added, Is.EqualTo(new[] { "OC_S2_01" }));
            Assert.That(report.Removed, Is.EqualTo(new[] { "OC_S1_03" }));
            _script.TryGetLine("OC_S1_01", out first);
            Assert.That(first.Audio, Is.SameAs(_clip));
        }

        [Test]
        public void RealAndNullClipsFromTheAssetRetime()
        {
            _script.Import(PipelineTestData.Script(), "x.md");
            _script.TryGetLine("OC_S3_07", out var line);
            line.Audio = _clip;   // 0.1 s; every other line stays null

            var specs = new System.Collections.Generic.Dictionary<string, Pickleball.Cutscene.Timeline.Plan.LineSpec>();
            for (int i = 0; i < _script.LineCount; i++)
                specs[_script.GetLine(i).Id] = Pickleball.Cutscene.Timeline.Plan.LineSpec.FromScript(_script.GetLine(i));
            var layout = Pickleball.Cutscene.Timeline.Plan.TimelineLayout.Layout(3,
                Pickleball.Cutscene.Timeline.Plan.OpeningTimingPlan.Scene(3), specs);

            double placeholder = PipelineTestData.Layout(3).Duration;
            // The 0.1 s clip's slot is the minimum card time (the clip still plays its 0.1 s).
            double voiced = LineTiming.VoicedSlotSpeech(0.1f, LineTiming.DefaultMinCardSeconds);
            double expected = placeholder + voiced - LineTiming.PlaceholderSpeech(line.DisplayText.Length);
            Assert.That(layout.Duration, Is.EqualTo(expected).Within(1e-4));
            Assert.That(layout.FindLine("OC_S3_07").Line.HasAudio, Is.True);
            Assert.That(layout.FindLine("OC_S3_08").Line.HasAudio, Is.False);
        }

        [Test]
        public void NullAudioIsNormal()
        {
            _script.Import(PipelineTestData.Script(), "x.md");
            _script.TryGetLine("OC_S5_06", out var line);
            Assert.That(line.Audio, Is.Null);
            Assert.That(line.AudioSeconds, Is.LessThan(0f));
            Assert.That(line.Cards, Is.EqualTo(new[] { "..." }));
            Assert.That(Enumerable.Range(0, _script.LineCount).All(i => _script.GetLine(i).Audio == null));
        }
    }
}
