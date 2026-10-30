using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Pulse
{
    /// <summary>
    /// The knockback pulse: every Pulse Interval she gathers light for Pulse
    /// Charge Time, then releases a ring of force. A player within Pulse Reach
    /// (horizontally, at any height: it cannot be jumped) takes Pulse Damage and
    /// is shoved straight away from her, hard enough to slide out to Pulse Target
    /// Radius (<see cref="PulseMath"/>). It keeps a player from camping under her
    /// between punish windows, without a hard counter: the charge is the warning.
    ///
    /// HOW only. The brain decides WHEN (<see cref="BeginPulse"/>, on its own
    /// timer) and cancels a charge when she topples or starts recalibrating. The
    /// charge counts down on HER clock (<see cref="HypatiaContext.BossDelta"/>), so
    /// freezing her freezes the charge too. Raises C# events for presentation
    /// (charge glow, expanding shell); never touches VFX itself.
    ///
    /// The push goes through <see cref="ChibiLocomotion.AddMomentum"/> once, as an
    /// impulse (its contract), fetched from the bound player in <see cref="Bind"/>
    /// and null-safe: a player without one is still damaged, just not moved.
    /// Ticks in Update; <see cref="Tick"/> is public for EditMode tests.
    /// </summary>
    public class KnockbackPulse : MonoBehaviour, IPulseAttack
    {
        /// <summary>A pulse started charging; it fires in this many seconds on her clock.</summary>
        public event Action<float> PulseCharging;

        /// <summary>The pulse fired; it reaches this far, m (draw the shell to it).</summary>
        public event Action<float> PulseFired;

        /// <summary>A charging pulse was dropped without firing (topple, window, StopAll).</summary>
        public event Action PulseCancelled;

        /// <summary>The pulse caught the player: the push velocity handed to their
        /// movement (m/s, horizontal) and the damage dealt before any filter.</summary>
        public event Action<Vector3, float> PulseHitPlayer;

        HypatiaContext _context;
        ChibiLocomotion _locomotion;
        float _chargeLeft;
        bool _charging;

        /// <inheritdoc/>
        public bool IsCharging => _charging;

        /// <summary>Seconds of charge left on her clock; 0 when not charging.</summary>
        public float ChargeRemaining => _charging ? Mathf.Max(0f, _chargeLeft) : 0f;

        /// <inheritdoc/>
        public void Bind(HypatiaContext context)
        {
            _context = context;
            _locomotion = context != null && context.Player != null
                ? context.Player.GetComponent<ChibiLocomotion>()
                : null;
        }

        /// <inheritdoc/>
        public bool BeginPulse()
        {
            if (_charging || _context == null || _context.Config == null) return false;
            float charge = Mathf.Max(0f, _context.Config.PulseChargeTime);
            _charging = true;
            _chargeLeft = charge;
            PulseCharging?.Invoke(charge);
            if (charge <= 0f) Fire();
            return true;
        }

        /// <inheritdoc/>
        public void CancelPulse()
        {
            if (!_charging) return;
            _charging = false;
            _chargeLeft = 0f;
            PulseCancelled?.Invoke();
        }

        /// <inheritdoc/>
        public void StopAll() => CancelPulse();

        void Update() => Tick(Time.deltaTime);

        // Disabling (or a script reload) drops a charge rather than firing it late.
        void OnDisable() => CancelPulse();

        /// <summary>Advance the charge by <paramref name="deltaTime"/> real seconds;
        /// her clock scales it. Called from Update; public for EditMode tests.</summary>
        public void Tick(float deltaTime)
        {
            if (!_charging || _context == null) return;
            float dt = _context.BossDelta(deltaTime);
            if (!(dt > 0f)) return;
            _chargeLeft -= dt;
            if (_chargeLeft <= 0f) Fire();
        }

        void Fire()
        {
            _charging = false;
            _chargeLeft = 0f;
            HypatiaConfig cfg = _context.Config;
            PulseFired?.Invoke(cfg.PulseReach);

            Transform player = _context.Player;
            if (player == null) return;
            Vector3 her = _context.Boss != null ? _context.Boss.position : transform.position;
            Vector3 herForward = _context.Boss != null ? _context.Boss.forward : transform.forward;
            if (!PulseMath.TryComputePush(her, player.position, player.forward, herForward,
                                          cfg.PulseReach, cfg.PulseTargetRadius, cfg.PulseAssumedGroundDrag,
                                          cfg.PulseExtraSpeed, out Vector3 push))
                return;

            if (_context.PlayerHealth != null && cfg.PulseDamage > 0f)
                _context.PlayerHealth.Damage(cfg.PulseDamage, player.position, recoverable: true);
            if (_locomotion != null) _locomotion.AddMomentum(push);
            PulseHitPlayer?.Invoke(push, cfg.PulseDamage);
        }
    }
}
