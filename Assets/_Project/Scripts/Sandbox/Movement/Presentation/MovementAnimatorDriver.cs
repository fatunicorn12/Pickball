using UnityEngine;

namespace Pickleball.Sandbox.Movement.Presentation
{
    /// <summary>
    /// Feeds the chibi's Animator from movement, and owns where the model sits on
    /// the capsule. The movement motor never touches the Animator; this listens.
    ///
    /// Two channels:
    ///  • <b>Events.</b> <see cref="IMovementState.MoveChanged"/> fires the one-shot
    ///    triggers the legacy graph uses (Jump, Stop, Flip, Dash), the gesture-layer
    ///    hook throw, and time-scales flips to the jump's real airtime.
    ///  • <b>Per frame</b>, after the motor (execution order +100): the continuous
    ///    parameters (Speed, SpeedMultiplier, Grounded, VerticalSpeed, …) and the
    ///    int <c>Move</c> / <c>JumpKind</c> parameters, which the newer states
    ///    (skid, dive, belly slide, spin, the chain's double/triple, side flip,
    ///    rollout) transition on — declarative, so nothing can latch.
    ///
    /// Trigger hygiene and the speed rest-snap came over from ChibiLocomotion
    /// with their reasons. Parameters the controller does not have are skipped, so
    /// an older controller degrades instead of spamming warnings.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class MovementAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private MovementMotor motor;
        [SerializeField] private MovementAnimationConfig config;
        [SerializeField] private Animator animator;
        [Tooltip("The model under the player root. Offsetting and tilting it moves the " +
                 "visual without moving the capsule.")]
        [SerializeField] private Transform modelRoot;

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
        static readonly int MovingHash = Animator.StringToHash("Moving");
        static readonly int MoveHash = Animator.StringToHash("Move");
        static readonly int JumpKindHash = Animator.StringToHash("JumpKind");
        static readonly int JumpVariantHash = Animator.StringToHash("JumpVariant");
        /// <summary>Gesture-layer state the hook throw lives in, crossfaded by name.</summary>
        const string FireState = "Fire";

        // Buffered one-shot triggers, in a fixed order; see TriggerLifetime.
        static readonly int[] Triggers = { JumpHash, StopHash, FlipHash, DashHash, JumpVariantHash };
        float[] _triggerExpiry = new float[5];

        ChibiFootIK _footIK;
        MovementAnimationConfig _fallbackConfig;
        int _gestureLayer = -1;
        float _gestureLeft, _gestureWeight;
        float _airborneUntil;
        float _rideLift;
        float _pose, _slide, _spinYaw;
        bool _paramsScanned, _hasMoveParam, _hasJumpKindParam, _hasJumpVariantParam, _hasFlipSpeedParam;

        MovementAnimationConfig Config
        {
            get
            {
                if (config != null) return config;
                if (_fallbackConfig == null)
                {
                    _fallbackConfig = ScriptableObject.CreateInstance<MovementAnimationConfig>();
                    _fallbackConfig.hideFlags = HideFlags.HideAndDontSave;
                }
                return _fallbackConfig;
            }
        }

        void Awake()
        {
            if (motor == null) motor = GetComponentInParent<MovementMotor>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (modelRoot == null && animator != null) modelRoot = animator.transform;
            if (animator != null)
            {
                // Holds a planted foot still while the body moves over it — matters
                // more than usual on chibi proportions, where retargeting error is big.
                animator.stabilizeFeet = true;
                // The orbit camera regularly sits close enough that the renderer
                // bounds leave the frustum for a frame; culling would freeze the pose.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _footIK = animator.GetComponent<ChibiFootIK>();
            }
            ApplyModelTransform();
        }

        void OnEnable()
        {
            _triggerExpiry ??= new float[Triggers.Length];
            if (motor == null) return;
            motor.MoveChanged += OnMoveChanged;
            motor.TrickPerformed += OnTrick;
        }

        void OnDisable()
        {
            if (motor == null) return;
            motor.MoveChanged -= OnMoveChanged;
            motor.TrickPerformed -= OnTrick;
        }

        void OnValidate()
        {
            // Live in the inspector, so the offset can be dialled in looking at the feet.
            ApplyModelTransform();
        }

        // ------------------------------------------------------------- events

        void OnMoveChanged(MoveKind from, MoveKind to)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            ScanParameters();   // a move can change before this component's first Update
            var c = Config;
            switch (to)
            {
                case MoveKind.Jump:
                    // A pending ground flip or run-stop is superseded by the jump.
                    ClearTrigger(FlipHash);
                    ClearTrigger(StopHash);
                    // The single jump is the legacy Jump state; every other kind has
                    // its own state, entered on JumpVariant + JumpKind. An older
                    // controller without them plays them all as the single.
                    var jump = motor.CurrentJump;
                    if (jump == JumpKind.Single || !_hasJumpVariantParam) SetTrigger(JumpHash);
                    else SetTrigger(JumpVariantHash);
                    if (jump == JumpKind.Triple || jump == JumpKind.SideFlip || jump == JumpKind.Rollout)
                        FitFlipToAirtime();
                    _airborneUntil = Time.time + c.AirborneLatch;
                    break;
                case MoveKind.AirJump:
                    FitFlipToAirtime();
                    SetTrigger(FlipHash);
                    _airborneUntil = Time.time + c.AirborneLatch;
                    break;
                case MoveKind.SpinJump:
                case MoveKind.Dive:
                    if (motor.VerticalSpeed > 0f) _airborneUntil = Time.time + c.AirborneLatch;
                    break;
                case MoveKind.RunStop:
                    SetTrigger(StopHash);
                    break;
                case MoveKind.Dash:
                    SetTrigger(DashHash);
                    _airborneUntil = Time.time + c.AirborneLatch;
                    break;
                case MoveKind.Tether:
                    // Crossfaded in and never transitioned out: the gesture layer is
                    // faded to zero weight while Fire holds its last frame. Exiting to
                    // the empty None state would blend the arm toward the bind pose at
                    // full weight first — a visible T-pose flick on every throw.
                    int layer = GestureLayer();
                    if (layer >= 0) animator.CrossFadeInFixedTime(FireState, 0.05f, layer, 0f);
                    _gestureLeft = c.GestureSeconds;
                    break;
            }
        }

        /// <summary>The ground front-flip trick (cosmetic).</summary>
        void OnTrick() => SetTrigger(FlipHash);

        /// <summary>Fits the trimmed front flip to the airtime the jump will actually
        /// have, so it never finishes early (a T-pose-ish fall) or is still
        /// spinning on landing.</summary>
        void FitFlipToAirtime()
        {
            if (!_hasFlipSpeedParam) return;
            var c = Config;
            float scale = c.FlipAirborneSeconds / Mathf.Max(motor.JumpAirTime, 0.05f);
            animator.SetFloat(FlipSpeedHash, Mathf.Clamp(scale, c.FlipSpeedRange.x, c.FlipSpeedRange.y));
        }

        // ---------------------------------------------------------- per frame

        void Update()
        {
            if (motor == null) return;
            float dt = Time.deltaTime;
            var c = Config;
            UpdateRideLift(dt);
            UpdatePose(dt);
            if (animator == null || animator.runtimeAnimatorController == null) return;
            ScanParameters();
            UpdateGesture(dt);

            MoveKind kind = motor.Current;
            float speed = motor.PlanarSpeed;
            animator.SetFloat(SpeedHash, speed, c.SpeedDampTime, dt);
            // Read back the DAMPED value: the multiplier tracks what the blend tree
            // is showing, not where it is headed.
            float shown = animator.GetFloat(SpeedHash);
            if (speed <= 0f && shown > 0f && shown < c.SpeedRestSnap)
            {
                animator.SetFloat(SpeedHash, 0f);
                shown = 0f;
            }
            float topStride = kind == MoveKind.Crouch ? c.CrouchClipStride : c.TopClipStride;
            float overspeed = topStride > 0.01f && kind != MoveKind.Skate ? Mathf.Max(1f, shown / topStride) : 1f;
            animator.SetFloat(MultiplierHash, c.StrideScale * overspeed);

            animator.SetBool(GroundedHash, motor.IsGrounded && Time.time >= _airborneUntil);
            animator.SetBool(CrouchHash, kind == MoveKind.Crouch);
            animator.SetBool(SkatingHash, kind == MoveKind.Skate);
            animator.SetBool(TetheredHash, kind == MoveKind.Tether);
            animator.SetFloat(VerticalHash, motor.VerticalSpeed);
            // Intent, not speed: Run To Stop decelerates through every speed
            // threshold on its own, so only fresh input may cut it short.
            animator.SetBool(MovingHash, motor.HasMoveIntent);
            if (_hasMoveParam) animator.SetInteger(MoveHash, (int)kind);
            if (_hasJumpKindParam) animator.SetInteger(JumpKindHash, (int)motor.CurrentJump);

            if (_footIK != null) _footIK.Suppressed = kind == MoveKind.BellySlide;
            ExpireTriggers();
        }

        void ScanParameters()
        {
            if (_paramsScanned) return;
            _paramsScanned = true;
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == MoveHash) _hasMoveParam = true;
                else if (p.nameHash == JumpKindHash) _hasJumpKindParam = true;
                else if (p.nameHash == JumpVariantHash) _hasJumpVariantParam = true;
                else if (p.nameHash == FlipSpeedHash) _hasFlipSpeedParam = true;
            }
        }

        // --------------------------------------------------------- triggers

        void SetTrigger(int hash)
        {
            if (animator == null) return;
            animator.SetTrigger(hash);
            int i = System.Array.IndexOf(Triggers, hash);
            if (i >= 0) _triggerExpiry[i] = Time.time + Config.TriggerLifetime;
        }

        void ClearTrigger(int hash)
        {
            if (animator == null) return;
            animator.ResetTrigger(hash);
            int i = System.Array.IndexOf(Triggers, hash);
            if (i >= 0) _triggerExpiry[i] = 0f;
        }

        /// <summary>Withdraws triggers the Animator never consumed, so one set while
        /// the current state had no transition for it cannot fire seconds later.</summary>
        void ExpireTriggers()
        {
            for (int i = 0; i < Triggers.Length; i++)
            {
                if (_triggerExpiry[i] <= 0f || Time.time < _triggerExpiry[i]) continue;
                animator.ResetTrigger(Triggers[i]);
                _triggerExpiry[i] = 0f;
            }
        }

        // ---------------------------------------------------------- gesture

        /// <summary>Index of the masked gesture layer, resolved on first use — it
        /// cannot be read in Awake (the Animator has not built its graph yet and
        /// GetLayerIndex returns -1, which silently disabled the layer for good).</summary>
        int GestureLayer()
        {
            if (_gestureLayer >= 0) return _gestureLayer;
            if (animator == null || animator.runtimeAnimatorController == null) return -1;
            _gestureLayer = animator.GetLayerIndex("Gesture");
            return _gestureLayer;
        }

        /// <summary>Fades the gesture layer in and back out; held at zero weight the
        /// rest of the time so its empty state cannot overwrite the arms.</summary>
        void UpdateGesture(float dt)
        {
            int layer = GestureLayer();
            if (layer < 0) return;
            if (_gestureLeft > 0f) _gestureLeft -= dt;
            float wanted = _gestureLeft > 0f ? 1f : 0f;
            if (Mathf.Approximately(_gestureWeight, wanted)) return;
            _gestureWeight = Mathf.MoveTowards(_gestureWeight, wanted, dt * Config.GestureFadeRate);
            animator.SetLayerWeight(layer, _gestureWeight);
        }

        // ---------------------------------------------------- model transform

        /// <summary>Eases the model up onto the deck and back down, so mounting is
        /// not an instant pop.</summary>
        void UpdateRideLift(float dt)
        {
            var c = Config;
            float wanted = motor.Current == MoveKind.Skate ? c.SkateRideHeight : 0f;
            if (Mathf.Approximately(_rideLift, wanted)) return;
            _rideLift = Mathf.MoveTowards(_rideLift, wanted, c.RideLiftRate * dt);
        }

        /// <summary>Procedural poses: tilt into the dive and onto the belly (0 now
        /// that both have flat clips), lower the model for the slide, corkscrew
        /// through the spin jump. Exponentially smoothed, so they blend at any
        /// frame rate.</summary>
        void UpdatePose(float dt)
        {
            var c = Config;
            MoveKind kind = motor.Current;
            bool on = c.ProceduralPoses;
            float pitch = !on ? 0f
                        : kind == MoveKind.Dive ? c.DivePitch
                        : kind == MoveKind.BellySlide ? c.BellySlidePitch
                        : 0f;
            float k = 1f - Mathf.Exp(-c.PoseBlendRate * dt);
            _pose = Mathf.Lerp(_pose, pitch, k);
            _slide = Mathf.Lerp(_slide, on && kind == MoveKind.BellySlide ? 1f : 0f, k);

            if (on && kind == MoveKind.SpinJump)
                _spinYaw = Mathf.Repeat(_spinYaw + c.SpinYawSpeed * dt, 360f);
            else if (_spinYaw > 0f)
            {
                // Unwind the rest of the turn rather than snapping back.
                float step = Mathf.Abs(c.SpinYawSpeed) * dt;
                _spinYaw = 360f - _spinYaw <= step ? 0f : _spinYaw + step;
            }
            ApplyModelTransform();
        }

        void ApplyModelTransform()
        {
            if (modelRoot == null) return;
            // In edit mode only with a real asset: OnValidate must not mint one.
            if (config == null && !Application.isPlaying) return;
            var c = Config;
            Vector3 offset = Vector3.up * (c.ModelYOffset + _rideLift) + c.BellySlideOffset * _slide;
            modelRoot.localPosition = offset;
            modelRoot.localRotation = Quaternion.Euler(_pose, _spinYaw, 0f);
        }
    }
}
