using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Npc;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// The file-name contract the Mixamo scout and the setup menu share: which
    /// prefix makes a clip an idle, a talk gesture or a death, and which clip a
    /// talk emote plays when the NPC has no clip of its own for it.
    /// </summary>
    public class NpcClipRolesTests
    {
        [TestCase("Idle.fbx", NpcClipRole.Idle)]
        [TestCase("Idle_Breathing.fbx", NpcClipRole.Idle)]
        [TestCase("idle happy.FBX", NpcClipRole.Idle)]
        [TestCase("Talk_Talking.fbx", NpcClipRole.Talk)]
        [TestCase("Talk_Talk (1).fbx", NpcClipRole.Talk)]
        [TestCase("Talk_Excited_Wave.fbx", NpcClipRole.TalkExcited)]
        [TestCase("Talk_Thinking.fbx", NpcClipRole.TalkThinking)]
        [TestCase("Death_FallingFlatImpact.fbx", NpcClipRole.Death)]
        [TestCase("Assets/_Project/Animation/Birdy/Death.fbx", NpcClipRole.Death)]
        [TestCase(@"Assets\_Project\Animation\Birdy\Talk_Excited.fbx", NpcClipRole.TalkExcited)]
        public void TryGetRole_ReadsThePrefix(string file, NpcClipRole expected)
        {
            Assert.That(NpcClipRoles.TryGetRole(file, out var role), Is.True);
            Assert.That(role, Is.EqualTo(expected));
        }

        [TestCase("Talk_Wave.fbx")]
        [TestCase("Walking.fbx")]
        [TestCase("Idle.fbx.meta")]
        [TestCase("Idle.anim")]
        [TestCase("MyIdle.fbx")]
        [TestCase("")]
        [TestCase(null)]
        public void TryGetRole_IgnoresUnknownFiles(string file)
        {
            Assert.That(NpcClipRoles.TryGetRole(file, out _), Is.False);
        }

        [Test]
        public void NoPrefixIsAPrefixOfAnother()
        {
            // Otherwise a file could match two roles and the first one listed
            // would silently win.
            var prefixes = new List<string>();
            foreach (var role in NpcClipRoles.All) prefixes.Add(NpcClipRoles.FilePrefix(role).ToLowerInvariant());
            for (int i = 0; i < prefixes.Count; i++)
                for (int j = 0; j < prefixes.Count; j++)
                    if (i != j)
                        Assert.That(prefixes[j].StartsWith(prefixes[i]), Is.False,
                                    $"'{prefixes[i]}' is a prefix of '{prefixes[j]}'");
        }

        [TestCase(DialogueEmote.Talk)]
        [TestCase(DialogueEmote.Excited)]
        [TestCase(DialogueEmote.Thinking)]
        public void TalkStateName_MatchesTheEmotesFilePrefix(DialogueEmote emote)
        {
            // The controller's state names and the clip file prefixes are the
            // same strings, so a folder listing reads like the state machine.
            Assert.That(NpcAnimatorParameters.TalkStateName(emote),
                        Is.EqualTo(NpcClipRoles.FilePrefix(NpcClipRoles.TalkRole(emote))));
        }

        [Test]
        public void ResolveTalk_PrefersTheEmotesOwnClip()
        {
            Assert.That(NpcClipRoles.ResolveTalk(DialogueEmote.Excited, _ => true, out var role), Is.True);
            Assert.That(role, Is.EqualTo(NpcClipRole.TalkExcited));
        }

        [TestCase(DialogueEmote.Excited)]
        [TestCase(DialogueEmote.Thinking)]
        public void ResolveTalk_FallsBackToPlainTalk(DialogueEmote emote)
        {
            bool Has(NpcClipRole r) => r == NpcClipRole.Talk || r == NpcClipRole.Idle;
            Assert.That(NpcClipRoles.ResolveTalk(emote, Has, out var role), Is.True);
            Assert.That(role, Is.EqualTo(NpcClipRole.Talk));
        }

        [TestCase(DialogueEmote.Talk)]
        [TestCase(DialogueEmote.Excited)]
        [TestCase(DialogueEmote.Thinking)]
        public void ResolveTalk_FallsBackToTheIdleWhenThereAreNoTalkClips(DialogueEmote emote)
        {
            bool Has(NpcClipRole r) => r == NpcClipRole.Idle || r == NpcClipRole.Death;
            Assert.That(NpcClipRoles.ResolveTalk(emote, Has, out var role), Is.True);
            Assert.That(role, Is.EqualTo(NpcClipRole.Idle));
        }

        [Test]
        public void ResolveTalk_FailsOnlyWithNothingToPlay()
        {
            bool Has(NpcClipRole r) => r == NpcClipRole.Death;
            Assert.That(NpcClipRoles.ResolveTalk(DialogueEmote.Talk, Has, out _), Is.False);
        }
    }
}
