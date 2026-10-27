using UnityEngine;

namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// Closed-form jump arithmetic over a <see cref="GravityProfile"/>, and an
    /// exact integrator for it.
    ///
    /// Jumps are authored as APEX HEIGHTS in metres, never as launch speeds:
    /// with three gravities in play nobody can guess the speed that clears a
    /// 2 m ledge, but everybody can read "2 m". These functions convert.
    ///
    /// <see cref="Step"/> integrates gravity piecewise-exactly — it splits the
    /// frame wherever the gravity changes (entering or leaving the apex band,
    /// reaching the fall cap) — so the apex a jump reaches is the authored
    /// height at 30, 60 or 144 fps alike. Stepping v += g·dt, y += v·dt instead
    /// overshoots by about v·dt/2: 6 cm at 30 fps on a 7 m/s launch, which is
    /// the difference between clearing a ledge and clipping it.
    /// </summary>
    public static class JumpMath
    {
        /// <summary>Launch speed (m/s, up) whose held-jump apex is <paramref name="apexHeight"/> m.</summary>
        public static float LaunchSpeed(float apexHeight, in GravityProfile p)
        {
            if (apexHeight <= 0f) return 0f;
            float band = p.ApexBand;
            float bandHeight = band * band / (2f * p.Apex);   // climbed inside the band
            if (apexHeight <= bandHeight) return Mathf.Sqrt(2f * p.Apex * apexHeight);
            return Mathf.Sqrt(band * band + 2f * p.Rise * (apexHeight - bandHeight));
        }

        /// <summary>Apex height (m) of a held jump launched at <paramref name="launchSpeed"/> m/s.</summary>
        public static float ApexHeight(float launchSpeed, in GravityProfile p)
        {
            if (launchSpeed <= 0f) return 0f;
            float band = p.ApexBand;
            if (launchSpeed <= band) return launchSpeed * launchSpeed / (2f * p.Apex);
            return (launchSpeed * launchSpeed - band * band) / (2f * p.Rise)
                 + band * band / (2f * p.Apex);
        }

        /// <summary>Seconds from launch to apex for a held jump.</summary>
        public static float TimeToApex(float launchSpeed, in GravityProfile p)
        {
            if (launchSpeed <= 0f) return 0f;
            float band = p.ApexBand;
            if (launchSpeed <= band) return launchSpeed / p.Apex;
            return (launchSpeed - band) / p.Rise + band / p.Apex;
        }

        /// <summary>Seconds to fall <paramref name="height"/> m from rest with Jump
        /// still held: through the apex band, then at fall gravity up to the cap.</summary>
        public static float FallTime(float height, in GravityProfile p)
        {
            if (height <= 0f) return 0f;
            float band = Mathf.Min(p.ApexBand, p.MaxFallSpeed);
            float bandDrop = band * band / (2f * p.Apex);
            if (height <= bandDrop) return Mathf.Sqrt(2f * height / p.Apex);

            float t = band / p.Apex;
            float rest = height - bandDrop;
            float capDrop = (p.MaxFallSpeed * p.MaxFallSpeed - band * band) / (2f * p.Fall);
            if (rest <= capDrop)
            {
                float vEnd = Mathf.Sqrt(band * band + 2f * p.Fall * rest);
                return t + (vEnd - band) / p.Fall;
            }
            t += (p.MaxFallSpeed - band) / p.Fall;
            return t + (rest - capDrop) / p.MaxFallSpeed;
        }

        /// <summary>Total airtime of a held jump that lands at its launch height.
        /// Used to time-scale one-shot air animations (flips) to the arc.</summary>
        public static float AirTime(float launchSpeed, in GravityProfile p)
            => TimeToApex(launchSpeed, p) + FallTime(ApexHeight(launchSpeed, p), p);

        /// <summary>
        /// Advances vertical speed by <paramref name="dt"/> under the profile and
        /// returns the vertical DISPLACEMENT (m) over the step.
        ///
        /// Exact for the piecewise-constant gravity: the step is split at every
        /// speed where the gravity changes, so results do not depend on frame
        /// rate. Speed below −MaxFallSpeed is clamped to it (an entry already
        /// past the cap, e.g. from a launch, is pulled back to it at once).
        /// </summary>
        public static float Step(ref float vy, bool held, in GravityProfile p, float dt)
        {
            if (dt <= 0f) return 0f;
            float cap = -p.MaxFallSpeed;
            if (vy <= cap)
            {
                vy = cap;
                return cap * dt;
            }

            float moved = 0f;
            float left = dt;
            // At most: rise -> band -> fall -> cap, so four pieces suffice.
            for (int i = 0; i < 4 && left > 0f; i++)
            {
                float g = p.GravityAt(vy, held);
                // The next speed at which the gravity (or the cap) takes over.
                float boundary = NextBoundary(vy, held, p);
                float toBoundary = (vy - boundary) / g;
                if (toBoundary >= left)
                {
                    float v1 = vy - g * left;
                    moved += (vy + v1) * 0.5f * left;
                    vy = v1;
                    return moved;
                }
                float vb = boundary;
                moved += (vy + vb) * 0.5f * toBoundary;
                vy = vb;
                left -= toBoundary;
                if (vy <= cap)
                {
                    moved += cap * left;
                    return moved;
                }
            }
            moved += vy * left;   // float residue only
            return moved;
        }

        /// <summary>Next speed below <paramref name="vy"/> at which gravity changes
        /// or the cap is hit. Strictly below vy except at the cap.</summary>
        static float NextBoundary(float vy, bool held, in GravityProfile p)
        {
            float cap = -p.MaxFallSpeed;
            if (held)
            {
                // With a zero band these collapse to the single Rise -> Fall
                // switch at the apex itself.
                if (vy > p.ApexBand) return Mathf.Max(p.ApexBand, cap);
                if (vy > -p.ApexBand) return Mathf.Max(-p.ApexBand, cap);
            }
            return cap;
        }
    }
}
