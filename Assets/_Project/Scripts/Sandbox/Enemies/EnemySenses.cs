using UnityEngine;

namespace Pickleball.Sandbox.Enemies
{
    /// <summary>
    /// Everything an enemy knows when it decides what to do this frame. Gathered once
    /// by <see cref="Enemy"/> and handed to its hooks, so a subclass's rules read the
    /// same facts as the base rules and can be tested without a scene.
    /// </summary>
    public readonly struct EnemySenses
    {
        /// <summary>Its health ran out and it has landed.</summary>
        public readonly bool Defeated;

        /// <summary>False while stunned, juggled, getting up or staggered.</summary>
        public readonly bool CanAct;

        /// <summary>It has a target, and that target is alive.</summary>
        public readonly bool HasTarget;

        /// <summary>The target is inside the aggro range (with hysteresis).</summary>
        public readonly bool Aware;

        /// <summary>The target can be struck from where it stands.</summary>
        public readonly bool InReach;

        /// <summary>Horizontal metres to the target; 0 with no target.</summary>
        public readonly float FlatDistance;

        /// <summary>Unit horizontal direction to the target; zero with no target or
        /// when standing exactly on it.</summary>
        public readonly Vector3 FlatDirection;

        /// <summary>Fraction of normal speed, 0-1, from any slow on it.</summary>
        public readonly float SpeedMultiplier;

        /// <summary>Builds the snapshot; see the properties for each fact.</summary>
        public EnemySenses(bool defeated, bool canAct, bool hasTarget, bool aware, bool inReach,
                           float flatDistance, Vector3 flatDirection, float speedMultiplier)
        {
            Defeated = defeated;
            CanAct = canAct;
            HasTarget = hasTarget;
            Aware = aware;
            InReach = inReach;
            FlatDistance = flatDistance;
            FlatDirection = flatDirection;
            SpeedMultiplier = speedMultiplier;
        }
    }
}
