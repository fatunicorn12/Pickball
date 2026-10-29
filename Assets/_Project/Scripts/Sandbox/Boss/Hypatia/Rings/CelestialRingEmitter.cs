using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Rings
{
    /// <summary>
    /// Celestial Rings on Hypatia's root: rings expand from the arena centre,
    /// each with one gap, the gaps turning 45° per ring so the safe route is a
    /// spiral. The brain decides WHEN (<see cref="QueueRing"/>); this decides the
    /// ring, forecasts its gap, runs it and deals its damage.
    ///
    /// <para>A thin Unity shell around <see cref="RingField"/>, which holds all
    /// the state and rules: this component only reads the config and the
    /// player's capsule from the bound <see cref="HypatiaContext"/>, feeds her
    /// clock and real time to the field, applies hits to the player's Health and
    /// forwards the field's events. It draws nothing; presentation subscribes to
    /// the events below and reads <see cref="TryGetRing"/>.</para>
    ///
    /// <para><b>Slows.</b> Rings are her waves, not throwable objects: they have
    /// no collider and cannot be slowed one by one. A pending forecast runs on her
    /// clock (<see cref="HypatiaContext.BossDelta"/>; freezing her holds it), but
    /// a launched ring keeps expanding on real time (Stationary Point spec:
    /// released projectiles keep moving).</para>
    ///
    /// <para><b>Hot reload.</b> The field is not serialized, so after a script
    /// reload it is null and is rebuilt lazily (a fresh spiral, no live rings);
    /// the brain re-binds the context in its OnEnable. Subscribers are lost with
    /// the reload too and must subscribe again in their own OnEnable.</para>
    /// </summary>
    public class CelestialRingEmitter : MonoBehaviour, IRingAttack
    {
        HypatiaContext _context;
        RingField _field;
        System.Random _random;
        Vector3 _centreThisFrame;

        /// <summary>A ring was decided and its gap should show on the floor now.
        /// It launches after <see cref="RingSnapshot.LeadSeconds"/> on her clock;
        /// <see cref="RingLaunched"/> or <see cref="RingRetired"/> follows with
        /// the same id.</summary>
        public event Action<RingSnapshot> GapForecast;

        /// <summary>The forecast with this id became a live ring (read it with
        /// <see cref="TryGetRingById"/>).</summary>
        public event Action<int> RingLaunched;

        /// <summary>The ring or forecast with this id is gone (reached the rim,
        /// or <see cref="StopAll"/>). Exactly once per forecast id.</summary>
        public event Action<int> RingRetired;

        /// <summary>The ring with this id hit the player at this world point,
        /// after damage was applied. At most once per ring.</summary>
        public event Action<int, Vector3> RingHitPlayer;

        /// <summary>Rings alive now, not counting forecasts.</summary>
        public int LiveRings => _field != null ? _field.LiveCount : 0;

        /// <summary>Forecasts waiting to launch.</summary>
        public int PendingForecasts => _field != null ? _field.PendingCount : 0;

        /// <summary>World centre the rings expand from (the arena centre), and
        /// the floor height their heights are measured from.</summary>
        public Vector3 Centre => _context != null ? _context.Centre : transform.position;

        /// <inheritdoc/>
        public void Bind(HypatiaContext context)
        {
            _context = context;
            EnsureField();
        }

        /// <summary>
        /// Replace the random source that picks each fight's first gap and spiral
        /// direction (from the next fight on). For tests and seeded replays; by
        /// default it is time-seeded.
        /// </summary>
        public void SetRandom(System.Random random)
        {
            if (random == null) return;
            _random = random;
            _field?.SetRandom(random);
        }

        /// <inheritdoc/>
        public bool QueueRing()
        {
            if (_context == null || _context.Config == null) return false;
            EnsureField();
            var settings = RingSettings.FromConfig(_context.Config);
            if (!_field.TryQueue(in settings, _context.PhaseTwo, out RingSnapshot forecast))
                return false;
            GapForecast?.Invoke(forecast);
            return true;
        }

        /// <inheritdoc/>
        public void StopAll() => _field?.Clear();

        /// <summary>Live ring in <paramref name="slot"/> (0 = oldest, outermost),
        /// 0 ≤ slot &lt; <see cref="LiveRings"/>. For drawing every frame.</summary>
        public bool TryGetRing(int slot, out RingSnapshot ring)
        {
            if (_field != null) return _field.TryGetLive(slot, out ring);
            ring = default;
            return false;
        }

        /// <summary>Pending forecast in <paramref name="slot"/>, 0 ≤ slot &lt;
        /// <see cref="PendingForecasts"/>. Lets a reloaded presentation redraw
        /// floor markers it missed the forecast event for.</summary>
        public bool TryGetForecast(int slot, out RingSnapshot forecast)
        {
            if (_field != null) return _field.TryGetPending(slot, out forecast);
            forecast = default;
            return false;
        }

        /// <summary>A live ring or pending forecast by id.</summary>
        public bool TryGetRingById(int id, out RingSnapshot ring)
        {
            if (_field != null) return _field.TryGetById(id, out ring);
            ring = default;
            return false;
        }

        void OnEnable() => EnsureField();

        // Newton's volley does the same: a disabled attack leaves nothing live.
        void OnDisable() => StopAll();

        void Update()
        {
            if (_context == null || _context.Config == null) return;
            EnsureField();

            float dt = Time.deltaTime;
            var settings = RingSettings.FromConfig(_context.Config);
            _centreThisFrame = _context.Centre;

            bool hasPlayer = _context.TryGetPlayerCapsule(out Vector3 bottom, out _, out float radius);
            Vector2 offset = default;
            float feet = 0f;
            if (hasPlayer)
            {
                offset = new Vector2(bottom.x - _centreThisFrame.x, bottom.z - _centreThisFrame.z);
                feet = bottom.y - radius - _context.FloorY;
            }

            _field.Tick(in settings, _context.BossDelta(dt), dt, hasPlayer, offset, feet, radius);
        }

        void EnsureField()
        {
            if (_field != null) return;
            int capacity = _context != null && _context.Config != null
                ? _context.Config.MaxLiveRings
                : 8;
            _field = new RingField(capacity, _random);
            _field.Launched += OnFieldLaunched;
            _field.Retired += OnFieldRetired;
            _field.PlayerHit += OnFieldPlayerHit;
        }

        void OnFieldLaunched(int id) => RingLaunched?.Invoke(id);

        void OnFieldRetired(int id) => RingRetired?.Invoke(id);

        void OnFieldPlayerHit(int id, float damage, Vector3 local)
        {
            Vector3 at = new Vector3(_centreThisFrame.x + local.x,
                                     (_context != null ? _context.FloorY : 0f) + local.y,
                                     _centreThisFrame.z + local.z);
            Health health = _context != null ? _context.PlayerHealth : null;
            if (health != null) health.Damage(damage, at, recoverable: true);
            RingHitPlayer?.Invoke(id, at);
        }
    }
}
