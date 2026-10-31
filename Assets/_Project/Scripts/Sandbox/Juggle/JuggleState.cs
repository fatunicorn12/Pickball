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
    /// Only the vertical axis is simulated. Plain C# and dt-driven, with exact
    /// constant-gravity integration, so the hit a juggle gives out on does not
    /// depend on frame rate. The owner moves its visual by <see cref="Height"/>.
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

        public bool IsAirborne => Height > 0f || VerticalSpeed > 0f;
        public bool InHitstun => HitstunLeft > 0f;

        /// <summary>Whether its own behaviour (attacks, movement) may run. Not
        /// while stunned, and not while juggled: an enemy acts again only once
        /// it is on the ground with its hitstun spent.</summary>
        public bool CanAct => !InHitstun && !IsAirborne;

        public JuggleState(JuggleSettings settings) => Settings = settings;

        /// <summary>
        /// Take a hit. Every hit refreshes hitstun. In the air it relaunches by
        /// the weight rule and counts toward n; on the ground only a launcher
        /// lifts it.
        /// </summary>
        public void Hit(bool launch)
        {
            var s = Settings;
            HitstunLeft = s.Hitstun;

            if (IsAirborne)
            {
                // A spent lift is 0, so the hit still stops a fall dead
                // (max(vy, 0)) without lifting: hits stall the drop, they
                // no longer reverse it.
                float lift = Mathf.Min(s.RelaunchFor(AirHits), s.CeilingSpeed(Height));
                VerticalSpeed = Mathf.Max(VerticalSpeed, lift);
                AirHits++;
            }
            else if (launch)
            {
                VerticalSpeed = Mathf.Min(s.GroundLaunch, s.CeilingSpeed(0f));
            }
        }

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

        /// <summary>Advance by <paramref name="dt"/> seconds: count hitstun down
        /// and fly. Reports the tick it touches down on.</summary>
        public JuggleLanding Tick(float dt)
        {
            if (dt <= 0f) return JuggleLanding.None;
            HitstunLeft = Mathf.Max(0f, HitstunLeft - dt);
            if (!IsAirborne) return JuggleLanding.None;

            float g = Settings.Gravity;
            Height += VerticalSpeed * dt - 0.5f * g * dt * dt;
            VerticalSpeed -= g * dt;
            if (Height > 0f) return JuggleLanding.None;

            Height = 0f;
            VerticalSpeed = 0f;
            AirHits = 0;
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
        }
    }
}
