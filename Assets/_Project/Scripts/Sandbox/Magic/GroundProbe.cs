using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// Finds the floor under a point, for spells that land on the ground (the
    /// Freeze Zone, the Thunderbolt's strike point). The crosshair often rests on
    /// a wall, a target's chest or empty sky; the spell belongs on the ground
    /// beneath it.
    /// </summary>
    public static class GroundProbe
    {
        // Start just above the point, so a point lying exactly on the floor
        // still finds that floor.
        const float StartLift = 0.25f;

        /// <summary>
        /// Casts straight down from just above <paramref name="point"/> up to
        /// <paramref name="maxDrop"/> metres and returns the nearest hit that is
        /// not under <paramref name="ignore"/> (the caster). When nothing is
        /// found, returns the point itself and false (spells use
        /// <see cref="TryGroundTarget"/>, which refuses then). Triggers are ignored.
        /// </summary>
        /// <param name="buffer">Caller-owned hit buffer (no allocation).</param>
        public static bool TrySnap(Vector3 point, float maxDrop, LayerMask mask, Transform ignore,
                                   RaycastHit[] buffer, out Vector3 ground)
        {
            ground = point;
            if (buffer == null || buffer.Length == 0 || maxDrop <= 0f) return false;

            Vector3 from = point + Vector3.up * StartLift;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, buffer, maxDrop + StartLift, mask,
                                                QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = buffer[i];
                if (hit.collider == null) continue;
                if (ignore != null && hit.collider.transform.IsChildOf(ignore)) continue;
                if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;   // floors, not bodies
                if (hit.distance >= best) continue;
                best = hit.distance;
                ground = hit.point;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Where a ground-targeted spell (the Freeze Zone, an untargeted
        /// Thunderbolt) may land for an aim at <paramref name="point"/>: the floor
        /// straight under it (<see cref="TrySnap"/>, up to
        /// <paramref name="maxDrop"/>), no further than <paramref name="maxRange"/>
        /// metres across the ground from <paramref name="caster"/>.
        ///
        /// Returns false when there is no such floor: the crosshair on the sky, on
        /// a pit deeper than the probe, or on ground out of range. The caller must
        /// then refuse the cast (free, with the can't-cast feedback) rather than
        /// place the spell in mid-air at the aim point. <paramref name="ground"/>
        /// is the point itself on failure, and must not be used.
        /// </summary>
        /// <param name="buffer">Caller-owned hit buffer (no allocation).</param>
        public static bool TryGroundTarget(Vector3 point, Vector3 caster, float maxRange, float maxDrop,
                                           LayerMask mask, Transform ignore, RaycastHit[] buffer, out Vector3 ground)
        {
            if (!TrySnap(point, maxDrop, mask, ignore, buffer, out ground)) return false;
            Vector3 across = ground - caster;
            across.y = 0f;
            return maxRange > 0f && across.sqrMagnitude <= maxRange * maxRange;
        }

        /// <summary>The bottom-centre of <paramref name="target"/>'s collider bounds
        /// (its pivot when it has no enabled collider): where its feet are.</summary>
        public static Vector3 FeetOf(Transform target)
        {
            if (target == null) return Vector3.zero;
            if (target.TryGetComponent(out Collider col) && col.enabled)
            {
                Bounds b = col.bounds;
                return new Vector3(b.center.x, b.min.y, b.center.z);
            }
            return target.position;
        }
    }
}
