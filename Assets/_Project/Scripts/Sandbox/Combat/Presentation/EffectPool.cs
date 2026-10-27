using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// Reuses <see cref="PooledEffect"/> instances per prefab, so a five-hit
    /// chain does not instantiate five "+" pops every time. Instances are
    /// created on demand and kept under one root; an instance is free whenever
    /// it is inactive.
    ///
    /// A plain class owned by <see cref="SwordEffects"/>, which builds it in
    /// OnEnable and calls <see cref="Clear"/> in OnDisable — so a script reload
    /// in Play mode tears the pool down and rebuilds it instead of orphaning it.
    /// </summary>
    public sealed class EffectPool
    {
        readonly Transform _root;
        readonly Dictionary<GameObject, List<PooledEffect>> _byPrefab = new();

        public EffectPool(Transform root) => _root = root;

        /// <summary>Play <paramref name="prefab"/> for <paramref name="cue"/>.
        /// Does nothing for a null prefab, or one with no PooledEffect on it.</summary>
        public void Play(GameObject prefab, in EffectCue cue)
        {
            if (prefab == null || _root == null) return;
            if (!_byPrefab.TryGetValue(prefab, out var list))
                _byPrefab[prefab] = list = new List<PooledEffect>();

            PooledEffect free = null;
            foreach (var e in list)
                if (e != null && !e.IsPlaying) { free = e; break; }

            if (free == null)
            {
                var go = Object.Instantiate(prefab, _root);
                free = go.GetComponent<PooledEffect>();
                if (free == null)
                {
                    Debug.LogWarning($"[EffectPool] '{prefab.name}' has no PooledEffect; not played.");
                    Object.Destroy(go);
                    return;
                }
                list.Add(free);
            }

            free.transform.SetPositionAndRotation(cue.Position, cue.Rotation);
            free.gameObject.SetActive(true);
            free.Play(cue);
        }

        /// <summary>Destroy every instance.</summary>
        public void Clear()
        {
            foreach (var list in _byPrefab.Values)
                foreach (var e in list)
                    if (e != null) Object.Destroy(e.gameObject);
            _byPrefab.Clear();
        }
    }
}
