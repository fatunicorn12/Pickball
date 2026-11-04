using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// The character's combat voice as data: the attack grunts by tier, the finisher
    /// yells and the hurt sounds, plus how they play. See Docs/Systems/CombatVoice.md.
    /// Which tier a swing gets is <see cref="GruntLadder"/>'s business, not this asset's.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatVoiceBank", menuName = "Pickleball/Combat/Combat Voice Bank")]
    public class CombatVoiceBank : ScriptableObject
    {
        [Header("Attack grunts")]
        [Tooltip("Early hits of a chain.")]
        [SerializeField] private CombatVoiceGroup light = new(null);
        [Tooltip("The middle of a chain, and a chain's only ordinary hit.")]
        [SerializeField] private CombatVoiceGroup medium = new(null);
        [Tooltip("The hits leading into the finisher.")]
        [SerializeField] private CombatVoiceGroup heavy = new(null);

        [Header("Finishers")]
        [Tooltip("A finisher step always draws from here, however long the chain was.")]
        [SerializeField] private CombatVoiceGroup finisher = new(null);

        [Header("Hurt")]
        [Tooltip("Played when the player takes a discrete hit. Continuous damage, such as " +
                 "standing in a beam, stays silent.")]
        [SerializeField] private CombatVoiceGroup hurt = new(null);

        [Tooltip("Seconds before another hurt sound can play. Higher keeps a flurry of " +
                 "hits from stuttering; lower answers every hit.")]
        [Min(0f)] [SerializeField] private float hurtMinIntervalSeconds = 0.4f;

        [Header("Playback")]
        [Tooltip("Random pitch change per clip, as a fraction: 0.03 plays between 97% and " +
                 "103%. Higher hides repeats but starts to change who is speaking.")]
        [Range(0f, 0.2f)] [SerializeField] private float pitchVariation = 0.03f;

        [Tooltip("Seconds a playing sound takes to fade when the next one cuts it off. " +
                 "He has one voice, so sounds never overlap. Too low clicks; too high smears " +
                 "fast hits together.")]
        [Range(0.005f, 0.2f)] [SerializeField] private float handoffFadeSeconds = 0.03f;

        public CombatVoiceGroup Hurt => hurt;
        public float HurtMinIntervalSeconds => hurtMinIntervalSeconds;
        public float PitchVariation => pitchVariation;
        public float HandoffFadeSeconds => handoffFadeSeconds;

        /// <summary>The clips and volume for an attack tier.</summary>
        public CombatVoiceGroup GroupFor(GruntTier tier) => tier switch
        {
            GruntTier.Light => light,
            GruntTier.Medium => medium,
            GruntTier.Heavy => heavy,
            _ => finisher,
        };
    }
}
