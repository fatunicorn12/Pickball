using UnityEngine;

namespace Pickleball
{
    /// <summary>
    /// Fixed-position player (Wii-style — no lateral movement). Reacts to swing gestures from the
    /// <see cref="InputReader"/> and, when the ball is inside the hit zone, launches it toward the
    /// AI's court with arc, aim and power. Purely event-driven: no Update loop.
    ///
    /// Wire in the Inspector: BallConfig, the Ball, the HitZone, the InputReader, the
    /// "ball hit" channel (raised on a successful strike), and the game-state channel (gates swings).
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private BallConfig ballConfig;

        [Header("Dependencies")]
        [SerializeField] private BallController ball;
        [SerializeField] private HitZone hitZone;
        [SerializeField] private InputReader input;

        [Header("Event Channels")]
        [Tooltip("Raised with CourtSide.Player whenever the player successfully strikes the ball.")]
        [SerializeField] private CourtSideEventChannelSO ballHitChannel;
        [Tooltip("Listened to so the player can only swing during Serving/Rally.")]
        [SerializeField] private GameStateEventChannelSO gameStateChannel;

        private bool _canHit = true;

        private void OnEnable()
        {
            if (input != null) input.SwingPerformed += OnSwing;
            if (gameStateChannel != null) gameStateChannel.OnEventRaised += OnGameStateChanged;
        }

        private void OnDisable()
        {
            if (input != null) input.SwingPerformed -= OnSwing;
            if (gameStateChannel != null) gameStateChannel.OnEventRaised -= OnGameStateChanged;
        }

        private void OnGameStateChanged(GameStateId state)
        {
            _canHit = state == GameStateId.Serving || state == GameStateId.Rally;
        }

        private void OnSwing(SwingGesture gesture)
        {
            if (!_canHit || ballConfig == null || ball == null) return;
            if (ball.State == BallState.Dead) return;

            // Bounds check (not a trigger) so a freshly-parked serve ball is always hittable.
            if (hitZone != null && !hitZone.Contains(ball.transform.position)) return;

            ball.Hit(BuildVelocity(gesture), CourtSide.Player);
            ballHitChannel?.Raise(CourtSide.Player);
        }

        /// <summary>Maps a gesture to a launch velocity heading down-court (+Z) with an exaggerated arc.</summary>
        private Vector3 BuildVelocity(SwingGesture gesture)
        {
            float speed = Mathf.Lerp(ballConfig.shotPowerRange.x, ballConfig.shotPowerRange.y, gesture.Power);
            float side = gesture.Aim * ballConfig.maxHorizontalAim;

            return Vector3.forward * speed
                 + Vector3.up * ballConfig.shotArcLift
                 + Vector3.right * side;
        }
    }
}
