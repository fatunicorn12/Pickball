using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// The batting rule, kept out of <see cref="ConicOrbiter"/> so it is tested
    /// without a scene: which hits bat an orbiter at all, which way the swing
    /// sent it, and whether that counts as "at her".
    ///
    /// Everything is judged horizontally. The sword's push is
    /// <c>HitSettings.PushAlong(forward)</c>, which is flat for every swing but a
    /// launcher, and the player aims by facing her on the plate, not by pitch; so
    /// a ball hit at 1 m toward a woman hovering at 2.6 m still counts. The ball
    /// then homes in 3D (<see cref="ConicOrbiter"/>), so it climbs to her body.
    /// </summary>
    public static class OrbiterBatting
    {
        /// <summary>A knockback shorter than this, m/s, has no usable direction and
        /// the hit point is used instead.</summary>
        public const float MinKnockback = 1e-3f;

        /// <summary>
        /// Does a hit of <paramref name="kind"/> bat an orbiter? Every physical hit
        /// does; spells only with <paramref name="spellsBat"/>, so a stray orb or a
        /// lightning chain cannot topple her by accident (the topple is a sword skill).
        /// </summary>
        public static bool CanBat(DamageKind kind, bool spellsBat) => kind != DamageKind.Spell || spellsBat;

        /// <summary>
        /// The horizontal unit direction the swing sent the ball: the knockback,
        /// flattened; when that is ~zero (a hit with no push), the direction from
        /// the hit point through the ball. False when neither gives a direction (a
        /// zero push landing dead centre): the hit is then ignored.
        /// </summary>
        public static bool TrySwingDirection(Vector3 knockback, Vector3 hitPoint, Vector3 ball, out Vector3 direction)
        {
            direction = Flat(knockback);
            if (direction.sqrMagnitude > MinKnockback * MinKnockback)
            {
                direction.Normalize();
                return true;
            }
            direction = Flat(ball - hitPoint);
            if (direction.sqrMagnitude > 1e-8f)
            {
                direction.Normalize();
                return true;
            }
            direction = Vector3.zero;
            return false;
        }

        /// <summary>
        /// True when <paramref name="swingDirection"/> is within
        /// <paramref name="coneDegrees"/> of the horizontal line from
        /// <paramref name="ball"/> to <paramref name="herCentre"/>. A ball directly
        /// over or under her (no horizontal line) counts as at her.
        /// </summary>
        public static bool IsTowardHer(Vector3 swingDirection, Vector3 ball, Vector3 herCentre, float coneDegrees)
        {
            Vector3 toHer = Flat(herCentre - ball);
            if (toHer.sqrMagnitude < 1e-8f) return true;
            Vector3 swing = Flat(swingDirection);
            if (swing.sqrMagnitude < 1e-8f) return false;
            float cone = float.IsNaN(coneDegrees) ? 0f : Mathf.Clamp(coneDegrees, 0f, 180f);
            return Vector3.Angle(swing, toHer) <= cone + 1e-3f;
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}
