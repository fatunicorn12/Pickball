using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Npc;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// The driver against a real Talkable and Health: the Animator keeps
    /// animating while the conversation pauses time (UnscaledTime, restored
    /// afterwards, also on disable), emotes come from the dialogue asset,
    /// death sticks, and the NPC turns to its partner on unscaled time.
    ///
    /// No controller is assigned, so these cover the event wiring, update
    /// mode and facing, not the state machine itself (that is built by the
    /// setup menu and checked in the shared Editor).
    ///
    /// EditMode runs no lifecycle: OnEnable/OnDisable are invoked by
    /// reflection, and Health is filled with ResetToFull (Awake never runs,
    /// and an unfilled Health reads as dead).
    /// </summary>
    public class NpcAnimatorDriverTests : BossTestFixture
    {
        readonly List<Object> _extra = new();

        Health _health;
        Talkable _talkable;
        Animator _animator;
        NpcAnimatorDriver _driver;
        Transform _partner;
        DialogueDefinition _dialogue;

        [SetUp]
        public void Build()
        {
            _health = NewComponent<Health>();
            SetTunable(_health, "maxHealth", 40f);
            _health.ResetToFull();
            var root = _health.gameObject;

            _dialogue = DialogueDefinition.CreateRuntime("Birdy",
                new DialogueLine("one", DialogueEmote.Excited),
                new DialogueLine("two", DialogueEmote.Thinking));
            _extra.Add(_dialogue);

            _talkable = root.AddComponent<Talkable>();
            var so = new SerializedObject(_talkable);
            so.FindProperty("dialogue").objectReferenceValue = _dialogue;
            so.ApplyModifiedPropertiesWithoutUndo();

            var model = new GameObject("Model") { hideFlags = HideFlags.DontSave };
            model.transform.SetParent(root.transform, false);
            _animator = model.AddComponent<Animator>();
            _animator.updateMode = AnimatorUpdateMode.Fixed;

            _driver = root.AddComponent<NpcAnimatorDriver>();
            Invoke(_driver, "OnEnable");

            var partner = new GameObject("Partner") { hideFlags = HideFlags.DontSave };
            _extra.Add(partner);
            _partner = partner.transform;
            _partner.position = new Vector3(5f, 0f, 0f);     // due +X: yaw 90
        }

        [TearDown]
        public void DestroyExtras()
        {
            foreach (var o in _extra) if (o != null) Object.DestroyImmediate(o);
            _extra.Clear();
        }

        static void Invoke(Object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        void TickFrames(int frames, float unscaledDt, float scaledDt)
        {
            for (int i = 0; i < frames; i++) _driver.Tick(i * unscaledDt, unscaledDt, 0f, scaledDt);
        }

        [Test]
        public void Conversation_RunsTheAnimatorOnUnscaledTimeThenRestoresIt()
        {
            _talkable.NotifyConversationStarted(_partner);
            Assert.That(_animator.updateMode, Is.EqualTo(AnimatorUpdateMode.UnscaledTime));
            Assert.That(_driver.IsTalking, Is.True);

            _talkable.NotifyConversationEnded();
            Assert.That(_animator.updateMode, Is.EqualTo(AnimatorUpdateMode.Fixed),
                        "restores the mode it found, not a hard-coded Normal");
            Assert.That(_driver.IsTalking, Is.False);
        }

        [Test]
        public void SecondStart_DoesNotOverwriteTheSavedMode()
        {
            _talkable.NotifyConversationStarted(_partner);
            _talkable.NotifyConversationStarted(_partner);   // guarded by Talkable; the driver is guarded too
            _talkable.NotifyConversationEnded();
            Assert.That(_animator.updateMode, Is.EqualTo(AnimatorUpdateMode.Fixed));
        }

        [Test]
        public void AbortedConversation_RestoresTheMode()
        {
            _talkable.NotifyConversationStarted(_partner);
            _talkable.NotifyLineStarted(0);
            Invoke(_talkable, "OnDisable");                  // Talkable ends a running talk as Aborted
            Assert.That(_talkable.LastEndReason, Is.EqualTo(ConversationEndReason.Aborted));
            Assert.That(_animator.updateMode, Is.EqualTo(AnimatorUpdateMode.Fixed));
            Assert.That(_driver.IsTalking, Is.False);
        }

        [Test]
        public void LineOutOfRange_GesturesPlainTalk()
        {
            _talkable.NotifyConversationStarted(_partner);
            _talkable.NotifyLineStarted(0);
            _talkable.NotifyLineStarted(7);
            Assert.That(_driver.CurrentEmote, Is.EqualTo(DialogueEmote.Talk));
        }

        [Test]
        public void DisableMidConversation_RestoresTheMode()
        {
            _talkable.NotifyConversationStarted(_partner);
            Invoke(_driver, "OnDisable");
            Assert.That(_animator.updateMode, Is.EqualTo(AnimatorUpdateMode.Fixed));
        }

        [Test]
        public void ReEnableMidConversation_PicksTheConversationBackUp()
        {
            _talkable.NotifyConversationStarted(_partner);
            Invoke(_driver, "OnDisable");
            Invoke(_driver, "OnEnable");                     // as after a script reload
            Assert.That(_animator.updateMode, Is.EqualTo(AnimatorUpdateMode.UnscaledTime));
            Assert.That(_driver.IsTalking, Is.True);
        }

        [Test]
        public void LineStarted_ReadsTheLinesEmote()
        {
            _talkable.NotifyConversationStarted(_partner);
            _talkable.NotifyLineStarted(0);
            Assert.That(_driver.CurrentEmote, Is.EqualTo(DialogueEmote.Excited));
            _talkable.NotifyLineRevealed(0);
            _talkable.NotifyLineStarted(1);
            Assert.That(_driver.CurrentEmote, Is.EqualTo(DialogueEmote.Thinking));
        }

        [Test]
        public void Death_IsPermanent()
        {
            _health.Damage(1000f, Vector3.zero);
            Assert.That(_driver.IsDead, Is.True);
            _talkable.NotifyConversationStarted(_partner);
            _talkable.NotifyConversationEnded();
            Assert.That(_driver.IsDead, Is.True);
        }

        [Test]
        public void TurnsToThePartnerWhileTimeIsPaused()
        {
            _talkable.NotifyConversationStarted(_partner);
            TickFrames(90, 1f / 60f, 0f);                   // 1.5 s of real time, game time frozen
            float yaw = _driver.transform.eulerAngles.y;
            Assert.That(Mathf.DeltaAngle(yaw, 90f), Is.EqualTo(0f).Within(1f));
        }

        [Test]
        public void DoesNotTurnOnceDead()
        {
            _health.Damage(1000f, Vector3.zero);
            _talkable.NotifyConversationStarted(_partner);
            TickFrames(60, 1f / 60f, 0f);
            Assert.That(Mathf.DeltaAngle(_driver.transform.eulerAngles.y, 0f), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void TurnsBackToRestAfterTheDelay()
        {
            _talkable.NotifyConversationStarted(_partner);
            TickFrames(90, 1f / 60f, 0f);
            _talkable.NotifyConversationEnded();

            // Well past the return delay, on game time.
            const float dt = 1f / 60f;
            for (int i = 0; i < 300; i++) _driver.Tick(0f, dt, Time.time + 100f + i * dt, dt);
            Assert.That(Mathf.DeltaAngle(_driver.transform.eulerAngles.y, 0f), Is.EqualTo(0f).Within(1f));
        }
    }
}
