using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// Conic Sections: HOW each conic volley plays out, once the brain has chosen
    /// WHEN and the needle has shown e. One volley at a time:
    /// <list type="bullet">
    /// <item><b>Parabola</b> (e = 1): lobs at the player's predicted position, one
    /// every Lob Stagger, each with a landing marker for its whole flight. Finished
    /// when the last lob lands.</item>
    /// <item><b>Ellipse</b> (e &lt; 1): orbiters on Kepler orbits with her at a focus,
    /// one every Orbiter Spawn Stagger, eccentricity near the needle's. They outlive
    /// the volley (Orbiter Lifetime); finished when the last has spawned.</item>
    /// <item><b>Hyperbola</b> (e &gt; 1): the asymptotes are telegraphed for Asymptote
    /// Lead, then one fast shot flies the curve. Finished when it leaves.</item>
    /// </list>
    ///
    /// Clocks: everything this component times itself (staggers, the marker lead
    /// delay, the asymptote countdown) runs on HER clock, <see cref="HypatiaContext.BossDelta"/>.
    /// Once released, a projectile runs on its own clock only, so freezing her does
    /// not freeze what she has thrown (Docs/Design/StationaryPoint.md).
    ///
    /// Gameplay only: presentation subscribes to the C# events below for markers,
    /// asymptote lines, trails and sounds. Ticks in Update; <see cref="Tick"/> is
    /// public so EditMode tests can drive it.
    /// </summary>
    [RequireComponent(typeof(OrbiterRegistry))]
    public class ConicCaster : MonoBehaviour, IConicAttack
    {
        /// <summary>Each orbiter's e is the needle's ± up to this (then clamped to the
        /// config's ellipse range), so a volley reads as one family of orbits without
        /// every orbit being identical. From the design brief.</summary>
        public const float OrbiterEccentricityJitter = 0.1f;

        [Header("Projectile prefabs (null = a bare object with the component)")]
        [Tooltip("The parabola lob. Needs ConicLob; visuals are the prefab's.")]
        [SerializeField] private ConicLob lobPrefab;
        [Tooltip("An elliptical orbiter. Needs ConicOrbiter.")]
        [SerializeField] private ConicOrbiter orbiterPrefab;
        [Tooltip("The hyperbola flyby shot. Needs HyperbolicShot.")]
        [SerializeField] private HyperbolicShot shotPrefab;

        [Header("Scene (optional)")]
        [Tooltip("Where lobs leave from (her hand or the astrolabe). Empty = her root.")]
        [SerializeField] private Transform launchPoint;

        // ─── Events for presentation ────────────────────────────────────────

        /// <summary>A lob has been aimed: show its landing marker at
        /// <c>landingPoint</c> (on the plate) for <c>secondsToImpact</c>. The marker
        /// leads by at least Lob Marker Lead; a slowed lob lands later than this
        /// says, so clear the marker on <see cref="LobLanded"/> rather than by timer.</summary>
        public event Action<Vector3, float> LobAimed;

        /// <summary>A lob landed and blasted at <c>at</c> with radius <c>radius</c>, m.</summary>
        public event Action<Vector3, float> LobLanded;

        /// <summary>A new orbiter is live. Its Orbit and Plane describe the whole ellipse.</summary>
        public event Action<ConicOrbiter> OrbiterSpawned;

        /// <summary>An orbiter is leaving (lifetime, live cap or StopAll) and is about
        /// to be despawned. Drop any reference to it.</summary>
        public event Action<ConicOrbiter> OrbiterRetired;

        /// <summary>The flyby is coming: draw the asymptotes, periapsis and path.</summary>
        public event Action<HyperbolaTelegraph> AsymptotesTelegraphed;

        /// <summary>The flyby shot is out (attach a trail to it).</summary>
        public event Action<HyperbolicShot> ShotReleased;

        /// <summary>The flyby shot left the arena and is about to be despawned.</summary>
        public event Action<HyperbolicShot> ShotFinished;

        /// <summary><see cref="StopAll"/> ran: clear every marker and telegraph.
        /// Orbiters also get <see cref="OrbiterRetired"/>; lobs and the shot just go.</summary>
        public event Action Stopped;

        /// <inheritdoc/>
        public event Action<ConicKind> VolleyFinished;

        // ─── State (all lazily rebuilt; non-serialized state is lost on reload) ─

        struct PendingLob
        {
            public Vector3 Target;
            public float LaunchIn;
        }

        HypatiaContext _context;
        System.Random _random;
        OrbiterRegistry _registry;

        ConicKind _kind;
        bool _busy;

        // Parabola
        List<PendingLob> _pendingLobs;
        List<ConicLob> _liveLobs;
        int _lobsToAim;
        float _nextAimIn;

        // Ellipse
        int _orbitersToSpawn;
        float _nextSpawnIn;
        float _volleyEccentricity;

        // Hyperbola
        ConicPolyline _path;
        bool _shotPending;
        float _releaseIn;
        HyperbolicShot _liveShot;

        List<PendingLob> PendingLobs => _pendingLobs ??= new List<PendingLob>(4);
        List<ConicLob> LiveLobs => _liveLobs ??= new List<ConicLob>(4);
        ConicPolyline Path => _path ??= new ConicPolyline();
        System.Random Rng => _random ??= new System.Random();

        /// <summary>The live-orbiter registry on this root.</summary>
        public OrbiterRegistry Registry
        {
            get
            {
                if (_registry == null) _registry = GetComponent<OrbiterRegistry>();
                return _registry;
            }
        }

        /// <inheritdoc/>
        public bool IsBusy => _busy;

        /// <summary>The kind of the current (or last) volley.</summary>
        public ConicKind CurrentKind => _kind;

        /// <summary>Lobs in flight now.</summary>
        public int LiveLobCount => LiveLobs.Count;

        /// <summary>The i-th lob in flight, oldest first (a marker that wants to
        /// follow its lob's own clock can read <see cref="ConicLob.Age"/>).</summary>
        public ConicLob LiveLobAt(int index) => LiveLobs[index];

        /// <summary>The flyby shot in flight, or null.</summary>
        public HyperbolicShot LiveShot => _liveShot;

        /// <inheritdoc/>
        public void Bind(HypatiaContext context) => _context = context;

        /// <summary>Use this random source for orbiter counts, shapes and hyperbola
        /// orientation (tests seed it; default is time-seeded).</summary>
        public void SetRandom(System.Random random) => _random = random;

        /// <inheritdoc/>
        public void Fire(ConicKind kind, float eccentricity)
        {
            if (_context == null || _context.Config == null)
            {
                Debug.LogWarning($"[ConicCaster] Fire({kind}) before Bind; ignored.", this);
                return;
            }
            if (_busy)
            {
                Debug.LogWarning($"[ConicCaster] Fire({kind}) while a {_kind} volley is running; ignored.", this);
                return;
            }

            _kind = kind;
            _busy = true;
            switch (kind)
            {
                case ConicKind.Parabola: BeginParabola(); break;
                case ConicKind.Ellipse: BeginEllipse(eccentricity); break;
                case ConicKind.Hyperbola: BeginHyperbola(eccentricity); break;
            }
            // Act on anything due now (first lob, first orbiter, a zero lead) in
            // the same frame as the needle fires, not one frame late.
            if (_busy) StepVolley(0f);
        }

        /// <inheritdoc/>
        public void StopAll()
        {
            _busy = false;
            _lobsToAim = 0;
            _orbitersToSpawn = 0;
            _shotPending = false;
            PendingLobs.Clear();

            for (int i = LiveLobs.Count - 1; i >= 0; i--)
            {
                ConicLob lob = LiveLobs[i];
                if (lob == null) continue;
                lob.Landed -= OnLobLanded;
                lob.Retire();
                ConicObjects.Despawn(lob);
            }
            LiveLobs.Clear();

            if (_liveShot != null)
            {
                _liveShot.Left -= OnShotLeft;
                _liveShot.Retire();
                ConicObjects.Despawn(_liveShot);
            }
            _liveShot = null;

            if (Registry != null)
            {
                Registry.PruneDestroyed();
                while (Registry.Count > 0) Retire(Registry.Oldest);
            }

            Stopped?.Invoke();
        }

        void Update() => Tick(Time.deltaTime);

        // Disabling (or a script reload, which disables first) ends everything
        // cleanly instead of orphaning projectiles that no longer have an owner.
        void OnDisable() => StopAll();

        /// <summary>
        /// Advance the volley by <paramref name="deltaTime"/> real seconds; her
        /// clock scales it. Called from Update; public for EditMode tests.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (Registry != null) Registry.PruneDestroyed();
            if (!_busy || _context == null) return;
            StepVolley(_context.BossDelta(deltaTime));
        }

        void StepVolley(float bossDelta)
        {
            switch (_kind)
            {
                case ConicKind.Parabola: StepParabola(bossDelta); break;
                case ConicKind.Ellipse: StepEllipse(bossDelta); break;
                case ConicKind.Hyperbola: StepHyperbola(bossDelta); break;
            }
        }

        void Finish()
        {
            if (!_busy) return;
            _busy = false;
            VolleyFinished?.Invoke(_kind);
        }

        Vector3 BossPosition => _context.Boss != null ? _context.Boss.position : transform.position;

        // ─── Parabola ───────────────────────────────────────────────────────

        void BeginParabola()
        {
            PendingLobs.Clear();
            _lobsToAim = Mathf.Max(1, _context.Config.LobCount(_context.PhaseTwo));
            _nextAimIn = 0f;
        }

        void StepParabola(float bossDelta)
        {
            HypatiaConfig cfg = _context.Config;

            _nextAimIn -= bossDelta;
            while (_busy && _lobsToAim > 0 && _nextAimIn <= 0f)
            {
                _lobsToAim--;
                _nextAimIn += cfg.LobStagger;
                AimLob();
            }

            for (int i = PendingLobs.Count - 1; i >= 0; i--)
            {
                PendingLob pending = PendingLobs[i];
                pending.LaunchIn -= bossDelta;
                if (pending.LaunchIn > 0f)
                {
                    PendingLobs[i] = pending;
                    continue;
                }
                PendingLobs.RemoveAt(i);
                LaunchLob(pending.Target);
            }

            TryFinishParabola();
        }

        /// <summary>
        /// Predict, raise the marker, and queue the launch. The marker must lead the
        /// impact by Lob Marker Lead, so when the flight is shorter than that the
        /// launch waits the difference (on her clock), and the lead is predicted
        /// over the whole time to impact rather than the flight alone.
        /// </summary>
        void AimLob()
        {
            HypatiaConfig cfg = _context.Config;
            float flight = Mathf.Max(BallisticSolve.MinFlightTime, cfg.LobFlightTime);
            float toImpact = Mathf.Max(flight, cfg.LobMarkerLead);

            Vector3 player = _context.Player != null ? _context.Player.position : _context.Centre;
            Vector3 target = BallisticSolve.PredictTarget(player, _context.PlayerVelocity.Velocity,
                                                          toImpact, cfg.LobLeadFraction, cfg.LobMaxLead);
            target = BallisticSolve.ClampToDisc(target, _context.Centre, cfg.ArenaRadius);
            target.y = _context.FloorY;

            LobAimed?.Invoke(target, toImpact);

            float launchIn = toImpact - flight;
            if (launchIn <= 0f) LaunchLob(target);
            else PendingLobs.Add(new PendingLob { Target = target, LaunchIn = launchIn });
        }

        void LaunchLob(Vector3 target)
        {
            HypatiaConfig cfg = _context.Config;
            Vector3 origin = launchPoint != null ? launchPoint.position : BossPosition;
            ConicLob lob = ConicObjects.Spawn(lobPrefab, origin, "ConicLob");
            lob.Launch(_context, origin, target, cfg.LobFlightTime, cfg.LobGravity, cfg.LobBlastRadius, cfg.LobDamage);
            lob.Landed += OnLobLanded;
            LiveLobs.Add(lob);
        }

        void OnLobLanded(ConicLob lob)
        {
            lob.Landed -= OnLobLanded;
            LiveLobs.Remove(lob);
            LobLanded?.Invoke(lob.Target, lob.BlastRadius);
            lob.Retire();
            ConicObjects.Despawn(lob);
            TryFinishParabola();
        }

        void TryFinishParabola()
        {
            if (_busy && _kind == ConicKind.Parabola && _lobsToAim == 0 &&
                PendingLobs.Count == 0 && LiveLobs.Count == 0)
                Finish();
        }

        // ─── Ellipse ────────────────────────────────────────────────────────

        void BeginEllipse(float eccentricity)
        {
            HypatiaConfig cfg = _context.Config;
            _orbitersToSpawn = Rng.Next(cfg.OrbiterCountMin, cfg.OrbiterCountMax + 1);
            _nextSpawnIn = 0f;
            _volleyEccentricity = eccentricity;
        }

        void StepEllipse(float bossDelta)
        {
            _nextSpawnIn -= bossDelta;
            while (_busy && _orbitersToSpawn > 0 && _nextSpawnIn <= 0f)
            {
                _orbitersToSpawn--;
                _nextSpawnIn += _context.Config.OrbiterSpawnStagger;
                SpawnOrbiter();
            }
            if (_busy && _orbitersToSpawn == 0) Finish();
        }

        void SpawnOrbiter()
        {
            HypatiaConfig cfg = _context.Config;
            OrbiterRegistry registry = Registry;

            registry.PruneDestroyed();
            while (registry.Count >= Mathf.Max(1, cfg.MaxLiveOrbiters)) Retire(registry.Oldest);

            float jitter = ((float)Rng.NextDouble() * 2f - 1f) * OrbiterEccentricityJitter;
            float e = Mathf.Clamp(_volleyEccentricity + jitter, cfg.EllipseEccentricityMin, cfg.EllipseEccentricityMax);
            OrbitShape shape = OrbitFit.Fit(e, cfg.OrbitPeriapsisMin, cfg.OrbitApoapsisMax, (float)Rng.NextDouble());
            float period = Mathf.Lerp(cfg.OrbitPeriodMin, cfg.OrbitPeriodMax, (float)Rng.NextDouble());
            float angle = (float)(Rng.NextDouble() * 2.0 * Math.PI);
            int sense = Rng.NextDouble() < 0.5 ? 1 : -1;

            Vector3 her = BossPosition;
            var plane = new ConicPlane(new Vector3(her.x, _context.FloorY + cfg.OrbitHeight, her.z), angle, sense);
            var orbit = new KeplerOrbit(shape.SemiMajor, shape.Eccentricity, period);

            ConicOrbiter orbiter = ConicObjects.Spawn(orbiterPrefab, plane.ToWorld(orbit.PositionAt(0f)), "ConicOrbiter");
            orbiter.Launch(_context, orbit, plane, cfg.OrbiterLifetime, cfg.OrbiterRadius,
                           cfg.OrbiterDamage, cfg.OrbiterRehitDelay);
            orbiter.Expired += OnOrbiterExpired;
            registry.Register(orbiter);
            OrbiterSpawned?.Invoke(orbiter);
        }

        void OnOrbiterExpired(ConicOrbiter orbiter) => Retire(orbiter);

        void Retire(ConicOrbiter orbiter)
        {
            if (orbiter == null)
            {
                Registry.PruneDestroyed();
                return;
            }
            orbiter.Expired -= OnOrbiterExpired;
            Registry.Unregister(orbiter);
            orbiter.Retire();
            OrbiterRetired?.Invoke(orbiter);
            ConicObjects.Despawn(orbiter);
        }

        // ─── Hyperbola ──────────────────────────────────────────────────────

        void BeginHyperbola(float eccentricity)
        {
            HypatiaConfig cfg = _context.Config;
            float e = eccentricity > 1f + ConicPath.ParabolaTolerance * 10f ? eccentricity : cfg.HyperbolaEccentricityMin;
            var conic = new ConicPath(cfg.HyperbolaPeriapsis, e);

            Vector3 her = BossPosition;
            var focus = new Vector3(her.x, _context.FloorY + cfg.HyperbolaHeight, her.z);
            int sense = Rng.NextDouble() < 0.5 ? 1 : -1;

            float angle;
            if (cfg.HyperbolaExitAtPlayer && _context.Player != null)
            {
                Vector3 p = _context.Player.position;
                angle = HyperbolaFlyby.ExitAngleTowards(conic, new Vector2(p.x - focus.x, p.z - focus.z), sense);
            }
            else
            {
                angle = (float)(Rng.NextDouble() * 2.0 * Math.PI);
            }

            var plane = new ConicPlane(focus, angle, sense);
            HyperbolaFlyby.FillPath(Path, conic, plane, cfg.HyperbolaReach);

            _shotPending = true;
            _releaseIn = cfg.AsymptoteLead;
            AsymptotesTelegraphed?.Invoke(HyperbolaFlyby.Telegraph(conic, plane, Path, cfg.AsymptoteLead));
        }

        void StepHyperbola(float bossDelta)
        {
            if (!_shotPending) return;
            _releaseIn -= bossDelta;
            if (_releaseIn > 0f) return;

            _shotPending = false;
            HypatiaConfig cfg = _context.Config;
            HyperbolicShot shot = ConicObjects.Spawn(shotPrefab, Path.PointAt(0f), "HyperbolicShot");
            shot.Launch(_context, Path, cfg.HyperbolaShotSpeed, cfg.HyperbolaShotRadius, cfg.HyperbolaDamage);
            shot.Left += OnShotLeft;
            _liveShot = shot;
            ShotReleased?.Invoke(shot);
        }

        void OnShotLeft(HyperbolicShot shot)
        {
            shot.Left -= OnShotLeft;
            if (_liveShot == shot) _liveShot = null;
            ShotFinished?.Invoke(shot);
            shot.Retire();
            ConicObjects.Despawn(shot);
            if (_kind == ConicKind.Hyperbola) Finish();
        }
    }
}
