using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Rings
{
    /// <summary>
    /// The Celestial Rings simulation as plain C#: the fight's gap spiral, the
    /// pending forecasts, the live rings and their contacts with the player.
    /// <see cref="CelestialRingEmitter"/> is a thin Unity shell around it (reads
    /// the config and the player's capsule, applies damage, forwards events), so
    /// the whole attack can be driven frame by frame in an EditMode test.
    ///
    /// <para><b>Two clocks.</b> A forecast is her intention, so it counts down on
    /// HER clock (the <c>bossDt</c> passed to <see cref="Tick"/>; slowing or
    /// freezing her delays or holds the launch). A launched ring is a released
    /// wave, so it expands on real time (<c>realDt</c>) whatever happens to her,
    /// per the Stationary Point spec (released projectiles keep moving). Rings
    /// have no collider and are not individually slowable or throwable: they are
    /// her waves, not objects.</para>
    ///
    /// <para><b>Lifecycle per id:</b> forecast (<see cref="TryQueue"/>) →
    /// <see cref="Launched"/> → at most one <see cref="PlayerHit"/> →
    /// <see cref="Retired"/>. <see cref="Retired"/> is raised exactly once for
    /// every forecast id, also for a forecast cancelled by <see cref="Clear"/>
    /// before it launched, so presentation can free whatever it made for that id
    /// on that one event.</para>
    ///
    /// <para>Storage is two arrays sized to the live cap and grown only when the
    /// cap is raised, so a running fight allocates nothing per frame. Live rings
    /// keep launch order: slot 0 is the oldest (outermost).</para>
    /// </summary>
    public sealed class RingField
    {
        struct Ring
        {
            public int Id;
            public float Radius;
            public float Thickness;
            public float GapCentre;
            public float GapWidth;
            public bool Tall;
            public bool Burning;
            public float VisualHeight;
            public float Lead;
            public float Damage;
            public bool HasHitPlayer;
        }

        Ring[] _live;
        Ring[] _pending;
        int _liveCount;
        int _pendingCount;

        System.Random _random;
        bool _fightStarted;
        float _firstGap;
        int _spiralSign = 1;
        int _nextIndex;

        bool _hasPlayerHistory;
        float _prevPlayerDistance;

        // Bumped by Clear so a tick notices when an event handler stopped the
        // attack under it (a ring kills the player → the brain calls StopAll).
        int _version;

        /// <summary>A ring left her (its forecast ended). Arg: id.</summary>
        public event Action<int> Launched;

        /// <summary>A ring reached the arena rim, or a ring or forecast was
        /// cleared. Raised exactly once per forecast id. Arg: id.</summary>
        public event Action<int> Retired;

        /// <summary>A ring hit the player (once per ring at most). Args: id,
        /// damage (HP), hit point relative to the arena centre at floor level
        /// (x = world X, y = height above floor, z = world Z).</summary>
        public event Action<int, float, Vector3> PlayerHit;

        /// <param name="capacity">Initial slots; grows to the settings' live cap.</param>
        /// <param name="random">Picks the fight's first gap and spiral direction.
        /// Null = a time-seeded one. Inject a seeded one for repeatable tests.</param>
        public RingField(int capacity, System.Random random = null)
        {
            int n = Mathf.Max(1, capacity);
            _live = new Ring[n];
            _pending = new Ring[n];
            _random = random ?? new System.Random();
        }

        /// <summary>Rings expanding now (not counting forecasts).</summary>
        public int LiveCount => _liveCount;

        /// <summary>Forecasts waiting to launch.</summary>
        public int PendingCount => _pendingCount;

        /// <summary>True once the first ring of this fight was queued (the
        /// spiral's first gap and direction are then fixed until <see cref="Clear"/>).</summary>
        public bool FightStarted => _fightStarted;

        /// <summary>This fight's first gap centre, °. Valid once <see cref="FightStarted"/>.</summary>
        public float FirstGapDegrees => _firstGap;

        /// <summary>This fight's spiral direction, +1 or −1 (+1 turns toward +X
        /// from +Z, clockwise from above). Valid once <see cref="FightStarted"/>.</summary>
        public int SpiralSign => _spiralSign;

        /// <summary>Use another random source from the next fight on (the current
        /// fight's spiral, once chosen, is kept).</summary>
        public void SetRandom(System.Random random)
        {
            if (random != null) _random = random;
        }

        /// <summary>
        /// Decide the next ring now (gap from the spiral, tall from the cadence,
        /// burning if <paramref name="phaseTwo"/>) and hold it as a forecast that
        /// launches <see cref="RingSettings.ForecastLead"/> later on her clock.
        /// Deciding at queue time is what makes the floor forecast honest: the
        /// ring that launches is exactly the one that was shown. Skipped (false,
        /// and the spiral does not advance) when live rings plus forecasts already
        /// reach the cap.
        /// </summary>
        public bool TryQueue(in RingSettings settings, bool phaseTwo, out RingSnapshot forecast)
        {
            int cap = Mathf.Max(1, settings.MaxLiveRings);
            EnsureCapacity(cap);
            if (_liveCount + _pendingCount >= cap)
            {
                forecast = default;
                return false;
            }

            if (!_fightStarted) BeginFight(settings.GapSnapDegrees);

            int index = _nextIndex++;
            bool burning = phaseTwo;
            bool tall = burning || RingMath.IsTall(index, settings.TallRingEvery);
            float damage = burning ? settings.FireRingDamage
                         : tall ? settings.TallRingDamage
                         : settings.LowRingDamage;

            var ring = new Ring
            {
                Id = index,
                Radius = settings.StartRadius,
                Thickness = settings.Thickness,
                GapCentre = RingMath.GapCentreDegrees(index, _firstGap, _spiralSign,
                                                      settings.GapStepDegrees,
                                                      settings.GapSnapDegrees),
                GapWidth = settings.GapWidthDegrees,
                Tall = tall,
                Burning = burning,
                VisualHeight = tall ? settings.TallRingVisualHeight : settings.LowRingHeight,
                Lead = Mathf.Max(0f, settings.ForecastLead),
                Damage = damage,
            };
            _pending[_pendingCount++] = ring;
            forecast = ToSnapshot(in ring);
            return true;
        }

        /// <summary>
        /// Advance one frame: live rings expand by real time and test the player,
        /// rings at the rim retire, then forecasts count down on her clock and
        /// launch. A ring launched this frame starts testing next frame, from its
        /// start radius.
        /// </summary>
        /// <param name="bossDt">This frame on her clock (0 while she is frozen).</param>
        /// <param name="realDt">This frame of game time.</param>
        /// <param name="hasPlayer">False when there is no player to hit.</param>
        /// <param name="playerOffsetXZ">Player position minus arena centre: x = world X, y = world Z.</param>
        /// <param name="playerFeetHeight">Bottom of the player's capsule above the floor, m.</param>
        /// <param name="playerRadius">Player's capsule radius, m.</param>
        public void Tick(in RingSettings settings, float bossDt, float realDt, bool hasPlayer,
                         Vector2 playerOffsetXZ, float playerFeetHeight, float playerRadius)
        {
            int version = _version;
            float playerNow = playerOffsetXZ.magnitude;
            float playerPrev = _hasPlayerHistory ? _prevPlayerDistance : playerNow;
            _hasPlayerHistory = hasPlayer;
            _prevPlayerDistance = playerNow;
            float azimuth = RingMath.AzimuthDegrees(playerOffsetXZ);
            float step = Mathf.Max(0f, settings.Speed) * Mathf.Max(0f, realDt);

            int i = 0;
            while (i < _liveCount)
            {
                float prev = _live[i].Radius;
                float now = prev + step;
                _live[i].Radius = now;

                if (hasPlayer && !_live[i].HasHitPlayer
                    && RingMath.RingHitsPlayer(prev, now, _live[i].Thickness,
                                               _live[i].GapCentre, _live[i].GapWidth,
                                               _live[i].Tall, settings.LowRingHeight,
                                               playerPrev, playerNow, azimuth,
                                               playerFeetHeight, playerRadius))
                {
                    _live[i].HasHitPlayer = true;
                    Vector3 at = RingMath.DirectionFromAzimuth(azimuth) * now;
                    at.y = Mathf.Max(0f, playerFeetHeight) + Mathf.Max(0f, playerRadius);
                    PlayerHit?.Invoke(_live[i].Id, _live[i].Damage, at);
                    if (version != _version) return;
                }

                if (now >= settings.ArenaRadius)
                {
                    int id = _live[i].Id;
                    RemoveAt(_live, ref _liveCount, i);
                    Retired?.Invoke(id);
                    if (version != _version) return;
                    continue;
                }
                i++;
            }

            float bossStep = Mathf.Max(0f, bossDt);
            i = 0;
            while (i < _pendingCount)
            {
                _pending[i].Lead -= bossStep;
                if (_pending[i].Lead > 0f)
                {
                    i++;
                    continue;
                }

                Ring ring = _pending[i];
                ring.Lead = 0f;
                RemoveAt(_pending, ref _pendingCount, i);
                if (_liveCount >= _live.Length) EnsureCapacity(_liveCount + 1);
                _live[_liveCount++] = ring;
                Launched?.Invoke(ring.Id);
                if (version != _version) return;
            }
        }

        /// <summary>Live ring in <paramref name="slot"/> (0 = oldest, outermost),
        /// 0 ≤ slot &lt; <see cref="LiveCount"/>.</summary>
        public bool TryGetLive(int slot, out RingSnapshot ring)
        {
            if (slot < 0 || slot >= _liveCount)
            {
                ring = default;
                return false;
            }
            ring = ToSnapshot(in _live[slot]);
            return true;
        }

        /// <summary>Pending forecast in <paramref name="slot"/> (0 = launches
        /// first), 0 ≤ slot &lt; <see cref="PendingCount"/>.</summary>
        public bool TryGetPending(int slot, out RingSnapshot forecast)
        {
            if (slot < 0 || slot >= _pendingCount)
            {
                forecast = default;
                return false;
            }
            forecast = ToSnapshot(in _pending[slot]);
            return true;
        }

        /// <summary>A live ring or pending forecast by id.</summary>
        public bool TryGetById(int id, out RingSnapshot ring)
        {
            for (int i = 0; i < _liveCount; i++)
                if (_live[i].Id == id)
                {
                    ring = ToSnapshot(in _live[i]);
                    return true;
                }
            for (int i = 0; i < _pendingCount; i++)
                if (_pending[i].Id == id)
                {
                    ring = ToSnapshot(in _pending[i]);
                    return true;
                }
            ring = default;
            return false;
        }

        /// <summary>
        /// Drop every live ring and forecast, raising <see cref="Retired"/> for
        /// each, and end the fight: the next queued ring rolls a new spiral and
        /// starts at index 0. Idempotent. Safe to call from inside an event this
        /// field raised.
        /// </summary>
        public void Clear()
        {
            _version++;
            _fightStarted = false;
            _nextIndex = 0;
            _hasPlayerHistory = false;

            // Drain from the end and raise after removing, so a handler that
            // calls Clear again just continues the same drain.
            while (_liveCount > 0)
            {
                _liveCount--;
                Retired?.Invoke(_live[_liveCount].Id);
            }
            while (_pendingCount > 0)
            {
                _pendingCount--;
                Retired?.Invoke(_pending[_pendingCount].Id);
            }
        }

        void BeginFight(float snap)
        {
            int slots = snap > 0f ? Mathf.Max(1, Mathf.RoundToInt(360f / snap)) : 1;
            _firstGap = RingMath.NormaliseDegrees(_random.Next(slots) * Mathf.Max(0f, snap));
            _spiralSign = _random.Next(2) == 0 ? -1 : 1;
            _nextIndex = 0;
            _fightStarted = true;
        }

        void EnsureCapacity(int capacity)
        {
            if (_live.Length < capacity) Array.Resize(ref _live, capacity);
            if (_pending.Length < capacity) Array.Resize(ref _pending, capacity);
        }

        static void RemoveAt(Ring[] rings, ref int count, int index)
        {
            for (int k = index; k < count - 1; k++) rings[k] = rings[k + 1];
            count--;
        }

        static RingSnapshot ToSnapshot(in Ring r)
        {
            return new RingSnapshot(r.Id, r.Radius, r.Thickness, r.GapCentre, r.GapWidth,
                                    r.Tall, r.Burning, r.VisualHeight, r.Lead, r.Damage);
        }
    }
}
