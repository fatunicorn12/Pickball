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
        /// <param name="bossHealth">Her Health: a batted orbiter damages her through it,
        /// so her damage filter and the player's regain credit apply. Null = batted
        /// orbiters deal no damage.</param>
        /// <param name="bossHurtbox">Her body's capsule (it hovers and sinks with her):
        /// what a batted orbiter homes on and strikes. Null = a default capsule on her root.</param>
        public HypatiaContext(HypatiaConfig config, Transform boss, Transform arenaCentre,
                              Transform player, Health playerHealth,
                              CharacterController playerController,
                              Func<float> bossTimeScale,
                              Health bossHealth = null, CapsuleCollider bossHurtbox = null)
        {
            Config = config;
            Boss = boss;
            ArenaCentre = arenaCentre;
            Player = player;
            PlayerHealth = playerHealth;
            PlayerController = playerController;
            _bossTimeScale = bossTimeScale;
            BossHealth = bossHealth;
            BossHurtbox = bossHurtbox;
            PlayerVelocity = new VelocityTracker();
        }

        /// <summary>Her Health; may be null (tests, a half-wired scene).</summary>
        public Health BossHealth { get; }

        /// <summary>Her body's capsule; may be null (a default capsule is used).</summary>
        public CapsuleCollider BossHurtbox { get; }

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

        /// <summary>Default body when no hurtbox is wired: a 1.7 m capsule standing on
        /// her root (her height, Setup Hypatia Fight's capsule radius).</summary>
        const float DefaultBossHeight = 1.7f, DefaultBossRadius = 0.34f;

        /// <summary>
        /// Her body capsule's axis (sphere centres) and radius in world space, read
        /// from the hurtbox transform every call, so it follows her hover, the drop
        /// into the recalibration window and the topple. Assumes the capsule's Y
        /// axis (Setup's) and a unit scale. False when there is no boss at all.
        /// </summary>
        public bool TryGetBossCapsule(out Vector3 bottom, out Vector3 top, out float radius)
        {
            if (BossHurtbox != null)
            {
                Transform t = BossHurtbox.transform;
                radius = BossHurtbox.radius;
                float half = Mathf.Max(0f, BossHurtbox.height * 0.5f - radius);
                Vector3 centre = t.TransformPoint(BossHurtbox.center);
                bottom = centre - t.up * half;
                top = centre + t.up * half;
                return true;
            }
            if (Boss == null)
            {
                bottom = top = Vector3.zero;
                radius = 0f;
                return false;
            }
            radius = DefaultBossRadius;
            Vector3 feet = Boss.position;
            bottom = feet + Vector3.up * radius;
            top = feet + Vector3.up * (DefaultBossHeight - radius);
            return true;
        }

        /// <summary>The middle of her body capsule (what a batted orbiter steers to);
        /// the arena centre when there is no boss.</summary>
        public Vector3 BossCentre =>
            TryGetBossCapsule(out Vector3 bottom, out Vector3 top, out _) ? (bottom + top) * 0.5f : Centre;
    }
}
