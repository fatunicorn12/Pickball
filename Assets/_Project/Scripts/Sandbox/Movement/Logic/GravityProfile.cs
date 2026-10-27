using UnityEngine;

namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// The three gravities a Sunshine-style jump runs on, plus the fall cap.
    ///
    /// A single constant gravity makes a jump either floaty AND slow to fall or
    /// snappy AND short — the two cannot be tuned apart. Splitting it gives the
    /// Mario shape: a light pull while rising with Jump held (height and hang
    /// you control), a lighter one still in a band around the apex (the "hang"
    /// that makes it read as floaty), and a heavier one for the fall and for an
    /// early release (so letting go cuts the jump short and landings stay
    /// crisp). Every value is a positive magnitude in m/s², pointing down.
    /// </summary>
    public readonly struct GravityProfile
    {
        /// <summary>Gravity while rising faster than <see cref="ApexBand"/> with Jump held, m/s².</summary>
        public readonly float Rise;
        /// <summary>Gravity while |vertical speed| is within <see cref="ApexBand"/> with Jump held, m/s².</summary>
        public readonly float Apex;
        /// <summary>Gravity everywhere else — falling, or rising after Jump was released, m/s².</summary>
        public readonly float Fall;
        /// <summary>Half-width of the apex band, m/s of vertical speed. 0 disables the hang.</summary>
        public readonly float ApexBand;
        /// <summary>Terminal fall speed, m/s (positive).</summary>
        public readonly float MaxFallSpeed;

        public GravityProfile(float rise, float apex, float fall, float apexBand, float maxFallSpeed)
        {
            // Clamped so a zeroed config field can never divide by zero below.
            Rise = Mathf.Max(rise, 0.01f);
            Apex = Mathf.Max(apex, 0.01f);
            Fall = Mathf.Max(fall, 0.01f);
            ApexBand = Mathf.Max(apexBand, 0f);
            MaxFallSpeed = Mathf.Max(maxFallSpeed, 0.01f);
        }

        /// <summary>One gravity for everything — the profile of a plain arc
        /// (dash exit, tether release, a launch).</summary>
        public static GravityProfile Uniform(float gravity, float maxFallSpeed)
            => new(gravity, gravity, gravity, 0f, maxFallSpeed);

        /// <summary>The same profile with a different terminal speed — the spin
        /// jump's slow descent is this, not a separate gravity model.</summary>
        public GravityProfile WithMaxFallSpeed(float maxFallSpeed)
            => new(Rise, Apex, Fall, ApexBand, maxFallSpeed);

        /// <summary>Gravity magnitude at vertical speed <paramref name="vy"/>.
        /// The apex band applies on both sides of the apex, but only while Jump
        /// is held: released, everything is <see cref="Fall"/>.
        ///
        /// The intervals are half-open on the side speed is heading (down), so a
        /// speed sitting exactly on a boundary already gets the gravity of the
        /// interval it is about to enter — <see cref="JumpMath.Step"/> relies on
        /// that to never integrate a whole piece at the wrong gravity.</summary>
        public float GravityAt(float vy, bool held)
        {
            if (!held) return Fall;
            if (vy > ApexBand) return Rise;
            if (vy > -ApexBand) return Apex;
            return Fall;
        }
    }
}
