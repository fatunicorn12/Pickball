using UnityEngine;

namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// Where a skid's sparks come from: the outer edge of the foot that has gone
    /// furthest along the way he was running. That foot is the one he is braking
    /// on, and it is also the furthest from the way he is about to go.
    ///
    /// Kept pure so the choice can be tested without a rig. It compares positions
    /// along the travel direction only, so it gives the same answer whichever way
    /// the body is facing and however the stride happens to be split.
    /// </summary>
    public static class FurtherFootEdge
    {
        /// <summary>The point <paramref name="outset"/> metres past the furthest part
        /// of the further foot, along planar <paramref name="travel"/>. Height is the
        /// foot's own; the caller decides where the floor is. Pass a foot's own
        /// position as its toes on a rig without toe bones. With no planar travel
        /// there is no "further", so this returns the right foot unchanged.</summary>
        public static Vector3 Find(Vector3 leftFoot, Vector3 leftToes, Vector3 rightFoot,
                                   Vector3 rightToes, Vector3 travel, float outset)
        {
            travel.y = 0f;
            if (travel.sqrMagnitude < 1e-6f) return rightFoot;
            travel.Normalize();

            Vector3 left = Furthest(leftFoot, leftToes, travel);
            Vector3 right = Furthest(rightFoot, rightToes, travel);
            Vector3 tip = Vector3.Dot(left, travel) > Vector3.Dot(right, travel) ? left : right;
            return tip + travel * outset;
        }

        static Vector3 Furthest(Vector3 a, Vector3 b, Vector3 travel)
            => Vector3.Dot(a, travel) >= Vector3.Dot(b, travel) ? a : b;
    }
}
