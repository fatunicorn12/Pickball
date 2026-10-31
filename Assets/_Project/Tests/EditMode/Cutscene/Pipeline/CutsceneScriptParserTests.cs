using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Script;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>The markdown parse: ids, speakers, tags, display text, stage directions.</summary>
    public class CutsceneScriptParserTests
    {
        ParsedScript _script;

        [OneTimeSetUp]
        public void Parse() => _script = PipelineTestData.Script();

        [Test]
        public void SixtyFiveLines()
        {
            Assert.That(_script.Lines.Count, Is.EqualTo(65));
            Assert.That(_script.Lines.Select(l => l.Id).Distinct().Count(), Is.EqualTo(65), "ids are unique");
        }

        [Test]
        public void LinesPerScene()
        {
            int[] expected = { 7, 10, 14, 8, 7, 19 };
            for (int s = 1; s <= 6; s++)
                Assert.That(_script.Lines.Count(l => l.Scene == s), Is.EqualTo(expected[s - 1]), $"scene {s}");
            Assert.That(_script.Scenes.Select(s => s.Number), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void IdsAreTwoDigitScriptOrder()
        {
            foreach (var group in _script.Lines.GroupBy(l => l.Scene))
            {
                int n = 0;
                foreach (var line in group)
                {
                    n++;
                    Assert.That(line.Id, Is.EqualTo($"OC_S{group.Key}_{n:00}"));
                    Assert.That(line.Index, Is.EqualTo(n));
                }
            }
        }

        [Test]
        public void Speakers()
        {
            Assert.That(_script.Lines.All(l => l.Speaker == CutsceneSpeakers.MC || l.Speaker == CutsceneSpeakers.Birdy));
            Assert.That(_script.Find("OC_S1_01").Speaker, Is.EqualTo(CutsceneSpeakers.MC));
            Assert.That(_script.Find("OC_S2_01").Speaker, Is.EqualTo(CutsceneSpeakers.Birdy));
            Assert.That(_script.Lines.Count(l => l.Speaker == CutsceneSpeakers.MC), Is.EqualTo(28));
            Assert.That(_script.Lines.Count(l => l.Speaker == CutsceneSpeakers.Birdy), Is.EqualTo(37));
        }

        [Test]
        public void LeadingTagsAreStrippedAndLowerCased()
        {
            var line = _script.Find("OC_S1_03");
            Assert.That(line.RawText, Is.EqualTo("[slowly, then building] Wait wait wait. White void. No body pain. Weird calm feeling."));
            Assert.That(line.DisplayText, Is.EqualTo("Wait wait wait. White void. No body pain. Weird calm feeling."));
            Assert.That(line.Tags.Count, Is.EqualTo(1));
            Assert.That(line.Tags[0].Text, Is.EqualTo("slowly, then building"));
            Assert.That(line.Tags[0].Position, Is.EqualTo(0));
            Assert.That(line.Tags[0].IsMidLine, Is.False);
        }

        [Test]
        public void MidLinePauseKeepsItsPosition()
        {
            var line = _script.Find("OC_S2_09");
            Assert.That(line.DisplayText,
                Is.EqualTo("It's round! It spins! It's about this big! I can't show you how big. My wings are very small."));
            Assert.That(line.Tags.Count, Is.EqualTo(2));
            Assert.That(line.Tags[0].Text, Is.EqualTo("eager"));
            Assert.That(line.Tags[1].Text, Is.EqualTo("pause"));
            Assert.That(line.Tags[1].IsMidLine, Is.True);
            Assert.That(line.DisplayText.Substring(line.Tags[1].Position), Does.StartWith("I can't show you"));
            Assert.That(line.MidHoldAfterCard, Is.EqualTo(0));
        }

        [Test]
        public void EllipsisIsKept()
        {
            Assert.That(_script.Find("OC_S5_06").DisplayText, Is.EqualTo("..."));
            Assert.That(_script.Find("OC_S5_06").Tags[0].Text, Is.EqualTo("long pause"));
            Assert.That(_script.Find("OC_S1_01").DisplayText, Is.EqualTo("...Okay. White void. I'm dead. Probably dead."));
            Assert.That(_script.Find("OC_S2_08").DisplayText, Is.EqualTo("That's... not helpful."));
        }

        [Test]
        public void UntaggedLinesPassThrough()
        {
            var line = _script.Find("OC_S3_09");
            Assert.That(line.Tags.Count, Is.EqualTo(0));
            Assert.That(line.DisplayText, Is.EqualTo("I have no idea what you're talking about."));
        }

        [Test]
        public void DisplayLengthsMatchTheShotList()
        {
            // A sample of the §3 Chars column.
            var chars = new Dictionary<string, int>
            {
                { "OC_S1_01", 45 }, { "OC_S1_02", 5 }, { "OC_S2_05", 97 }, { "OC_S2_09", 93 }, { "OC_S3_08", 80 },
                { "OC_S5_06", 3 }, { "OC_S6_05", 111 }, { "OC_S6_07", 88 }, { "OC_S6_17", 4 },
            };
            foreach (var kv in chars) Assert.That(_script.Find(kv.Key).DisplayText.Length, Is.EqualTo(kv.Value), kv.Key);
        }

        [Test]
        public void NoTagSurvivesInDisplayText()
        {
            foreach (var line in _script.Lines)
            {
                Assert.That(line.DisplayText, Does.Not.Contain("["), line.Id);
                Assert.That(line.DisplayText, Does.Not.Contain("  "), line.Id);
                Assert.That(line.DisplayText, Is.EqualTo(line.DisplayText.Trim()), line.Id);
            }
        }

        [Test]
        public void StageDirectionsBecomeSceneNotes()
        {
            var s1 = _script.Scenes[0];
            Assert.That(s1.Title, Is.EqualTo("The white void"));
            Assert.That(s1.Notes.Count, Is.EqualTo(2));
            Assert.That(s1.Notes[0], Does.StartWith("Pure white."));
            Assert.That(_script.Scenes[5].Notes.Last(), Does.Contain("Gameplay begins."));
        }

        [Test]
        public void TagsOutsideTextBlocksAreIgnored()
        {
            const string md = "# T\n\n- **Tags** like \\[excited\\]\n\n## Scene 1: A\n\n*Note.*\n\n```text\nMC: [x] Hi.\nBIRDY: Yo [pause] there.\n```\n\n## How to\n\n```text\nMC: not a line\n```\n";
            var parsed = CutsceneScriptParser.Parse(md);
            Assert.That(parsed.Lines.Count, Is.EqualTo(2));
            Assert.That(parsed.Lines[1].DisplayText, Is.EqualTo("Yo there."));
            Assert.That(parsed.Lines[1].Tags[0].Position, Is.EqualTo(3));
            Assert.That(parsed.Scenes.Count, Is.EqualTo(1));
            Assert.That(parsed.Scenes[0].Notes, Is.EqualTo(new[] { "Note." }));
        }
    }
}
