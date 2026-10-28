using System;
using System.Collections.Generic;
using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// The naming rules that turn an NPC's clip files into animator roles, and
    /// the fallback chain a talk emote follows when the NPC has no clip for it.
    ///
    /// Lives in the runtime assembly only so EditMode tests (which reference
    /// <c>Pickleball.Runtime</c> alone) can cover it; the setup menus are its
    /// only callers. Nothing here touches the AssetDatabase.
    ///
    /// File prefixes (case-insensitive, <c>.fbx</c> only):
    /// <c>Idle*</c>, <c>Talk_Talk*</c>, <c>Talk_Excited*</c>,
    /// <c>Talk_Thinking*</c>, <c>Death*</c>. No prefix is a prefix of
    /// another, so a file has at most one role.
    /// </summary>
    public static class NpcClipRoles
    {
        /// <summary>Every role, in the order setup menus report them.</summary>
        public static IReadOnlyList<NpcClipRole> All { get; } = new[]
        {
            NpcClipRole.Idle, NpcClipRole.Talk, NpcClipRole.TalkExcited,
            NpcClipRole.TalkThinking, NpcClipRole.Death,
        };

        /// <summary>The file-name prefix that marks a clip as <paramref name="role"/>.</summary>
        public static string FilePrefix(NpcClipRole role) => role switch
        {
            NpcClipRole.Idle => "Idle",
            NpcClipRole.Talk => "Talk_Talk",
            NpcClipRole.TalkExcited => "Talk_Excited",
            NpcClipRole.TalkThinking => "Talk_Thinking",
            NpcClipRole.Death => "Death",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

        /// <summary>
        /// The role a clip file plays, from its name. False for anything that
        /// is not an <c>.fbx</c> or carries no known prefix (a stray
        /// <c>Talk_Wave.fbx</c> is ignored, not guessed at).
        /// </summary>
        /// <param name="fileName">File name, with or without a directory.</param>
        public static bool TryGetRole(string fileName, out NpcClipRole role)
        {
            role = NpcClipRole.Idle;
            if (string.IsNullOrEmpty(fileName)) return false;

            int slash = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\'));
            string name = slash >= 0 ? fileName.Substring(slash + 1) : fileName;
            if (!name.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return false;

            foreach (var candidate in All)
            {
                if (!name.StartsWith(FilePrefix(candidate), StringComparison.OrdinalIgnoreCase)) continue;
                role = candidate;
                return true;
            }
            return false;
        }

        /// <summary>The clip role that delivers <paramref name="emote"/>.</summary>
        public static NpcClipRole TalkRole(DialogueEmote emote) => emote switch
        {
            DialogueEmote.Excited => NpcClipRole.TalkExcited,
            DialogueEmote.Thinking => NpcClipRole.TalkThinking,
            _ => NpcClipRole.Talk,
        };

        /// <summary>
        /// Which role's clip an emote's talk state plays: its own if the NPC
        /// has one, else the plain Talk gesture, else the main idle. False only
        /// when the NPC has none of those, in which case the state has nothing
        /// to play.
        ///
        /// Content picks the mood, and a character with fewer clips still plays
        /// something sensible (<see cref="DialogueEmote"/>'s contract).
        /// </summary>
        /// <param name="emote">The line's emote.</param>
        /// <param name="hasClip">Whether the NPC has at least one clip for a role.</param>
        /// <param name="role">The role whose clip to play.</param>
        public static bool ResolveTalk(DialogueEmote emote, Func<NpcClipRole, bool> hasClip,
                                       out NpcClipRole role)
        {
            role = TalkRole(emote);
            if (hasClip(role)) return true;
            role = NpcClipRole.Talk;
            if (hasClip(role)) return true;
            role = NpcClipRole.Idle;
            return hasClip(role);
        }
    }
}
