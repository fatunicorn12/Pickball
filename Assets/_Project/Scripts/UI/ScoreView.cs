using TMPro;
using UnityEngine;

namespace Pickleball
{
    /// <summary>
    /// Minimal top-centre scoreboard. Listens to the score + game-state channels and reflects
    /// them in a TextMeshPro label. No Update loop — it only redraws on events.
    /// </summary>
    public class ScoreView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Text scoreText;

        [Header("Event Channels")]
        [SerializeField] private ScoreEventChannelSO scoreChannel;
        [SerializeField] private GameStateEventChannelSO gameStateChannel;

        private ScoreSnapshot _score;
        private GameStateId _state;

        private void OnEnable()
        {
            if (scoreChannel != null) scoreChannel.OnEventRaised += OnScore;
            if (gameStateChannel != null) gameStateChannel.OnEventRaised += OnState;
            Redraw();
        }

        private void OnDisable()
        {
            if (scoreChannel != null) scoreChannel.OnEventRaised -= OnScore;
            if (gameStateChannel != null) gameStateChannel.OnEventRaised -= OnState;
        }

        private void OnScore(ScoreSnapshot s) { _score = s; Redraw(); }
        private void OnState(GameStateId s) { _state = s; Redraw(); }

        private void Redraw()
        {
            if (scoreText == null) return;

            string server = _score.Server == CourtSide.Player ? "▼ You" : "▲ AI";
            string line = $"You {_score.PlayerScore} — {_score.AiScore} AI";

            string status = _state switch
            {
                GameStateId.Serving => $"Serving: {server}",
                GameStateId.PointScored => "Point!",
                GameStateId.GameOver => _score.PlayerScore > _score.AiScore ? "You Win!" : "AI Wins!",
                _ => $"Server: {server}"
            };

            scoreText.text = $"{line}\n<size=60%>{status}</size>";
        }
    }
}
