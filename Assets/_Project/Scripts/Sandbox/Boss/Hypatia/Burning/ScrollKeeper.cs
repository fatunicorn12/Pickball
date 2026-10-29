using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Sandbox.Boss.Hypatia.Burning
{
    /// <summary>
    /// The optional burning scrolls of Alexandria's Burning: spawns them in the
    /// burned bands, caps how many are alive, charges the player for collecting
    /// one, and counts them for the fight. Lives on Hypatia's root beside
    /// <see cref="ArenaBurn"/>, whose state it reads; ArenaBurn knows nothing
    /// about scrolls, and the brain does not drive this (it is not an attack).
    ///
    /// Off unless <see cref="HypatiaConfig.ScrollsEnabled"/>. When on, while the
    /// fight is live and at least one band is burned, a scroll appears every
    /// <see cref="HypatiaConfig.ScrollInterval"/> on HER clock (they are her
    /// fire's doing) at a random point on the burned floor, up to
    /// <see cref="HypatiaConfig.MaxLiveScrolls"/> (<see cref="ScrollSpawnRule"/>).
    ///
    /// Collecting one costs <see cref="HypatiaConfig.ScrollHealthCost"/> as
    /// PERMANENT damage (not recoverable: a scroll is a choice, not a hit to win
    /// back) and marked continuous, so a pickup neither opens hit grace nor is
    /// refused by it. It grants nothing now; <see cref="CollectedThisFight"/>
    /// is per component instance (no static state), for a future Academy
    /// library to read at the end of the fight.
    ///
    /// The keeper POLLS <see cref="ArenaBurn.IsStopped"/> each frame instead of
    /// subscribing to its Extinguished event, so there is no subscription to
    /// lose on a script reload or to order against the brain's Bind: once the
    /// burn stops, every live scroll is despawned and none spawns again.
    ///
    /// Presentation listens to <see cref="ScrollSpawned"/>,
    /// <see cref="ScrollCollected"/> and <see cref="ScrollDespawning"/>.
    /// </summary>
    public class ScrollKeeper : MonoBehaviour
    {
        [Tooltip("The floor fire on the same root. Found on this GameObject when empty.")]
        [SerializeField] private ArenaBurn burn;

        [Tooltip("Prefab with a BurningScroll (and its look). Empty = a bare, invisible " +
                 "scroll made at runtime, which still works for gameplay.")]
        [SerializeField] private BurningScroll scrollPrefab;

        /// <summary>A scroll appeared (already positioned and launched).</summary>
        public event Action<BurningScroll> ScrollSpawned;

        /// <summary>The player collected a scroll; arg is the fight's running
        /// total, counting this one. Raised once per scroll.</summary>
        public event Action<int> ScrollCollected;

        /// <summary>A scroll is about to be destroyed: collected (true) or burned
        /// away / swept up when the fight ended (false). Raised before the
        /// GameObject goes, so a visual can detach and fade.</summary>
        public event Action<BurningScroll, bool> ScrollDespawning;

        List<BurningScroll> _live;
        float _spawnTimer;
        int _collectedThisFight;
        System.Random _rng;

        /// <summary>The floor fire this keeper reads.</summary>
        public ArenaBurn Burn => burn;

        /// <summary>The fight context, through <see cref="Burn"/>; null before
        /// the brain binds it.</summary>
        public HypatiaContext Context => burn != null ? burn.Context : null;

        /// <summary>Scrolls alive now.</summary>
        public int LiveCount => _live?.Count ?? 0;

        /// <summary>A live scroll by index (0 to <see cref="LiveCount"/> - 1).</summary>
        public BurningScroll GetLive(int index) => _live[index];

        /// <summary>Scrolls the player has collected in this fight.</summary>
        public int CollectedThisFight => _collectedThisFight;

        /// <summary>Make spawn points and drift repeatable (tests, replays).</summary>
        public void SeedRandom(int seed) => _rng = new System.Random(seed);

        void Awake()
        {
            if (burn == null) burn = GetComponent<ArenaBurn>();
        }

        void Update() => Tick(Time.deltaTime);

        void OnDestroy() => DespawnAll();

        /// <summary>
        /// Advance by <paramref name="dt"/> real seconds (the spawn timer runs on
        /// her clock). Update calls it with Time.deltaTime; public so a test or
        /// harness can drive time.
        /// </summary>
        public void Tick(float dt)
        {
            PruneDestroyed();
            if (burn == null) return;
            if (burn.IsStopped)
            {
                _spawnTimer = 0f;
                if (LiveCount > 0) DespawnAll();
                return;
            }

            var context = burn.Context;
            if (context?.Config == null) return;
            var config = context.Config;

            bool gate = ScrollSpawnRule.GateOpen(config.ScrollsEnabled, burn.IsStopped, burn.BurnedBands);
            _spawnTimer = ScrollSpawnRule.Advance(_spawnTimer, context.BossDelta(dt), config.ScrollInterval,
                                                  gate, LiveCount, config.MaxLiveScrolls, out bool spawn);
            if (spawn) Spawn(context);
        }

        /// <summary>
        /// The player collected <paramref name="scroll"/>: charge the cost, count
        /// it and despawn it. Called by the scroll's swept pickup test. Returns
        /// false, changing nothing, for a scroll that is not this keeper's, is
        /// already finished (so a scroll counts once), or after the fight ended.
        /// </summary>
        public bool Collect(BurningScroll scroll)
        {
            if (scroll == null || scroll.IsFinished || scroll.Keeper != this) return false;
            if (burn == null || burn.IsStopped) return false;

            _live?.Remove(scroll);
            scroll.MarkFinished();

            var context = Context;
            if (context?.PlayerHealth != null && context.Config != null)
                context.PlayerHealth.Damage(context.Config.ScrollHealthCost, scroll.transform.position,
                                            recoverable: false, continuous: true);

            _collectedThisFight++;
            ScrollCollected?.Invoke(_collectedThisFight);
            Despawn(scroll, collected: true);
            return true;
        }

        /// <summary>The scroll's lifetime ran out: it burns away.</summary>
        internal void Expire(BurningScroll scroll)
        {
            if (scroll == null || scroll.IsFinished || scroll.Keeper != this) return;
            _live?.Remove(scroll);
            scroll.MarkFinished();
            Despawn(scroll, collected: false);
        }

        /// <summary>Remove every live scroll (no cost, not counted).</summary>
        public void DespawnAll()
        {
            if (_live == null) return;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var scroll = _live[i];
                _live.RemoveAt(i);
                if (scroll == null) continue;
                scroll.MarkFinished();
                Despawn(scroll, collected: false);
            }
        }

        void Spawn(HypatiaContext context)
        {
            _rng ??= new System.Random();
            if (!burn.TryPointOnBurnedFloor(Next01(), Next01(), Next01(), out Vector3 offset)) return;

            var config = context.Config;
            Vector3 centre = context.Centre;
            var position = new Vector3(centre.x + offset.x, context.FloorY + config.ScrollHeight, centre.z + offset.z);

            BurningScroll scroll = scrollPrefab != null
                ? Instantiate(scrollPrefab, position, Quaternion.identity)
                : new GameObject("BurningScroll").AddComponent<BurningScroll>();
            scroll.transform.position = position;
            scroll.Launch(this, _rng.Next(2) == 0 ? -1f : 1f, Next01() * 100f,
                          config.ScrollLifetime, config.ScrollPickupRadius);

            (_live ??= new List<BurningScroll>(Mathf.Max(1, config.MaxLiveScrolls))).Add(scroll);
            ScrollSpawned?.Invoke(scroll);
        }

        float Next01() => (float)_rng.NextDouble();

        void Despawn(BurningScroll scroll, bool collected)
        {
            ScrollDespawning?.Invoke(scroll, collected);
            if (scroll == null) return; // a listener destroyed it
            if (Application.isPlaying) Destroy(scroll.gameObject);
            else Object.DestroyImmediate(scroll.gameObject);
        }

        void PruneDestroyed()
        {
            if (_live == null) return;
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i] == null) _live.RemoveAt(i);
        }
    }
}
