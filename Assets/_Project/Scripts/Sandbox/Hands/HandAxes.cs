using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// The three anatomical directions of one hand, as unit vectors in the
    /// hand bone's own space: which way the knuckles point, which side the
    /// thumb is on, and which way the palm faces.
    ///
    /// Built by <see cref="GripFrameSolver.TryHandAxes"/> from rig data, never
    /// assumed from a bone's local axes: the Tripo rigs, Mixamo rigs and Blender
    /// rigs in this project all orient their hand bones differently, and this
    /// is what lets one grab point convention work on every one of them.
    /// </summary>
    public readonly struct HandAxes
    {
        /// <summary>Wrist toward the knuckles, along the length of the hand.</summary>
        public readonly Vector3 Knuckle;

        /// <summary>Across the hand toward the thumb (little finger → index),
        /// perpendicular to <see cref="Knuckle"/>.</summary>
        public readonly Vector3 Thumb;

        /// <summary>Out of the palm, perpendicular to both other axes.</summary>
        public readonly Vector3 Palm;

        /// <summary>True for a right hand. A left hand is the mirror image, and
        /// a mirror can't be expressed as a rotation, so code that turns things
        /// toward the palm needs to know which hand it is on.</summary>
        public readonly bool RightHand;

        /// <summary>Use <see cref="GripFrameSolver.TryHandAxes"/>, which makes
        /// the axes orthonormal; this constructor stores them as given.</summary>
        public HandAxes(Vector3 knuckle, Vector3 thumb, Vector3 palm, bool rightHand)
        {
            Knuckle = knuckle;
            Thumb = thumb;
            Palm = palm;
            RightHand = rightHand;
        }
    }
}
