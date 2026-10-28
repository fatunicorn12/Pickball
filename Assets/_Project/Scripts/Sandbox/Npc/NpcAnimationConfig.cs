using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// Presentation tunables for talking NPCs: how they turn toward whoever
    /// they are talking to, how gestures and idles are timed, and the blend
    /// times the setup menu bakes into their Animator controllers.
    ///
    /// One asset can serve every NPC. The setup menu seeds it once and never
    /// overwrites it, so hand-tuning survives a re-run. Fields under
    /// "Animator controller" are read by <c>Rebuild Birdy Animator</c>; re-run
    /// it after changing them. Everything else is read live every frame.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/NPC Animation Config", fileName = "NpcAnimationConfig")]
    public class NpcAnimationConfig : ScriptableObject
    {
        [Header("Facing the player")]
        [Tooltip("How fast the NPC turns toward the player once a conversation starts, 1/s " +
                 "(exponential). 6 settles in about half a second; higher snaps round.")]
        [SerializeField, Min(0.1f)] private float turnRate = 6f;

        [Tooltip("Top turning speed, °/s. Caps the first moments of a large turn so a " +
                 "180° about-face reads as a pivot, not a snap. 0 = no cap.")]
        [SerializeField, Min(0f)] private float maxTurnSpeed = 300f;

        [Tooltip("After a conversation, turn back to the facing the NPC was placed with once " +
                 "the player has walked away. Off: it keeps facing wherever the player last stood.")]
        [SerializeField] private bool returnToRestFacing = true;

        [Tooltip("After a conversation the NPC keeps turning to watch the player while they " +
                 "stay within this distance, m, and turns back to its rest facing once they " +
                 "are further. Turning away while the player still stands there feels rude.")]
        [SerializeField, Min(0f)] private float restReturnDistance = 4f;

        [Tooltip("How fast the NPC turns back to its rest facing, 1/s (exponential). Slower " +
                 "than the turn toward the player, so it drifts back to what it was doing.")]
        [SerializeField, Min(0.1f)] private float restTurnRate = 2.5f;

        [Header("Talking")]
        [Tooltip("A line's gesture plays at least this long, s (unscaled), even if the " +
                 "player skips the typing. Below ~0.3 a skipped line shows no gesture at all.")]
        [SerializeField, Min(0f)] private float minGestureSeconds = 0.8f;

        [Header("Death")]
        [Tooltip("How far the model is eased up over the death clip, m, so the body ends lying " +
                 "on the ground instead of in it (Mixamo deaths are made for a thinner body). " +
                 "RE-TUNE THIS FOR EVERY NEW DEATH CLIP: read the 'death sink' line of Tools ▸ " +
                 "Pickleball ▸ Diagnostics ▸ Report Birdy Poses, and look at the end pose: lifting " +
                 "the big helmet fully clear floats the belly, so pick the value where the body " +
                 "rests on the ground. 0 = no lift.")]
        [SerializeField, Min(0f)] private float deathGroundLift = 0.06f;

        [Tooltip("Normalised time in the death clip at which the lift starts easing in (it is " +
                 "complete at the clip's end). Set it to where the body starts to go down, so " +
                 "the standing part of the death does not float. Re-tune per death clip.")]
        [SerializeField, Range(0f, 1f)] private float deathLiftStart = 0.6f;

        [Header("Idling")]
        [Tooltip("With several idle clips: chance, after each loop of the main idle, that it " +
                 "loops again instead of playing a fidget. 1 = never fidget; 0 = fidget " +
                 "every other loop.")]
        [SerializeField, Range(0f, 1f)] private float mainIdleChance = 0.6f;

        [Header("Animator controller (baked by Rebuild Birdy Animator)")]
        [Tooltip("Cross-fade into a talk gesture when a line starts, s.")]
        [SerializeField, Min(0f)] private float talkBlend = 0.2f;

        [Tooltip("Cross-fade from a gesture back to the listening idle once the line is shown, " +
                 "and from idle into listening when a conversation starts, s.")]
        [SerializeField, Min(0f)] private float settleBlend = 0.35f;

        [Tooltip("Cross-fade between idle variants, and from a conversation back to idle, s.")]
        [SerializeField, Min(0f)] private float idleBlend = 0.3f;

        [Tooltip("Cross-fade into the death clip, s. Short: death should land as a beat.")]
        [SerializeField, Min(0f)] private float deathBlend = 0.12f;

        [Tooltip("Normalised time in an idle loop at which it may cross-fade to the next idle " +
                 "variant. Just under 1 so the blend overlaps the loop seam.")]
        [SerializeField, Range(0.5f, 1f)] private float idleExitTime = 0.92f;

        [Tooltip("Playback speed of the Talk state when the NPC has no talk clip and it falls " +
                 "back to the idle. A little faster than idle reads as animated speech.")]
        [SerializeField, Range(0.5f, 2f)] private float fallbackTalkSpeed = 1.2f;

        [Tooltip("Same, for the Excited emote falling back to the idle.")]
        [SerializeField, Range(0.5f, 2f)] private float fallbackExcitedSpeed = 1.5f;

        [Tooltip("Same, for the Thinking emote falling back to the idle. Slower reads as musing.")]
        [SerializeField, Range(0.5f, 2f)] private float fallbackThinkingSpeed = 0.8f;

        /// <summary>Turn rate toward a conversation partner, 1/s.</summary>
        public float TurnRate => turnRate;
        /// <summary>Turn speed cap, °/s; 0 = uncapped.</summary>
        public float MaxTurnSpeed => maxTurnSpeed;
        /// <summary>Whether the NPC turns back to its placed facing after talking.</summary>
        public bool ReturnToRestFacing => returnToRestFacing;
        /// <summary>Distance beyond which the NPC stops watching the player and turns back, m.</summary>
        public float RestReturnDistance => restReturnDistance;
        /// <summary>How far the model is eased up by the end of the death clip, m.</summary>
        public float DeathGroundLift => deathGroundLift;
        /// <summary>Normalised death-clip time at which the lift starts.</summary>
        public float DeathLiftStart => deathLiftStart;
        /// <summary>Turn rate back to the rest facing, 1/s.</summary>
        public float RestTurnRate => restTurnRate;
        /// <summary>Minimum gesture length per line, s (unscaled).</summary>
        public float MinGestureSeconds => minGestureSeconds;
        /// <summary>Chance the main idle repeats instead of fidgeting, 0–1.</summary>
        public float MainIdleChance => mainIdleChance;
        /// <summary>Blend into a talk gesture, s.</summary>
        public float TalkBlend => talkBlend;
        /// <summary>Blend from a gesture to listening, and idle to listening, s.</summary>
        public float SettleBlend => settleBlend;
        /// <summary>Blend between idles and from a conversation back to idle, s.</summary>
        public float IdleBlend => idleBlend;
        /// <summary>Blend into death, s.</summary>
        public float DeathBlend => deathBlend;
        /// <summary>Normalised exit time of an idle loop toward the next variant.</summary>
        public float IdleExitTime => idleExitTime;
        /// <summary>Idle-fallback speed for the Talk emote.</summary>
        public float FallbackTalkSpeed => fallbackTalkSpeed;
        /// <summary>Idle-fallback speed for the Excited emote.</summary>
        public float FallbackExcitedSpeed => fallbackExcitedSpeed;
        /// <summary>Idle-fallback speed for the Thinking emote.</summary>
        public float FallbackThinkingSpeed => fallbackThinkingSpeed;
    }
}
