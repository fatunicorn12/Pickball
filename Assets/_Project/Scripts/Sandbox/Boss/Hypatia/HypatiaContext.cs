using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// What every Hypatia attack needs to know about the fight, handed to it by
    /// the brain through <see cref="IHypatiaAttack.Bind"/>. Rebuilt by the brain
    /// in every <c>OnEnable</c>, so a script reload in Play mode re-binds every
    /// attack instead of leaving them holding nulls (Newton's reload gap).
    ///
    /// Plain C# so attack maths can be tested with a context made in a test.
    /// </summary>
    public sealed class HypatiaContext
    {
        readonly Func<float> _bossTimeScale;

        /// <param name="bossTimeScale">Her slow multiplier (0-1), read live; null = 1.</param>
        public HypatiaContext(HypatiaConfig config, Transform boss, Transform arenaCentre,
                              Transform player, Health playerHealth,
                              CharacterController playerController,
                              Func<float> bossTimeScale)
        {
            Config = config;
            Boss = boss;
            ArenaCentre = arenaCentre;
            Player = player;
            PlayerHealth = playerHealth;
            PlayerController = playerController;
            _bossTimeScale = bossTimeScale;
            PlayerVelocity = new VelocityTracker();
        }

        public HypatiaConfig Config { get; }

        /// <summary>Her root. Its horizontal position is the focus of every conic.</summary>
        public Transform Boss { get; }

        /// <summary>The plate's centre; its Y is the floor.</summary>
        public Transform ArenaCentre { get; }

        public Transform Player { get; }
        public Health PlayerHealth { get; }

        /// <summary>The player's capsule source; may be null (a default capsule is used).</summary>
        public CharacterController PlayerController { get; }

        /// <summary>The player's smoothed velocity; the brain samples it every frame.</summary>
        public VelocityTracker PlayerVelocity { get; }

        /// <summary>True from phase 2 on. One-way.</summary>
        public bool PhaseTwo { get; private set; }

        /// <summary>World Y of the plate's surface.</summary>
        public float FloorY => ArenaCentre != null ? ArenaCentre.position.y : 0f;

        /// <summary>Plate centre in world space.</summary>
        public Vector3 Centre => ArenaCentre != null ? ArenaCentre.position : Vector3.zero;

        /// <summary>Her slow multiplier now (0-1). Timers she owns run at dt × this.
        /// Released projectiles use their OWN multiplier instead (Stationary Point
        /// spec: freezing her does not freeze what she has already thrown).</summary>
        public float BossTimeScale
        {
            get
            {
                float s = _bossTimeScale?.Invoke() ?? 1f;
                return float.IsNaN(s) ? 1f : Mathf.Clamp01(s);
            }
        }

        /// <summary>A dt on her clock: <paramref name="dt"/> × <see cref="BossTimeScale"/>.</summary>
        public float BossDelta(float dt) => dt * BossTimeScale;

        /// <summary>Called once by the brain at the phase-2 threshold.</summary>
        public void EnterPhaseTwo() => PhaseTwo = true;

        /// <summary>
        /// The player's capsule axis (sphere centres) and radius. False when there
        /// is no player.
        /// </summary>
        public bool TryGetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float radius)
        {
            if (Player == null)
            {
                bottom = top = Vector3.zero;
                radius = 0f;
                return false;
            }

            float height = 1.6f, centreY = 0.8f;
            radius = 0.35f;
            if (PlayerController != null)
            {
                height = PlayerController.height;
                radius = PlayerController.radius;
                centreY = PlayerController.center.y;
            }

            Vector3 basePos = Player.position;
            float half = Mathf.Max(0f, height * 0.5f - radius);
            bottom = basePos + Vector3.up * (centreY - half);
            top = basePos + Vector3.up * (centreY + half);
            return true;
        }
    }
}
