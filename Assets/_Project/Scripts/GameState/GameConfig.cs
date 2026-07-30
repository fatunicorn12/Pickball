using UnityEngine;

namespace Pickleball
{
    /// <summary>Tunable game-flow / scoring values.</summary>
    [CreateAssetMenu(menuName = "Pickleball/Config/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("Scoring")]
        public int pointsToWin = 11;
        public int winBy = 2;

        [Header("Flow Timing (seconds)")]
        [Tooltip("Pause after a point is scored before returning to Serving.")]
        public float pointPauseDuration = 1.5f;

        [Tooltip("Delay after entering Serving before the serve auto-launches / is enabled.")]
        public float serveDelay = 0.75f;

        [Tooltip("Pause shown on the Game Over state.")]
        public float gameOverPauseDuration = 3f;

        [Header("Serve")]
        [Tooltip("Which side serves first when a new game starts.")]
        public CourtSide firstServer = CourtSide.Player;
    }
}
