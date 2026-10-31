using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Subtitles;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Subtitles
{
    /// <summary>
    /// The plain-C# pieces behind the subtitle HUD: speaker styling, the fade, the tag guard, the
    /// skip-ring rules and the safe-area maths.
    /// </summary>
    public class SubtitleLogicTests
    {
        // ------------------------------------------------------------ speaker colours

        [Test]
        public void SpeakerColour_HouseSpeakers_AndFallback()
        {
            var style = ScriptableObject.CreateInstance<SubtitleStyle>();
            try
            {
                var defaults = SubtitleStyle.DefaultSpeakers();
                Color birdy = defaults[0].Colour, mc = defaults[1].Colour;

                Assert.AreEqual(birdy, style.SpeakerColour(CutsceneSpeakers.Birdy));
                Assert.AreEqual(birdy, style.SpeakerColour("birdy"), "ids match ignoring case");
                Assert.AreEqual(mc, style.SpeakerColour(CutsceneSpeakers.MC));
                Assert.AreEqual(style.FallbackSpeakerColour, style.SpeakerColour("NARRATOR"));
                Assert.AreEqual(style.FallbackSpeakerColour, style.SpeakerColour(null));
                Assert.AreEqual(style.FallbackSpeakerColour, style.SpeakerColour(""));

                // Birdy warm gold (red > green > blue, saturated), MC a soft grey (near-equal channels).
                Assert.Greater(birdy.r, birdy.g);
                Assert.Greater(birdy.g, birdy.b + 0.3f);
                Assert.Less(Mathf.Max(mc.r, mc.g, mc.b) - Mathf.Min(mc.r, mc.g, mc.b), 0.1f);
            }
            finally { Object.DestroyImmediate(style); }
        }

        [Test]
        public void SpeakerName_UsesDisplayName_ElseTheId()
        {
            var style = ScriptableObject.CreateInstance<SubtitleStyle>();
            try
            {
                Assert.AreEqual("Birdy", style.SpeakerName(CutsceneSpeakers.Birdy));
                Assert.AreEqual("MC", style.SpeakerName(CutsceneSpeakers.MC));
                Assert.AreEqual("NARRATOR", style.SpeakerName("NARRATOR"));
                Assert.AreEqual(string.Empty, style.SpeakerName(null));
            }
            finally { Object.DestroyImmediate(style); }
        }

        [Test]
        public void EnsureSpeaker_AddsOnce_AndNeverOverwrites()
        {
            var style = ScriptableObject.CreateInstance<SubtitleStyle>();
            try
            {
                int before = style.SpeakerCount;
                Assert.IsFalse(style.EnsureSpeaker("Birdy", "X", Color.red), "existing id, any case");
                Assert.AreEqual(SubtitleStyle.DefaultSpeakers()[0].Colour, style.SpeakerColour("BIRDY"));

                Assert.IsTrue(style.EnsureSpeaker("HYPATIA", "Hypatia", Color.cyan));
                Assert.IsFalse(style.EnsureSpeaker("HYPATIA", "Other", Color.red));
                Assert.AreEqual(before + 1, style.SpeakerCount);
                Assert.AreEqual(Color.cyan, style.SpeakerColour("hypatia"));
            }
            finally { Object.DestroyImmediate(style); }
        }

        [Test]
        public void StyleDefaults_MatchTheBrief()
        {
            var style = ScriptableObject.CreateInstance<SubtitleStyle>();
            try
            {
                Assert.AreEqual(42, style.MaxCharsPerLine);
                Assert.AreEqual(2, style.MaxLines);
                Assert.AreEqual(0.15f, style.FadeSeconds, 1e-6f);
                Assert.AreEqual(Color.white, style.TextColour);
            }
            finally { Object.DestroyImmediate(style); }
        }

        // ------------------------------------------------------------ fade

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Fade_IsFrameRateIndependent(float dt)
        {
            // 1/6 s is a whole number of frames at 30, 60 and 144 fps.
            const float seconds = 0.5f;
            int frames = Mathf.RoundToInt((1f / 6f) / dt);
            float alpha = 0f;
            for (int i = 0; i < frames; i++) alpha = SubtitleFade.Step(alpha, 1f, dt, seconds);
            Assert.AreEqual(1f / 3f, alpha, 1e-4f);

            // And back down at the same rate.
            for (int i = 0; i < frames; i++) alpha = SubtitleFade.Step(alpha, 0f, dt, seconds);
            Assert.AreEqual(0f, alpha, 1e-4f);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Fade_CompletesInFadeSeconds_WithoutOvershoot(float dt)
        {
            const float seconds = 0.15f;
            float alpha = 0f, time = 0f;
            while (alpha < 1f && time < 1f)
            {
                alpha = SubtitleFade.Step(alpha, 1f, dt, seconds);
                time += dt;
                Assert.LessOrEqual(alpha, 1f);
            }
            Assert.AreEqual(1f, alpha);
            Assert.AreEqual(seconds, time, dt + 1e-5f);
        }

        [Test]
        public void Fade_ZeroSecondsSnaps_NegativeDtHolds()
        {
            Assert.AreEqual(1f, SubtitleFade.Step(0f, 1f, 0.001f, 0f));
            Assert.AreEqual(0.4f, SubtitleFade.Step(0.4f, 1f, -1f, 0.15f));
        }

        // ------------------------------------------------------------ tag guard

        [Test]
        public void TagGuard_StripsPause()
        {
            Assert.AreEqual("Wait.", SubtitleTagGuard.Strip("[pause] Wait.", out bool stripped));
            Assert.IsTrue(stripped);
        }

        [Test]
        public void TagGuard_StripsMidLineAndMultipleTags()
        {
            Assert.AreEqual("It's about this big! I can't show you how big.",
                            SubtitleTagGuard.Strip("[eager] It's about this big! [pause] I can't show you how big.",
                                                   out bool stripped));
            Assert.IsTrue(stripped);
            Assert.AreEqual("Huh?", SubtitleTagGuard.Strip("Huh?[confused]", out _));
        }

        [Test]
        public void TagGuard_LeavesCleanTextUntouched()
        {
            const string clean = "...Okay. White void. I'm dead. Probably dead.";
            Assert.AreSame(clean, SubtitleTagGuard.Strip(clean, out bool stripped));
            Assert.IsFalse(stripped);
            Assert.AreEqual("a [b", SubtitleTagGuard.Strip("a [b", out stripped), "unmatched '[' isn't a tag");
            Assert.IsFalse(stripped);
            Assert.AreEqual("a ] b [", SubtitleTagGuard.Strip("a ] b [", out stripped));
            Assert.IsFalse(stripped);
        }

        // ------------------------------------------------------------ skip ring

        [Test]
        public void SkipRules_HiddenWhenNotPlaying()
        {
            foreach (float p in new[] { 0f, 0.5f, 1f })
            {
                Assert.AreEqual(0f, SkipIndicatorRules.RingTarget(false, p));
                Assert.AreEqual(0f, SkipIndicatorRules.LabelTarget(false, p));
                Assert.AreEqual(0f, SkipIndicatorRules.Fill(false, p));
            }
        }

        [Test]
        public void SkipRules_RingFromFirstHold_LabelOnlyMidHold()
        {
            Assert.AreEqual(0f, SkipIndicatorRules.RingTarget(true, 0f), "idle: no UI");
            Assert.AreEqual(0f, SkipIndicatorRules.LabelTarget(true, 0f));

            Assert.AreEqual(1f, SkipIndicatorRules.RingTarget(true, 0.01f));
            Assert.AreEqual(1f, SkipIndicatorRules.LabelTarget(true, 0.5f));

            Assert.AreEqual(1f, SkipIndicatorRules.RingTarget(true, 1f), "full ring shows as it skips");
            Assert.AreEqual(0f, SkipIndicatorRules.LabelTarget(true, 1f), "no hint once complete");
        }

        [Test]
        public void SkipRules_FillClamps()
        {
            Assert.AreEqual(0.25f, SkipIndicatorRules.Fill(true, 0.25f));
            Assert.AreEqual(0f, SkipIndicatorRules.Fill(true, -1f));
            Assert.AreEqual(1f, SkipIndicatorRules.Fill(true, 3f));
            Assert.AreEqual(0f, SkipIndicatorRules.Fill(true, float.NaN));
        }

        // ------------------------------------------------------------ safe area

        [Test]
        public void SafeArea_NotchedPhone_ToAnchors()
        {
            // 19.5:9 landscape phone, notch on the left, home indicator at the bottom.
            SafeAreaMath.Anchors(new Rect(132f, 63f, 2340f - 132f - 132f, 1080f - 63f), 2340, 1080,
                                 out var min, out var max);
            Assert.AreEqual(132f / 2340f, min.x, 1e-5f);
            Assert.AreEqual(63f / 1080f, min.y, 1e-5f);
            Assert.AreEqual((2340f - 132f) / 2340f, max.x, 1e-5f);
            Assert.AreEqual(1f, max.y, 1e-5f);
        }

        [Test]
        public void SafeArea_Degenerate_IsFullScreen()
        {
            SafeAreaMath.Anchors(new Rect(0f, 0f, 0f, 0f), 1920, 1080, out var min, out var max);
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
            SafeAreaMath.Anchors(new Rect(0f, 0f, 100f, 100f), 0, 0, out min, out max);
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }
    }
}
