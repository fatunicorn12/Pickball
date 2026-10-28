using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// The string contract between the setup menu that builds an NPC's
    /// Animator controller (<c>BirdyNpcSetup</c>) and the
    /// <see cref="NpcAnimatorDriver"/> that feeds it. Both read the names from
    /// here, so a rename cannot leave one side writing a parameter the other
    /// no longer has.
    ///
    /// Parameters:
    /// <list type="bullet">
    /// <item><see cref="Talking"/> (bool): in a conversation, from ConversationStarted to ConversationEnded.</item>
    /// <item><see cref="Speaking"/> (bool): a line is typing out (held for at least the config's minimum gesture time).</item>
    /// <item><see cref="Emote"/> (int): the current line's <see cref="DialogueEmote"/>.</item>
    /// <item><see cref="Dead"/> (bool): the NPC's Health has died. Nothing leaves the Death state.</item>
    /// <item><see cref="IdleVariant"/> (int): which idle the idle loop moves to next; 0 is the main idle.</item>
    /// </list>
    /// </summary>
    public static class NpcAnimatorParameters
    {
        /// <summary>Bool: the NPC is in a conversation.</summary>
        public const string Talking = "Talking";
        /// <summary>Bool: the current line is typing out.</summary>
        public const string Speaking = "Speaking";
        /// <summary>Int: the current line's <see cref="DialogueEmote"/>.</summary>
        public const string Emote = "Emote";
        /// <summary>Bool: the NPC is dead.</summary>
        public const string Dead = "Dead";
        /// <summary>Int: the idle variant to move to after the current idle loop.</summary>
        public const string IdleVariant = "IdleVariant";

        /// <summary>Name of the base layer the controller is built on.</summary>
        public const string BaseLayer = "Base Layer";
        /// <summary>Conversation idle: between lines, and before the first one.</summary>
        public const string TalkIdleState = "TalkIdle";
        /// <summary>The one-shot death, held on its last frame.</summary>
        public const string DeathState = "Death";
        /// <summary>Most idle variants a controller may have; the driver probes no further.</summary>
        public const int MaxIdleVariants = 8;

        /// <summary>Hash of <see cref="Talking"/>.</summary>
        public static readonly int TalkingHash = Animator.StringToHash(Talking);
        /// <summary>Hash of <see cref="Speaking"/>.</summary>
        public static readonly int SpeakingHash = Animator.StringToHash(Speaking);
        /// <summary>Hash of <see cref="Emote"/>.</summary>
        public static readonly int EmoteHash = Animator.StringToHash(Emote);
        /// <summary>Hash of <see cref="Dead"/>.</summary>
        public static readonly int DeadHash = Animator.StringToHash(Dead);
        /// <summary>Hash of <see cref="IdleVariant"/>.</summary>
        public static readonly int IdleVariantHash = Animator.StringToHash(IdleVariant);

        /// <summary>State name of idle variant <paramref name="variant"/>: "Idle", then "Idle_1", "Idle_2"…</summary>
        public static string IdleStateName(int variant) => variant <= 0 ? "Idle" : "Idle_" + variant;

        /// <summary>State name for an emote's talk gesture: "Talk_Talk", "Talk_Excited", "Talk_Thinking".
        /// Matches the clip file prefix for that emote (<see cref="NpcClipRoles"/>).</summary>
        public static string TalkStateName(DialogueEmote emote) => "Talk_" + emote;

        /// <summary>Full-path hash of a base-layer state, as
        /// <see cref="AnimatorStateInfo.fullPathHash"/> reports it.</summary>
        public static int FullPathHash(string stateName) => Animator.StringToHash(BaseLayer + "." + stateName);
    }
}
