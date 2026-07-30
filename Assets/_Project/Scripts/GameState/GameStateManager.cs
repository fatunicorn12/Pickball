using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Pickleball
{
    /// <summary>
    /// Drives the whole game loop (Serving → Rally → PointScored → GameOver) purely through
    /// ScriptableObject event channels. Implements simplified singles pickleball scoring:
    /// only the serving side can score; losing a rally on serve is a side-out (serve passes,
    /// no point). First to <c>pointsToWin</c>, win by <c>winBy</c>.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private GameConfig config;

        [Header("Scene Dependencies")]
        [SerializeField] private BallController ball;
        [SerializeField] private AIController ai;
        [SerializeField] private Transform playerServeAnchor;
        [SerializeField] private Transform aiServeAnchor;

        [Header("Event Channels — Raised")]
        [SerializeField] private GameStateEventChannelSO gameStateChannel;
        [SerializeField] private ScoreEventChannelSO scoreChannel;

        [Header("Event Channels — Listened")]
        [SerializeField] private CourtSideEventChannelSO ballHitChannel;
        [SerializeField] private BallContactEventChannelSO bounceChannel;
        [SerializeField] private BallContactEventChannelSO ballDeadChannel;

        public GameStateId State { get; private set; }

        private int _playerScore;
        private int _aiScore;
        private CourtSide _server;

        // Rally bookkeeping used to decide who lost the point.
        private CourtSide _lastHitter;
        private int _inBoundsBouncesSinceHit;
        private CourtSide _firstBounceSide;
        private bool _resolvingPoint;

        private CancellationToken _lifetime;

        private void Awake() => _lifetime = this.GetCancellationTokenOnDestroy();

        private void OnEnable()
        {
            if (ballHitChannel != null) ballHitChannel.OnEventRaised += OnBallHit;
            if (bounceChannel != null) bounceChannel.OnEventRaised += OnBounce;
            if (ballDeadChannel != null) ballDeadChannel.OnEventRaised += OnBallDead;
        }

        private void OnDisable()
        {
            if (ballHitChannel != null) ballHitChannel.OnEventRaised -= OnBallHit;
            if (bounceChannel != null) bounceChannel.OnEventRaised -= OnBounce;
            if (ballDeadChannel != null) ballDeadChannel.OnEventRaised -= OnBallDead;
        }

        private void Start() => StartNewGame();

        // ---- Game lifecycle ----------------------------------------------------

        private void StartNewGame()
        {
            _playerScore = 0;
            _aiScore = 0;
            _server = config.firstServer;
            RaiseScore();
            EnterServing(_lifetime).Forget();
        }

        private async UniTaskVoid EnterServing(CancellationToken ct)
        {
            SetState(GameStateId.Serving);
            ResetRallyBookkeeping();

            // Park the ball in the server's hit zone, ready to be struck.
            Transform anchor = _server == CourtSide.Player ? playerServeAnchor : aiServeAnchor;
            if (anchor != null) ball.ResetTo(anchor.position);

            await UniTask.Delay(TimeSpan.FromSeconds(config.serveDelay), cancellationToken: ct);

            if (_server == CourtSide.AI)
            {
                ai.Serve(); // AI auto-serves; the player taps to serve their own.
            }
        }

        private void EnterRally()
        {
            SetState(GameStateId.Rally);
        }

        private async UniTaskVoid ResolvePoint(CourtSide winner, CancellationToken ct)
        {
            if (_resolvingPoint) return;
            _resolvingPoint = true;

            SetState(GameStateId.PointScored);
            AwardRally(winner);
            RaiseScore();

            if (HasWon(_playerScore, _aiScore) || HasWon(_aiScore, _playerScore))
            {
                await UniTask.Delay(TimeSpan.FromSeconds(config.gameOverPauseDuration), cancellationToken: ct);
                SetState(GameStateId.GameOver);
                await UniTask.Delay(TimeSpan.FromSeconds(config.gameOverPauseDuration), cancellationToken: ct);
                _resolvingPoint = false;
                StartNewGame();
                return;
            }

            await UniTask.Delay(TimeSpan.FromSeconds(config.pointPauseDuration), cancellationToken: ct);
            _resolvingPoint = false;
            EnterServing(ct).Forget();
        }

        // ---- Scoring -----------------------------------------------------------

        /// <summary>Singles pickleball: scoring only on serve, side-out otherwise.</summary>
        private void AwardRally(CourtSide winner)
        {
            if (winner == _server)
            {
                if (_server == CourtSide.Player) _playerScore++;
                else _aiScore++;
            }
            else
            {
                _server = winner; // side-out: serve passes, no point
            }
        }

        private bool HasWon(int score, int opponent)
            => score >= config.pointsToWin && score - opponent >= config.winBy;

        // ---- Event handlers ----------------------------------------------------

        private void OnBallHit(CourtSide hitter)
        {
            _lastHitter = hitter;
            _inBoundsBouncesSinceHit = 0;

            // The serve transitions us from Serving into the Rally.
            if (State == GameStateId.Serving && hitter == _server)
            {
                EnterRally();
            }
        }

        private void OnBounce(BallContactInfo info)
        {
            if (info.Zone == CourtZone.Net || info.Zone == CourtZone.OutOfBounds) return;
            if (_inBoundsBouncesSinceHit == 0) _firstBounceSide = info.Side;
            _inBoundsBouncesSinceHit++;
        }

        private void OnBallDead(BallContactInfo info)
        {
            if (State != GameStateId.Rally && State != GameStateId.Serving) return;

            CourtSide loser = DetermineLoser(info);
            CourtSide winner = Opposite(loser);
            ResolvePoint(winner, _lifetime).Forget();
        }

        /// <summary>Works out who lost the rally from the killing contact + rally bookkeeping.</summary>
        private CourtSide DetermineLoser(BallContactInfo info)
        {
            switch (info.Zone)
            {
                case CourtZone.Net:
                    // Whoever hit it into the net loses.
                    return _lastHitter;

                case CourtZone.OutOfBounds:
                    // Flew out with no prior bounce -> the hitter shanked it.
                    // Bounced in first, then out without being returned -> the receiver failed.
                    return _inBoundsBouncesSinceHit == 0 ? _lastHitter : _firstBounceSide;

                default:
                    // In-bounds double bounce: the side it died on failed to return.
                    return info.Side;
            }
        }

        // ---- Helpers -----------------------------------------------------------

        private void ResetRallyBookkeeping()
        {
            _inBoundsBouncesSinceHit = 0;
            _lastHitter = _server;
        }

        private void SetState(GameStateId next)
        {
            State = next;
            gameStateChannel?.Raise(next);
        }

        private void RaiseScore()
        {
            scoreChannel?.Raise(new ScoreSnapshot(_playerScore, _aiScore, _server));
        }

        private static CourtSide Opposite(CourtSide side)
            => side == CourtSide.Player ? CourtSide.AI : CourtSide.Player;
    }
}
