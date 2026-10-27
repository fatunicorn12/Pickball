using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// The timing of one swing, as fractions of its on-screen duration.
    ///
    /// Fractions rather than seconds so a step's playback speed can be retuned
    /// without moving the strike out of its hit window: the Mixamo swings peak at
    /// 40-53% of their length whatever speed they are played at.
    /// </summary>
    [Serializable]
    public struct SwingWindows
    {
        [Tooltip("Fraction of the swing (0-1) at which the blade starts hitting. " +
                 "Earlier feels snappier but lets the wind-up deal damage.")]
        [Range(0f, 1f)] [SerializeField] private float activeStart;

        [Tooltip("Fraction of the swing (0-1) at which the blade stops hitting.")]
        [Range(0f, 1f)] [SerializeField] private float activeEnd;

        [Tooltip("Earliest fraction (0-1) at which a buffered press chains into " +
                 "the next step. Opening this before the blade finishes is what " +
                 "makes the chain feel responsive rather than turn-based.")]
        [Range(0f, 1f)] [SerializeField] private float comboOpen;

        [Tooltip("Fraction (0-1) after which the chain is dropped and the swing " +
                 "is treated as over. The rest of the clip plays out while the " +
                 "Attack layer fades.")]
        [Range(0f, 1f)] [SerializeField] private float comboClose;

        public float ActiveStart => activeStart;
        public float ActiveEnd => activeEnd;
        public float ComboOpen => comboOpen;
        public float ComboClose => comboClose;

        public SwingWindows(float activeStart, float activeEnd, float comboOpen, float comboClose)
        {
            this.activeStart = Mathf.Clamp01(activeStart);
            this.activeEnd = Mathf.Clamp01(activeEnd);
            this.comboOpen = Mathf.Clamp01(comboOpen);
            this.comboClose = Mathf.Clamp01(comboClose);
        }

        /// <summary>Windows that suit the sword-and-shield pack: long wind-up,
        /// blade live through the middle, chain opening at the half.</summary>
        public static SwingWindows Default => new(0.34f, 0.62f, 0.5f, 0.92f);
    }
}
