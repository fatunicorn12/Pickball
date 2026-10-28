using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// The Talkable contract: who can talk, the double-start guard, idempotent
    /// end, the end reason, the current line, and registry membership.
    /// </summary>
    public class TalkableTests : DialogueTestFixture
    {
        [Test]
        public void CanTalk_EnabledWithLines_IsTrue()
        {
            var talkable = NewTalkable(Definition("Hello."));
            Assert.That(talkable.CanTalk, Is.True);
        }

        [Test]
        public void CanTalk_NoDefinitionOrNoLines_IsFalse()
        {
            Assert.That(NewTalkable(null).CanTalk, Is.False);
            Assert.That(NewTalkable(Definition()).CanTalk, Is.False);
        }

        [Test]
        public void CanTalk_DisabledOrInactive_IsFalse()
        {
            var disabled = NewTalkable(Definition("Hi."));
            disabled.enabled = false;
            var inactive = NewTalkable(Definition("Hi."));
            inactive.gameObject.SetActive(false);

            Assert.That(disabled.CanTalk, Is.False);
            Assert.That(inactive.CanTalk, Is.False);
        }

        [Test]
        public void CanTalk_WithADeadHealthOnTheRoot_IsFalse_AndAliveIsTrue()
        {
            var talkable = NewTalkable(Definition("Hi."));
            var health = talkable.gameObject.AddComponent<Health>();
            // EditMode never runs Health.Awake, so Current starts at 0 (dead).
            Assert.That(talkable.CanTalk, Is.False);

            health.ResetToFull();
            Assert.That(talkable.CanTalk, Is.True);

            health.Damage(health.Max * 10f, Vector3.zero);
            Assert.That(health.IsDead, Is.True);
            Assert.That(talkable.CanTalk, Is.False);
        }

        [Test]
        public void CanTalk_WhileInConversation_IsFalse()
        {
            var talkable = NewTalkable(Definition("Hi."));
            talkable.NotifyConversationStarted(NewHost("Player").transform);
            Assert.That(talkable.CanTalk, Is.False);
        }

        [Test]
        public void Started_Twice_IsGuarded()
        {
            var talkable = NewTalkable(Definition("Hi."));
            var first = NewHost("Player").transform;
            int started = 0;
            talkable.ConversationStarted += _ => started++;

            talkable.NotifyConversationStarted(first);
            talkable.NotifyConversationStarted(NewHost("Other").transform);

            Assert.That(started, Is.EqualTo(1));
            Assert.That(talkable.Partner, Is.SameAs(first));
        }

        [Test]
        public void Ended_IsIdempotent_AndCarriesTheReason()
        {
            var talkable = NewTalkable(Definition("Hi."));
            int ended = 0;
            ConversationEndReason seen = ConversationEndReason.Aborted;
            talkable.ConversationEnded += t => { ended++; seen = t.LastEndReason; };

            talkable.NotifyConversationEnded();            // not started: ignored
            talkable.NotifyConversationStarted(NewHost("Player").transform);
            talkable.NotifyConversationEnded();
            talkable.NotifyConversationEnded(ConversationEndReason.Aborted);

            Assert.That(ended, Is.EqualTo(1));
            Assert.That(seen, Is.EqualTo(ConversationEndReason.Completed), "set before the event fires");
            Assert.That(talkable.IsInConversation, Is.False);
            Assert.That(talkable.Partner, Is.Null);
        }

        [Test]
        public void CurrentLine_IsSetBeforeLineStarted_AndClearedAtTheEnd()
        {
            var def = Track(DialogueDefinition.CreateRuntime("Birdy",
                new DialogueLine("Hello.", DialogueEmote.Talk),
                new DialogueLine("Big news!", DialogueEmote.Excited)));
            var talkable = NewTalkable(def);
            DialogueLine seenLine = default;
            int seenIndex = -2;
            talkable.LineStarted += (t, i) => { seenIndex = t.CurrentLineIndex; seenLine = t.CurrentLine; };

            Assert.That(talkable.CurrentLineIndex, Is.EqualTo(-1));
            talkable.NotifyConversationStarted(NewHost("Player").transform);
            talkable.NotifyLineStarted(1);

            Assert.That(seenIndex, Is.EqualTo(1));
            Assert.That(seenLine.Text, Is.EqualTo("Big news!"));
            Assert.That(seenLine.Emote, Is.EqualTo(DialogueEmote.Excited));

            talkable.NotifyConversationEnded();
            Assert.That(talkable.CurrentLineIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Disabled_MidConversation_EndsItAsAborted()
        {
            var talkable = NewTalkable(Definition("Hi."));
            talkable.NotifyConversationStarted(NewHost("Player").transform);

            Lifecycle(talkable, "OnDisable");

            Assert.That(talkable.IsInConversation, Is.False);
            Assert.That(talkable.LastEndReason, Is.EqualTo(ConversationEndReason.Aborted));
        }

        [Test]
        public void Registry_JoinedOnEnable_LeftOnDisable()
        {
            var set = NewSet();
            var talkable = NewTalkable(Definition("Hi."));
            SetRef(talkable, "registry", set);

            Lifecycle(talkable, "OnEnable");
            Lifecycle(talkable, "OnEnable");   // a reload re-runs it: still one entry
            Assert.That(set.Count, Is.EqualTo(1));
            Assert.That(set[0], Is.SameAs(talkable));

            Lifecycle(talkable, "OnDisable");
            Assert.That(set.Count, Is.EqualTo(0));
        }

        [Test]
        public void TalkableSet_AddIsOnce_RemoveIsSafe()
        {
            var set = NewSet();
            var talkable = NewTalkable(Definition("Hi."));

            set.Add(talkable);
            set.Add(talkable);
            set.Add(null);
            Assert.That(set.Count, Is.EqualTo(1));

            set.Remove(talkable);
            set.Remove(talkable);
            Assert.That(set.Count, Is.EqualTo(0));
        }

        [Test]
        public void DisplayName_FallsBackToTheObjectName()
        {
            var named = NewTalkable(Definition("Hi."));
            var unnamed = NewTalkable(Track(DialogueDefinition.CreateRuntime("", new DialogueLine("Hi."))));

            Assert.That(named.DisplayName, Is.EqualTo("Birdy"));
            Assert.That(unnamed.DisplayName, Is.EqualTo(unnamed.gameObject.name));
        }
    }
}
