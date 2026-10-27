using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// When an opener or finisher is allowed to replace the hit it stands in for.
    ///
    /// Deliberately small. Kingdom Hearts 2's context rules are what make its
    /// combo feel clever, but every extra condition is one more reason the
    /// player cannot predict what the next press will do; a handful of distance
    /// and lock-on checks covers Sliding Dash, Hurricane Blast and the ranged
    /// finishers. The default value (all zero) is "always".
    /// </summary>
    [Serializable]
    public struct ComboCondition
    {
        [Tooltip("Only when lock-on has a target. Leave off for abilities that " +
                 "should also fire when swinging at nothing.")]
        [SerializeField] private bool requireLockOn;

        [Tooltip("Metres to the locked target's surface. The step only qualifies " +
                 "at or beyond this distance — a gap-closer opener uses it so it " +
                 "never fires point-blank. 0 = no minimum.")]
        [Min(0f)] [SerializeField] private float minTargetDistance;

        [Tooltip("Metres to the locked target's surface. The step only qualifies " +
                 "at or inside this distance. 0 = no maximum.")]
        [Min(0f)] [SerializeField] private float maxTargetDistance;

        public bool RequireLockOn => requireLockOn;
        public float MinTargetDistance => minTargetDistance;
        public float MaxTargetDistance => maxTargetDistance;

        /// <summary>Code-side constructor, for tests and runtime-built steps.</summary>
        public ComboCondition(bool requireLockOn, float minTargetDistance = 0f,
                              float maxTargetDistance = 0f)
        {
            this.requireLockOn = requireLockOn;
            this.minTargetDistance = Mathf.Max(0f, minTargetDistance);
            this.maxTargetDistance = Mathf.Max(0f, maxTargetDistance);
        }

        /// <summary>
        /// True if the step may play in this context. A distance limit implies
        /// a target: with nothing locked there is no distance to compare, so a
        /// ranged condition fails rather than guessing.
        /// </summary>
        public bool IsMetBy(in ComboContext context)
        {
            bool needsTarget = requireLockOn || minTargetDistance > 0f || maxTargetDistance > 0f;
            if (!needsTarget) return true;
            if (!context.HasTarget) return false;
            if (minTargetDistance > 0f && context.TargetDistance < minTargetDistance) return false;
            if (maxTargetDistance > 0f && context.TargetDistance > maxTargetDistance) return false;
            return true;
        }
    }
}
