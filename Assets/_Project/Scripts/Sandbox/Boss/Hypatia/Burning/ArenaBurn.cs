using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Burning
{
    /// <summary>
    /// Alexandria's Burning: in phase 2 the Rete's floor burns band by band,
    /// from the rim inward, until only the hub is left to stand on. Lives on
    /// Hypatia's root; the brain decides WHEN (it calls <see cref="IgniteNext"/>
    /// every burn interval) and this owns HOW: the warning, the ignition and the
    /// fire damage. The band rules are <see cref="BurnSchedule"/>; the "is the
    /// player in it" rule is <see cref="FireRule"/>.
    ///
    /// <b>Two clocks, on purpose.</b> The smoulder countdown runs on HER clock
    /// (<see cref="HypatiaContext.BossDelta"/>): the warning is her spell being
    /// cast, so slowing her slows it and freezing her holds it. The fire itself
    /// runs on real time: once a band is alight it is the arena's state, not
    /// something she is doing, and freezing her must not make the floor safe
    /// (Docs/Design/StationaryPoint.md: freezing a caster does not freeze what
    /// she has already released).
    ///
    /// <b>Damage.</b> Fire is recoverable (grey) and continuous, dps × dt each
    /// frame: the player wins it back by attacking her, and per-frame ticks
    /// neither open nor are refused by hit grace (see <see cref="Health.Damage"/>).
    ///
    /// <b>Presentation</b> listens to <see cref="BandSmouldering"/>,
    /// <see cref="BandIgnited"/>, <see cref="PlayerBurning"/> and
    /// <see cref="Extinguished"/>, and reads <see cref="GetBandState"/> to draw
    /// the floor (for example after a reload). This component draws nothing.
    ///
    /// <b>After <see cref="StopAll"/></b> (her death or the player's) pending
    /// smoulders are cancelled, the fire deals no more damage and nothing new
    /// ignites; burned bands stay <see cref="BandState.Burned"/> so the floor
    /// still reads as burned, but they are inert. Stopping is permanent for
    /// this component's lifetime (a new fight is a new scene load).
    /// </summary>
    public class ArenaBurn : MonoBehaviour, IArenaBurn
    {
        /// <summary>A band started its warning. Args: band, inner radius (m),
        /// outer radius (m), seconds until it ignites on HER clock (a slow
        /// stretches it in real time).</summary>
        public event Action<int, float, float, float> BandSmouldering;

        /// <summary>A band caught fire. Args: band, inner radius (m), outer radius (m).</summary>
        public event Action<int, float, float> BandIgnited;

        /// <summary>The player started (true) or stopped (false) burning. Edge
        /// only: raised on the change, never every frame.</summary>
        public event Action<bool> PlayerBurning;

        /// <summary>The fight ended: smoulders were called off and the fire went
        /// inert. Raised once, by the first <see cref="StopAll"/>.</summary>
        public event Action Extinguished;

        HypatiaContext _context;
        BurnSchedule _schedule; // [Serializable]: may survive a script reload
        bool _stopped;
        bool _playerBurning;
        Action<int> _onIgnited; // cached so Tick never allocates

        /// <summary>The context from the last <see cref="Bind"/>; null before.
        /// Read by <see cref="ScrollKeeper"/>, which is not an attack the brain
        /// binds.</summary>
        public HypatiaContext Context => _context;

        /// <summary>True after <see cref="StopAll"/>.</summary>
        public bool IsStopped => _stopped;

        /// <summary>True while the player stands in a burned band below the
        /// flames' height (and the fire is live).</summary>
        public bool IsPlayerBurning => _playerBurning;

        /// <summary>Number of floor bands, hub included; 0 before Bind.</summary>
        public int BandCount => Built ? _schedule.BandCount : 0;

        /// <summary>A band's state; Unburned for an unknown band or before Bind.</summary>
        public BandState GetBandState(int band) => Built ? _schedule.StateOf(band) : BandState.Unburned;

        /// <summary>Inner radius of a band, m from the centre.</summary>
        public float GetBandInnerRadius(int band) => Built ? _schedule.InnerRadius(band) : 0f;

        /// <summary>Outer radius of a band, m from the centre.</summary>
        public float GetBandOuterRadius(int band) => Built ? _schedule.OuterRadius(band) : 0f;

        /// <summary>Seconds on her clock until a smouldering band ignites; 0 otherwise.</summary>
        public float GetSmoulderRemaining(int band) => Built ? _schedule.SmoulderRemaining(band) : 0f;

        /// <inheritdoc/>
        public bool CanIgnite => !_stopped && Built && _schedule.CanIgnite;

        /// <inheritdoc/>
        public int BurnedBands => Built ? _schedule.BurnedBands : 0;

        /// <summary>Bands currently in their warning.</summary>
        public int SmoulderingBands => Built ? _schedule.SmoulderingBands : 0;

        /// <inheritdoc/>
        /// <remarks>A smouldering band still counts as floor; see
        /// <see cref="ProjectedSafeRadius"/>.</remarks>
        public float SafeRadius => Built ? _schedule.SafeRadius : ConfigArenaRadius;

        /// <summary>The safe radius once every pending smoulder has caught, m.</summary>
        public float ProjectedSafeRadius => Built ? _schedule.ProjectedSafeRadius : ConfigArenaRadius;

        /// <summary>The plate's radius as the burn sees it (the last band edge), m.</summary>
        public float ArenaRadius => Built ? _schedule.ArenaRadius : ConfigArenaRadius;

        /// <summary>True when this horizontal distance from the centre is in a
        /// band that is alight (inert or not).</summary>
        public bool IsBurnedAt(float radius) => Built && _schedule.IsBurnedAt(radius);

        /// <summary>A uniformly random point on the burned floor as a horizontal
        /// offset from the centre, from three 0-1 draws (see
        /// <see cref="BurnSchedule.TryPointOnBurnedFloor"/>). False when nothing
        /// is burned.</summary>
        public bool TryPointOnBurnedFloor(float pick, float u, float v, out Vector3 offset)
        {
            offset = Vector3.zero;
            return Built && _schedule.TryPointOnBurnedFloor(pick, u, v, out offset);
        }

        bool Built => _schedule != null && _schedule.IsBuilt;

        float ConfigArenaRadius => _context?.Config != null ? _context.Config.ArenaRadius : 0f;

        /// <summary>
        /// Take the fight's context. Builds the band schedule from the config
        /// the first time; a later Bind (the brain's OnEnable after a script
        /// reload) keeps a schedule that survived, so the floor does not
        /// silently un-burn. Band radii are read once here; the damage, fire
        /// height and warning lead are read live, so tuning them in Play mode
        /// takes effect at once.
        /// </summary>
        public void Bind(HypatiaContext context)
        {
            _context = context;
            _onIgnited ??= HandleIgnited;
            if (Built) return;

            var config = context?.Config;
            if (config == null)
            {
                Debug.LogWarning($"[ArenaBurn] {name} bound without a HypatiaConfig; the floor will not burn.", this);
                return;
            }
            _schedule = new BurnSchedule(config.BandOuterRadii, config.ArenaRadius, config.MinSafeRadius);
        }

        /// <inheritdoc/>
        /// <remarks>Raises <see cref="BandSmouldering"/>. With a warning lead of
        /// 0 the band ignites immediately.</remarks>
        public void IgniteNext()
        {
            if (!CanIgnite || _context?.Config == null) return;

            float lead = Mathf.Max(0f, _context.Config.BurnWarningLead);
            int band = _schedule.StartSmoulder(lead);
            if (band < 0) return;

            BandSmouldering?.Invoke(band, _schedule.InnerRadius(band), _schedule.OuterRadius(band), lead);
            if (lead <= 0f) _schedule.Tick(0f, _onIgnited ??= HandleIgnited);
        }

        /// <inheritdoc/>
        public void StopAll()
        {
            if (_stopped) return;
            _stopped = true;
            if (Built) _schedule.CancelSmoulders();
            SetPlayerBurning(false);
            Extinguished?.Invoke();
        }

        void Update() => Tick(Time.deltaTime);

        void OnDisable() => SetPlayerBurning(false);

        /// <summary>
        /// Advance by <paramref name="dt"/> real seconds: count smoulders down
        /// on her clock, then burn the player on real time. Update calls it
        /// with Time.deltaTime; public so a test or harness can drive time.
        /// </summary>
        public void Tick(float dt)
        {
            if (_stopped || _context?.Config == null || !Built) return;
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;

            _schedule.Tick(_context.BossDelta(dt), _onIgnited ??= HandleIgnited);
            BurnPlayer(dt);
        }

        void BurnPlayer(float dt)
        {
            var player = _context.Player;
            if (player == null)
            {
                SetPlayerBurning(false);
                return;
            }

            var config = _context.Config;
            Vector3 feet = player.position;
            bool burning = FireRule.IsBurning(_schedule, _context.Centre, _context.FloorY,
                                              feet, config.FireHeight);
            SetPlayerBurning(burning);
            if (!burning || _context.PlayerHealth == null) return;

            float amount = FireRule.DamageThisFrame(config.FireDamagePerSecond, dt);
            if (amount > 0f)
                _context.PlayerHealth.Damage(amount, feet, recoverable: true, continuous: true);
        }

        void HandleIgnited(int band)
            => BandIgnited?.Invoke(band, _schedule.InnerRadius(band), _schedule.OuterRadius(band));

        void SetPlayerBurning(bool burning)
        {
            if (_playerBurning == burning) return;
            _playerBurning = burning;
            PlayerBurning?.Invoke(burning);
        }
    }
}
