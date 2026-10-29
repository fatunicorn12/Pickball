using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// Spawning and despawning for the conic projectiles, in one place so both
    /// rules hold everywhere: a missing prefab still yields a working projectile
    /// (a bare GameObject with the component, so tests and a half-wired scene run),
    /// and despawning works outside Play mode, where <c>Destroy</c> is refused and
    /// EditMode tests would otherwise leak objects. Stateless.
    /// </summary>
    internal static class ConicObjects
    {
        public static T Spawn<T>(T prefab, Vector3 position, string fallbackName) where T : HypatiaProjectile
        {
            if (prefab != null) return Object.Instantiate(prefab, position, Quaternion.identity);

            var go = new GameObject(fallbackName);
            go.transform.position = position;
            return go.AddComponent<T>(); // RequireComponent adds the SphereCollider
        }

        public static void Despawn(Component component)
        {
            if (component == null) return;
            if (Application.isPlaying) Object.Destroy(component.gameObject);
            else Object.DestroyImmediate(component.gameObject);
        }
    }
}
