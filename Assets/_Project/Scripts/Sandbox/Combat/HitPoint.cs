using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Where on a collider a sword hit lands — the point nearest the blade or
    /// the ring's centre — without tripping Physics.ClosestPoint's limits.
    ///
    /// Unity's ClosestPoint only works on box, sphere, capsule and CONVEX mesh
    /// colliders. On anything else (a level's non-convex MeshCollider, a
    /// CharacterController, terrain) it logs "Physics.ClosestPoint can only be
    /// used with…" and returns a meaningless point. The burst ring used to call
    /// it on every collider its overlap sphere touched, map geometry included,
    /// which flooded the console (~1,300 warnings in one TrainingStage session)
    /// and mis-measured the ring's reach. Callers now resolve the target first
    /// and come here only for colliders that belong to one; unsupported shapes
    /// fall back to the nearest point on their world bounds, which is exact for
    /// a box and close for the capsule-ish bodies characters use.
    /// </summary>
    public static class HitPoint
    {
        /// <summary>True for the collider types Physics.ClosestPoint supports.</summary>
        public static bool SupportsClosestPoint(Collider collider) => collider switch
        {
            BoxCollider or SphereCollider or CapsuleCollider => true,
            MeshCollider mesh => mesh.convex,
            _ => false,
        };

        /// <summary>The point on <paramref name="collider"/> nearest
        /// <paramref name="from"/>: exact where Unity supports it, otherwise the
        /// nearest point on its world-space bounds. Never logs.</summary>
        public static Vector3 Nearest(Collider collider, Vector3 from)
            => SupportsClosestPoint(collider) ? collider.ClosestPoint(from) : collider.bounds.ClosestPoint(from);
    }
}
