using UnityEngine;

namespace Pickleball.Sandbox.Magic.Presentation
{
    /// <summary>
    /// Spawns a spell's optional model (<see cref="SpellDefinition.ModelPrefab"/>,
    /// <see cref="SpellDefinition.SecondaryModelPrefab"/>) as pure presentation:
    /// parented, scaled, and stripped of anything that could touch gameplay.
    ///
    /// Spells find what they hit with physics queries, so a collider left on an
    /// imported model (a Tripo mesh often carries one) would be "hit" by the
    /// spell's own sweep and burst it on launch. Colliders are disabled at once
    /// (Destroy only lands at the end of the frame) and then destroyed, and
    /// rigidbodies are removed, so a model can never fall, collide or be aimed at.
    /// </summary>
    public static class SpellModel
    {
        /// <summary>
        /// Instantiates <paramref name="prefab"/> under <paramref name="parent"/> at its
        /// local origin with a uniform <paramref name="scale"/>. Returns null when
        /// there is no prefab, so callers fall back to their procedural look.
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, float scale)
        {
            if (prefab == null || parent == null) return null;
            var model = Object.Instantiate(prefab, parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(body);
            foreach (var col in model.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = false;
                Object.Destroy(col);
            }
            return model;
        }
    }
}
