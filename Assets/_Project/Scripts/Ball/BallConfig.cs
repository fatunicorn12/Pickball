using UnityEngine;

namespace Pickleball
{
    /// <summary>All tunable ball physics + shot values. No ball behaviour is hardcoded.</summary>
    [CreateAssetMenu(menuName = "Pickleball/Config/Ball Config", fileName = "BallConfig")]
    public class BallConfig : ScriptableObject
    {
        // NOTE: These defaults are a conservative starting point that keep shots inside a 13.4m
        // court. Ball "feel" is best dialled in live — tweak this asset while watching Play mode.
        [Header("Floaty Physics")]
        [Tooltip("Fraction of normal gravity applied to the ball (Wii-style hang time).")]
        [Range(0.1f, 1f)] public float gravityScale = 0.55f;

        [Tooltip("Extra downward acceleration applied while the ball is descending, for a snappy fall after the floaty peak.")]
        public float descendForceBoost = 6f;

        [Tooltip("Vertical speed (m/s) below which the ball counts as 'at the peak' for the floaty hold.")]
        public float peakVelocityThreshold = 1.2f;

        [Tooltip("Gravity multiplier applied near the peak to exaggerate hang time (lower = floatier).")]
        [Range(0.05f, 1f)] public float peakGravityScale = 0.35f;

        [Header("Bounce (manual, not PhysX restitution)")]
        [Tooltip("Vertical restitution on bounce. 1 = keeps all upward speed, 0 = no bounce.")]
        [Range(0f, 1f)] public float bounciness = 0.75f;

        [Tooltip("Fraction of horizontal speed kept through a bounce.")]
        [Range(0f, 1f)] public float bounceHorizontalKeep = 0.85f;

        [Tooltip("Minimum upward speed after a bounce so even soft landings give a visible arcade pop.")]
        public float minBounceUpSpeed = 1.5f;

        public float linearDamping = 0.05f;
        public float angularDamping = 0.05f;
        public float mass = 0.1f;
        public float radius = 0.12f;

        [Tooltip("Number of bounces on a side before the ball is considered Dead and the point ends.")]
        public int bouncesBeforeDead = 2;

        [Header("Launch / Shots")]
        [Tooltip("Min/max forward speed imparted by a rally shot (and the serve), before swipe-power scaling.")]
        public Vector2 shotPowerRange = new Vector2(4.5f, 7f);

        [Tooltip("Upward velocity component added to every shot to create the arc.")]
        public float shotArcLift = 4.5f;

        [Tooltip("Maximum horizontal (sideways) velocity a swipe can impart for aiming.")]
        public float maxHorizontalAim = 2.5f;

        [Header("Bounds")]
        [Tooltip("If the ball drops below this Y it is considered dead (failsafe).")]
        public float killFloorY = -2f;
    }
}
