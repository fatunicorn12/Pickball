using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Pins the conversation state machine: how fast text types (independent of
    /// frame rate), what each press does, where punctuation pauses land, and
    /// that rich-text tags are free.
    /// </summary>
    public class DialogueRunnerTests : DialogueTestFixture
    {
        static TypewriterTiming Timing(float cps = 20f, float comma = 0f, float sentence = 0f, float lockout = 0f)
            => new() { CharactersPerSecond = cps, CommaPause = comma, SentencePause = sentence, AdvanceLockout = lockout };

        /// <summary>Ticks <paramref name="seconds"/> in steps of <paramref name="dt"/>.</summary>
        static void Run(DialogueRunner runner, float seconds, float dt)
        {
            int steps = UnityEngine.Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < steps; i++) runner.Tick(dt);
        }

        // ------------------------------------------------------------ typewriter

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Typewriter_RevealsCharactersAtTheConfiguredRate_AtAnyFrameRate(float dt)
        {
            var runner = new DialogueRunner(Timing(cps: 20f));
            runner.Begin(Definition("abcdefghijklmnopqrstuvwxyz0123456789"));

            // 0.525 s at 20 chars/s sits midway between the 10th and 11th character,
            // so float rounding in how the time was sliced cannot change the count.
            Run(runner, 0.525f, dt);

            Assert.That(runner.VisibleCharacters, Is.InRange(10, 11));
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Typing));
        }

        [Test]
        public void Typewriter_OneBigTickAndManySmallOnesShowTheSameCount()
        {
            var a = new DialogueRunner(Timing(cps: 40f, comma: 0.1f, sentence: 0.3f));
            var b = new DialogueRunner(Timing(cps: 40f, comma: 0.1f, sentence: 0.3f));
            var def = Definition("Hello, there. How are you, friend? Fine.");
            a.Begin(def);
            b.Begin(def);

            a.Tick(0.71f);
            for (int i = 0; i < 71; i++) b.Tick(0.01f);

            Assert.That(b.VisibleCharacters, Is.InRange(a.VisibleCharacters - 1, a.VisibleCharacters + 1));
        }

        [Test]
        public void Typewriter_FinishingTheLineRaisesLineRevealedOnce()
        {
            var runner = new DialogueRunner(Timing(cps: 100f));
            int revealed = 0;
            runner.LineRevealed += _ => revealed++;
            runner.Begin(Definition("Short."));

            runner.Tick(1f);
            runner.Tick(1f);

            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Revealed));
            Assert.That(runner.VisibleCharacters, Is.EqualTo(6));
            Assert.That(revealed, Is.EqualTo(1));
        }

        [Test]
        public void Typewriter_SentencePauseLandsOnTheSpaceAfterTheFullStop()
        {
            // 10 chars/s: each character costs 0.1 s; the space after "Hi." costs 0.1 + 0.5.
            var runner = new DialogueRunner(Timing(cps: 10f, sentence: 0.5f));
            runner.Begin(Definition("Hi. Yo"));

            runner.Tick(0.35f);   // "Hi." shown
            Assert.That(runner.VisibleCharacters, Is.EqualTo(3));
            runner.Tick(0.5f);    // 0.85 s: the space (due at 0.9) is still held back
            Assert.That(runner.VisibleCharacters, Is.EqualTo(3));
            runner.Tick(0.1f);    // 0.95 s
            Assert.That(runner.VisibleCharacters, Is.EqualTo(4));
        }

        [Test]
        public void Typewriter_NoPauseInsideANumber()
        {
            var runner = new DialogueRunner(Timing(cps: 10f, sentence: 5f));
            runner.Begin(Definition("3.14"));

            runner.Tick(0.45f);

            Assert.That(runner.VisibleCharacters, Is.EqualTo(4));
        }

        [Test]
        public void Typewriter_ClosingQuoteCarriesThePause()
        {
            var runner = new DialogueRunner(Timing(cps: 10f, sentence: 1f));
            runner.Begin(Definition("\"Hi.\" He"));

            runner.Tick(0.55f);   // the five characters up to the closing quote
            Assert.That(runner.VisibleCharacters, Is.EqualTo(5));
            runner.Tick(0.5f);    // 1.05 s: the space after the quote is due at 1.6
            Assert.That(runner.VisibleCharacters, Is.EqualTo(5));
        }

        [Test]
        public void Typewriter_RichTextTagsAreFreeAndNotCounted()
        {
            var runner = new DialogueRunner(Timing(cps: 10f));
            runner.Begin(Definition("<b>Hey</b> you"));

            Assert.That(runner.TotalCharacters, Is.EqualTo(7));
            runner.Tick(0.35f);
            Assert.That(runner.VisibleCharacters, Is.EqualTo(3));
            Assert.That(DialogueRunner.CountVisible("<color=#ff0>A</color><3"), Is.EqualTo(3));
        }

        // TMP counting: only TMP-valid tags are tags; character tags and emoji count once.
        [TestCase("a < b > c", 9, TestName = "Count_SpacedAngleBracketsAreText")]
        [TestCase("x<br>y", 3, TestName = "Count_BrIsOneCharacter")]
        [TestCase("<b>Hey</b>", 3, TestName = "Count_FormattingTagsAreFree")]
        [TestCase("<#ff0000>red</color>", 3, TestName = "Count_HexColourTagIsFree")]
        [TestCase("a<nbsp>b<sprite=0>", 4, TestName = "Count_NbspAndSpriteAreCharacters")]
        [TestCase("Hi \U0001F600!", 5, TestName = "Count_EmojiIsOneCharacter")]
        [TestCase("<b unclosed", 11, TestName = "Count_UnclosedTagIsText")]
        [TestCase("<b<i>x", 3, TestName = "Count_AngleBeforeCloseIsText")]
        public void CountVisible_MatchesTextMeshPro(string text, int expected)
        {
            Assert.That(DialogueRunner.CountVisible(text), Is.EqualTo(expected));
        }

        [Test]
        public void Typewriter_RevealsAnEmojiAsOneStep()
        {
            var runner = new DialogueRunner(Timing(cps: 10f));
            runner.Begin(Definition("\U0001F600\U0001F600"));

            runner.Tick(0.15f);
            Assert.That(runner.VisibleCharacters, Is.EqualTo(1));
            runner.Tick(0.1f);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Revealed));
            Assert.That(runner.VisibleCharacters, Is.EqualTo(2));
        }

        [Test]
        public void Typewriter_BrTakesATurnAndPausesLikeALineBreak()
        {
            // "Hi." then <br> (one character, whitespace, so it owes the sentence pause).
            var runner = new DialogueRunner(Timing(cps: 10f, sentence: 0.5f));
            runner.Begin(Definition("Hi.<br>Yo"));

            runner.Tick(0.85f);   // the <br> is due at 0.3 + 0.1 + 0.5 = 0.9
            Assert.That(runner.VisibleCharacters, Is.EqualTo(3));
            runner.Tick(0.1f);
            Assert.That(runner.VisibleCharacters, Is.EqualTo(4));
        }

        [Test]
        public void Typewriter_ZeroSpeedShowsEachLineAtOnce()
        {
            var runner = new DialogueRunner(Timing(cps: 0f));
            runner.Begin(Definition("All of it at once."));

            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Revealed));
            Assert.That(runner.VisibleCharacters, Is.EqualTo(runner.TotalCharacters));
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        public void Typewriter_SpeedTunedToZeroOrNaNMidLine_ShowsTheRest(float cps)
        {
            var runner = new DialogueRunner(Timing(cps: 10f));
            runner.Begin(Definition("Typing along."));
            runner.Tick(0.15f);
            Assert.That(runner.VisibleCharacters, Is.EqualTo(1));

            runner.Timing = Timing(cps: cps);
            runner.Tick(0.01f);

            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Revealed));
            Assert.That(runner.VisibleCharacters, Is.EqualTo(runner.TotalCharacters));
        }

        [Test]
        public void Typewriter_ZeroOrNegativeTickDoesNothing()
        {
            var runner = new DialogueRunner(Timing(cps: 10f));
            runner.Begin(Definition("abc"));

            runner.Tick(0f);
            runner.Tick(-1f);
            runner.Tick(float.NaN);

            Assert.That(runner.VisibleCharacters, Is.EqualTo(0));
        }

        // --------------------------------------------------------------- advance

        [Test]
        public void Advance_WhileTyping_RevealsTheWholeLineAndStaysOnIt()
        {
            var runner = new DialogueRunner(Timing(cps: 10f));
            var revealed = new List<int>();
            runner.LineRevealed += revealed.Add;
            runner.Begin(Definition("A long first line.", "Second."));
            runner.Tick(0.2f);

            var result = runner.Advance();

            Assert.That(result, Is.EqualTo(DialogueRunner.AdvanceResult.RevealedLine));
            Assert.That(runner.LineIndex, Is.EqualTo(0));
            Assert.That(runner.VisibleCharacters, Is.EqualTo(runner.TotalCharacters));
            Assert.That(revealed, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void Advance_OnARevealedLine_StartsTheNextLine()
        {
            var runner = new DialogueRunner(Timing(cps: 10f));
            var started = new List<int>();
            runner.LineStarted += started.Add;
            runner.Begin(Definition("One.", "Two."));
            runner.Advance();

            var result = runner.Advance();

            Assert.That(result, Is.EqualTo(DialogueRunner.AdvanceResult.NextLine));
            Assert.That(runner.LineIndex, Is.EqualTo(1));
            Assert.That(runner.VisibleCharacters, Is.EqualTo(0));
            Assert.That(runner.CurrentText, Is.EqualTo("Two."));
            Assert.That(started, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Advance_OnTheLastRevealedLine_Finishes()
        {
            var runner = new DialogueRunner(Timing(cps: 10f));
            int finished = 0;
            runner.Finished += () => finished++;
            runner.Begin(Definition("Only line."));
            runner.Advance();

            var result = runner.Advance();

            Assert.That(result, Is.EqualTo(DialogueRunner.AdvanceResult.Finished));
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Finished));
            Assert.That(finished, Is.EqualTo(1));
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Ignored));
            Assert.That(finished, Is.EqualTo(1));
        }

        [Test]
        public void Advance_FullConversation_RaisesEachEventTheRightNumberOfTimes()
        {
            var runner = new DialogueRunner(Timing(cps: 50f));
            int started = 0, revealed = 0, finished = 0;
            runner.LineStarted += _ => started++;
            runner.LineRevealed += _ => revealed++;
            runner.Finished += () => finished++;
            runner.Begin(Definition("One.", "Two, typed out.", "Three."));

            runner.Advance();            // skip line 0
            runner.Advance();            // to line 1
            runner.Tick(5f);             // line 1 types out by itself
            runner.Advance();            // to line 2
            runner.Advance();            // skip line 2
            runner.Advance();            // finish

            Assert.That((started, revealed, finished), Is.EqualTo((3, 3, 1)));
        }

        [Test]
        public void Advance_InsideTheLockout_IsIgnored()
        {
            var runner = new DialogueRunner(Timing(cps: 10f, lockout: 0.2f));
            runner.Begin(Definition("Line one.", "Line two."));

            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Ignored));
            runner.Tick(0.25f);
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.RevealedLine));
            // The reveal restarts the lockout, so a double-tap cannot skip the line unread.
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Ignored));
            runner.Tick(0.25f);
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.NextLine));
        }

        [Test]
        public void Advance_WhenIdle_IsIgnored()
        {
            var runner = new DialogueRunner(Timing());
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Ignored));
        }

        // ------------------------------------------------------------- lifecycle

        [Test]
        public void Begin_WithNoLinesOrNoDefinition_StaysIdle()
        {
            var runner = new DialogueRunner(Timing());

            Assert.That(runner.Begin(null), Is.False);
            Assert.That(runner.Begin(Definition()), Is.False);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Idle));
            Assert.That(runner.LineIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Begin_AnEmptyFirstLine_IsRevealedAtOnce()
        {
            var runner = new DialogueRunner(Timing(cps: 10f));
            int revealed = 0;
            runner.LineRevealed += _ => revealed++;

            runner.Begin(Definition("", "Next."));

            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Revealed));
            Assert.That(revealed, Is.EqualTo(1));
        }

        [Test]
        public void Stop_DropsTheConversationWithoutFinished()
        {
            var runner = new DialogueRunner(Timing());
            int finished = 0;
            runner.Finished += () => finished++;
            runner.Begin(Definition("Line."));

            runner.Stop();

            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Idle));
            Assert.That(runner.CurrentText, Is.Empty);
            Assert.That(finished, Is.EqualTo(0));
        }
    }
}
