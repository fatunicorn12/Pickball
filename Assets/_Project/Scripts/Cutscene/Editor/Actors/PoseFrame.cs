using UnityEngine;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// One frame of a clip being baked, handed to a spec's procedural layer
    /// (<see cref="PoseClipSpec.Procedural"/>) so it can add oscillations the keys can't express
    /// cheaply: a hover bob, wing flaps, a wobble.
    /// </summary>
    public sealed class PoseFrame
    {
        /// <summary>Muscle values, indexed like <see cref="HumanTrait.MuscleName"/>.</summary>
        public float[] Muscles;

        /// <summary>Body offset in metres (actor frame), applied after grounding.</summary>
        public Vector3 Offset;

        /// <summary>Extra body rotation in Euler degrees (actor frame).</summary>
        public Vector3 Turn;

        /// <summary>Add to a muscle by name (no-op for an unknown name).</summary>
        public void Add(string muscle, float delta)
        {
            int i = HumanMuscles.Index(muscle);
            if (i >= 0) Muscles[i] += delta;
        }

        /// <summary>Set a muscle by name (no-op for an unknown name).</summary>
        public void Set(string muscle, float value)
        {
            int i = HumanMuscles.Index(muscle);
            if (i >= 0) Muscles[i] = value;
        }
    }
}
