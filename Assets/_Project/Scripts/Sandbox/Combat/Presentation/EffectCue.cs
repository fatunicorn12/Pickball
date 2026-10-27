using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>Where and how big to play a pooled effect. One shape for every
    /// effect so <see cref="SwordEffects"/> never has to know which kind it is
    /// spawning.</summary>
    public readonly struct EffectCue
    {
        /// <summary>World position to play at.</summary>
        public Vector3 Position { get; }

        /// <summary>Facing: the attacker's forward for a hit, identity for a ring.</summary>
        public Quaternion Rotation { get; }

        /// <summary>Size in metres — a ring's final radius, a pop's height.</summary>
        public float Radius { get; }

        /// <summary>Seconds the effect should take to grow; 0 = its own default.</summary>
        public float GrowSeconds { get; }

        /// <summary>Tint, usually the step's trail colour.</summary>
        public Color Color { get; }

        public EffectCue(Vector3 position, Quaternion rotation, float radius, float growSeconds, Color color)
        {
            Position = position;
            Rotation = rotation;
            Radius = radius;
            GrowSeconds = growSeconds;
            Color = color;
        }
    }
}
