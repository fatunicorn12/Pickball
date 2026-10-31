using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Editor.Subtitles;
using Pickleball.Cutscene.Subtitles;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Pickleball.Tests.Cutscene.Subtitles
{
    /// <summary>
    /// Builds the real HUD hierarchy (through the setup menu's own <see cref="SubtitleHudBuilder.BuildInto"/>)
    /// on a temporary object, drives a real <see cref="SubtitleRelay"/>, and checks what the TMP texts,
    /// the tab and the ring show. No scene, no assets written. The ring is driven through its
    /// explicit-state Tick: a test-assembly MonoBehaviour can't be added as a component (Unity
    /// refuses editor-assembly scripts), so there is no fake ICutscenePlayback component.
    /// </summary>
    public class SubtitleViewTests
    {
        const string FontPath = "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Roboto-Bold SDF.asset";

        GameObject _hud;
        GameObject _host;
        SubtitleStyle _style;
        SubtitleRelay _relay;
        SubtitleView _view;
        SkipIndicatorView _skip;
        TMP_Text _body;
        TMP_Text _name;
        Image _tab;
        CanvasGroup _group;

        [SetUp]
        public void SetUp()
        {
            _hud = new GameObject("TestCutsceneHUD", typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
            _host = new GameObject("TestCutsceneHost") { hideFlags = HideFlags.DontSave };
            _relay = _host.AddComponent<SubtitleRelay>();
            _style = ScriptableObject.CreateInstance<SubtitleStyle>();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            (_view, _skip) = SubtitleHudBuilder.BuildInto((RectTransform)_hud.transform, _style, font, null, null,
                                                          _relay, null);
            var card = _hud.transform.Find("SafeArea/Subtitle/Card");
            _body = card.Find("Body").GetComponent<TMP_Text>();
            _tab = card.Find("Tab").GetComponent<Image>();
            _name = card.Find("Tab/Name").GetComponent<TMP_Text>();
            _group = _view.GetComponent<CanvasGroup>();

            // AddComponent in EditMode runs no OnEnable: subscribe as Play mode would.
            Invoke(_view, "OnEnable");
            Invoke(_skip, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            if (_view != null) Invoke(_view, "OnDisable");
            if (_skip != null) Invoke(_skip, "OnDisable");
            Object.DestroyImmediate(_hud);
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_style);
        }

        static void Invoke(MonoBehaviour target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        static SubtitleCue Cue(string id, string speaker, string text, int index = 0, int count = 1) =>
            new SubtitleCue(id, speaker, text, index, count);

        [Test]
        public void Built_HiddenWithNoCue_OnItsOwnCanvasAbove60()
        {
            var canvas = _hud.GetComponent<Canvas>();
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.AreEqual(SubtitleHudBuilder.SortingOrder, canvas.sortingOrder);
            Assert.Greater(canvas.sortingOrder, 50, "above DialogueView's nested canvas");
            Assert.AreEqual(0f, _group.alpha);
            Assert.IsFalse(_view.IsShowing);
            Assert.AreEqual(TextWrappingModes.NoWrap, _body.textWrappingMode);
            Assert.IsFalse(_body.enableAutoSizing);
            Assert.IsNotNull(_hud.GetComponentInChildren<SafeAreaPanel>());
        }

        [Test]
        public void BirdyTwoCardLine_ShowsTextTabAndColour_SecondCardSwapsUnderAHeldBacking()
        {
            _relay.Show(Cue("OC_S2_05", CutsceneSpeakers.Birdy, "Can you help me find my propeller? Please?", 0, 2));

            Assert.AreEqual("Can you help me find my propeller? Please?", _body.text);   // 42: one line
            Assert.AreEqual("Birdy", _name.text);
            Assert.AreEqual(_style.SpeakerColour(CutsceneSpeakers.Birdy).r, _name.color.r, 1e-4f);
            Assert.AreEqual(_style.SpeakerColour(CutsceneSpeakers.Birdy).b, _name.color.b, 1e-4f);
            Assert.IsTrue((_name.fontStyle & FontStyles.SmallCaps) != 0, "small-caps tab");
            Assert.IsTrue(_tab.gameObject.activeSelf);
            Assert.IsTrue(_view.IsShowing);

            // Fades in over 0.15 s of unscaled time.
            _view.Tick(0.075f);
            Assert.AreEqual(0.5f, _group.alpha, 1e-3f);
            _view.Tick(0.1f);
            Assert.AreEqual(1f, _group.alpha);

            // Card b replaces card a: backing held, words fade in.
            _relay.Show(Cue("OC_S2_05", CutsceneSpeakers.Birdy, "I can't fly without it. I can barely stand without it.", 1, 2));
            Assert.AreEqual("I can't fly without it.\nI can barely stand without it.", _body.text);
            Assert.AreEqual(1f, _group.alpha, "backing holds through the swap");
            Assert.AreEqual(0f, _body.alpha, 1e-4f);
            _view.Tick(0.5f);
            Assert.AreEqual(1f, _body.alpha, 1e-4f);

            // The card is sized round the two lines.
            var card = (RectTransform)_body.transform.parent;
            Assert.Greater(card.sizeDelta.y, _body.rectTransform.sizeDelta.y);
            Assert.Greater(card.sizeDelta.x, _body.rectTransform.sizeDelta.x);
        }

        [Test]
        public void McLine_ThenEllipsis_ThenHide()
        {
            _relay.Show(Cue("OC_S2_06", CutsceneSpeakers.MC, "Okay, okay. I'll help. Where'd you last have it?"));
            Assert.AreEqual("Okay, okay. I'll help.\nWhere'd you last have it?", _body.text);
            Assert.AreEqual("MC", _name.text);
            Assert.AreEqual(_style.SpeakerColour(CutsceneSpeakers.MC).g, _name.color.g, 1e-4f);

            _relay.Show(Cue("OC_S5_06", CutsceneSpeakers.MC, "..."));
            Assert.AreEqual("...", _body.text);
            _view.Tick(1f);
            Assert.AreEqual(1f, _group.alpha);

            _relay.Hide("OC_S5_06");
            Assert.IsFalse(_view.IsShowing);
            _view.Tick(0.075f);
            Assert.AreEqual(0.5f, _group.alpha, 1e-3f);
            _view.Tick(0.1f);
            Assert.AreEqual(0f, _group.alpha);
        }

        [Test]
        public void HideForAnotherLine_IsIgnored()
        {
            _relay.Show(Cue("OC_S6_12", CutsceneSpeakers.Birdy, "Long enough that people stopped asking."));
            _view.Tick(1f);
            // The relay filters stale ids itself; the view does too, for any other source.
            typeof(SubtitleView).GetMethod("OnCueHidden", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_view, new object[] { "OC_S6_11" });
            Assert.IsTrue(_view.IsShowing);
            Assert.AreEqual("OC_S6_12", _view.CurrentLineId);
        }

        [Test]
        public void Cleared_HidesAtOnce()
        {
            _relay.Show(Cue("OC_S6_13", CutsceneSpeakers.Birdy, "But that's why you're here!"));
            _view.Tick(1f);
            _relay.Clear();
            Assert.AreEqual(0f, _group.alpha, "skip clears without a fade");
            Assert.IsNull(_view.CurrentLineId);

            Assert.IsFalse(_view.IsShowing);
        }

        [Test]
        public void UnknownSpeaker_FallbackColour_IdAsName()
        {
            _relay.Show(Cue("X_01", "NARRATOR", "Meanwhile."));
            Assert.AreEqual("NARRATOR", _name.text);
            Assert.AreEqual(_style.FallbackSpeakerColour.r, _name.color.r, 1e-4f);
        }

        [Test]
        public void TaggedCue_IsStripped_AndLoggedOnce()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[SubtitleView\] cue OC_S1_02 .*\[tag\]"));
            _relay.Show(Cue("OC_S1_02", CutsceneSpeakers.MC, "[pause] Wait."));
            Assert.AreEqual("Wait.", _body.text);

            _relay.Show(Cue("OC_S3_03", CutsceneSpeakers.Birdy, "[pause, staring at MC's back] Wait."));
            Assert.AreEqual("Wait.", _body.text);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SkipRing_FollowsProgress_HintOnlyMidHold()
        {
            _skip.Tick(1f);
            Assert.AreEqual(0f, _skip.RingVisibility, "no playback wired: nothing shows");

            _skip.Tick(1f, true, 0f);
            Assert.AreEqual(0f, _skip.RingVisibility, "playing, not held: no UI");

            _skip.Tick(0.075f, true, 0.4f);
            Assert.AreEqual(0.5f, _skip.RingVisibility, 1e-3f, "fades in over 0.15 s");
            _skip.Tick(1f, true, 0.4f);
            Assert.AreEqual(1f, _skip.RingVisibility);
            Assert.AreEqual(1f, _skip.LabelVisibility);
            Assert.AreEqual(0.4f, _skip.FillAmount, 1e-4f);

            _skip.Tick(1f, true, 1f);
            Assert.AreEqual(1f, _skip.RingVisibility);
            Assert.AreEqual(0f, _skip.LabelVisibility, "no hint once the hold completes");

            _skip.Tick(0.075f, true, 0f);
            Assert.AreEqual(0.5f, _skip.RingVisibility, 1e-3f, "released: fades out");
            _skip.Tick(1f, true, 0f);
            Assert.AreEqual(0f, _skip.RingVisibility);

            _skip.Tick(1f, false, 0.7f);
            Assert.AreEqual(0f, _skip.RingVisibility, "hidden when not playing");
            Assert.AreEqual(0f, _skip.LabelVisibility);
        }
    }
}
