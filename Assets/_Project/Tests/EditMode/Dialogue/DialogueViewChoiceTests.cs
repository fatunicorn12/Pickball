using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using Pickleball.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// The option list as the view draws it, driven by a real director: one row
    /// per option shown, the ► only on the highlighted row, greyed options in
    /// the disabled colour, the mouse's hit test, and the list gone once a pick
    /// is made. Rows are built in code the way Setup Dialogue lays them out.
    /// </summary>
    public class DialogueViewChoiceTests : DialogueTestFixture
    {
        const int Rows = 4;
        const float ChoiceLockout = 0.5f;

        DialogueConfig _config;
        DialogueDirector _director;
        DialogueView _view;
        Talkable _npc;
        Transform _player;
        DialogueViewOptionRow[] _rows;

        [SetUp]
        public void SetUpView()
        {
            _config = NewConfig();
            SetFloat(_config, "settleSeconds", 0f);
            SetFloat(_config, "textStartDelaySeconds", 0f);
            SetFloat(_config, "charactersPerSecond", 0f);
            SetFloat(_config, "choiceLockoutSeconds", ChoiceLockout);
            SetBool(_config, "pauseAudio", false);

            var playerHost = NewHost("Player");
            _player = playerHost.transform;
            _director = NewDirector(_config, playerHost.AddComponent<PlayerInputGate>());
            SetRef(_director, "choiceStore", DialogueChoiceDirectorTests.MemoryStoreHost(_director.gameObject));

            _npc = NewTalkable(Track(DialogueDefinition.CreateRuntime("Birdy", "view.test", "hi",
                DialogueNode.CreateLine("hi", "Well?", DialogueEmote.Talk, "ask"),
                DialogueNode.CreateChoice("ask", "", DialogueEmote.Talk, "",
                    DialogueOption.Create("a", "First", ""),
                    DialogueOption.Create("b", "Locked", "", new[] { FactCondition.Fact(FactConditionKind.FactExists, "key") },
                                          UnmetOptionMode.Disable),
                    DialogueOption.Create("c", "Third", "")))), new Vector3(0f, 0f, 2f));

            var panel = NewHost("Panel");
            panel.AddComponent<RectTransform>();
            var list = new GameObject("Choices", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            list.SetParent(panel.transform, false);
            _rows = new DialogueViewOptionRow[Rows];
            for (int i = 0; i < Rows; i++)
            {
                var row = new GameObject($"Option{i + 1}", typeof(RectTransform), typeof(Image), typeof(LayoutElement))
                    .GetComponent<RectTransform>();
                row.GetComponent<LayoutElement>().minHeight = 64f;    // as Setup Dialogue builds them
                row.SetParent(list, false);
                row.sizeDelta = new Vector2(300f, 60f);
                row.position = new Vector3(500f, 400f - 70f * i, 0f);   // first option on top
                var label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                label.transform.SetParent(row, false);
                var cursor = new GameObject("Cursor", typeof(RectTransform)).AddComponent<Image>();
                cursor.transform.SetParent(row, false);
                _rows[i] = new DialogueViewOptionRow(row, row.GetComponent<Image>(), label, cursor);
            }

            _view = panel.AddComponent<DialogueView>();
            SetRef(_view, "director", _director);
            SetRef(_view, "config", _config);
            SetRef(_view, "optionList", list);
            SetRef(_view, "optionGroup", list.GetComponent<CanvasGroup>());
            var so = new SerializedObject(_view);
            var array = so.FindProperty("optionRows");
            array.arraySize = Rows;
            for (int i = 0; i < Rows; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("row").objectReferenceValue = _rows[i].Row;
                element.FindPropertyRelative("plate").objectReferenceValue = _rows[i].Plate;
                element.FindPropertyRelative("label").objectReferenceValue = _rows[i].Label;
                element.FindPropertyRelative("cursor").objectReferenceValue = _rows[i].Cursor;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Lifecycle(_view, "OnEnable");
        }

        [TearDown]
        public void DisableView() => Lifecycle(_view, "OnDisable");      // drops its input listener

        void StartToOptions()
        {
            Assert.That(_director.TryStart(_npc, _player), Is.True);
            _director.Tick(0.01f, false);
            _director.Tick(0.01f, false);
            Assert.That(_director.IsChoosing, Is.True, $"state {_director.State}");
        }

        [Test]
        public void TheList_ShowsOneRowPerOption_TheCursorOnTheHighlight_AndGreysTheLockedOne()
        {
            Assert.That(_rows[0].Row.gameObject.activeSelf, Is.False, "hidden until options are up");
            StartToOptions();

            Assert.That(_view.OptionsShown, Is.True);
            Assert.That(_view.OptionCount, Is.EqualTo(3));
            for (int i = 0; i < Rows; i++)
                Assert.That(_rows[i].Row.gameObject.activeSelf, Is.EqualTo(i < 3), $"row {i}");
            Assert.That(_rows[0].Label.text, Is.EqualTo("First"));
            Assert.That(_rows[1].Label.text, Is.EqualTo("Locked"));
            Assert.That(_rows[1].Label.color, Is.EqualTo(_config.OptionDisabledTextColour));
            Assert.That(_rows[0].Cursor.enabled, Is.True);
            Assert.That(_rows[1].Cursor.enabled || _rows[2].Cursor.enabled, Is.False);

            _director.MoveChoiceHighlight(1);       // skips the locked one
            Lifecycle(_view, "Update");
            Assert.That(_director.HighlightedChoice, Is.EqualTo(2));
            Assert.That(_rows[2].Cursor.enabled, Is.True);
            Assert.That(_rows[0].Cursor.enabled, Is.False);
        }

        [Test]
        public void RowAt_FindsTheRowUnderAPoint()
        {
            StartToOptions();
            Assert.That(_view.RowAt(new Vector2(500f, 400f)), Is.EqualTo(0));
            Assert.That(_view.RowAt(new Vector2(450f, 260f)), Is.EqualTo(2));
            Assert.That(_view.RowAt(new Vector2(500f, 190f)), Is.EqualTo(-1), "the unused fourth row");
            Assert.That(_view.RowAt(new Vector2(900f, 400f)), Is.EqualTo(-1));
        }

        [Test]
        public void APick_TakesTheListDown()
        {
            StartToOptions();
            _director.Tick(ChoiceLockout + 0.01f, false);

            Assert.That(_director.ConfirmChoice(2), Is.True);

            Assert.That(_view.OptionsShown, Is.False);
            for (int i = 0; i < Rows; i++) Assert.That(_rows[i].Row.gameObject.activeSelf, Is.False, $"row {i}");
        }

        [Test]
        public void TheColumn_IsAsWideAsItsLongestAnswer_UpToTheMaximum()
        {
            StartToOptions();
            var list = _rows[0].Row.parent as RectTransform;
            float shortWidth = list.rect.width;
            Assert.That(shortWidth, Is.GreaterThan(0f).And.LessThan(_config.OptionMaxWidth));

            _director.Abort();
            for (int i = 0; i < 20; i++) _director.Tick(0.1f, false);
            var longTalk = NewTalkable(Track(DialogueDefinition.CreateRuntime("Birdy", "view.long", "ask",
                DialogueNode.CreateChoice("ask", "Well?", DialogueEmote.Talk, "",
                    DialogueOption.Create("a", "An answer far too long to sit on one line beside an NPC's wing", ""),
                    DialogueOption.Create("b", "Short.", "")))), new Vector3(0f, 0f, 2f));
            Assert.That(_director.TryStart(longTalk, _player), Is.True);
            _director.Tick(0.01f, false);
            _director.Tick(0.01f, false);
            Assert.That(_director.IsChoosing, Is.True);
            Assert.That(list.rect.width, Is.EqualTo(_config.OptionMaxWidth).Within(0.01f), "capped; the answer wraps");
            float wrapped = _rows[0].Row.GetComponent<LayoutElement>().preferredHeight;
            float single = _rows[1].Row.GetComponent<LayoutElement>().preferredHeight;
            Assert.That(single, Is.EqualTo(64f), "a one-line answer keeps the row's minimum height");
            Assert.That(wrapped, Is.GreaterThan(single), "only the wrapped answer's row grows");
        }

        [Test]
        public void AnAbortMidChoice_TakesTheListDown()
        {
            StartToOptions();
            _director.Abort();
            Assert.That(_view.OptionsShown, Is.False);
            Assert.That(_rows[0].Row.gameObject.activeSelf, Is.False);
        }
    }
}
