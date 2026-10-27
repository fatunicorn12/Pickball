using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Grounds each foot independently by raycasting the world, plants the feet
    /// while the character is standing, and turns the head toward a look target.
    /// All three live here because OnAnimatorIK is only sent to components on
    /// the Animator's own GameObject, and this is the one already sitting there
    /// on both the chibi and Newton.
    ///
    /// The Animator's "Foot IK" checkbox only replays the IK goals authored in
    /// the clip — it has no idea what the ground under the character looks like.
    /// On flat ground that is enough; on a scanned mesh with uneven paving it
    /// leaves one foot hovering and the other buried, because both are placed
    /// at the height the animation assumed.
    ///
    /// This casts under each foot every frame and shifts it by how much higher
    /// or lower the surface under THAT foot is than the surface under the
    /// character, then drops the whole body by the deeper of the two
    /// corrections so the legs are not forced to over-extend reaching down.
    ///
    /// The shift preserves the animated foot height. The previous version
    /// pinned both feet to the surface outright, every frame, whatever the
    /// clip was doing — so the swing foot of every walk and run cycle was
    /// dragged along the floor, and a ground flip's feet were yanked down
    /// whenever they passed within reach of the ground. A foot is still never
    /// allowed below the surface, which is what keeps retargeting error from
    /// sinking it.
    ///
    /// It also takes out a sustained HOVER: the clips' root height is based on
    /// the Center of Mass, so any pose that carries the COM lower than standing
    /// (crouch, landing squat) floats by the difference. The lower foot's gap
    /// is low-pass filtered and removed from the body and both feet together.
    ///
    /// <b>Standing plant.</b> While he stands still in a plant state (see
    /// <see cref="plantStates"/>), both feet are pinned flat on the surface at a
    /// fixed WORLD position and re-planted with a short step whenever the pose
    /// has drifted too far from them. Measured in a Play-mode harness (both
    /// rigs, their idles, flat floor and a 2.4 cm-noise scan-like patch):
    ///   - turning on the spot (lock-on squaring up, Newton tracking the
    ///     player at 90°/s, a well dragging a stood-still player) rotated or
    ///     slid the whole leg set with the root: the feet skated 5-11 mm every
    ///     frame and were never still. Planted, each foot is dead still about
    ///     half the frames and moves in ~9 steps per 4 s of turning.
    ///   - on the noisy patch, a sliding foot hopped between triangles: 2-3 mm
    ///     mean, 15 mm worst frame-to-frame jerk. Planted, it is zero.
    ///   - at rest the chibi's right sole hovered 1.0-1.7 cm; planted, 0.
    /// Before the idle clips' left-leg reference fix this also mattered far
    /// more: the retargeted idles lifted the LEFT foot 13 cm (chibi) and
    /// 17.6 cm (Newton) off the floor, and "keep the animated height" kept it.
    /// The plant would hold a foot down through any similar regression.
    ///
    /// Requires "IK Pass" on the Animator's base layer, which both setup
    /// scripts enable; without it Unity never calls OnAnimatorIK and this
    /// silently does nothing.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class ChibiFootIK : MonoBehaviour
    {
        [Tooltip("How far above/below the foot to search for ground.")]
        [SerializeField] private float castAbove = 0.5f;
        [SerializeField] private float castBelow = 0.6f;

        [Tooltip("Extra clearance under the sole, on top of the avatar's own " +
                 "ankle-to-sole height (Animator.left/rightFeetBottomHeight: " +
                 "0.085 m on the chibi, 0.099 m on Newton since the rig fix). " +
                 "That height used to be a hard-coded 0.06 here, which sank the " +
                 "re-rigged feet ~2.5 cm into the floor.")]
        [SerializeField] private float extraSoleClearance = 0f;

        [Tooltip("Largest downward body shift allowed when one foot is much lower.")]
        [SerializeField] private float maxBodyDrop = 0.35f;

        [Tooltip("Largest correction for a pose whose feet hover above the ground " +
                 "while grounded. The clips' root height is based on the Center " +
                 "of Mass, so a pose whose COM sits lower than standing (a crouch, " +
                 "a landing squat) is played with the COM pinned at standing " +
                 "height and the whole character floats — 28-34 cm for the crouch.")]
        [SerializeField] private float maxFloatCorrection = 0.6f;
        [Tooltip("How quickly a hover is taken out, 1/s. Deliberately slow next to " +
                 "a stride, so a run's flight phase (both feet briefly up) is not " +
                 "flattened, while a held crouch settles within a few tenths of a second.")]
        [SerializeField] private float floatSettleRate = 6f;

        [Tooltip("How fast IK weights and body height follow changes.")]
        [SerializeField] private float smoothing = 12f;

        [Tooltip("How quickly each foot's terrain offset follows the surface under " +
                 "it, 1/s. The scanned world collider is a dense, noisy mesh, so a " +
                 "foot moving a few millimetres can land on a triangle a centimetre " +
                 "higher; applied raw, that popped the foot every frame it crossed " +
                 "an edge. Fast enough to follow a real step-up within a stride.")]
        [SerializeField] private float terrainFollowRate = 25f;

        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private bool alignToSlope = true;

        [Header("Standing plant")]
        [Tooltip("Pin both feet to the floor at a fixed world position while he " +
                 "stands still, stepping to re-plant when the body has turned or " +
                 "drifted away from them. See the class summary for what it fixes.")]
        [SerializeField] private bool plantWhenStanding = true;
        [Tooltip("Base-layer states the plant may engage in, by name. Locomotion is " +
                 "the idle/walk blend on both characters; Beam is Newton's held cast, " +
                 "which he tracks the player through for seconds at a time. States " +
                 "whose clips step or stagger (Stop, Land, casts, Hit) are left out " +
                 "so their authored footwork plays untouched.")]
        [SerializeField] private string[] plantStates = { "Locomotion", "Beam" };
        [Tooltip("Speed parameter (the damped one the blend tree reads) below which " +
                 "he counts as standing. The chibi's walk threshold is 0.98 and " +
                 "Newton's 1.82, so this is under 15% walk on either.")]
        [SerializeField] private float plantMaxSpeed = 0.12f;
        [Tooltip("Plant blend-in rate, weight per second. Slow enough that a foot " +
                 "the clip lifted settles onto the floor rather than snapping.")]
        [SerializeField] private float plantBlendIn = 6f;
        [Tooltip("Plant blend-out rate, weight per second. Fast, so the first step " +
                 "of a walk is not held back by feet that are still pinned.")]
        [SerializeField] private float plantBlendOut = 14f;

        [Tooltip("A planted foot steps once the pose wants it this far away, metres. " +
                 "Above the idle's own 4 cm sway, so a standing idle never shuffles.")]
        [SerializeField] private float stepDistance = 0.12f;
        [Tooltip("...or turned this far from where it is planted, degrees. At " +
                 "Newton's 90°/s turn that is a step every third of a second.")]
        [SerializeField] private float stepAngle = 30f;
        [Tooltip("Length of one re-planting step, seconds.")]
        [SerializeField] private float stepSeconds = 0.2f;
        [Tooltip("Peak lift of the foot during a step, metres.")]
        [SerializeField] private float stepHeight = 0.06f;
        [Tooltip("When one foot finishes a step, the other follows straight away if " +
                 "it is at least this fraction of the way to needing one, so a turn " +
                 "ends with both feet squared up instead of one left twisted behind.")]
        [SerializeField, Range(0f, 1f)] private float followThrough = 0.35f;
        [Tooltip("Further than this from its plant (a teleport, a respawn, a shove " +
                 "faster than steps can follow) a foot stops being held and simply " +
                 "tracks the pose until it can plant again.")]
        [SerializeField] private float releaseDistance = 0.5f;

        [Header("Look-at")]
        [Tooltip("Overall look-at weight while a target is set (ChibiLocomotion: the " +
                 "lock-on target; NewtonBoss: the player).")]
        [SerializeField, Range(0f, 1f)] private float lookWeight = 0.8f;
        [Tooltip("How much of the turn the chest takes. Small: the head does the looking.")]
        [SerializeField, Range(0f, 1f)] private float lookBodyWeight = 0.15f;
        [SerializeField, Range(0f, 1f)] private float lookHeadWeight = 0.9f;
        [Tooltip("0.5 = the head can turn up to 90° off the body's facing, beyond " +
                 "which Unity fades the look-at out instead of wrenching the neck.")]
        [SerializeField, Range(0f, 1f)] private float lookClampWeight = 0.55f;
        [Tooltip("How fast the look-at fades in and out when the target appears or " +
                 "goes away, weight per second.")]
        [SerializeField] private float lookBlendSpeed = 2.5f;
        [Tooltip("How fast the look point chases the target, 1/s, so a lock-on " +
                 "switch swings the head across rather than teleporting it.")]
        [SerializeField] private float lookFollowRate = 8f;

        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        Animator _anim;
        Transform _root;
        bool _paramsScanned, _hasGroundedParam, _hasSpeedParam;
        float _bodyDrop;
        float _float;
        float _weight;
        float _referenceY;
        int[] _plantStateHashes;
        float _plantWeight;
        Plant _leftPlant, _rightPlant;
        float _leftTerrain, _rightTerrain;
        bool _terrainPrimed;
        float _lookBlend;
        Vector3 _lookPoint;
        bool _lookPrimed;
        int _lastIKFrame = -10;

        /// <summary>World point to turn the head toward, or null for none. Set it
        /// every frame it applies; the look-at eases out once it is cleared.</summary>
        public Vector3? LookTarget { get; set; }

        /// <summary>Fade the feet back to the clip, as if airborne — for a death
        /// fall or anything else whose feet must not be held to the floor.
        /// Faded rather than switched off so the legs do not pop.</summary>
        public bool Suppressed { get; set; }

        /// <summary>True while the IK pass is actually running and planting is on —
        /// i.e. turning on the spot is being taken care of by stepping feet.
        /// NewtonBoss reads this to decide whether he still needs the old walk
        /// shuffle (an un-rebuilt controller has no IK pass, so this stays false
        /// and the shuffle carries on).</summary>
        public bool HandlesTurning =>
            plantWhenStanding && isActiveAndEnabled && Time.frameCount - _lastIKFrame <= 2;

        struct Plant
        {
            public bool active;
            public Vector3 pos;
            public Quaternion rot;
            public bool stepping;
            public float t;
            public Vector3 fromPos;
            public Quaternion fromRot;
        }

        void Awake()
        {
            _anim = GetComponent<Animator>();
            // The character root is the ground reference and the thing the casts
            // must never hit. The chibi's is the CharacterController; Newton has a
            // plain CapsuleCollider on the parent of his model instead, and a foot
            // cast clipping the bottom of that capsule would stand him on himself.
            var cc = GetComponentInParent<CharacterController>();
            _root = cc != null ? cc.transform
                  : transform.parent != null ? transform.parent : transform;
            CachePlantStates();
        }

        void OnValidate() => CachePlantStates();

        void OnDisable()
        {
            _leftPlant = default;
            _rightPlant = default;
            _plantWeight = 0f;
            _lookBlend = 0f;
            _terrainPrimed = false;
        }

        void CachePlantStates()
        {
            _plantStateHashes = new int[plantStates?.Length ?? 0];
            for (int i = 0; i < _plantStateHashes.Length; i++)
                _plantStateHashes[i] = Animator.StringToHash(plantStates[i]);
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (_anim == null || layerIndex != 0 || !isActiveAndEnabled) return;
            _lastIKFrame = Time.frameCount;
            float dt = Time.deltaTime;

            // Looked up once per controller rather than reading
            // Animator.parameters every frame, which allocates a fresh array.
            if (!_paramsScanned)
            {
                _paramsScanned = true;
                foreach (var p in _anim.parameters)
                {
                    if (p.nameHash == GroundedHash) _hasGroundedParam = true;
                    if (p.nameHash == SpeedHash) _hasSpeedParam = true;
                }
            }

            UpdateLookAt(dt);

            // Fade IK out entirely while airborne — planting feet on the ground
            // during a jump is worse than no IK at all. A controller without a
            // Grounded parameter (Newton's) is a character that never leaves it.
            bool grounded = !Suppressed && (!_hasGroundedParam || _anim.GetBool(GroundedHash));
            _weight = Mathf.MoveTowards(_weight, grounded ? 1f : 0f, smoothing * dt);
            if (_weight <= 0.001f)
            {
                _bodyDrop = 0f;
                _float = 0f;
                _plantWeight = 0f;
                _leftPlant.active = _rightPlant.active = false;
                _terrainPrimed = false;
                return;
            }

            // The surface the animation is standing on: straight under the
            // character. Each foot is corrected by its OWN surface's offset
            // from this one.
            _referenceY = _root.position.y;
            Vector3 centre = _root.position + Vector3.up * castAbove;
            if (CastGround(centre, castAbove + castBelow, out var under))
                _referenceY = under.point.y;

            bool okL = Probe(AvatarIKGoal.LeftFoot, _anim.leftFeetBottomHeight, out var left);
            bool okR = Probe(AvatarIKGoal.RightFoot, _anim.rightFeetBottomHeight, out var right);

            // Hover: how far the LOWER foot sits above its own surface. Smoothed
            // slowly and taken out of the body and both feet together, so the
            // legs keep their shape and the stance comes down onto the floor.
            float hover = 0f;
            if (okL || okR)
                hover = Mathf.Min(okL ? left.gap : float.MaxValue, okR ? right.gap : float.MaxValue);
            hover = Mathf.Clamp(hover, 0f, maxFloatCorrection);
            _float = Mathf.Lerp(_float, hover, 1f - Mathf.Exp(-floatSettleRate * dt));

            // Each foot's height difference from the reference surface, low-passed
            // so a noisy scan does not pop the foot from triangle to triangle.
            float follow = 1f - Mathf.Exp(-terrainFollowRate * dt);
            if (!_terrainPrimed)
            {
                _leftTerrain = okL ? left.hit.point.y - _referenceY : 0f;
                _rightTerrain = okR ? right.hit.point.y - _referenceY : 0f;
                _terrainPrimed = true;
            }
            if (okL) _leftTerrain = Mathf.Lerp(_leftTerrain, left.hit.point.y - _referenceY, follow);
            if (okR) _rightTerrain = Mathf.Lerp(_rightTerrain, right.hit.point.y - _referenceY, follow);

            float dropL = okL ? Solve(ref left, _leftTerrain) : 0f;
            float dropR = okR ? Solve(ref right, _rightTerrain) : 0f;

            UpdatePlant(grounded && okL && okR, ref left, ref right, dt);

            if (okL) Write(AvatarIKGoal.LeftFoot, left, ref _leftPlant);
            if (okR) Write(AvatarIKGoal.RightFoot, right, ref _rightPlant);

            // Body follows the foot that needs to go furthest down.
            float wanted = Mathf.Clamp(Mathf.Min(dropL, dropR), -maxBodyDrop, 0f);
            _bodyDrop = Mathf.Lerp(_bodyDrop, wanted, 1f - Mathf.Exp(-smoothing * dt));

            float shift = _bodyDrop - _float;
            if (Mathf.Abs(shift) > 0.0005f)
            {
                var body = _anim.bodyPosition;
                body.y += shift * _weight;
                _anim.bodyPosition = body;
            }
        }

        // ---------------------------------------------------------- terrain

        struct FootProbe
        {
            public Vector3 target;
            public Quaternion animRot;
            public RaycastHit hit;
            public float sole;
            /// <summary>Height above its own surface after terrain correction.</summary>
            public float gap;

            // Filled by Solve: the terrain-following answer, which keeps the
            // animated lift...
            public Vector3 solvedPos;
            public Quaternion solvedRot;
            public float solvedRotWeight;
            // ...and the same foot set flat on its surface, which is what a
            // plant pins.
            public Vector3 groundPos;
            public Quaternion groundRot;
        }

        /// <summary>Cast under one foot. False if there is no ground in reach.</summary>
        bool Probe(AvatarIKGoal goal, float soleHeight, out FootProbe probe)
        {
            probe = default;
            probe.target = _anim.GetIKPosition(goal);
            probe.animRot = _anim.GetIKRotation(goal);
            Vector3 origin = probe.target + Vector3.up * castAbove;
            if (!CastGround(origin, castAbove + castBelow, out probe.hit)) return false;
            probe.sole = soleHeight + extraSoleClearance;
            // The animated height above the surface the animation assumed (the
            // one under the character), which is what this foot should keep
            // above its own surface.
            probe.gap = probe.target.y - _referenceY - probe.sole;
            return true;
        }

        /// <summary>Nearest ground hit below <paramref name="origin"/> that is not
        /// part of this character. All hits rather than the first: Newton's own
        /// capsule can be the first thing a cast meets.</summary>
        bool CastGround(Vector3 origin, float distance, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, distance,
                                            groundMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].transform.IsChildOf(_root)) continue;
                if (Hits[i].distance >= nearest) continue;
                nearest = Hits[i].distance;
                best = Hits[i];
            }
            return nearest < float.MaxValue;
        }

        /// <summary>Correct one foot for the surface under it and the hover
        /// removal. Returns the correction beyond the hover removal (negative =
        /// the foot had to reach further down).</summary>
        float Solve(ref FootProbe f, float terrain)
        {
            float lowered = f.target.y - _float;
            float floor = f.hit.point.y + f.sole;
            // Keep the animated lift (swing foot, flip, step-up), shift it by
            // the terrain's height difference, and never let it end up inside
            // the surface.
            float solvedY = Mathf.Max(lowered + terrain, floor);
            f.solvedPos = new Vector3(f.target.x, solvedY, f.target.z);

            Quaternion toNormal = alignToSlope
                ? Quaternion.FromToRotation(Vector3.up, f.hit.normal) : Quaternion.identity;
            f.groundRot = toNormal * f.animRot;
            f.groundPos = new Vector3(f.target.x, floor, f.target.z);

            // Only a foot that is actually down gets rolled onto the slope; a
            // foot mid-swing keeps the clip's own rotation.
            float lift = solvedY - floor;
            f.solvedRot = f.groundRot;
            f.solvedRotWeight = alignToSlope ? Mathf.Clamp01(1f - lift / 0.15f) : 0f;
            return solvedY - lowered;
        }

        void Write(AvatarIKGoal goal, in FootProbe f, ref Plant plant)
        {
            Vector3 pos = f.solvedPos;
            Quaternion rot = f.solvedRot;
            float rotWeight = f.solvedRotWeight;
            if (_plantWeight > 0f && plant.active)
            {
                PlantedPose(plant, f, out var pPos, out var pRot);
                pos = Vector3.Lerp(pos, pPos, _plantWeight);
                rot = Quaternion.Slerp(rot, pRot, _plantWeight);
                rotWeight = Mathf.Lerp(rotWeight, 1f, _plantWeight);
            }
            _anim.SetIKPositionWeight(goal, _weight);
            _anim.SetIKPosition(goal, pos);
            _anim.SetIKRotationWeight(goal, _weight * rotWeight);
            _anim.SetIKRotation(goal, rot);
        }

        // ------------------------------------------------------------ plant

        void UpdatePlant(bool canPlant, ref FootProbe left, ref FootProbe right, float dt)
        {
            bool want = plantWhenStanding && canPlant && IsStanding();
            _plantWeight = Mathf.MoveTowards(_plantWeight, want ? 1f : 0f,
                                             (want ? plantBlendIn : plantBlendOut) * dt);
            if (_plantWeight <= 0f)
            {
                _leftPlant.active = _rightPlant.active = false;
                return;
            }

            // A fresh plant goes down where the pose has the foot, flattened onto
            // the floor. The blend weight ramps it in, so nothing pops.
            if (!_leftPlant.active) Begin(ref _leftPlant, left);
            if (!_rightPlant.active) Begin(ref _rightPlant, right);

            bool landedL = Advance(ref _leftPlant, left, dt);
            bool landedR = Advance(ref _rightPlant, right, dt);

            Follow(ref _leftPlant, left);
            Follow(ref _rightPlant, right);

            if (_leftPlant.stepping || _rightPlant.stepping) return;

            float needL = Need(_leftPlant, left), needR = Need(_rightPlant, right);
            // One foot at a time, the one furthest out first; a foot that just
            // landed hands over to the other if it is lagging at all.
            if (landedL && needR >= followThrough) StartStep(ref _rightPlant);
            else if (landedR && needL >= followThrough) StartStep(ref _leftPlant);
            else if (needL >= 1f && needL >= needR) StartStep(ref _leftPlant);
            else if (needR >= 1f) StartStep(ref _rightPlant);
        }

        /// <summary>Standing still, in a state whose clip has nothing for the feet
        /// to do. Transitions count only when both ends are plant states, so the
        /// plant lets go the moment a step-out (Stop, Land, a cast) begins.</summary>
        bool IsStanding()
        {
            if (_hasSpeedParam && _anim.GetFloat(SpeedHash) > plantMaxSpeed) return false;
            if (!IsPlantState(_anim.GetCurrentAnimatorStateInfo(0).shortNameHash)) return false;
            return !_anim.IsInTransition(0) ||
                   IsPlantState(_anim.GetNextAnimatorStateInfo(0).shortNameHash);
        }

        bool IsPlantState(int hash)
        {
            for (int i = 0; i < _plantStateHashes.Length; i++)
                if (_plantStateHashes[i] == hash) return true;
            return false;
        }

        static void Begin(ref Plant p, in FootProbe f)
        {
            p.active = true;
            p.stepping = false;
            p.pos = f.groundPos;
            p.rot = f.groundRot;
        }

        static void StartStep(ref Plant p)
        {
            p.stepping = true;
            p.t = 0f;
            p.fromPos = p.pos;
            p.fromRot = p.rot;
        }

        /// <summary>Moves a step along; true on the frame it lands.</summary>
        bool Advance(ref Plant p, in FootProbe f, float dt)
        {
            if (!p.stepping) return false;
            p.t += dt / Mathf.Max(stepSeconds, 0.01f);
            if (p.t < 1f) return false;
            p.stepping = false;
            p.pos = f.groundPos;
            p.rot = f.groundRot;
            return true;
        }

        /// <summary>A foot left too far behind to step after stops being held.</summary>
        void Follow(ref Plant p, in FootProbe f)
        {
            if (p.stepping) return;
            Vector3 d = f.groundPos - p.pos;
            d.y = 0f;
            if (d.sqrMagnitude <= releaseDistance * releaseDistance) return;
            p.pos = f.groundPos;
            p.rot = f.groundRot;
        }

        /// <summary>How badly a planted foot wants to step: 1 = at the threshold.
        /// Yaw only — the idle's own foot pitch changes are not a reason to step.</summary>
        float Need(in Plant p, in FootProbe f)
        {
            Vector3 d = f.groundPos - p.pos;
            d.y = 0f;
            float dist = d.magnitude / Mathf.Max(stepDistance, 0.001f);
            Vector3 a = Vector3.ProjectOnPlane(p.rot * Vector3.forward, Vector3.up);
            Vector3 b = Vector3.ProjectOnPlane(f.groundRot * Vector3.forward, Vector3.up);
            float yaw = a.sqrMagnitude > 1e-4f && b.sqrMagnitude > 1e-4f ? Vector3.Angle(a, b) : 0f;
            return Mathf.Max(dist, yaw / Mathf.Max(stepAngle, 0.001f));
        }

        /// <summary>Where a plant puts the foot this frame: held still, or partway
        /// through a step toward where the pose now has it, lifted on an arc.</summary>
        void PlantedPose(in Plant p, in FootProbe f, out Vector3 pos, out Quaternion rot)
        {
            if (!p.stepping)
            {
                pos = p.pos;
                rot = p.rot;
                return;
            }
            float t = Mathf.Clamp01(p.t);
            float e = t * t * (3f - 2f * t);
            pos = Vector3.Lerp(p.fromPos, f.groundPos, e) + Vector3.up * (stepHeight * Mathf.Sin(Mathf.PI * t));
            rot = Quaternion.Slerp(p.fromRot, f.groundRot, e);
        }

        // ---------------------------------------------------------- look-at

        void UpdateLookAt(float dt)
        {
            bool has = LookTarget.HasValue;
            _lookBlend = Mathf.MoveTowards(_lookBlend, has ? 1f : 0f, lookBlendSpeed * dt);
            if (has)
            {
                if (!_lookPrimed) { _lookPoint = LookTarget.Value; _lookPrimed = true; }
                _lookPoint = Vector3.Lerp(_lookPoint, LookTarget.Value,
                                          1f - Mathf.Exp(-lookFollowRate * dt));
            }
            if (_lookBlend <= 0.001f)
            {
                _lookPrimed = has;
                return;
            }
            // Eased, so the head turns toward a new target and settles rather
            // than starting and stopping at full rate.
            float w = _lookBlend * _lookBlend * (3f - 2f * _lookBlend) * lookWeight;
            _anim.SetLookAtWeight(w, lookBodyWeight, lookHeadWeight, 0f, lookClampWeight);
            _anim.SetLookAtPosition(_lookPoint);
        }
    }
}
