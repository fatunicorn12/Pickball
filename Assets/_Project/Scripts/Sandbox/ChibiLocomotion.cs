using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Camera-relative locomotion for the chibi: walk/jog/sprint blending,
    /// jump (Space) and crouch (Ctrl).
    ///
    /// Speeds are taken from what the Mixamo clips are actually authored at,
    /// measured off the source FBXs rather than guessed. The base set is the
    /// ARMED sword-and-shield locomotion:
    ///   armed walk 0.61 m/s; armed run 4.84 m/s (raw 1.686 on the pack's
    ///   small rig, scale-normalised to the old Running.fbx rig — see
    ///   ChibiSandboxSetup.WalkSpeed/RunSpeed for the measurements).
    /// The blend-tree thresholds use the same numbers, so the stride matches
    /// real travel and the feet stop sliding. Holding no modifier jogs; the
    /// walk clip is what he passes through while accelerating from rest.
    ///
    /// Jump physics are matched to Jumping.fbx: the clip is airborne for 0.37s
    /// with the hips rising ~0.24, so the default jump height is set to put the
    /// character in the air for about the same time.
    ///
    /// Standalone from the pickleball systems, and Input-System-only (this
    /// project has activeInputHandler = 1, so legacy Input would throw).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ChibiLocomotion : MonoBehaviour
    {
        [Header("Speeds (m/s) — match the clips' authored speeds")]
        [Tooltip("Default move speed. Sits between the armed walk (0.61) and " +
                 "run (4.84) clips so the blend tree crossfades walk->run, which " +
                 "reads better than the Standing Sprint Forward clip did.")]
        [SerializeField] private float moveSpeed = 2.8f;
        [SerializeField] private float sprintSpeed = 4.84f;
        [Tooltip("Authored speed of the Crouched Walking clip.")]
        [SerializeField] private float crouchSpeed = 0.39f;
        [SerializeField] private float acceleration = 14f;

        [Header("Run to stop")]
        [Tooltip("Releasing the stick above this speed plays the Run To Stop clip.")]
        [SerializeField] private float stopTriggerSpeed = 3f;
        [Tooltip("Deceleration used for that stop. Run To Stop takes 0.97s to " +
                 "come to rest, so 4.75 / 5 lands almost exactly on the clip.")]
        [SerializeField] private float stopDeceleration = 5f;

        [Header("Turning")]
        [SerializeField] private float turnSmoothTime = 0.09f;

        [Header("Jump / gravity")]
        [Tooltip("Standing jump. Jumping.fbx is aloft 0.37s, which this matches.")]
        [SerializeField] private float jumpHeight = 0.35f;
        [Tooltip("Running jump. Running Jump.fbx is aloft 0.50s, needing a taller " +
                 "arc; height is lerped between the two by current speed so the " +
                 "clip and the physics agree at both ends.")]
        [SerializeField] private float runJumpHeight = 0.56f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float groundedStick = -2f;
        [Tooltip("Grace period after leaving a ledge during which jump still works.")]
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing still fires on landing.")]
        [SerializeField] private float jumpBuffer = 0.12f;

        [Header("Double jump (feather)")]
        [Tooltip("Higher than the ground jump so the flip has room to read. " +
                 "The clip is also time-scaled to the airtime, so this can be " +
                 "tuned freely without the animation ever over- or under-running.")]
        [SerializeField] private float doubleJumpHeight = 1.5f;
        [Tooltip("Airborne length of the trimmed Front Flip (frames 5-41).")]
        [SerializeField] private float flipAirborneSeconds = 1.20f;
        [Tooltip("Puff spawned at the feet on the air jump — the cloud-in-a-bottle beat.")]
        [SerializeField] private ParticleSystem doubleJumpEffect;

        [Header("Dash (protractor) — fixed 45°")]
        [Tooltip("Speed along the dash vector. 14 m/s over 0.22s covers 3.08m: " +
                 "2.18 across and 2.18 up. Fixed, so gaps and ledges can be " +
                 "designed against an exact reach.")]
        [SerializeField] private float dashSpeed = 14f;
        [SerializeField] private float dashDuration = 0.22f;
        [SerializeField] private float dashCooldown = 0.6f;
        [Tooltip("Fraction of the dash's upward speed kept when it ends. Low " +
                 "values give a crisp stop at the apex; high values float on.")]
        [SerializeField, Range(0f, 1f)] private float dashExitVerticalKeep = 0.35f;
        [SerializeField, Range(0f, 1f)] private float dashExitHorizontalKeep = 0.7f;
        [SerializeField] private DashEffects dashEffects;

        [Header("Carried momentum")]
        [Tooltip("Horizontal velocity carried from swings and launches, on top " +
                 "of input movement. Walking alone never sets this; it exists " +
                 "so a tether release can throw you and have it mean something.")]
        [SerializeField] private float momentumAirDrag = 1.6f;
        [SerializeField] private float momentumGroundDrag = 11f;

        [Header("Tether (orbital)")]
        [SerializeField] private float tetherMaxLength = 14f;
        [Tooltip("Floor on rope length. Short ropes orbit viciously fast — at " +
                 "1.6m a swing from horizontal already laps the anchor about " +
                 "once a second — so latching close still gives a sane arc.")]
        [SerializeField] private float tetherMinLength = 4f;
        [Tooltip("Gravity while swinging. Gentler than the jump gravity, which " +
                 "is tuned for snappy platforming and makes a brutal pendulum.")]
        [SerializeField] private float swingGravity = -11f;
        [Tooltip("Push along the tangent from movement input — this is now the " +
                 "only way to gain speed, so a swing has to be worked up.")]
        [SerializeField] private float swingPumpAccel = 7f;
        [Tooltip("Bleeds off swing energy so it settles instead of oscillating forever.")]
        [SerializeField] private float swingDamping = 0.12f;
        [Tooltip("Hard ceiling on swing speed. Without it any small energy gain " +
                 "compounds every orbit until the solver is flinging you.")]
        [SerializeField] private float maxSwingSpeed = 13f;
        [Tooltip("How much of your run-up carries into the swing on latching. " +
                 "Low, so you start gentle and build from there.")]
        [SerializeField] private float entrySpeedCap = 4.5f;
        [Tooltip("Speed kept after a solid collision mid-swing. 0 = dead stop.")]
        [SerializeField, Range(0f, 1f)] private float impactSpeedRetain = 0.25f;
        [Tooltip("Fraction of travel that must be lost before it counts as a hit " +
                 "rather than a graze along a wall.")]
        [SerializeField, Range(0f, 1f)] private float impactMinLoss = 0.25f;
        [Tooltip("Upward kick when you jump off the rope, on top of the swing's own velocity.")]
        [SerializeField] private float tetherReleaseJump = 4.2f;
        [SerializeField] private TetherSystem tether;
        [Tooltip("How long the hook-throw gesture is blended in over the upper " +
                 "body. The latch itself is instant; this is purely the tell. " +
                 "Matched to the trimmed throw clip (frames 55-80 = 0.87s) so " +
                 "the arm finishes its follow-through rather than snapping back.")]
        [SerializeField] private float gestureSeconds = 0.87f;

        [Header("Skateboard mount")]
        [Tooltip("Skating is fast; the clips are authored ~0.95 m/s but the feet " +
                 "never leave the board, so a faster ride does not read as sliding.")]
        [SerializeField] private float skateSpeed = 6.2f;
        [SerializeField] private float skateAcceleration = 5f;
        [Tooltip("Skating turns lazily — a board cannot pivot on the spot.")]
        [SerializeField] private float skateTurnSmoothTime = 0.28f;
        [Tooltip("How far the model rises when mounted — he stands ON the deck, " +
                 "not in it. Measured from the board's bounds by the setup.")]
        [SerializeField] private float skateRideHeight = 0.2f;

        [Header("Crouch")]
        [Tooltip("Capsule height while crouched, as a fraction of standing height.")]
        [SerializeField, Range(0.3f, 1f)] private float crouchHeightFraction = 0.62f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [Tooltip("The model under this controller. Offsetting it moves the " +
                 "visual without moving the capsule.")]
        [SerializeField] private Transform modelRoot;
        [Tooltip("Vertical nudge for the model against the capsule. Humanoid " +
                 "retargeting onto chibi proportions can leave the feet below " +
                 "the capsule base; raise this slightly if they still clip. " +
                 "Try Foot IK first — this is the blunt instrument.")]
        [SerializeField, Range(-0.3f, 0.7f)] private float modelYOffset;
        [Tooltip("Nudge if the feet still slide — scales clip playback against travel.")]
        [SerializeField, Range(0.5f, 2f)] private float strideScale = 1f;

        [Header("Camera")]
        [SerializeField] private Transform cameraTransform;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int MultiplierHash = Animator.StringToHash("SpeedMultiplier");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int CrouchHash = Animator.StringToHash("Crouch");
        static readonly int JumpHash = Animator.StringToHash("Jump");
        static readonly int VerticalHash = Animator.StringToHash("VerticalSpeed");
        static readonly int StopHash = Animator.StringToHash("Stop");
        static readonly int FlipHash = Animator.StringToHash("Flip");
        static readonly int SkatingHash = Animator.StringToHash("Skating");
        static readonly int FlipSpeedHash = Animator.StringToHash("FlipSpeed");
        static readonly int DashHash = Animator.StringToHash("Dash");
        static readonly int TetheredHash = Animator.StringToHash("Tethered");
        static readonly int FireHash = Animator.StringToHash("Fire");

        CharacterController _cc;
        InputAction _move, _sprint, _jump, _crouch, _flip, _mount, _dash, _tether;
        Vector3 _dashVelocity;
        float _dashTimeLeft, _dashReadyAt;
        bool _airDashUsed;

        /// <summary>Horizontal momentum from swings/launches, decaying. Kept
        /// separate from input movement so steering still works while it bleeds
        /// off, instead of the two fighting for the same variable.</summary>
        Vector3 _momentum;
        Vector3 _swingVelocity;
        int _gestureLayer = -1;
        float _gestureTimeLeft, _gestureWeight;
        TetherAnchor _tetherAnchor;
        float _ropeLength;
        bool _tethered;
        PlayerAbilities _abilities;
        SkateboardMount _board;
        int _airJumpsUsed;
        bool _skating;
        float _rideLift;

        float _currentSpeed, _turnVelocity, _verticalVelocity;
        float _lastGroundedTime, _jumpPressedTime, _jumpLatchUntil;
        bool _crouching, _stopping;
        float _standHeight, _standRadius;
        Vector3 _standCenter;

        /// <summary>Set by MeleeCombat while a swing is playing: input steering is
        /// suppressed but gravity, momentum and the attack's lunge still apply,
        /// so an attack plants the character without freezing him mid-air.</summary>
        public bool MovementLocked { get; set; }

        /// <summary>True while another system owns movement — tether, skateboard
        /// or dash. Attacks refuse to start during these rather than fighting
        /// them for the CharacterController.</summary>
        public bool IsBusy => _tethered || _skating || _dashTimeLeft > 0f;

        public bool IsGrounded { get; private set; }

        /// <summary>Push used by attack lunges and anything else that needs to
        /// shove the character without owning his movement outright.</summary>
        public void AddMomentum(Vector3 delta) => _momentum += delta;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _abilities = GetComponent<PlayerAbilities>();
            _board = GetComponentInChildren<SkateboardMount>(true);
            _standHeight = _cc.height;
            _standCenter = _cc.center;
            _standRadius = _cc.radius;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                // Helps hold a planted foot still while the body moves over it,
                // which matters more than usual on chibi proportions where
                // retargeting error is large.
                animator.stabilizeFeet = true;
                // Masked upper-body layer for the hook throw. Held at zero
                // weight so an empty state cannot overwrite the arms; it is
                // faded in only for the gesture itself.
                _gestureLayer = animator.GetLayerIndex("Gesture");
                if (_gestureLayer >= 0) animator.SetLayerWeight(_gestureLayer, 0f);
            }
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
            ApplyModelOffset();
        }

        void OnValidate()
        {
            // Live in the inspector so the offset can be dialled in while
            // looking at the feet, without entering play mode.
            ApplyModelOffset();
        }

        void ApplyModelOffset()
        {
            if (modelRoot == null) return;
            var p = modelRoot.localPosition;
            modelRoot.localPosition = new Vector3(p.x, modelYOffset + _rideLift, p.z);
        }

        /// <summary>Eases the model up onto the deck and back down, so mounting
        /// is not an instant pop.</summary>
        void UpdateRideLift(float dt)
        {
            float wanted = _skating ? skateRideHeight : 0f;
            if (Mathf.Approximately(_rideLift, wanted)) return;
            _rideLift = Mathf.MoveTowards(_rideLift, wanted, 1.2f * dt);
            ApplyModelOffset();
        }

        void OnEnable()
        {
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _sprint = new InputAction("Sprint", InputActionType.Button);
            _sprint.AddBinding("<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftTrigger");

            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _crouch = new InputAction("Crouch", InputActionType.Button);
            _crouch.AddBinding("<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Keyboard>/rightCtrl");
            _crouch.AddBinding("<Gamepad>/buttonEast");

            _flip = new InputAction("Flip", InputActionType.Button);
            _flip.AddBinding("<Keyboard>/f");
            _flip.AddBinding("<Gamepad>/buttonNorth");

            _mount = new InputAction("Mount", InputActionType.Button);
            _mount.AddBinding("<Keyboard>/r");
            _mount.AddBinding("<Gamepad>/buttonWest");

            _dash = new InputAction("Dash", InputActionType.Button);
            _dash.AddBinding("<Keyboard>/e");
            _dash.AddBinding("<Gamepad>/rightShoulder");

            _tether = new InputAction("Tether", InputActionType.Button);
            _tether.AddBinding("<Keyboard>/q");
            _tether.AddBinding("<Mouse>/rightButton");
            _tether.AddBinding("<Gamepad>/leftShoulder");

            _move.Enable(); _sprint.Enable(); _jump.Enable(); _crouch.Enable();
            _flip.Enable(); _mount.Enable(); _dash.Enable(); _tether.Enable();
        }

        void OnDisable()
        {
            foreach (var a in new[] { _move, _sprint, _jump, _crouch, _flip, _mount, _dash, _tether })
            {
                a?.Disable();
                a?.Dispose();
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool grounded = CheckGrounded();
            IsGrounded = grounded;
            if (grounded) _lastGroundedTime = Time.time;

            HandleMount(grounded);
            UpdateRideLift(dt);
            UpdateGesture(dt);
            if (!_skating) HandleCrouch(grounded);

            // A dash overrides everything: fixed velocity, no gravity, no input
            // steering. Bailing out here keeps the arc exactly 45° and exactly
            // as long as advertised, which is what makes it designable.
            // The tether owns movement entirely while latched.
            HandleTetherInput(grounded);
            if (_tethered) { UpdateSwing(dt); return; }

            if (grounded) _airDashUsed = false;
            if (TryStartDash(grounded)) return;
            if (_dashTimeLeft > 0f)
            {
                _dashTimeLeft -= dt;
                _cc.Move(_dashVelocity * dt);
                if (_dashTimeLeft <= 0f) EndDash();
                if (animator != null)
                {
                    animator.SetFloat(SpeedHash, _currentSpeed);
                    animator.SetBool(GroundedHash, false);
                }
                return;
            }

            // ---- planar movement -------------------------------------------
            Vector2 input = _move.ReadValue<Vector2>();
            if (input.sqrMagnitude > 1f) input.Normalize();
            // An attack plants him: steering and turning stop, but gravity and
            // the lunge carry on below.
            if (MovementLocked) input = Vector2.zero;

            Vector3 forward = Vector3.forward, right = Vector3.right;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            }

            Vector3 desired = forward * input.y + right * input.x;
            float magnitude = Mathf.Clamp01(desired.magnitude);
            if (magnitude > 0.001f) desired /= magnitude;

            float top = _skating ? skateSpeed
                      : _crouching ? crouchSpeed
                      : _sprint.IsPressed() ? sprintSpeed
                      : moveSpeed;
            float target = magnitude * top;

            // Releasing the stick at speed hands the deceleration to the
            // Run To Stop clip, at a rate slow enough for the plant to read.
            if (target < 0.01f && !_crouching && !_skating &&
                _currentSpeed > stopTriggerSpeed && !_stopping)
            {
                _stopping = true;
                if (animator != null) animator.SetTrigger(StopHash);
            }
            else if (_stopping && (target > 0.01f || _currentSpeed < 0.05f))
            {
                _stopping = false;
            }

            float accel = _skating ? skateAcceleration
                        : _stopping ? stopDeceleration
                        : acceleration;
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, target, accel * dt);

            if (magnitude > 0.001f)
            {
                float wanted = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
                float smooth = _skating ? skateTurnSmoothTime : turnSmoothTime;
                float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, wanted,
                                                    ref _turnVelocity, smooth);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }

            // ---- jump / gravity --------------------------------------------
            // Front flip on the ground is a trick; in the air it is the double
            // jump's animation and is triggered from the jump path below.
            if (_flip.WasPressedThisFrame() && grounded && !_crouching && !_skating
                && animator != null)
                animator.SetTrigger(FlipHash);

            if (_jump.WasPressedThisFrame()) _jumpPressedTime = Time.time;

            if (grounded) _airJumpsUsed = 0;

            bool canJump = Time.time - _lastGroundedTime <= coyoteTime
                           && !_crouching && !_skating;
            bool wantsJump = Time.time - _jumpPressedTime <= jumpBuffer;

            // Second press while airborne: the feather's double jump. Requires
            // the ability, and only once per airtime.
            bool canAirJump = !canJump && !_skating && _airJumpsUsed < 1
                              && _abilities != null
                              && _abilities.Has(AbilityKind.DoubleJump);

            if (!canJump && canAirJump && _jump.WasPressedThisFrame())
            {
                _airJumpsUsed++;
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * doubleJumpHeight);
                _jumpPressedTime = -99f;
                _jumpLatchUntil = Time.time + 0.15f;
                if (animator != null)
                {
                    // Fit the flip to the hang time exactly: airtime is the rise
                    // plus the fall, i.e. 2v/g. Without this the clip either
                    // finishes early and he falls in a T-pose-ish idle, or is
                    // still spinning when he lands.
                    float airtime = 2f * _verticalVelocity / Mathf.Abs(gravity);
                    float scale = flipAirborneSeconds / Mathf.Max(airtime, 0.05f);
                    animator.SetFloat(FlipSpeedHash, Mathf.Clamp(scale, 0.5f, 2.5f));
                    animator.SetTrigger(FlipHash);
                }
                if (doubleJumpEffect != null)
                {
                    doubleJumpEffect.transform.position = transform.position;
                    doubleJumpEffect.Play();
                }
            }
            else if (canJump && wantsJump)
            {
                // v = sqrt(2gh) puts the apex exactly at the chosen height.
                float h = Mathf.Lerp(jumpHeight, runJumpHeight,
                                     Mathf.Clamp01(_currentSpeed / Mathf.Max(sprintSpeed, 0.01f)));
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * h);
                _stopping = false;
                _jumpPressedTime = -99f;
                _lastGroundedTime = -99f;
                grounded = false;
                // CharacterController.isGrounded still reports true for a frame
                // or two after launch. Without this latch the Jump -> Land
                // transition fires immediately and clips the jump.
                _jumpLatchUntil = Time.time + 0.15f;
                if (animator != null) animator.SetTrigger(JumpHash);
            }
            else if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStick;
            }
            else
            {
                _verticalVelocity += gravity * dt;
            }

            // Input movement plus whatever momentum is still being carried.
            Vector3 motion = transform.forward * _currentSpeed + _momentum;
            motion.y = _verticalVelocity;
            _cc.Move(motion * dt);

            float drag = grounded ? momentumGroundDrag : momentumAirDrag;
            _momentum = Vector3.MoveTowards(_momentum, Vector3.zero, drag * dt);

            // ---- animator ---------------------------------------------------
            if (animator == null) return;
            animator.SetFloat(SpeedHash, _currentSpeed, 0.08f, dt);
            animator.SetFloat(MultiplierHash, strideScale);
            animator.SetBool(GroundedHash, grounded && Time.time >= _jumpLatchUntil);
            animator.SetBool(CrouchHash, _crouching);
            animator.SetBool(SkatingHash, _skating);
            animator.SetFloat(VerticalHash, _verticalVelocity);
        }

        /// <summary>Fades the masked gesture layer in and back out.</summary>
        void UpdateGesture(float dt)
        {
            if (animator == null || _gestureLayer < 0) return;
            if (_gestureTimeLeft > 0f) _gestureTimeLeft -= dt;
            float wanted = _gestureTimeLeft > 0f ? 1f : 0f;
            _gestureWeight = Mathf.MoveTowards(_gestureWeight, wanted, dt * 6f);
            animator.SetLayerWeight(_gestureLayer, _gestureWeight);
        }

        // ------------------------------------------------------------ tether

        void HandleTetherInput(bool grounded)
        {
            if (_abilities == null || !_abilities.Has(AbilityKind.Tether)) return;

            if (_tether.WasPressedThisFrame())
            {
                if (_tethered) ReleaseTether(false);
                else if (tether != null && tether.Best != null) Latch(tether.Best);
            }
            // Jumping off the rope keeps the swing's momentum and adds a kick —
            // that trade (let go at the right moment) is the whole skill of it.
            else if (_tethered && _jump.WasPressedThisFrame())
            {
                ReleaseTether(true);
            }
        }

        void Latch(TetherAnchor anchor)
        {
            _tetherAnchor = anchor;
            _tethered = true;
            _ropeLength = Mathf.Clamp(
                Vector3.Distance(transform.position, anchor.Position),
                tetherMinLength, tetherMaxLength);

            // Enter gently. Only the TANGENTIAL part of your current motion is
            // carried in — velocity straight along the rope means nothing to a
            // pendulum, and feeding it in is what made latching launch you into
            // an orbit. Capped as well, so the swing is something you build up
            // rather than arrive at.
            Vector3 entry = transform.forward * _currentSpeed + _momentum;
            entry.y = _verticalVelocity;

            Vector3 radial = (transform.position - anchor.Position).normalized;
            entry = Vector3.ProjectOnPlane(entry, radial);
            _swingVelocity = Vector3.ClampMagnitude(entry, entrySpeedCap);

            _currentSpeed = 0f;
            _momentum = Vector3.zero;
            _stopping = false;
            if (tether != null) tether.Attach(anchor);
            if (animator != null)
            {
                animator.SetBool(TetheredHash, true);
                animator.SetTrigger(FireHash);
            }
            _gestureTimeLeft = gestureSeconds;
        }

        void ReleaseTether(bool withJump)
        {
            _tethered = false;
            _tetherAnchor = null;
            if (tether != null) tether.Detach();
            if (animator != null) animator.SetBool(TetheredHash, false);

            // Hand the swing's velocity back: horizontal becomes momentum,
            // vertical becomes the jump arc. Releasing at the bottom of a swing
            // should fling you, and this is what makes that true.
            _verticalVelocity = _swingVelocity.y + (withJump ? tetherReleaseJump : 0f);
            _momentum = new Vector3(_swingVelocity.x, 0f, _swingVelocity.z);
            _currentSpeed = 0f;
            _swingVelocity = Vector3.zero;

            // Face the way you are travelling, and level out — the rig was
            // rotated to hang along the rope.
            var flat = new Vector3(_momentum.x, 0f, _momentum.z);
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            else
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        /// <summary>
        /// Pendulum step: integrate freely, then project back onto the sphere of
        /// radius ropeLength and strip the radial velocity. Only constrains when
        /// the rope is taut (dist > length), so it behaves as a rope rather than
        /// a rigid rod — you can fall inward freely.
        /// </summary>
        void UpdateSwing(float dt)
        {
            if (_tetherAnchor == null) { ReleaseTether(false); return; }

            Vector3 anchor = _tetherAnchor.Position;
            Vector3 pos = transform.position;
            Vector2 input = _move.ReadValue<Vector2>();

            // Rope length is fixed at whatever it was when you latched. Reeling
            // it in shrank the radius while tangential speed stayed put, which
            // sent angular velocity through the roof — and since W is the
            // natural "forward" key, it happened without meaning to.
            _swingVelocity += Vector3.up * swingGravity * dt;

            // Movement input pushes along the tangent plane, so you accelerate
            // in whichever direction you lean. Camera-relative, matching every
            // other movement in the game.
            Vector3 radialDir = (pos - anchor).normalized;
            if (input.sqrMagnitude > 0.0001f)
            {
                Vector3 camF = Vector3.forward, camR = Vector3.right;
                if (cameraTransform != null)
                {
                    camF = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                    camR = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
                }
                Vector3 push = camF * input.y + camR * input.x;
                push = Vector3.ProjectOnPlane(push, radialDir);   // tangent only
                if (push.sqrMagnitude > 0.0001f)
                    _swingVelocity += push.normalized * (swingPumpAccel * dt);
            }

            _swingVelocity *= 1f - Mathf.Clamp01(swingDamping * dt);
            _swingVelocity = Vector3.ClampMagnitude(_swingVelocity, maxSwingSpeed);

            Vector3 next = pos + _swingVelocity * dt;
            Vector3 offset = next - anchor;
            float dist = offset.magnitude;
            if (dist > _ropeLength && dist > 0.0001f)
            {
                Vector3 radial = offset / dist;
                next = anchor + radial * _ropeLength;
                float outward = Vector3.Dot(_swingVelocity, radial);
                if (outward > 0f) _swingVelocity -= radial * outward;
            }

            float intended = _swingVelocity.magnitude;
            CollisionFlags flags = _cc.Move(next - pos);

            // Re-derive from what actually happened, so hitting a wall kills the
            // swing instead of the solver insisting on velocity it never got.
            // Re-clamped: a collision resolve can spike this for one frame, and
            // an unclamped spike compounds into a runaway orbit.
            if (dt > 0f)
                _swingVelocity = Vector3.ClampMagnitude((transform.position - pos) / dt,
                                                        maxSwingSpeed);

            // Re-deriving alone is not enough: sliding along a wall keeps almost
            // all of the tangential speed, so a scrape reads as no impact at all.
            // Scale the penalty by how much travel was actually lost, so a graze
            // costs little and a solid hit costs most of the swing.
            if (flags != CollisionFlags.None && intended > 0.5f)
            {
                float lost = Mathf.Clamp01(1f - _swingVelocity.magnitude / intended);
                if (lost > impactMinLoss)
                {
                    float keep = Mathf.Lerp(1f, impactSpeedRetain, lost);
                    _swingVelocity *= keep;
                }
            }

            // Hang along the rope, facing the way you are going.
            Vector3 up = (anchor - transform.position).normalized;
            Vector3 travel = Vector3.ProjectOnPlane(_swingVelocity, up);
            if (travel.sqrMagnitude > 0.05f)
                transform.rotation = Quaternion.LookRotation(travel.normalized, up);

            if (animator != null)
            {
                animator.SetFloat(SpeedHash, _swingVelocity.magnitude);
                animator.SetBool(GroundedHash, false);
            }
        }

        /// <summary>
        /// Launch a dash if one is available. The vector is built as
        /// (horizontal facing, 1) normalised — with the horizontal part unit
        /// length, that is exactly 45° every time, independent of where the
        /// camera happens to be pitched.
        /// </summary>
        bool TryStartDash(bool grounded)
        {
            if (_dashTimeLeft > 0f) return false;
            if (!_dash.WasPressedThisFrame()) return false;
            if (_skating || _crouching) return false;
            if (Time.time < _dashReadyAt) return false;
            if (_abilities == null || !_abilities.Has(AbilityKind.Dash)) return false;
            if (!grounded && _airDashUsed) return false;   // one per airtime

            if (!grounded) _airDashUsed = true;

            Vector3 flat = transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            Vector3 dir = (flat + Vector3.up).normalized;   // exactly 45°
            _dashVelocity = dir * dashSpeed;
            _dashTimeLeft = dashDuration;
            _dashReadyAt = Time.time + dashDuration + dashCooldown;
            _verticalVelocity = 0f;
            _stopping = false;

            if (animator != null)
            {
                animator.SetTrigger(DashHash);
                animator.SetBool(GroundedHash, false);
            }
            if (dashEffects != null) dashEffects.Play(dir, dashDuration);
            return true;
        }

        void EndDash()
        {
            _dashTimeLeft = 0f;
            // Hand momentum back so the dash flows into the fall rather than
            // stopping dead in the air.
            _verticalVelocity = _dashVelocity.y * dashExitVerticalKeep;
            var flat = new Vector3(_dashVelocity.x, 0f, _dashVelocity.z);
            _currentSpeed = Mathf.Min(flat.magnitude * dashExitHorizontalKeep, sprintSpeed);
            _dashVelocity = Vector3.zero;
        }

        /// <summary>R mounts/dismounts the board once the skateboard pickup has
        /// been collected. Mounting is refused mid-air so he cannot conjure a
        /// board under himself during a jump.</summary>
        void HandleMount(bool grounded)
        {
            if (_abilities == null || !_abilities.Has(AbilityKind.Skateboard)) return;
            if (!_mount.WasPressedThisFrame()) return;
            if (!_skating && !grounded) return;

            _skating = !_skating;
            if (_skating)
            {
                // Standing up out of a crouch first, so the capsule is correct.
                if (_crouching && CanStand())
                {
                    _crouching = false;
                    _cc.height = _standHeight;
                    _cc.center = new Vector3(_standCenter.x, _standHeight * 0.5f, _standCenter.z);
                }
                _stopping = false;
            }
            if (_board != null) _board.SetRidden(_skating);
        }

        void HandleCrouch(bool grounded)
        {
            bool want = _crouch.IsPressed() && grounded;
            if (want == _crouching) return;

            // Refuse to stand up under a ceiling, or the capsule pops through it.
            if (!want && !CanStand()) return;

            _crouching = want;
            float h = want ? _standHeight * crouchHeightFraction : _standHeight;
            _cc.height = h;
            _cc.center = new Vector3(_standCenter.x, h * 0.5f, _standCenter.z);
        }

        /// <summary>
        /// Ground test by spherecast rather than CharacterController.isGrounded.
        ///
        /// isGrounded is only refreshed by Move() and is well known to report
        /// true spuriously while still airborne — which surfaces as the landing
        /// animation playing in midair, because the Fall -> Land transition
        /// keys off it. A spherecast against real geometry is stable and also
        /// lets us reject steep faces that the character should slide down.
        /// </summary>
        bool CheckGrounded()
        {
            float radius = Mathf.Max(_cc.radius * 0.9f, 0.05f);
            Vector3 origin = transform.position + Vector3.up * (_cc.radius + 0.05f);
            float distance = _cc.radius + 0.05f + GroundProbe;

            var hits = Physics.SphereCastAll(origin, radius, Vector3.down, distance,
                                             ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.transform == transform || h.transform.IsChildOf(transform)) continue;
                if (h.distance <= 0f) continue;   // started inside a collider
                if (Vector3.Angle(h.normal, Vector3.up) > _cc.slopeLimit) continue;
                return true;
            }
            return false;
        }

        const float GroundProbe = 0.12f;

        bool CanStand()
        {
            Vector3 bottom = transform.position + Vector3.up * (_standRadius + 0.02f);
            Vector3 top = transform.position + Vector3.up * (_standHeight - _standRadius);
            // OverlapCapsule rather than CheckCapsule: the character's own
            // controller sits inside this volume and would otherwise report a
            // hit every frame, trapping him in a crouch forever.
            var hits = Physics.OverlapCapsule(bottom, top, _standRadius * 0.95f,
                                              ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (h.transform != transform && !h.transform.IsChildOf(transform))
                    return false;
            return true;
        }
    }
}
