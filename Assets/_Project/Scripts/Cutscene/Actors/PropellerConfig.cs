using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Spin tunables for Birdy's propeller, on him (<see cref="CutsceneRotor"/>) and loose
    /// (<see cref="PropellerProp"/>). One asset for both so the hand-over between the two
    /// (pop-off in scene 2, seat in scene 3) keeps the same speed.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Propeller Config", fileName = "PropellerConfig")]
    public sealed class PropellerConfig : ScriptableObject
    {
        [Header("Speeds")]
        [Tooltip("Spin while Birdy stands or idles with the propeller on, in °/s. 180 is a lazy " +
                 "half-turn a second; higher reads as fidgety.")]
        [Min(0f)]
        [SerializeField] private float idleSpin = 180f;

        [Tooltip("Spin while Birdy hovers, in °/s. Fast enough to blur at 30 fps (above ~1000) so " +
                 "it reads as flight rather than a slow fan.")]
        [Min(0f)]
        [SerializeField] private float hoverSpin = 1440f;

        [Tooltip("Spin for a rev (the zip-off), in °/s.")]
        [Min(0f)]
        [SerializeField] private float revSpin = 2520f;

        [Header("Response")]
        [Tooltip("How fast the spin follows a new target, 1/s. 4 ≈ a quarter-second to change; " +
                 "lower feels like a heavy rotor spooling up.")]
        [Min(0f)]
        [SerializeField] private float spinResponse = 4f;

        [Tooltip("Seconds a loose propeller takes to wind down to a stop after it lands. Longer " +
                 "makes the ledge landing linger.")]
        [Min(0f)]
        [SerializeField] private float spinDownSeconds = 1.6f;

        /// <summary>Idle spin, °/s.</summary>
        public float IdleSpin => idleSpin;

        /// <summary>Hover spin, °/s.</summary>
        public float HoverSpin => hoverSpin;

        /// <summary>Rev spin, °/s.</summary>
        public float RevSpin => revSpin;

        /// <summary>Spin easing rate, 1/s.</summary>
        public float SpinResponse => spinResponse;

        /// <summary>Wind-down time of a loose propeller, s.</summary>
        public float SpinDownSeconds => spinDownSeconds;
    }
}
