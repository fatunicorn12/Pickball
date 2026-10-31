using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Tunables for <see cref="MouthFlap"/>: how loud counts as talking, how fast the beak or
    /// jaw follows the voice, and how far it moves. One asset per character temperament (Birdy
    /// flaps bigger and faster than MC).
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Mouth Flap Config", fileName = "MouthFlapConfig")]
    public sealed class MouthFlapConfig : ScriptableObject
    {
        [Header("Level")]
        [Tooltip("RMS of the voice output (linear, 0..1) at or below which the mouth stays shut. " +
                 "Raise it if breaths and room tone make the beak chatter.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float noiseFloor = 0.01f;

        [Tooltip("RMS (linear, 0..1) that opens the mouth fully. Lower makes quiet lines flap " +
                 "harder; ElevenLabs dialogue peaks around 0.1–0.2.")]
        [Range(0.001f, 1f)]
        [SerializeField] private float fullOpen = 0.12f;

        [Header("Response")]
        [Tooltip("Opening rate, 1/s. 30 ≈ open within a syllable's attack (~30 ms); lower " +
                 "looks lazy and late.")]
        [Min(0f)]
        [SerializeField] private float attackRate = 30f;

        [Tooltip("Closing rate, 1/s. Lower than the attack so the mouth doesn't buzz between " +
                 "syllables; 12 ≈ closed in 80 ms.")]
        [Min(0f)]
        [SerializeField] private float releaseRate = 12f;

        [Header("Pose")]
        [Tooltip("Head nod at full openness, in degrees about the character's right axis " +
                 "(positive tips the head down). Neither rig has a jaw or beak bone, so the whole " +
                 "head nods; 4–8° reads as talking without looking like agreement.")]
        [Range(-30f, 30f)]
        [SerializeField] private float maxAngle = 6f;

        [Header("Analysis")]
        [Tooltip("Samples read from the voice each frame (a power of two, 64–8192). 256 at 48 kHz " +
                 "is 5 ms: enough to catch syllables without lagging them.")]
        [SerializeField] private int sampleCount = 256;

        /// <summary>RMS noise floor (linear).</summary>
        public float NoiseFloor => noiseFloor;

        /// <summary>RMS for full openness (linear).</summary>
        public float FullOpen => fullOpen;

        /// <summary>Opening rate, 1/s.</summary>
        public float AttackRate => attackRate;

        /// <summary>Closing rate, 1/s.</summary>
        public float ReleaseRate => releaseRate;

        /// <summary>Nod at full openness, degrees.</summary>
        public float MaxAngle => maxAngle;

        /// <summary>Samples per read, clamped to a power of two in [64, 8192].</summary>
        public int SampleCount => Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(64, sampleCount)), 64, 8192);
    }
}
