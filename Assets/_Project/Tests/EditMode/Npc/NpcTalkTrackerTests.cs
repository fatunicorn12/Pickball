using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Npc;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// What the NPC's body does through a conversation: listen, gesture each
    /// line with its emote, settle, and die for good. Time is passed in
    /// explicitly (seconds of unscaled time), as the driver does while the
    /// game is paused.
    /// </summary>
    public class NpcTalkTrackerTests
    {
        const float MinGesture = 0.8f;

        [Test]
        public void Fresh_IsIdle()
        {
            var t = new NpcTalkTracker(MinGesture);
            Assert.That(t.Talking, Is.False);
            Assert.That(t.Dead, Is.False);
            Assert.That(t.IsSpeaking(0f), Is.False);
        }

        [Test]
        public void ConversationStart_ListensWithoutGesturing()
        {
            var t = new NpcTalkTracker(MinGesture);
            t.BeginConversation();
            Assert.That(t.Talking, Is.True);
            Assert.That(t.IsSpeaking(0f), Is.False);
        }

        [Test]
        public void Line_GesturesWithItsEmoteUntilRevealed()
        {
            var t = new NpcTalkTracker(MinGesture);
            t.BeginConversation();
            t.BeginLine(DialogueEmote.Excited, 10f);

            Assert.That(t.Emote, Is.EqualTo(DialogueEmote.Excited));
            Assert.That(t.IsSpeaking(10f), Is.True);
            Assert.That(t.IsSpeaking(13f), Is.True, "still typing after the minimum");

            t.RevealLine();
            Assert.That(t.IsSpeaking(13f), Is.False, "a long line settles the moment it is shown");
        }

        [Test]
        public void SkippedLine_StillGesturesForTheMinimum()
        {
            var t = new NpcTalkTracker(MinGesture);
            t.BeginConversation();
            t.BeginLine(DialogueEmote.Thinking, 5f);
            t.RevealLine();                         // skipped in the same frame

            Assert.That(t.IsSpeaking(5f), Is.True);
            Assert.That(t.IsSpeaking(5f + MinGesture - 0.01f), Is.True);
            Assert.That(t.IsSpeaking(5f + MinGesture + 0.01f), Is.False);
        }

        [Test]
        public void ZeroMinimum_SettlesOnReveal()
        {
            var t = new NpcTalkTracker(0f);
            t.BeginConversation();
            t.BeginLine(DialogueEmote.Talk, 1f);
            t.RevealLine();
            Assert.That(t.IsSpeaking(1f), Is.False);
        }

        [Test]
        public void NextLine_TakesOverTheEmote()
        {
            var t = new NpcTalkTracker(MinGesture);
            t.BeginConversation();
            t.BeginLine(DialogueEmote.Excited, 0f);
            t.RevealLine();
            t.BeginLine(DialogueEmote.Thinking, 2f);
            Assert.That(t.Emote, Is.EqualTo(DialogueEmote.Thinking));
            Assert.That(t.IsSpeaking(2f), Is.True);
        }

        [Test]
        public void End_StopsEverything()
        {
            var t = new NpcTalkTracker(MinGesture);
            t.BeginConversation();
            t.BeginLine(DialogueEmote.Talk, 0f);
            t.EndConversation();
            Assert.That(t.Talking, Is.False);
            Assert.That(t.IsSpeaking(0f), Is.False, "the minimum gesture does not outlive the conversation");
        }

        [Test]
        public void LineWithoutStart_ImpliesAConversation()
        {
            // A script reload mid-conversation loses ConversationStarted.
            var t = new NpcTalkTracker(MinGesture);
            t.BeginLine(DialogueEmote.Talk, 0f);
            Assert.That(t.Talking, Is.True);
            Assert.That(t.IsSpeaking(0f), Is.True);
        }

        [Test]
        public void Death_OutranksTalkingAndIsPermanent()
        {
            var t = new NpcTalkTracker(MinGesture);
            t.BeginConversation();
            t.BeginLine(DialogueEmote.Excited, 0f);
            t.Die();
            Assert.That(t.Dead, Is.True);
            Assert.That(t.IsSpeaking(0f), Is.False);

            t.EndConversation();
            t.BeginConversation();
            Assert.That(t.Dead, Is.True);
            Assert.That(t.IsSpeaking(0f), Is.False);
        }
    }
}
