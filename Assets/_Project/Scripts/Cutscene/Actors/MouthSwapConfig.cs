using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Thresholds for <see cref="MouthSwap"/>: at what voice openness the painted mouth goes half
    /// and fully open, and the lower levels at which it falls back, so a level hovering at a
    /// threshold doesn't flicker between shapes. Openness is <see cref="MouthFlap.Openness"/>
    /// (0..1, already smoothed by the flap config's attack and release).
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Mouth Swap Config", fileName = "MouthSwapConfig")]
    public sealed class MouthSwapConfig : ScriptableObject
    {
        [Header("Opening (openness 0..1)")]
        [Tooltip("Openness (0..1) at which a closed mouth goes half-open. Lower makes quiet speech " +
                 "move the mouth; higher keeps it shut on breaths and soft consonants.")]
        [Range(0f, 1f)]
        [SerializeField] private float halfOpenAt = 0.18f;

        [Tooltip("Openness (0..1) at which the mouth goes fully open (loud vowels).")]
        [Range(0f, 1f)]
        [SerializeField] private float openAt = 0.55f;

        [Header("Closing (hysteresis, openness 0..1)")]
        [Tooltip("Openness (0..1) below which an open or half mouth closes. Keep it under " +
                 "halfOpenAt: the gap is what stops flicker at the threshold.")]
        [Range(0f, 1f)]
        [SerializeField] private float closeBelow = 0.10f;

        [Tooltip("Openness (0..1) below which an open mouth drops to half. Keep it under openAt.")]
        [Range(0f, 1f)]
        [SerializeField] private float halfBelow = 0.40f;

        [Header("Timing")]
        [Tooltip("Shortest time a shape stays on screen once shown, in seconds. 0.06 s is two " +
                 "frames at 30 fps: long enough not to strobe, short enough to catch syllables.")]
        [Min(0f)]
        [SerializeField] private float minHoldSeconds = 0.06f;

        /// <summary>Closed → half threshold.</summary>
        public float HalfOpenAt => halfOpenAt;

        /// <summary>→ open threshold.</summary>
        public float OpenAt => openAt;

        /// <summary>→ closed threshold (hysteresis).</summary>
        public float CloseBelow => Mathf.Min(closeBelow, halfOpenAt);

        /// <summary>Open → half threshold (hysteresis).</summary>
        public float HalfBelow => Mathf.Min(halfBelow, openAt);

        /// <summary>Minimum hold per shape, seconds.</summary>
        public float MinHoldSeconds => minHoldSeconds;
    }
}
