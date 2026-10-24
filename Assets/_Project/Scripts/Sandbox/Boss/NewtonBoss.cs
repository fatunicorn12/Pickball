using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Isaac Newton, as a fight.
    ///
    /// The loop the phases are built to produce:
    ///
    ///   read the wells -> find the shallow corridor -> close the distance,
    ///   paying inverse-square chip on the way in -> land the combo -> cash the
    ///   grey bar back -> disengage before the beam charges.
    ///
    /// His weak point is close range, which is the tension: the wells make
    /// approach expensive and the beam makes leaving expensive, so the fight is
    /// about choosing when to be committed rather than about being safe.
    ///
    /// Phase one is wells, the beam and the inverse-square volley. Phase two adds
    /// the apple barrage, which inverts the value of standing near a well — see
    /// <see cref="AppleBarrage"/> — and doubles the volley up.
    ///
    /// The four attacks are built to pull against each other: the beam says stop
    /// moving, the volley says jump, the wells drag you off your line and the
    /// apples say get out from under them. No single habit is safe.
    ///
    /// When he dies he drops the Gravity Anchor pickup.
    ///
    /// <b>No animation clips exist for Newton</b> (Animation/Newton/ is empty and
    /// NewtonBoss.controller has a single motionless Idle state), so every tell
    /// here is procedural: a wind-up crouch and lean, a scale pulse on release,
    /// a recoil on being hit. The animator parameters are still driven, so
    /// dropping Mixamo FBXs into Animation/Newton/ and re-running the setup
    /// layers real clips on top without touching this file.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class NewtonBoss : MonoBehaviour
    {
        [Header("Phases")]
        [Tooltip("Health fraction at which the apple barrage joins the fight.")]
        [Range(0f, 1f)]
        [SerializeField] private float phaseTwoAt = 0.55f;

        [Header("Gravity wells")]
        [SerializeField] private GravityWell wellPrefab;
        [SerializeField] private float wellIntervalPhaseOne = 4.5f;
        [SerializeField] private float wellIntervalPhaseTwo = 6.5f;
        [SerializeField] private int maxLiveWells = 3;

        [Tooltip("When a scheduled drop cannot happen — the live cap is full, or " +
                 "no placement passed — retry this soon instead of losing the beat " +
                 "and waiting a whole interval. PROVISIONAL: short enough that a " +
                 "blocked drop is not felt, not tuned for cadence.")]
        [SerializeField] private float wellRetryInterval = 0.25f;

        [Tooltip("Never drop a well closer than this to the player. Being trapped " +
                 "by a well that materialised on top of you is not a read.")]
        [SerializeField] private float wellMinDistanceFromPlayer = 5f;

        [Tooltip("How far from himself Newton will place one.")]
        [SerializeField] private float wellPlacementRadius = 11f;

        [Header("Fluxion beam")]
        [SerializeField] private FluxionBeam beam;
        [SerializeField] private float beamIntervalPhaseOne = 9f;
        [SerializeField] private float beamIntervalPhaseTwo = 7f;

        [Header("Inverse square volley")]
        [SerializeField] private InverseSquareVolley volley;
        [SerializeField] private float volleyIntervalPhaseOne = 6f;
        [SerializeField] private float volleyIntervalPhaseTwo = 4.5f;

        [Tooltip("The volley timer runs this many times faster while the player " +
                 "is inside weakPointRange, so camping at his feet gets punished.")]
        [SerializeField] private float volleyCloseUrgency = 2.5f;

        [Header("Apple barrage")]
        [SerializeField] private AppleBarrage barrage;

        [Tooltip("Barrage runs in bursts rather than continuously, so phase two " +
                 "still has windows to attack in.")]
        [SerializeField] private float barrageOnDuration = 6f;
        [SerializeField] private float barrageOffDuration = 5f;

        [Header("Body")]
        [SerializeField] private Transform body;
        [SerializeField] private Animator animator;

        [Tooltip("Distance at which hits on him do exactly their listed damage. " +
                 "Closer scales up by (range/r)², further scales down by it — the " +
                 "same law his volley uses on you.")]
        [SerializeField] private float weakPointRange = 4.5f;

        [Range(0f, 1f)]
        [SerializeField] private float farDamageMultiplier = 0.25f;

        [Tooltip("Ceiling on the close-range bonus.")]
        [SerializeField] private float closeDamageCap = 2f;

        [Header("Personality / reward")]
        [SerializeField] private NewtonBarks barks;

        [Tooltip("Dropped where he falls. Built by Setup Newton Fight as the " +
                 "Gravity Anchor pickup.")]
        [SerializeField] private GameObject rewardPrefab;

        [SerializeField] private float unpromptedBarkInterval = 22f;

        [SerializeField] private float turnDegreesPerSecond = 90f;

        public int Phase { get; private set; } = 1;
        public Health Health { get; private set; }

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int Attack2Hash = Animator.StringToHash("Attack2");
        static readonly int HitHash = Animator.StringToHash("Hit");
        static readonly int DeadHash = Animator.StringToHash("Dead");

        float _wellTimer, _beamTimer, _barrageTimer, _volleyTimer, _barkTimer;
        bool _barrageOn;

        ChibiLocomotion _player;
        Vector3 _bodyBaseScale = Vector3.one;
        float _windUp, _recoil;

        /// <summary>Where he is trying to face, owned by FacePlayer alone. Kept
        /// separately from transform.rotation because the tells may be writing to
        /// that same transform — see UpdateTells.</summary>
        Quaternion _facing = Quaternion.identity;

        /// <summary>True when there is no separate model child to animate and the
        /// procedural tells have to ride on the root.</summary>
        bool _bodyIsRoot;

        void Awake()
        {
            Health = GetComponent<Health>();
            if (body == null) body = transform;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (volley == null) volley = GetComponent<InverseSquareVolley>();
            if (barks == null) barks = GetComponent<NewtonBarks>();
            _bodyBaseScale = body.localScale;
            _facing = transform.rotation;

            _bodyIsRoot = body == transform;
            if (_bodyIsRoot)
                Debug.LogWarning($"[NewtonBoss] No 'body' transform wired on {name} " +
                                 "— falling back to the root. The tells still play " +
                                 "but now scale his collider with him. Re-run " +
                                 "Tools ▸ Pickleball ▸ Setup Newton Fight.");
        }

        void OnEnable()
        {
            Health.Hurt += OnHurt;
            Health.Died += OnDied;
            Health.IncomingDamageFilter = ScaleByRange;
        }

        void OnDisable()
        {
            Health.Hurt -= OnHurt;
            Health.Died -= OnDied;
            Health.IncomingDamageFilter = null;
        }

        void Start()
        {
            _player = FindFirstObjectByType<ChibiLocomotion>();
            _wellTimer = 2f;                 // a beat before the first well
            _beamTimer = beamIntervalPhaseOne;
            _barrageTimer = barrageOffDuration;
            _volleyTimer = volleyIntervalPhaseOne * 0.6f;
            _barkTimer = unpromptedBarkInterval;
            Bark(NewtonBarks.Occasion.Intro);
        }

        void Update()
        {
            if (Health.IsDead) return;
            float dt = Time.deltaTime;

            UpdatePhase();
            FacePlayer(dt);
            UpdateTells(dt);

            _wellTimer -= dt;
            if (_wellTimer <= 0f)
            {
                // Counted at the moment of asking rather than snapshotted at the
                // top of the frame. GravityWell.Active is appended to the instant
                // a well is instantiated, so a snapshot misses one UpdatePhase
                // just dropped and would let a fourth past the cap.
                bool dropped = LiveWellCount() < maxLiveWells && DropWell();

                // Blocked is not the same as skipped. The cap limits how many
                // wells are up at once; it is not meant to cost a scheduled drop,
                // and neither is a round of placement candidates all landing too
                // near the player. Retrying shortly also bounds the other half of
                // the Active-list lag: a well that collapsed earlier this frame is
                // only pruned when its OnDisable runs at end of frame, so it still
                // occupies a slot here — for a quarter of a second now, rather
                // than pushing the next well a full interval out.
                _wellTimer = dropped
                    ? (Phase >= 2 ? wellIntervalPhaseTwo : wellIntervalPhaseOne)
                    : wellRetryInterval;
            }

            _beamTimer -= dt;
            if (_beamTimer <= 0f && beam != null && !beam.IsActive)
            {
                beam.Fire();
                Telegraph(0.9f);
                Bark(NewtonBarks.Occasion.Beam);
                if (animator != null) animator.SetTrigger(Attack2Hash);
                _beamTimer = Phase >= 2 ? beamIntervalPhaseTwo : beamIntervalPhaseOne;
            }

            UpdateVolley(dt);

            if (Phase >= 2 && barrage != null) UpdateBarrage(dt);

            _barkTimer -= dt;
            if (_barkTimer <= 0f)
            {
                _barkTimer = unpromptedBarkInterval;
                Bark(NewtonBarks.Occasion.Unprompted);
            }
        }

        void UpdateVolley(float dt)
        {
            if (volley == null || _player == null) return;

            bool close = Vector3.Distance(_player.transform.position, transform.position)
                         <= weakPointRange;
            _volleyTimer -= dt * (close ? volleyCloseUrgency : 1f);
            if (_volleyTimer > 0f) return;

            volley.Fire();
            // Phase two staggers a second ring behind the first, so a single
            // jump no longer clears the whole attack.
            if (Phase >= 2) volley.Fire(0.65f);

            Telegraph(1f);
            Bark(NewtonBarks.Occasion.Volley);
            if (animator != null) animator.SetTrigger(AttackHash);
            _volleyTimer = Phase >= 2 ? volleyIntervalPhaseTwo : volleyIntervalPhaseOne;
        }

        void Bark(NewtonBarks.Occasion occasion)
        {
            if (barks != null) barks.Say(occasion);
        }

        void UpdatePhase()
        {
            if (Phase >= 2) return;
            if (Health.CurrentNormalised > phaseTwoAt) return;

            Phase = 2;
            Debug.Log("Newton phase 2 — apple barrage online.");
            Bark(NewtonBarks.Occasion.PhaseTwo);
            // A fresh well immediately, so the first barrage already has a field
            // to bend through rather than falling straight for its opening seconds.
            if (LiveWellCount() < maxLiveWells) DropWell();
            _barrageTimer = 1.5f;
        }

        /// <summary>
        /// Wells that are up right now.
        ///
        /// Nulls are skipped for the reason OnDied already guards for: the list is
        /// static, so it outlives any single fight and a leftover entry must not
        /// silently eat a slot.
        /// </summary>
        static int LiveWellCount()
        {
            int count = 0;
            for (int i = 0; i < GravityWell.Active.Count; i++)
                if (GravityWell.Active[i] != null) count++;
            return count;
        }

        void UpdateBarrage(float dt)
        {
            _barrageTimer -= dt;
            if (_barrageTimer > 0f) return;

            _barrageOn = !_barrageOn;
            if (_barrageOn)
            {
                barrage.Begin();
                _barrageTimer = barrageOnDuration;
                Telegraph(1.2f);
                Bark(NewtonBarks.Occasion.Barrage);
                if (animator != null) animator.SetTrigger(AttackHash);
            }
            else
            {
                barrage.End();
                _barrageTimer = barrageOffDuration;
            }
        }

        /// <summary>
        /// Place a well somewhere that is worth reading: not on the player, and
        /// biased toward the line between Newton and the player so it actually
        /// obstructs the approach instead of decorating the far wall.
        /// </summary>
        /// <returns>False when nothing was placed, so the caller can retry rather
        /// than treat a refused drop as a spent one.</returns>
        bool DropWell()
        {
            if (wellPrefab == null || _player == null) return false;

            Vector3 toPlayer = _player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f) toPlayer = Vector3.forward;
            Vector3 lane = toPlayer.normalized;

            Vector3 chosen = Vector3.zero;
            bool found = false;

            // A handful of candidates, best-of. Cheap, and much steadier than
            // rejection-sampling until something passes.
            for (int i = 0; i < 8; i++)
            {
                float along = Random.Range(0.35f, 0.9f) * wellPlacementRadius;
                float lateral = Random.Range(-1f, 1f) * wellPlacementRadius * 0.5f;
                Vector3 candidate = transform.position
                                  + lane * along
                                  + Vector3.Cross(Vector3.up, lane) * lateral;
                candidate.y = transform.position.y;

                if (Vector3.Distance(candidate, _player.transform.position)
                    < wellMinDistanceFromPlayer) continue;

                chosen = candidate;
                found = true;
                break;
            }

            if (!found) return false;

            Instantiate(wellPrefab, chosen, Quaternion.identity);
            Telegraph(0.7f);
            Bark(NewtonBarks.Occasion.Well);
            if (animator != null) animator.SetTrigger(AttackHash);
            return true;
        }

        void FacePlayer(float dt)
        {
            if (_player == null) return;
            Vector3 look = _player.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) return;

            // Turned from the stored facing rather than from transform.rotation.
            // When there is no model child the tells write their lean onto this
            // same transform, and reading that back would feed the lean into the
            // turn and let the rate limit spend itself undoing a pitch.
            _facing = Quaternion.RotateTowards(
                _facing,
                Quaternion.LookRotation(look.normalized, Vector3.up),
                turnDegreesPerSecond * dt);
            transform.rotation = _facing;

            if (animator != null) animator.SetFloat(SpeedHash, 0f);
        }

        /// <summary>Procedural stand-in for animation. A crouch-and-swell before a
        /// cast, a snap back after, and a recoil when struck — enough for the
        /// player to read intent off the silhouette alone.</summary>
        void UpdateTells(float dt)
        {
            _windUp = Mathf.MoveTowards(_windUp, 0f, dt * 1.6f);
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 4f);

            float swell = 1f + _windUp * 0.12f - _recoil * 0.08f;
            float squash = 1f - _windUp * 0.09f + _recoil * 0.05f;

            body.localScale = new Vector3(_bodyBaseScale.x * swell,
                                          _bodyBaseScale.y * squash,
                                          _bodyBaseScale.z * swell);

            Quaternion tilt = Quaternion.Euler(_windUp * 9f - _recoil * 14f, 0f, 0f);

            if (_bodyIsRoot)
            {
                // No model child, so the tell rides on the very transform
                // FacePlayer aims. Composed onto the facing instead of written
                // over it: assigning the lean alone here erases the yaw FacePlayer
                // set a moment earlier in this same Update, and the result is a
                // boss who never turns to face you — a failure that looks like a
                // broken AI rather than a missing reference.
                body.rotation = _facing * tilt;
                return;
            }

            if (_windUp > 0.01f || _recoil > 0.01f)
            {
                body.localRotation = tilt;
            }
            else if (body.localRotation != Quaternion.identity)
            {
                body.localRotation = Quaternion.RotateTowards(
                    body.localRotation, Quaternion.identity, 90f * dt);
            }
        }

        void Telegraph(float amount) => _windUp = Mathf.Max(_windUp, amount);

        /// <summary>Damage that actually landed on him is the player's regain
        /// currency — this is where a good exchange pays back a bad one.</summary>
        void OnHurt(float amount, Vector3 at)
        {
            _recoil = 1f;
            if (animator != null) animator.SetTrigger(HitHash);
            Bark(NewtonBarks.Occasion.Hurt);

            if (_player == null) return;
            var playerHealth = _player.GetComponent<Health>();
            if (playerHealth != null) playerHealth.CreditDamageDealt(amount);
        }

        void OnDied(Health health)
        {
            if (animator != null) animator.SetBool(DeadHash, true);
            if (barrage != null) { barrage.End(); barrage.ClearAirborne(); }
            if (beam != null) beam.Stop();
            if (volley != null) volley.Clear();
            Bark(NewtonBarks.Occasion.Death);

            // Collapse the field with him — leaving wells running after the fight
            // means the player is still being dragged around a won arena.
            for (int i = GravityWell.Active.Count - 1; i >= 0; i--)
                if (GravityWell.Active[i] != null) GravityWell.Active[i].Collapse();

            DropReward();
            Debug.Log("Newton defeated.");
        }

        /// <summary>The Gravity Anchor pickup, set down on the floor in front of
        /// him so the player walks into it on the way to his body.</summary>
        void DropReward()
        {
            if (rewardPrefab == null) return;
            Vector3 at = transform.position + transform.forward * 2f;
            float floorY = barrage != null ? barrage.GroundY : transform.position.y;
            at.y = floorY + 0.9f;
            Instantiate(rewardPrefab, at, Quaternion.identity);
        }

        /// <summary>
        /// His weak point is close range, by the inverse-square law: damage is
        /// scaled by (weakPointRange / r)², clamped between farDamageMultiplier
        /// and closeDamageCap. Applied as a filter on his own Health so the sword
        /// stays ignorant of who it hit.
        /// </summary>
        float ScaleByRange(float rawDamage, Vector3 point)
        {
            if (_player == null) return rawDamage;

            float distance = Vector3.Distance(_player.transform.position, transform.position);
            return rawDamage * RangeMultiplier(distance, weakPointRange,
                                               farDamageMultiplier, closeDamageCap);
        }

        /// <summary>Pure so the falloff can be tested without a scene.</summary>
        public static float RangeMultiplier(float distance, float reference, float min, float max)
        {
            float d = Mathf.Max(distance, 0.01f);
            float ratio = reference / d;
            return Mathf.Clamp(ratio * ratio, min, max);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, weakPointRange);
        }
    }
}
