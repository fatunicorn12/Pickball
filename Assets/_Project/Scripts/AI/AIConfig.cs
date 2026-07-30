using UnityEngine;

namespace Pickleball
{
    /// <summary>All tunable AI opponent behaviour values.</summary>
    [CreateAssetMenu(menuName = "Pickleball/Config/AI Config", fileName = "AIConfig")]
    public class AIConfig : ScriptableObject
    {
        [Header("Difficulty")]
        [Tooltip("0 = easy (misses often, slow), 1 = hard (rarely misses, fast). Scales the values below.")]
        [Range(0f, 1f)] public float difficulty = 0.6f;

        [Header("Reaction")]
        [Tooltip("Seconds the AI waits after a landing position is known before it starts moving.")]
        public float reactionDelay = 0.25f;

        [Header("Movement")]
        [Tooltip("Lateral move speed (m/s) toward the projected landing spot.")]
        public float moveSpeed = 5f;

        [Tooltip("How close (m) the AI must be to the target X to count as 'in position'.")]
        public float positionTolerance = 0.3f;

        [Header("Accuracy")]
        [Tooltip("Max random sideways error (m) added to where the AI tries to stand. Reduced by difficulty.")]
        public float accuracyOffset = 1.2f;

        [Tooltip("Base chance (0-1) to whiff a returnable ball. Reduced by difficulty.")]
        [Range(0f, 1f)] public float baseMissChance = 0.35f;

        [Header("Return Shot")]
        public Vector2 returnPowerRange = new Vector2(4.5f, 6.5f);
        public float returnArcLift = 4.5f;

        [Tooltip("Max random horizontal aim (m/s) the AI adds to keep rallies varied.")]
        public float aimRandomness = 2f;

        /// <summary>Effective miss chance after difficulty scaling.</summary>
        public float EffectiveMissChance => Mathf.Lerp(baseMissChance, 0.02f, difficulty);

        /// <summary>Effective standing error after difficulty scaling.</summary>
        public float EffectiveAccuracyOffset => Mathf.Lerp(accuracyOffset, 0.1f, difficulty);

        /// <summary>Effective reaction delay after difficulty scaling.</summary>
        public float EffectiveReactionDelay => Mathf.Lerp(reactionDelay, 0.05f, difficulty);
    }
}
