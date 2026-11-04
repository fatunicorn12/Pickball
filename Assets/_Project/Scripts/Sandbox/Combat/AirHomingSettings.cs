using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// How hard an air swing pulls the player to its target. Serialized inline on
    /// <see cref="MeleeCombat"/>, beside the ground lunge's homing settings.
    /// </summary>
    [Serializable]
    public struct AirHomingSettings
    {
        [Tooltip("A target further than this, in metres, is out of reach: the swing doesn't " +
                 "home. Measured from his chest in 3D. 0 = air homing off.")]
        [Min(0f)] [SerializeField] private float range;

        [Tooltip("Fastest he closes the gap, in m/s. Higher reaches further inside one swing, " +
                 "and past about 15 m/s it reads as a teleport.")]
        [Min(0.1f)] [SerializeField] private float maxSpeed;

        [Tooltip("Gap he stops short of the target's surface, in metres, measured from his " +
                 "centre. Keep it inside blade reach, or he stops where the swing can't connect, " +
                 "and wider than his own capsule (0.84 m with its skin), or he lands on the " +
                 "target's shoulders and slides off instead of coming down beside it.")]
        [Min(0f)] [SerializeField] private float stopGap;

        [Tooltip("Most height one swing can change, in metres, up or down. Keeps a target far " +
                 "overhead from yanking him into the air.")]
        [Min(0f)] [SerializeField] private float maxHeightChange;

        [Tooltip("A target less than this far above him, in metres, doesn't lift him, so swings " +
                 "at a target already level don't bob him up.")]
        [Min(0f)] [SerializeField] private float heightTolerance;

        [Tooltip("Height of his chest above his feet, in metres: the point lined up with the " +
                 "target's chest.")]
        [Min(0f)] [SerializeField] private float chestHeight;

        [Tooltip("How far above the target he may stay, in metres, before a swing pulls him " +
                 "down, and it pulls him only to this height above it. The blade reaches below " +
                 "his chest, and a juggled body is usually rising to meet him, so a smaller " +
                 "value yanks him up and down between swings.")]
        [Min(0f)] [SerializeField] private float dropTolerance;

        public float Range => range;
        public float MaxSpeed => maxSpeed;
        public float StopGap => stopGap;
        public float MaxHeightChange => maxHeightChange;
        public float HeightTolerance => heightTolerance;
        public float ChestHeight => chestHeight;
        public float DropTolerance => dropTolerance;

        public AirHomingSettings(float range, float maxSpeed, float stopGap, float maxHeightChange,
                                 float heightTolerance, float chestHeight, float dropTolerance)
        {
            this.dropTolerance = Mathf.Max(0f, dropTolerance);
            this.range = Mathf.Max(0f, range);
            this.maxSpeed = Mathf.Max(0.1f, maxSpeed);
            this.stopGap = Mathf.Max(0f, stopGap);
            this.maxHeightChange = Mathf.Max(0f, maxHeightChange);
            this.heightTolerance = Mathf.Max(0f, heightTolerance);
            this.chestHeight = Mathf.Max(0f, chestHeight);
        }

        /// <summary>Starting numbers, to be tuned in play.</summary>
        public static AirHomingSettings Default => new(6f, 12f, 1f, 3f, 0.35f, 0.9f, 0.6f);
    }
}
