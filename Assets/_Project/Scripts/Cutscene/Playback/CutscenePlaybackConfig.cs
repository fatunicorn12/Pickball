using UnityEngine;

namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// Tunables for cutscene playback: how long the skip button must be held and how fast the
    /// gameplay HUD fades away and back, and the minimum subtitle card time the timeline builders lay
    /// voiced lines out with. Read by <see cref="CutscenePlayer"/>; created by the
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

        [Header("Subtitles (timeline layout)")]
        [Tooltip("Shortest time a voiced line's card stays up, in seconds. A clip shorter than this " +
                 "still plays at its real length, and the line's slot is stretched to this, so later " +
                 "lines move back a little (it reads as a beat). Also the floor for each card of a " +
                 "multi-card line where the line is long enough. Baked into the timelines: run " +
                 "Rebuild Timelines and 6 Build Cameras after changing it.")]
        [Min(0f)]
        [SerializeField] private float minCardSeconds = Pickleball.Cutscene.Script.LineTiming.DefaultMinCardSeconds;

        /// <summary>Skip hold time, seconds.</summary>
        public float SkipHoldSeconds => skipHoldSeconds;

        /// <summary>HUD fade rate, 1/s.</summary>
        public float HudFadeRate => hudFadeRate;

        /// <summary>HUD alpha during a cutscene.</summary>
        public float HudHiddenAlpha => hudHiddenAlpha;

        /// <summary>Minimum card time for voiced lines, seconds (read by the timeline builders).</summary>
        public float MinCardSeconds => minCardSeconds;
    }
}
