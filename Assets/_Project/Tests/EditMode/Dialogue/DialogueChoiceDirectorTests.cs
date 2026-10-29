using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Choices through the whole director lifecycle with explicit time: the
    /// options appear in a paused world, the Talkable hears ChoicesShown and
    /// ChoiceMade, Interact / a click / a number key pick through the wired
    /// store provider, the gate keeps its one All claim, and every abort path
    /// mid-choice unwinds as before with nothing recorded. The store is the
    /// real <see cref="ChoiceStoreHost"/> with <c>persist</c> off: memory only,
    /// it never reads or writes a save file.
    /// </summary>
    public class DialogueChoiceDirectorTests : DialogueTestFixture
    {
        const float Settle = 0.25f;
        const float TextDelay = 0.2f;
        const float ChoiceLockout = 0.5f;
        const float ChoiceQuiet = 1.1f;

        DialogueConfig _config;
        PlayerInputGate _gate;
        DialogueDirector _director;
        ChoiceStoreHost _host;
        Talkable _npc;
        Transform _player;
        int _factWrites;

        IChoiceStore Store => _host.Store;

        /// <summary>A store host that lives in memory only: persist is switched off
        /// before anything builds its store, so no save file is ever touched.</summary>
        internal static ChoiceStoreHost MemoryStoreHost(GameObject go)
        {
            var host = go.AddComponent<ChoiceStoreHost>();
            SetBool(host, "persist", false);
            Assert.That(host.Persist, Is.False);
            return host;
        }

        [SetUp]
        public void SetUpConversation()
        {
            _config = NewConfig();
            SetFloat(_config, "settleSeconds", Settle);
            SetFloat(_config, "textStartDelaySeconds", TextDelay);
            SetFloat(_config, "charactersPerSecond", 0f);        // instant text: the choice logic is the point
            SetFloat(_config, "advanceLockoutSeconds", 0f);
            SetFloat(_config, "choiceLockoutSeconds", ChoiceLockout);
            SetFloat(_config, "choiceQuietSeconds", ChoiceQuiet);
            SetBool(_config, "pauseAudio", false);

            var playerHost = NewHost("Player");
            _player = playerHost.transform;
            _gate = playerHost.AddComponent<PlayerInputGate>();
            _director = NewDirector(_config, _gate);
            _host = MemoryStoreHost(_director.gameObject);
            SetRef(_director, "choiceStore", _host);
            _factWrites = 0;
            Store.FactChanged += _ => _factWrites++;

            var def = Track(DialogueDefinition.CreateRuntime("Birdy", "test.talk", "check",
                DialogueNode.CreateBranch("check", new[] { FactCondition.Fact(FactConditionKind.FactEquals, "promised", FactValue.Of(true)) },
                                          "again", "hello"),
                DialogueNode.CreateLine("hello", "Could you help me?", DialogueEmote.Thinking, "ask"),
                DialogueNode.CreateChoice("ask", "", DialogueEmote.Talk, "",
                    DialogueOption.Create("yes", "Sure.", "thanks",
                        effects: new[] { FactEffect.Set("promised", FactValue.Of(true)) }),
                    DialogueOption.Create("no", "Not now.", "")),
                DialogueNode.CreateLine("thanks", "Thank you!", DialogueEmote.Excited),
                DialogueNode.CreateLine("again", "You came back!", DialogueEmote.Excited)));
            _npc = NewTalkable(def, new Vector3(0f, 0f, 2f));
        }

        /// <summary>Start, settle, open: the first line is up and, being instant,
        /// already revealed, so the options are up too.</summary>
        void StartToOptions()
        {
            Assert.That(_director.TryStart(_npc, _player), Is.True);
            _director.Tick(Settle + 0.01f, false);
            _director.Tick(TextDelay + 0.01f, false);
            Assert.That(_director.IsChoosing, Is.True, $"state {_director.State}");
        }

        /// <summary>Long enough, with Interact left alone, that a press would pick.</summary>
        void PastLockout() => _director.Tick(Mathf.Max(ChoiceLockout, ChoiceQuiet) + 0.01f, false);

        [Test]
        public void Options_AppearInThePausedWorld_AndTheNpcHearsAboutThem()
        {
            var shown = new List<string>();
            _npc.ChoicesShown += (t, node) => shown.Add(node);
            int directorShown = 0;
            _director.ChoicesShown += _ => directorShown++;

            StartToOptions();

            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.All), "the one All claim, nothing new");
            Assert.That(_director.Choices.Count, Is.EqualTo(2));
            Assert.That(_director.HighlightedChoice, Is.EqualTo(0));
            Assert.That(_director.CurrentText, Is.EqualTo("Could you help me?"));
            Assert.That(shown, Is.EqualTo(new[] { "ask" }));
            Assert.That(directorShown, Is.EqualTo(1));
            Assert.That(_npc.IsAwaitingChoice, Is.True);
            Assert.That(_npc.CurrentLine.Emote, Is.EqualTo(DialogueEmote.Thinking), "a graph line reaches the NPC with its emote");
        }

        [Test]
        public void Interact_PicksTheHighlighted_RecordsIt_AndTheTalkGoesOn()
        {
            var made = new List<string>();
            _npc.ChoiceMade += (t, node, choice) => made.Add($"{node}/{choice}");
            StartToOptions();
            PastLockout();

            _director.Tick(0.01f, true);

            Assert.That(made, Is.EqualTo(new[] { "ask/yes" }));
            Assert.That(Store.Choices.Count, Is.EqualTo(1));
            Assert.That(Store.Choices[0].Matches("test.talk", "ask", "yes"), Is.True);
            Assert.That(Store.GetBool("promised"), Is.True);
            Assert.That(_director.IsChoosing, Is.False);
            Assert.That(_npc.IsAwaitingChoice, Is.False);
            Assert.That(_director.CurrentText, Is.EqualTo("Thank you!"));
            Assert.That(_npc.CurrentLine.Emote, Is.EqualTo(DialogueEmote.Excited));
        }

        [Test]
        public void TheSecondTalk_TakesTheOtherBranch_OnceTheFactIsSet()
        {
            StartToOptions();
            PastLockout();
            _director.Tick(0.01f, true);    // "Sure."
            _director.Tick(0.01f, true);    // dismiss "Thank you!"
            for (int i = 0; i < 20; i++) _director.Tick(0.1f, false);   // close, release, cool down
            Assert.That(_director.CanStartConversation, Is.True);

            Assert.That(_director.TryStart(_npc, _player), Is.True);
            _director.Tick(Settle + 0.01f, false);
            _director.Tick(TextDelay + 0.01f, false);
            Assert.That(_director.CurrentText, Is.EqualTo("You came back!"));
        }

        [Test]
        public void HoverAndClick_NumberKeys_PickAtOnce_WhileInteractStillWaits()
        {
            var highlighted = new List<int>();
            _director.ChoiceHighlighted += highlighted.Add;
            StartToOptions();

            _director.MoveChoiceHighlight(1);
            _director.MoveChoiceHighlight(1);       // wraps back to 0
            Assert.That(_director.HighlightChoice(1), Is.True);
            Assert.That(_director.HighlightChoice(5), Is.False);
            Assert.That(highlighted, Is.EqualTo(new[] { 1, 0, 1 }));

            Assert.That(_director.IsChoiceReady, Is.False, "Interact would not pick yet");
            _director.Tick(0.01f, true);
            Assert.That(Store.Choices, Is.Empty);

            Assert.That(_director.ConfirmChoice(1), Is.True, "a click or number key is deliberate");
            Assert.That(Store.LastChoice("test.talk", "ask"), Is.EqualTo("no"));
            Assert.That(Store.Facts, Is.Empty, "'no' has no effects");
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Closing), "an empty next ends the talk");
            Assert.That(_director.ConfirmChoice(0), Is.False);
        }

        [Test]
        public void IsChoiceReady_OnlyOnceTheAnswersWereUpAndInteractLeftAlone()
        {
            StartToOptions();
            Assert.That(_director.IsChoiceReady, Is.False);
            _director.Tick(ChoiceQuiet * 0.6f, false);
            Assert.That(_director.IsChoiceReady, Is.False);
            _director.Tick(0.01f, true);                           // a press too soon restarts the quiet
            _director.Tick(ChoiceQuiet * 0.6f, false);
            Assert.That(_director.IsChoiceReady, Is.False);
            _director.Tick(ChoiceQuiet * 0.5f, false);
            Assert.That(_director.IsChoiceReady, Is.True);
            _director.Tick(0.01f, true);
            Assert.That(Store.LastChoice("test.talk", "ask"), Is.EqualTo("yes"));
        }

        [Test]
        public void ATalkThatWouldSayNothing_IsNeitherOfferedNorStarted()
        {
            var gated = NewTalkable(Track(DialogueDefinition.CreateRuntime("Birdy", "test.gated", "gate",
                DialogueNode.CreateBranch("gate", new[] { FactCondition.Fact(FactConditionKind.FactEquals, "promised", FactValue.Of(true)) },
                                          "thanks", ""),
                DialogueNode.CreateLine("thanks", "You kept your promise!"))), new Vector3(1f, 0f, 2f));

            Assert.That(gated.CanTalk, Is.True, "the Talkable alone can't tell");
            Assert.That(_director.WouldSaySomething(gated), Is.False);
            Assert.That(_director.TryStart(gated, _player), Is.False);
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Idle));

            Store.SetFact("promised", FactValue.Of(true));
            Assert.That(_director.WouldSaySomething(gated), Is.True);
            Assert.That(_director.TryStart(gated, _player), Is.True);
        }

        [Test]
        public void ThePressThatRevealsTheLine_NeverAlsoPicks()
        {
            SetFloat(_config, "charactersPerSecond", 10f);
            Assert.That(_director.TryStart(_npc, _player), Is.True);
            _director.Tick(Settle + 0.01f, false);
            _director.Tick(TextDelay + 0.01f, false);

            _director.Tick(0.01f, true);            // shows the whole line; the options come up
            Assert.That(_director.IsChoosing, Is.True);
            for (int i = 0; i < 10; i++) _director.Tick(0.05f, true);   // mashing: every press restarts the lockout
            Assert.That(Store.Choices, Is.Empty);
        }

        // ---------------------------------------------------------- aborts

        [Test]
        public void Abort_NpcDisabledMidChoice_UnwindsAsBefore_AndRecordsNothing()
        {
            Time.timeScale = 0.8f;
            StartToOptions();
            PastLockout();

            _npc.gameObject.SetActive(false);
            _director.Tick(0.01f, true);    // the press arrives with the abort

            Assert.That(Store.Choices, Is.Empty);
            Assert.That(_factWrites, Is.Zero);
            Assert.That(Time.timeScale, Is.EqualTo(0.8f));
            Assert.That(_npc.LastEndReason, Is.EqualTo(ConversationEndReason.Aborted));
            Assert.That(_npc.IsAwaitingChoice, Is.False);
            Assert.That(_director.IsChoosing, Is.False);
            _director.Tick(0.01f, false);
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));
        }

        [Test]
        public void Abort_NpcKilledMidChoice_RecordsNothing()
        {
            var health = _npc.gameObject.AddComponent<Health>();
            health.ResetToFull();
            StartToOptions();
            PastLockout();

            health.Damage(health.Max * 10f, Vector3.zero);
            _director.Tick(0.01f, true);

            Assert.That(Store.Choices, Is.Empty);
            Assert.That(Store.Facts, Is.Empty);
            Assert.That(_npc.LastEndReason, Is.EqualTo(ConversationEndReason.Aborted));
        }

        [Test]
        public void Abort_DirectorDisabledMidChoice_UnwindsAtOnce_AndRecordsNothing()
        {
            Time.timeScale = 0.7f;
            StartToOptions();
            PastLockout();

            Lifecycle(_director, "OnDisable");

            Assert.That(Store.Choices, Is.Empty);
            Assert.That(_factWrites, Is.Zero);
            Assert.That(Time.timeScale, Is.EqualTo(0.7f));
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));
            Assert.That(_director.ConfirmChoice(0), Is.False);
        }

        // ---------------------------------------------------------- no store

        [Test]
        public void NoStore_TheTalkStillRuns_EffectsAreDropped_AndItWarnsOnce()
        {
            SetRef(_director, "choiceStore", null);
            LogAssert.Expect(LogType.Warning, new Regex("no choice store is wired"));

            for (int talk = 0; talk < 2; talk++)
            {
                StartToOptions();
                PastLockout();
                _director.Tick(0.01f, true);            // "Sure.": its effect has nowhere to go
                Assert.That(_director.CurrentText, Is.EqualTo("Thank you!"), "never 'You came back!'");
                _director.Tick(0.01f, true);
                for (int i = 0; i < 20; i++) _director.Tick(0.1f, false);
            }
            Assert.That(Store.Choices, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TheProviderField_RefusesAComponentThatIsNotAProvider()
        {
            // On an object with no provider at all (the director's own has the host).
            var other = NewHost("NotAStore").AddComponent<PlayerInputGate>();
            LogAssert.Expect(LogType.Warning, new Regex("is not an IChoiceStoreProvider"));
            SetRef(_director, "choiceStore", other);
            Lifecycle(_director, "OnValidate");      // a no-op if applying the field already ran it
            Assert.That(_director.Store, Is.Null);
        }

        [Test]
        public void TheProviderField_GivenTheHostsObject_TakesTheProviderOnIt()
        {
            var host = NewHost("StoreHost");
            host.AddComponent<PlayerInputGate>();               // the first MonoBehaviour, as a drag would pick
            var provider = MemoryStoreHost(host);
            SetRef(_director, "choiceStore", host.GetComponent<PlayerInputGate>());
            Lifecycle(_director, "OnValidate");
            Assert.That(_director.Store, Is.SameAs(provider.Store));
        }
    }
}
