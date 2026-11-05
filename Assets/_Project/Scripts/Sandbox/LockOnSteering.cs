using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The maths that aims the camera at a locked target, without a scene.
    ///
    /// The problem it solves: the wanted yaw used to be the bearing from the player to the target.
    /// Walking under a floating target shrinks that flat vector through zero, so the bearing turns
    /// at a speed with no limit and flips 180°, and the player, whose look is taken over while
    /// locked, could not fight it. Here the wanted yaw is the bearing of
    /// <c>flat(player to target) + holdRadius * cameraForward</c>. Far away the first term wins and
    /// it follows the target. Close or underneath, the second wins and it holds the current yaw. No
    /// threshold, because the two blend, and the result is defined at exactly zero. A hard turn rate
    /// bounds whatever is left, the swing round once the player has walked well past included.
    ///
    /// The camera separates where it orbits from where it looks: the orbit pitch stays above the
    /// floor (<see cref="OrbitPitch"/>) and a look tilt on top (<see cref="LookTilt"/>) raises the
    /// view only as far as an overhead target needs.
    /// </summary>
    public static class LockOnSteering
    {
        /// <summary>Beyond this far from the goal, in degrees, a turn already under way keeps its
        /// direction, instead of reversing when the goal crosses the far side of the circle.</summary>
        const float CommitDegrees = 150f;

        /// <summary>Turn speed that counts as already under way, degrees per second.</summary>
        const float CommitSpeed = 10f;

        /// <summary>
        /// The yaw to steer to, in degrees: see the class notes. Equal to
        /// <paramref name="currentYaw"/> when the target is exactly overhead.
        /// </summary>
        /// <param name="toTarget">From the player's follow point to the aim point, world space.</param>
        /// <param name="holdRadius">Metres over which the current heading outweighs the target's bearing.</param>
        public static float HoldYaw(float currentYaw, Vector3 toTarget, float holdRadius)
        {
            float r = currentYaw * Mathf.Deg2Rad;
            float x = toTarget.x + holdRadius * Mathf.Sin(r);
            float z = toTarget.z + holdRadius * Mathf.Cos(r);
            if (x * x + z * z < 1e-8f) return currentYaw;
            return Mathf.Atan2(x, z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Elevation of the target above the player, in degrees, softened near the vertical by the
        /// same radius as <see cref="HoldYaw"/>, so it doesn't race to 90° as the flat distance
        /// goes to zero. Negative when the target is below him.
        /// </summary>
        public static float SoftRise(Vector3 toTarget, float holdRadius)
        {
            float flatSqr = toTarget.x * toTarget.x + toTarget.z * toTarget.z;
            float across = Mathf.Sqrt(flatSqr + holdRadius * holdRadius);
            if (across < 1e-4f && Mathf.Abs(toTarget.y) < 1e-4f) return 0f;
            return Mathf.Atan2(toTarget.y, across) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The orbit pitch to steer to, in degrees (positive looks down): the resting pitch, eased
        /// up for a higher target.
        /// </summary>
        /// <param name="rise">From <see cref="SoftRise"/>.</param>
        /// <param name="restPitch">Pitch for a target level with the player.</param>
        /// <param name="follow">How much of the rise to follow, 0..1.</param>
        /// <param name="minPitch">Lowest the orbit may swing, which is what keeps the lens off the floor.</param>
        /// <param name="maxPitch">Highest it may swing.</param>
        public static float OrbitPitch(float rise, float restPitch, float follow, float minPitch, float maxPitch)
            => Mathf.Clamp(restPitch - rise * follow, minPitch, Mathf.Max(minPitch, maxPitch));

        /// <summary>
        /// One frame of a smoothed turn toward <paramref name="goal"/>, in degrees, moving no faster
        /// than <paramref name="maxRate"/> whatever the frame rate. Unlike
        /// <c>Mathf.SmoothDampAngle</c>'s own limit, this one is a hard per-frame bound. A turn under
        /// way doesn't reverse when the goal crosses the far side of the circle. At a
        /// <paramref name="dt"/> of zero nothing changes (<c>SmoothDamp</c> itself can divide by it).
        /// </summary>
        /// <param name="velocity">Turn speed carried between frames, degrees per second.</param>
        /// <param name="smoothTime">Seconds, as for <c>SmoothDamp</c>.</param>
        public static float Turn(float current, float goal, ref float velocity, float smoothTime, float maxRate, float dt)
        {
            if (dt <= 0f) return current;
            float limit = Mathf.Max(0f, maxRate);
            float delta = Mathf.DeltaAngle(current, goal);
            if (Mathf.Abs(delta) > CommitDegrees && Mathf.Abs(velocity) > CommitSpeed
                                                 && Mathf.Sign(velocity) != Mathf.Sign(delta))
                delta -= 360f * Mathf.Sign(delta);

            float next = Mathf.SmoothDamp(current, current + delta, ref velocity, Mathf.Max(1e-4f, smoothTime), limit, dt);
            velocity = Mathf.Clamp(velocity, -limit, limit);
            return current + Mathf.Clamp(next - current, -limit * dt, limit * dt);
        }

        /// <summary>
        /// How far to raise the view above the orbit, in degrees, to bring the aim point down to
        /// <paramref name="frameTop"/>: zero while it's already below that. Limited so the player's
        /// head stays above <paramref name="playerFloor"/>. When the two can't both be met, the
        /// target leaves the frame, and the lock's lost-sight grace decides what happens.
        /// </summary>
        /// <param name="aim">Aim point in the orbit's camera space (x right, y up, z forward).</param>
        /// <param name="head">The player's follow point in the same space.</param>
        /// <param name="tanHalfFov">Tangent of half the vertical field of view.</param>
        /// <param name="frameTop">Highest the aim point should sit, as a viewport height 0..1.</param>
        /// <param name="playerFloor">Lowest the head should sit, as a viewport height 0..1.</param>
        /// <param name="maxTilt">Upper limit, degrees.</param>
        public static float LookTilt(Vector3 aim, Vector3 head, float tanHalfFov, float frameTop, float playerFloor, float maxTilt)
        {
            float aimAngle = Mathf.Atan2(aim.y, aim.z) * Mathf.Rad2Deg;
            float top = Mathf.Atan((2f * frameTop - 1f) * tanHalfFov) * Mathf.Rad2Deg;
            float headAngle = Mathf.Atan2(head.y, head.z) * Mathf.Rad2Deg;
            float floor = Mathf.Atan((2f * playerFloor - 1f) * tanHalfFov) * Mathf.Rad2Deg;
            float limit = Mathf.Min(headAngle - floor, maxTilt);
            return Mathf.Clamp(aimAngle - top, 0f, Mathf.Max(0f, limit));
        }

        /// <summary>
        /// Extra camera distance for a high target, in metres: none for a target level with the
        /// player, all of <paramref name="pullback"/> at <paramref name="fullRise"/> degrees and up.
        /// Standing further off narrows the angle between the target and the player.
        /// </summary>
        public static float RisePullback(float rise, float pullback, float fullRise)
            => pullback * Mathf.Clamp01(rise / Mathf.Max(1f, fullRise));
    }
}
