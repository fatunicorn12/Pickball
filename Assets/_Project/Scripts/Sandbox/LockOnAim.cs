using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Measures a lock-on target from what the sword can actually hit, so the lock sits on the
    /// region a swing connects with rather than on whatever is drawn near it.
    ///
    /// A collider counts when <c>MeleeCombat.TestCapsule</c> would strike this target through it:
    /// enabled, not a trigger, on a layer the blade's mask includes, and with this target as the
    /// nearest damageable above it (a Hypatia orbiter is its own damageable, and lives under her).
    /// Anything under a <see cref="LockOnIgnore"/> is left out.
    ///
    /// The bounds come from each collider's own shape and transform, not <c>Collider.bounds</c>.
    /// <c>Physics.autoSyncTransforms</c> is off in this project, so <c>Collider.bounds</c> only
    /// updates at the physics step, and an aim read from it would stair-step on a moving target
    /// at any other frame rate. Read this way the point tracks the transform on the frame it moves,
    /// and it does not breathe: colliders here are capsules that animation doesn't touch.
    /// </summary>
    public static class LockOnAim
    {
        /// <summary>The chest as a fraction of the way from the middle of the body to its top.</summary>
        public const float DefaultChestBias = 0.2f;

        /// <summary>Height of the default chest above the root, m, for a target with nothing to
        /// measure.</summary>
        public const float DefaultChestHeight = 1.2f;

        /// <summary>Radius and half-height of that default target, m.</summary>
        public const float DefaultSize = 0.6f;

        /// <summary>
        /// Whether the blade could strike <paramref name="owner"/> through <paramref name="collider"/>.
        /// </summary>
        /// <param name="layerMask">The blade's mask (<c>MeleeCombat.HittableLayers</c>).</param>
        public static bool IsHittable(Collider collider, Health owner, int layerMask)
        {
            if (collider == null || owner == null) return false;
            if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) return false;
            if ((layerMask & (1 << collider.gameObject.layer)) == 0) return false;
            if (collider.GetComponentInParent<LockOnIgnore>() != null) return false;
            var damageable = collider.GetComponentInParent<IDamageable>();
            return damageable is Component component && component.gameObject == owner.gameObject;
        }

        /// <summary>
        /// World bounds of one collider from its shape and live transform (see the class notes).
        /// False when it has no shape, such as a mesh collider with no mesh.
        /// </summary>
        public static bool TryWorldBounds(Collider collider, out Bounds bounds)
        {
            Vector3 centre, extents;
            switch (collider)
            {
                case CapsuleCollider capsule:
                    centre = capsule.center;
                    extents = AxisExtents(capsule.radius, capsule.height, capsule.direction);
                    break;
                case CharacterController controller:
                    centre = controller.center;
                    extents = AxisExtents(controller.radius, controller.height, 1);
                    break;
                case SphereCollider sphere:
                    centre = sphere.center;
                    extents = Vector3.one * sphere.radius;
                    break;
                case BoxCollider box:
                    centre = box.center;
                    extents = box.size * 0.5f;
                    break;
                case MeshCollider mesh:
                    if (mesh.sharedMesh == null) { bounds = default; return false; }
                    centre = mesh.sharedMesh.bounds.center;
                    extents = mesh.sharedMesh.bounds.extents;
                    break;
                default:
                    bounds = collider.bounds;
                    return bounds.size != Vector3.zero;
            }

            // An axis-aligned box around the transformed box: each world extent is the sum of the
            // local extents projected onto that axis.
            Matrix4x4 m = collider.transform.localToWorldMatrix;
            var world = new Vector3(
                Mathf.Abs(m.m00) * extents.x + Mathf.Abs(m.m01) * extents.y + Mathf.Abs(m.m02) * extents.z,
                Mathf.Abs(m.m10) * extents.x + Mathf.Abs(m.m11) * extents.y + Mathf.Abs(m.m12) * extents.z,
                Mathf.Abs(m.m20) * extents.x + Mathf.Abs(m.m21) * extents.y + Mathf.Abs(m.m22) * extents.z);
            bounds = new Bounds(m.MultiplyPoint3x4(centre), world * 2f);
            return true;
        }

        static Vector3 AxisExtents(float radius, float height, int axis)
        {
            float half = Mathf.Max(height * 0.5f, radius);
            return axis switch
            {
                0 => new Vector3(half, radius, radius),
                2 => new Vector3(radius, radius, half),
                _ => new Vector3(radius, half, radius),
            };
        }

        /// <summary>
        /// The target's body from its hittable colliders. False when none is enabled.
        /// </summary>
        /// <param name="scratch">Reused list, so a per-frame caller does not allocate.</param>
        public static bool TryMeasureHittable(Health target, int layerMask, float chestBias,
                                              List<Collider> scratch, out LockOnBody body)
        {
            bool any = false;
            var total = new Bounds();
            target.GetComponentsInChildren(false, scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                var collider = scratch[i];
                if (!IsHittable(collider, target, layerMask)) continue;
                if (!TryWorldBounds(collider, out Bounds b)) continue;
                if (any) total.Encapsulate(b);
                else { total = b; any = true; }
            }
            scratch.Clear();
            body = any ? new LockOnBody(total, chestBias, true) : default;
            return any;
        }

        /// <summary>
        /// The fallback for a target with no hittable collider: every enabled mesh renderer under
        /// it, except particles, lines, trails and anything under a <see cref="LockOnIgnore"/>, or
        /// a default chest if nothing is left. These bounds breathe with the animation, so
        /// <see cref="LockOnTargeting"/> freezes them for a held lock.
        /// </summary>
        /// <param name="measured">False when it fell through to the default chest.</param>
        public static LockOnBody MeasureFallback(Health target, float chestBias, List<Renderer> scratch, out bool measured)
        {
            measured = false;
            var total = new Bounds();
            target.GetComponentsInChildren(false, scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                var r = scratch[i];
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!r.enabled) continue;
                if (r.GetComponentInParent<LockOnIgnore>() != null) continue;
                if (measured) total.Encapsulate(r.bounds);
                else { total = r.bounds; measured = true; }
            }
            scratch.Clear();
            if (measured) return new LockOnBody(total, chestBias, false);

            // Exact default chest, whatever the bias: the centre is set so the aim lands on it.
            Vector3 chest = target.transform.position + target.BodyOffset + Vector3.up * DefaultChestHeight;
            float bias = Mathf.Clamp01(chestBias);
            var size = new Bounds(chest - Vector3.up * (DefaultSize * bias), Vector3.one * (DefaultSize * 2f));
            return new LockOnBody(size, bias, false);
        }
    }
}
