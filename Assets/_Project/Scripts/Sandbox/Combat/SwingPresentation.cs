using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// How a step looks and feels, as opposed to what it does. Read only by the
    /// presentation listeners (<c>SwordEffects</c>); gameplay never touches it,
    /// so restyling a slash can never change its damage or timing.
    /// </summary>
    [Serializable]
    public struct SwingPresentation
    {
        [Tooltip("Tint of the blade ribbon for this step. Alpha scales its " +
                 "opacity. Finishers read best in a colour the chain has not used.")]
        [SerializeField] private Color trailColor;

        [Tooltip("Camera shake on a connecting hit, in metres of jitter. ~0.15 " +
                 "for a light hit, ~0.3 for a finisher.")]
        [Min(0f)] [SerializeField] private float shakeAmount;

        [Tooltip("How long that shake lasts, in seconds.")]
        [Min(0f)] [SerializeField] private float shakeSeconds;

        [Tooltip("Field-of-view kick when the step starts, in degrees. 0 = none. " +
                 "A few degrees sells a finisher's wind-up.")]
        [SerializeField] private float fovPunch;

        [Tooltip("Optional effect spawned at each blade hit point, on top of the " +
                 "shared impact sparks — e.g. the '+' of + C. Pooled.")]
        [SerializeField] private GameObject hitEffect;

        [Tooltip("Optional effect played when the step's area burst is released, " +
                 "scaled to the burst radius — e.g. the Riemann ring. Pooled.")]
        [SerializeField] private GameObject burstEffect;

        public Color TrailColor => trailColor;
        public float ShakeAmount => shakeAmount;
        public float ShakeSeconds => shakeSeconds;
        public float FovPunch => fovPunch;
        public GameObject HitEffect => hitEffect;
        public GameObject BurstEffect => burstEffect;

        public SwingPresentation(Color trailColor, float shakeAmount = 0.15f,
                                 float shakeSeconds = 0.18f, float fovPunch = 0f)
        {
            this.trailColor = trailColor;
            this.shakeAmount = Mathf.Max(0f, shakeAmount);
            this.shakeSeconds = Mathf.Max(0f, shakeSeconds);
            this.fovPunch = fovPunch;
            hitEffect = null;
            burstEffect = null;
        }

        /// <summary>The look of the original chain: pale blue ribbon, light shake.</summary>
        public static SwingPresentation Default => new(new Color(0.8f, 0.95f, 1f, 0.9f));
    }
}
