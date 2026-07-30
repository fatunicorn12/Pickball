using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Pickleball
{
    public enum AIState { Idle, MoveToBall, Swing, Reset }

    /// <summary>
    /// Simple AI opponent. Fixed Z near its baseline; only slides along X. Its behaviour is a
    /// UniTask-driven state machine (Idle → MoveToBall → Swing → Reset) kicked off whenever the
    /// player strikes the ball. Reaction delay, movement speed, accuracy and miss chance all come
    /// from <see cref="AIConfig"/>. No Update loop — movement runs inside an async yield loop.
    /// </summary>
    public class AIController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private AIConfig aiConfig;

        [Header("Dependencies")]
        [SerializeField] private Court court;
        [SerializeField] private BallController ball;
        [SerializeField] private HitZone hitZone;

        [Header("Event Channels")]
        [Tooltip("Listened to: when the PLAYER hits, the AI starts reacting. Raised with AI when it returns.")]
        [SerializeField] private CourtSideEventChannelSO ballHitChannel;

        public AIState State { get; private set; } = AIState.Idle;

        private CancellationToken _lifetime;
        private bool _reacting;
        private Vector3 _home;

        private void Awake()
        {
            _lifetime = this.GetCancellationTokenOnDestroy();
            _home = transform.position; // start spot, returned to between rallies
        }

        private void OnEnable()
        {
            if (ballHitChannel != null) ballHitChannel.OnEventRaised += OnBallHit;
        }

        private void OnDisable()
        {
            if (ballHitChannel != null) ballHitChannel.OnEventRaised -= OnBallHit;
        }

        private void OnBallHit(CourtSide hitter)
        {
            // Only react to the ball coming toward us.
            if (hitter == CourtSide.Player && !_reacting)
            {
                ReactAndReturn(_lifetime).Forget();
            }
        }

        /// <summary>Called by the GameStateManager when it's the AI's serve.</summary>
        public void Serve()
        {
            ServeRoutine(_lifetime).Forget();
        }

        private async UniTaskVoid ServeRoutine(CancellationToken ct)
        {
            State = AIState.Swing;
            await UniTask.Delay(TimeSpan.FromSeconds(aiConfig.EffectiveReactionDelay), cancellationToken: ct);
            if (ball == null || ball.State == BallState.Dead) return;

            ball.Hit(BuildReturnVelocity(), CourtSide.AI);
            ballHitChannel?.Raise(CourtSide.AI);
            State = AIState.Idle;
        }

        private async UniTaskVoid ReactAndReturn(CancellationToken ct)
        {
            _reacting = true;
            try
            {
                State = AIState.MoveToBall;
                await UniTask.Delay(TimeSpan.FromSeconds(aiConfig.EffectiveReactionDelay), cancellationToken: ct);

                bool willMiss = UnityEngine.Random.value < aiConfig.EffectiveMissChance;

                // Move toward the projected landing spot until we can swing (or the ball dies).
                while (ball != null && ball.State != BallState.Dead)
                {
                    Vector3 target = ComputeTarget();
                    MoveToward(target);

                    bool ballOnOurSide = court == null || court.SideOf(ball.transform.position) == CourtSide.AI;
                    if (hitZone != null && hitZone.Contains(ball.transform.position) && ballOnOurSide)
                    {
                        if (!willMiss)
                        {
                            State = AIState.Swing;
                            ball.Hit(BuildReturnVelocity(), CourtSide.AI);
                            ballHitChannel?.Raise(CourtSide.AI);
                        }
                        break; // either we swung, or we deliberately whiffed
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }

                // Reset back toward the home spot for the next rally.
                State = AIState.Reset;
                while (Vector3.Distance(transform.position, _home) > aiConfig.positionTolerance)
                {
                    MoveToward(_home);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                State = AIState.Idle;
            }
            catch (OperationCanceledException) { /* destroyed mid-rally */ }
            finally
            {
                _reacting = false;
            }
        }

        /// <summary>Projected landing spot (X and Z) with difficulty-scaled aiming error, clamped to the AI's half.</summary>
        private Vector3 ComputeTarget()
        {
            Vector3 landing = ball.PredictLanding(0f);
            float err = aiConfig.EffectiveAccuracyOffset;
            float halfW = court != null ? court.HalfWidth : 3f;
            float baselineZ = court != null ? court.AiBaselineZ : 6.7f;

            float x = Mathf.Clamp(landing.x + UnityEngine.Random.Range(-1f, 1f) * err, -halfW, halfW);
            // Stay on our own side, between just past the net and the baseline.
            float z = Mathf.Clamp(landing.z + UnityEngine.Random.Range(-1f, 1f) * err, 0.6f, baselineZ);
            return new Vector3(x, transform.position.y, z);
        }

        private void MoveToward(Vector3 target)
        {
            Vector3 pos = transform.position;
            target.y = pos.y;
            transform.position = Vector3.MoveTowards(pos, target, aiConfig.moveSpeed * Time.deltaTime);
        }

        /// <summary>Builds a return velocity heading back toward the player (-Z) with arc and randomised aim.</summary>
        private Vector3 BuildReturnVelocity()
        {
            float power = UnityEngine.Random.Range(aiConfig.returnPowerRange.x, aiConfig.returnPowerRange.y);
            float aimX = UnityEngine.Random.Range(-aiConfig.aimRandomness, aiConfig.aimRandomness);

            return Vector3.back * power
                 + Vector3.up * aiConfig.returnArcLift
                 + Vector3.right * aimX;
        }
    }
}
