using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Burning
{
    /// <summary>
    /// One burning scroll from the Library of Alexandria, drifting in the fire
    /// (optional, behind <see cref="HypatiaConfig.ScrollsEnabled"/>). Touching it
    /// collects it, which costs health and grants nothing in this fight: the
    /// count is kept by <see cref="ScrollKeeper"/> for a future Academy library.
    ///
    /// A <see cref="HypatiaProjectile"/>, so it is slowable on its OWN clock
    /// (the Freeze Zone finds its trigger sphere) and moves, ages and burns
    /// away by <see cref="HypatiaProjectile.ScaledDeltaTime"/>; slowing her does
    /// not slow it. A frozen scroll can still be collected by walking into it.
    ///
    /// Simulation only: it raises nothing itself and draws nothing. The keeper
    /// raises the spawn, collect and despawn events; a visual on the prefab (or
    /// a listener to <see cref="ScrollKeeper.ScrollSpawned"/>) draws it. Every
    /// fight reference is read through the keeper each frame, so a script
    /// reload that empties the context just idles the scroll until the brain
    /// re-binds.
    /// </summary>
    public class BurningScroll : HypatiaProjectile
    {
        // A player displacement larger than this in one frame is a teleport
        // (respawn), not motion: no dash covers 4 m in a frame above 10 fps.
        // Sweeping across it would collect scrolls the player never touched.
        const float TeleportDistance = 4f;

        [Header("Drift")]
        [Tooltip("Largest swing off the circling direction, °. 0 = perfect circles " +
                 "round the arena; higher = a lazier, less predictable wander.")]
        [SerializeField, Range(0f, 180f)] private float wanderDegrees = 50f;

        [Tooltip("How fast the wander changes, 1/s. Higher = twitchier drift.")]
        [SerializeField, Min(0f)] private float wanderRate = 0.3f;

        ScrollKeeper _keeper;
        float _age;
        float _lifetime;
        float _direction = 1f;
        float _seed;
        Vector3 _lastPlayer;
        bool _hasLastPlayer;
        bool _finished;

        /// <summary>The keeper that spawned it; null before <see cref="Launch"/>.</summary>
        public ScrollKeeper Keeper => _keeper;

        /// <summary>Seconds lived, on the scroll's own clock.</summary>
        public float Age => _age;

        /// <summary>Seconds it lives before burning away, on its own clock.</summary>
        public float Lifetime => _lifetime;

        /// <summary>True once collected, burned away or despawned. A finished
        /// scroll never moves, collects or expires again.</summary>
        public bool IsFinished => _finished;

        /// <summary>
        /// Start it drifting. Called once by <see cref="ScrollKeeper"/> right
        /// after spawning it at its start point.
        /// </summary>
        /// <param name="direction">+1 circles clockwise in the top view, -1 anticlockwise.</param>
        /// <param name="seed">Wander noise offset, so no two scrolls drift alike.</param>
        /// <param name="lifetime">Seconds on its own clock.</param>
        /// <param name="pickupRadius">Pickup radius, m; also sizes the slow trigger.</param>
        public void Launch(ScrollKeeper keeper, float direction, float seed, float lifetime, float pickupRadius)
        {
            _keeper = keeper;
            _direction = direction < 0f ? -1f : 1f;
            _seed = seed;
            _lifetime = Mathf.Max(0f, lifetime);
            _age = 0f;
            _hasLastPlayer = false;
            _finished = false;
            SetSlowTriggerRadius(pickupRadius);
        }

        void Update() => Tick(ScaledDeltaTime);

        /// <summary>
        /// Advance by <paramref name="dt"/> seconds on the scroll's clock:
        /// drift, test the pickup (swept), then burn away when its time is up.
        /// Update calls it with <see cref="HypatiaProjectile.ScaledDeltaTime"/>;
        /// public so a test or harness can drive time.
        /// </summary>
        public void Tick(float dt)
        {
            if (_finished || _keeper == null) return;
            var context = _keeper.Context;
            var burn = _keeper.Burn;
            if (context?.Config == null || burn == null) return;
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;

            var config = context.Config;
            Vector3 centre = context.Centre;
            Vector3 from = transform.position;
            Vector3 to = from;
            if (dt > 0f)
            {
                _age += dt;
                Vector3 offset = ScrollMotion.Drift(from - centre, _age, dt, config.ScrollDriftSpeed,
                                                    _direction, _seed, wanderDegrees, wanderRate,
                                                    burn.SafeRadius, burn.ArenaRadius);
                to = new Vector3(centre.x + offset.x, context.FloorY + config.ScrollHeight, centre.z + offset.z);
                transform.position = to;
            }

            if (TouchesPlayer(context, from, to, config.ScrollPickupRadius))
            {
                _keeper.Collect(this);
                return;
            }
            if (_age >= _lifetime) _keeper.Expire(this);
        }

        bool TouchesPlayer(HypatiaContext context, Vector3 from, Vector3 to, float pickupRadius)
        {
            if (!context.TryGetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float radius))
            {
                _hasLastPlayer = false;
                return false;
            }

            Vector3 playerNow = context.Player.position;
            Vector3 playerFrom = _hasLastPlayer && (playerNow - _lastPlayer).sqrMagnitude
                                 <= TeleportDistance * TeleportDistance
                ? _lastPlayer
                : playerNow;
            _lastPlayer = playerNow;
            _hasLastPlayer = true;
            return ScrollMotion.Touches(from, to, playerFrom, playerNow, pickupRadius, bottom, top, radius);
        }

        /// <summary>Stop it for good; the keeper calls this before despawning it.</summary>
        internal void MarkFinished() => _finished = true;
    }
}
