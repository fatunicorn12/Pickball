using UnityEngine;

namespace Pickleball.Sandbox.Juggle
{
    /// <summary>
    /// One enemy's Castle-Crashers-style juggle: its height off the ground,
    /// the air hits it has taken since it last landed, its hitstun, and a
    /// defeat it may be carrying down to the ground.
    ///
    /// The weight class lives entirely in <see cref="JuggleSettings"/>: every
    /// air hit sets <c>vy = max(vy, relaunch × weightScale × decay^n)</c>, so a
    /// lightweight (decay 1) is held up forever, a middleweight's lift shrinks
    /// until it falls, and a heavyweight drops after a hit or two. The
    /// <c>max</c> means a hit never slows a body that is already rising faster.
    ///
    /// Each hit also carries a <see cref="HitReaction"/>, Kingdom Hearts II's
    /// split: a flinch or launch feeds the rule above, a heavy hit staggers for
    /// longer, and a knock-away ends the juggle. A knocked-away body flies clear
    /// on an arc, takes no further reaction until it has landed and recovered, and
    /// only then may act. That is what stops a finisher's ring re-lifting the body
    /// its blade just threw.
    ///
    /// Plain C# and dt-driven, with exact constant-gravity integration, so the
    /// hit a juggle gives out on does not depend on frame rate. Horizontal travel
    /// is only reported, as <see cref="KnockSpeed"/>: the owner knows the
    /// direction and moves its visual by that and <see cref="Height"/>.
    /// </summary>
    public sealed class JuggleState
    {
        /// <summary>Tuning, re-read every hit and tick so live inspector edits apply.</summary>
        public JuggleSettings Settings { get; set; }

        /// <summary>Metres above the ground it stands on. Never negative.</summary>
        public float Height { get; private set; }

        /// <summary>Upward speed in m/s; negative while falling.</summary>
        public float VerticalSpeed { get; private set; }

        /// <summary>Air hits taken since the last landing: the n in decay^n.</summary>
        public int AirHits { get; private set; }

        /// <summary>Seconds of hitstun left.</summary>
        public float HitstunLeft { get; private set; }

        /// <summary>Defeated in the air and waiting to land before it dies.</summary>
        public bool DefeatPending { get; private set; }

        /// <summary>Thrown by a knock-away and still flying.</summary>
        public bool IsKnockedAway { get; private set; }

        /// <summary>Horizontal speed of the throw, in m/s, away from the blow. Zero
        /// once it lands.</summary>
        public float KnockSpeed { get; private set; }

        /// <summary>Seconds left getting up after a knock-away landed.</summary>
        public float RecoverLeft { get; private set; }

        public bool IsAirborne => Height > 0f || VerticalSpeed > 0f;
        public bool InHitstun => HitstunLeft > 0f;
        public bool IsRecovering => RecoverLeft > 0f;

        /// <summary>Whether its own behaviour (attacks, movement) may run. Not
        /// while stunned, juggled or getting up: an enemy acts again only once it
        /// is on the ground with its hitstun and recovery spent.</summary>
        public bool CanAct => !InHitstun && !IsAirborne && !IsRecovering;

        public JuggleState(JuggleSettings settings) => Settings = settings;

        /// <summary>
        /// Take a small hit or a launcher. Every hit refreshes hitstun. In the air
        /// it relaunches by the weight rule and counts toward n; on the ground only
        /// a launcher lifts it.
        /// </summary>
        public void Hit(bool launch) => Hit(launch ? HitReaction.Launch : HitReaction.Flinch);

        /// <summary>
        /// Take a hit of any kind. A body already thrown or getting up ignores it:
        /// the blow that ended the juggle stays ended.
        /// </summary>
        /// <param name="reaction">The kind of blow.</param>
        /// <param name="knockSpeed">Horizontal speed of the blow, in m/s. Only a
        /// knock-away uses it.</param>
        public void Hit(HitReaction reaction, float knockSpeed = 0f)
        {
            if (IsKnockedAway || IsRecovering) return;

            var s = Settings;
            if (reaction == HitReaction.KnockAway)
            {
                KnockAway(s, knockSpeed);
                return;
            }

            HitstunLeft = reaction == HitReaction.Heavy ? s.Hitstun * s.HeavyStunScale : s.Hitstun;

            if (IsAirborne)
            {
                // A spent lift is 0, so the hit still stops a fall dead
                // (max(vy, 0)) without lifting: hits stall the drop, they
                // no longer reverse it.
                float lift = Mathf.Min(s.RelaunchFor(AirHits), s.CeilingSpeed(Height));
                VerticalSpeed = Mathf.Max(VerticalSpeed, lift);
                AirHits++;
            }
            else if (reaction == HitReaction.Launch)
            {
                VerticalSpeed = Mathf.Min(s.GroundLaunch, s.CeilingSpeed(0f));
            }
        }

        void KnockAway(in JuggleSettings s, float knockSpeed)
        {
            HitstunLeft = s.Hitstun;
            KnockSpeed = Mathf.Max(0f, knockSpeed) * s.KnockAwayCarry;
            // Set, not max: a body rising fast still gets thrown on the same arc.
            VerticalSpeed = Mathf.Min(s.KnockAwayLift * s.WeightScale, s.CeilingSpeed(Height));
            IsKnockedAway = true;
        }

        /// <summary>It has broken out of the combo (see <see cref="RevengeGauge"/>):
        /// the stun is shrugged off at once. A body in the air still has to land.</summary>
        public void BreakOut() => HitstunLeft = 0f;

        /// <summary>
        /// Its health ran out. True when it should die now: on the ground, or
        /// when <see cref="JuggleSettings.DefeatOnLanding"/> is off. Otherwise
        /// the defeat is held until <see cref="Tick"/> reports
        /// <see cref="JuggleLanding.LandedDefeated"/>, and it can still be
        /// juggled on the way down.
        /// </summary>
        public bool Defeat()
        {
            if (!IsAirborne || !Settings.DefeatOnLanding) return true;
            DefeatPending = true;
            return false;
        }

        /// <summary>Advance by <paramref name="dt"/> seconds: count hitstun and
        /// recovery down, and fly. Reports the tick it touches down on.</summary>
        public JuggleLanding Tick(float dt)
        {
            if (dt <= 0f) return JuggleLanding.None;
            HitstunLeft = Mathf.Max(0f, HitstunLeft - dt);
            RecoverLeft = Mathf.Max(0f, RecoverLeft - dt);
            if (!IsAirborne)
            {
                // Thrown with no lift to give (a weight scale of 0, or at the
                // ceiling on the ground): it has already landed.
                if (IsKnockedAway) return Land();
                return JuggleLanding.None;
            }

            float g = Settings.Gravity;
            Height += VerticalSpeed * dt - 0.5f * g * dt * dt;
            VerticalSpeed -= g * dt;
            return Height > 0f ? JuggleLanding.None : Land();
        }

        JuggleLanding Land()
        {
            Height = 0f;
            VerticalSpeed = 0f;
            AirHits = 0;
            if (IsKnockedAway)
            {
                IsKnockedAway = false;
                KnockSpeed = 0f;
                RecoverLeft = Settings.RecoverSeconds;
            }
            if (!DefeatPending) return JuggleLanding.Landed;
            DefeatPending = false;
            return JuggleLanding.LandedDefeated;
        }

        /// <summary>Back on the ground, unstunned, with nothing pending: a respawn.</summary>
        public void Reset()
        {
            Height = 0f;
            VerticalSpeed = 0f;
            AirHits = 0;
            HitstunLeft = 0f;
            DefeatPending = false;
            IsKnockedAway = false;
            KnockSpeed = 0f;
            RecoverLeft = 0f;
        }
    }
}
