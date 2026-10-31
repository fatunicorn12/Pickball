using UnityEngine;

namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// Tunables for cutscene playback: how long the skip button must be held and how fast the
    /// gameplay HUD fades away and back. Read by <see cref="CutscenePlayer"/>; created by the
    /// <c>4 Build Opening Scene</c> menu at <c>ScriptableObjects/Cutscene/Playback/</c> if missing.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Playback Config", fileName = "CutscenePlaybackConfig")]
    public sealed class CutscenePlaybackConfig : ScriptableObject
    {
        /// <summary>Hold time used when no config is wired.</summary>
        public const float DefaultSkipHoldSeconds = 1.0f;

        /// <summary>Fade rate used when no config is wired.</summary>
        public const float DefaultHudFadeRate = 6f;

        [Header("Skip")]
        [Tooltip("Seconds the skip input (touch anywhere, Space/Enter, gamepad South) must be held to " +
                 "skip to the end of the current block. Longer is safer against accidental skips; " +
                 "shorter feels more responsive. Unscaled time.")]
        [Min(0.1f)]
        [SerializeField] private float skipHoldSeconds = DefaultSkipHoldSeconds;

        [Header("HUD fade")]
        [Tooltip("Exponential fade rate of the gameplay HUD, per second (unscaled). 6 gets 95% of the " +
                 "way in 0.5 s; higher snaps, lower drifts.")]
        [Min(0.1f)]
        [SerializeField] private float hudFadeRate = DefaultHudFadeRate;

        [Tooltip("HUD alpha while a cutscene plays, 0..1. 0 hides it completely.")]
        [Range(0f, 1f)]
        [SerializeField] private float hudHiddenAlpha;

        /// <summary>Skip hold time, seconds.</summary>
        public float SkipHoldSeconds => skipHoldSeconds;

        /// <summary>HUD fade rate, 1/s.</summary>
        public float HudFadeRate => hudFadeRate;

        /// <summary>HUD alpha during a cutscene.</summary>
        public float HudHiddenAlpha => hudHiddenAlpha;
    }
}
