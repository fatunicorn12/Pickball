using System;
using Pickleball.Sandbox.Movement.Logic;
using Pickleball.Sandbox.Movement.States;
using UnityEngine;

namespace Pickleball.Sandbox.Movement
{
    /// <summary>
    /// The player's movement simulation: owns the CharacterController, runs the
    /// move state machine, and integrates the body (speed along a heading,
    /// vertical velocity, carried momentum, force fields).
    ///
    /// <b>One job.</b> It reads a <see cref="MovementIntent"/> from
    /// <see cref="PlayerMovementInput"/>, decides and integrates, and raises
    /// <see cref="MoveChanged"/>. It never touches the Animator, particles, the
    /// rope or the board — the presentation components under
    /// <c>Movement/Presentation</c> listen and do that. It never knows combat or
    /// lock-on exist; it is handed an <see cref="IFacingSource"/> instead.
    ///
    /// <b>Moves.</b> Each <see cref="MoveKind"/> is one class under
    /// <c>Movement/States</c>. The shared body data and helpers below are
    /// <c>internal</c> so only those states drive them; everything public is a
    /// read-only view (<see cref="IMovementState"/>) or the push API that
    /// ChibiLocomotion has always offered (impulse, force, lock).
    ///
    /// Moved out of the 1,300-line ChibiLocomotion, which is now a facade over
    /// this. The behaviour of the moves that already existed is preserved —
    /// their comments came with them, so the bugs they record stay fixed.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class MovementMotor : MonoBehaviour, IMovementState
    {
        [Tooltip("Every gameplay tunable. Tuned live: read each frame.")]
        [SerializeField] private MovementConfig config;
        [Tooltip("Where intent comes from. Found on this object if empty.")]
        [SerializeField] private PlayerMovementInput input;
        [Tooltip("Gates the ability moves (feather, dash, tether, board, anchor). Found " +
                 "on this object if empty.")]
        [SerializeField] private PlayerAbilities abilities;
        [Tooltip("Picks the tether anchor to latch to. Optional.")]
        [SerializeField] private TetherSystem tether;

        // ---------------------------------------------------------- machine
        MoveState[] _states;
        MoveState _current;
        MoveKind _currentKind = MoveKind.Ground;
        MoveKind _previousKind = MoveKind.Ground;
        float _timeInMove;
        MovementConfig _fallbackConfig;

        // ------------------------------------------------------------- body
        CharacterController _cc;
        MovementIntent _intent;
        float _turnVelocity;
        bool _facingHeld;
        Vector3 _travel = Vector3.forward;
        Vector3 _externalAccel, _frameFieldAccel;
        float _moveLockLeft;
        float _anchorLeft, _anchorReadyAt;
        float _jumpPressedAt = -99f;
        float _standHeight, _standRadius;
        Vector3 _standCenter;
        float _capsuleFraction = 1f, _wantedCapsule = 1f;
        ForceFieldChannel _field;
        SprintRamp _sprint;
        JumpChain _chain;
        SpinGesture _spin;

        // ------------------------------------------------------------ events
        /// <inheritdoc/>
        public event Action<MoveKind, MoveKind> MoveChanged;

        /// <summary>The ground front-flip trick (F / Y) was performed. Cosmetic: it
        /// changes no movement, so it is an event rather than a move.</summary>
        public event Action TrickPerformed;

        // ------------------------------------------------- IMovementState
        public MoveKind Current => _currentKind;
        public MoveKind Previous => _previousKind;
        public JumpKind CurrentJump { get; internal set; }
        public float TimeInMove => _timeInMove;
        public bool IsGrounded { get; private set; }
        public Vector3 Velocity => _cc != null ? _cc.velocity : Vector3.zero;
        public float PlanarSpeed => Speed;
        public float VerticalSpeed => VerticalVelocity;
        public Vector3 TravelDirection => _travel;
        public bool HasMoveIntent => _intent.MoveMagnitude > 0.003f && !IsMovementLocked;
        public float SprintBlend => _sprint != null
            ? _sprint.Blend(Config.SprintDelay, Config.SprintRampTime) : 0f;

        // ------------------------------------------- push API (stable contract)
        /// <summary>The tunables in use (a default instance if none is assigned).</summary>
        public MovementConfig Config
        {
            get
            {
                if (config != null) return config;
                // Not readonly and not serialized: rebuilt after a script reload.
                if (_fallbackConfig == null)
                {
                    _fallbackConfig = ScriptableObject.CreateInstance<MovementConfig>();
                    _fallbackConfig.hideFlags = HideFlags.HideAndDontSave;
                    Debug.LogWarning($"{name}: MovementMotor has no MovementConfig — using " +
                                     "defaults. Run Tools ▸ Pickleball ▸ Setup Movement.", this);
                }
                return _fallbackConfig;
            }
        }

        /// <summary>True while a move owns the CharacterController outright (dash,
        /// tether, skateboard). Attacks refuse to start rather than fight it.</summary>
        public bool IsBusy => _currentKind.IsExclusive();

        /// <summary>Lock-on / attack facing. Assigned by the player's glue component.</summary>
        public IFacingSource FacingSource { get; set; }

        /// <summary>Push for a launcher, a knockback, an explosion. An IMPULSE: call
        /// once per event with the velocity to add, never every frame — momentum
        /// only sheds its drag per second, so a per-frame call is a net
        /// acceleration and runs away (the old attack lunge slid the character
        /// tens of metres per combo exactly this way).</summary>
        public void AddMomentum(Vector3 delta) => Momentum += delta;

        /// <summary>Continuous force (a gravity well, wind). Call EVERY frame it acts,
        /// with an ACCELERATION in m/s². Summed within a frame, consumed exactly
        /// once per tick, never compounded across frames. The tether ignores it;
        /// the dash is bent by it (see <see cref="MovementConfig.DashFieldBend"/>).</summary>
        public void AddAcceleration(Vector3 metresPerSecondSquared)
            => _externalAccel += metresPerSecondSquared;

        /// <summary>The share of <see cref="Velocity"/> force fields actually
        /// delivered last tick. <c>Velocity − AppliedFieldVelocity</c> is exactly
        /// his own motion, on every path — the Fluxion Beam prices damage off it.</summary>
        public Vector3 AppliedFieldVelocity => _field != null ? _field.Applied : Vector3.zero;

        /// <summary>Takes away his drive for a moment (a well core has him). Steering,
        /// gravity and field drift carry on. Refreshed, not accumulated.</summary>
        public void LockMovement(float seconds) => _moveLockLeft = Mathf.Max(_moveLockLeft, seconds);

        public bool IsMovementLocked => _moveLockLeft > 0f;

        /// <summary>True while the Gravity Anchor holds his own gravity off.</summary>
        public bool IsAnchored => _anchorLeft > 0f;

        // ----------------------------------------- presentation-facing data
        /// <summary>Airtime (s) of the jump just launched, for time-scaling flips.</summary>
        public float JumpAirTime { get; internal set; }
        /// <summary>Unit direction of the dash in progress.</summary>
        public Vector3 DashDirection { get; internal set; }
        /// <summary>The anchor being swung from, while tethered.</summary>
        public TetherAnchor TetherAnchor { get; internal set; }
        /// <summary>Capsule height as a fraction of standing (crouch, belly slide).</summary>
        public float CapsuleFraction => _capsuleFraction;

        // =================================================== state-facing API
        // Internal: only the states in Movement/States drive these.

        internal CharacterController Controller => _cc;
        internal ref MovementIntent Intent => ref _intent;
        internal float Speed { get; set; }
        internal float VerticalVelocity { get; set; }
        internal Vector3 Momentum { get; set; }
        internal float LastGroundedTime { get; set; } = -99f;
        internal int AirJumpsUsed { get; set; }
        internal bool AirDashUsed { get; set; }
        internal float DashReadyAt { get; set; }
        internal float AirTopSpeed { get; set; }
        /// <summary>The jump the next <see cref="JumpMove"/> launches as.</summary>
        internal JumpKind PendingJump { get; set; } = JumpKind.Single;
        /// <summary>The next <see cref="SpinJumpMove"/> launches from the ground
        /// (true) or only converts an airborne arc into the slow spin (false).</summary>
        internal bool PendingSpinLift { get; set; }
        /// <summary>Seconds the stick has been neutral (0 while it is pushed).</summary>
        internal float NeutralTime { get; private set; }
        internal Vector3 FrameFieldAcceleration => _frameFieldAccel;
        internal SprintRamp Sprint => _sprint;
        internal JumpChain Chain => _chain;
        internal SpinGesture Spin => _spin;
        internal TetherSystem TetherSystem => tether;

        internal Vector3 Travel
        {
            get => _travel;
            set
            {
                value.y = 0f;
                if (value.sqrMagnitude > 1e-6f) _travel = value.normalized;
            }
        }

        internal bool HasAbility(AbilityKind kind) => abilities != null && abilities.Has(kind);

        /// <summary>Jump pressed within the buffer window and not yet used.</summary>
        internal bool JumpBuffered => Time.time - _jumpPressedAt <= Config.JumpBuffer;

        /// <summary>Still inside the coyote window after leaving the ground.</summary>
        internal bool InCoyoteTime => Time.time - LastGroundedTime <= Config.CoyoteTime;

        /// <summary>Spends the buffered jump press, so one press is one jump.</summary>
        internal void ConsumeJump()
        {
            _jumpPressedAt = -99f;
            _intent.JumpPressed = false;
        }

        /// <summary>Leaving the ground this frame: grounded is cleared at once (the
        /// probe would otherwise still see the floor for a frame or two), coyote
        /// time is spent, and the air speed cap is taken from the takeoff speed.</summary>
        internal void MarkAirborne()
        {
            IsGrounded = false;
            LastGroundedTime = -99f;
            SetAirTopSpeed();
        }

        /// <summary>Air steering never accelerates him past his takeoff speed (or
        /// run speed, from a standstill).</summary>
        internal void SetAirTopSpeed() => AirTopSpeed = Mathf.Max(Speed, Config.RunSpeed);

        /// <summary>The feather's air jump is unlocked and not yet used this airtime.</summary>
        internal bool CanFeatherJump => AirJumpsUsed < 1 && HasAbility(AbilityKind.DoubleJump);

        /// <summary>
        /// A jump from the ground (or the coyote window): a spin jump if the stick
        /// has just been rotated, otherwise the next step of the jump chain.
        /// Returns the move to hand over to.
        /// </summary>
        internal MoveKind BeginGroundJump()
        {
            var c = Config;
            if (_spin.HasSpin(c.SpinRequiredDegrees, c.SpinWindow))
            {
                PendingSpinLift = true;
                return MoveKind.SpinJump;
            }
            PendingJump = (JumpKind)_chain.NextStep(Time.time, Speed, c.ChainWindow, c.TripleMinSpeed);
            return MoveKind.Jump;
        }

        /// <summary>Spinning the stick mid-air: the slow spin descent, no lift.</summary>
        internal MoveKind BeginAirSpin()
        {
            PendingSpinLift = false;
            return MoveKind.SpinJump;
        }

        /// <summary>The ground move to land in: sprinting if the ramp says so.</summary>
        internal MoveKind GroundKind
            => _sprint.IsSprinting(Config.SprintDelay) ? MoveKind.Sprint : MoveKind.Ground;

        /// <summary>The capsule this move wants (1 = standing). Applied after the
        /// state ticks; growing is refused under a ceiling and retried each frame.</summary>
        internal void RequestCapsule(float fraction) => _wantedCapsule = fraction;

        internal void RaiseTrick() => TrickPerformed?.Invoke();

        // ---------------------------------------------------------- steering

        /// <summary>
        /// Heading steering shared by the ground moves: smooth-damps the body toward
        /// the stick (capped at <paramref name="maxTurnRate"/> °/s) and travels along
        /// the body — except while an <see cref="IFacingSource"/> holds the body on a
        /// target, when travel follows the stick directly and the body turns to
        /// face the target (lock-on strafe, attack square-up).
        /// </summary>
        /// <param name="driving">The move is asking for speed this frame (stick
        /// pushed and not locked) — a passive facing request only applies when not.</param>
        /// <param name="allowFacing">False for moves that ignore lock-on facing (the board).</param>
        internal void SteerHeading(Vector3 desired, float magnitude, bool driving,
                                   float smoothTime, float maxTurnRate, bool allowFacing = true)
        {
            if (allowFacing && TryFacing(desired, magnitude, driving)) return;
            if (!allowFacing && _facingHeld) { _facingHeld = false; Travel = transform.forward; }
            if (magnitude > 0.001f)
            {
                float wanted = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
                float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, wanted,
                                                    ref _turnVelocity, smoothTime, maxTurnRate);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }
            Travel = transform.forward;
        }

        /// <summary>
        /// Air steering: the planar velocity vector is pulled toward the stick at
        /// <see cref="MovementConfig.AirAcceleration"/>, so pulling back slows him
        /// down instead of turning him round mid-jump, and takeoff speed is kept
        /// (capped at <see cref="AirTopSpeed"/>). The body turns to follow travel.
        /// </summary>
        internal void SteerAir(float dt, float accelScale = 1f)
        {
            var c = Config;
            Vector3 v = _travel * Speed;
            bool driving = _intent.HasMove && !IsMovementLocked;
            Vector3 want = driving ? _intent.MoveDirection * (_intent.MoveMagnitude * AirTopSpeed) : Vector3.zero;
            float rate = driving ? c.AirAcceleration * accelScale : c.AirDrag;
            v = Vector3.MoveTowards(v, want, rate * dt);
            Speed = v.magnitude;
            if (Speed > 0.05f) Travel = v;

            if (TryFacing(_intent.MoveDirection, _intent.MoveMagnitude, driving)) return;
            if (Speed > 0.05f) FaceTowards(_travel, c.AirTurnSmoothTime);
        }

        /// <summary>Turns the heading toward the stick at a fixed rate (°/s) — the
        /// committed moves (dive, belly slide) that can only be nudged.</summary>
        internal void SteerAtRate(float degreesPerSecond, float dt)
        {
            if (_intent.HasMove && !IsMovementLocked)
            {
                float yaw = Mathf.Atan2(_travel.x, _travel.z) * Mathf.Rad2Deg;
                float wanted = Mathf.Atan2(_intent.MoveDirection.x, _intent.MoveDirection.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, wanted, degreesPerSecond * dt);
                Travel = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            }
            SetBodyYaw(Mathf.Atan2(_travel.x, _travel.z) * Mathf.Rad2Deg);
        }

        /// <summary>Smoothly turns the body (not travel) to face <paramref name="dir"/>.</summary>
        internal void FaceTowards(Vector3 dir, float smoothTime)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            float wanted = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, wanted, ref _turnVelocity, smoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        /// <summary>Sets the body yaw outright (degrees), and drops turn momentum.</summary>
        internal void SetBodyYaw(float yaw)
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _turnVelocity = 0f;
        }

        /// <summary>Lock-on / attack facing. Returns true when it took the body over
        /// this frame. Ported from ChibiLocomotion: movement is normally along the
        /// body's forward, so while facing is held on the target, travel is
        /// carried separately and follows the stick directly.</summary>
        bool TryFacing(Vector3 desired, float magnitude, bool driving)
        {
            var c = Config;
            bool face = false, urgent = false;
            Vector3 point = default;
            if (FacingSource != null && FacingSource.TryGetFacingTarget(out point, out urgent))
                face = urgent || (!driving && Speed < c.LockFaceSpeed);

            if (!face)
            {
                // Handing back: travel resumes from wherever the body now faces.
                if (_facingHeld) { _facingHeld = false; Travel = transform.forward; }
                return false;
            }

            if (!_facingHeld) { _facingHeld = true; Travel = transform.forward; }
            if (magnitude > 0.001f) Travel = desired;

            Vector3 to = point - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
            {
                float wanted = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float smooth = urgent ? c.LockAttackTurnTime : c.TurnSmoothTime;
                float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, wanted, ref _turnVelocity, smooth);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }
            return true;
        }

        // ---------------------------------------------------------- vertical

        /// <summary>Vertical step on the ground: held down onto the floor, unless
        /// the Gravity Anchor is on with Jump held, which lifts him off it.
        /// Returns the displacement, m.</summary>
        internal float GroundVertical(float dt)
        {
            var c = Config;
            if (IsAnchored && _intent.JumpHeld)
            {
                VerticalVelocity = Mathf.Min(Mathf.Max(VerticalVelocity, 0f) + c.AnchorRiseAcceleration * dt,
                                             c.AnchorMaxRiseSpeed);
                return VerticalVelocity * dt;
            }
            VerticalVelocity = c.GroundedStick;
            return VerticalVelocity * dt;
        }

        /// <summary>Vertical step in the air under <paramref name="profile"/>. The
        /// Gravity Anchor overrides it: his own gravity is off, Jump held inverts
        /// it. Returns the displacement, m.</summary>
        internal float AirVertical(in GravityProfile profile, bool held, float dt)
        {
            if (IsAnchored)
            {
                var c = Config;
                VerticalVelocity = _intent.JumpHeld
                    ? Mathf.Min(VerticalVelocity + c.AnchorRiseAcceleration * dt, c.AnchorMaxRiseSpeed)
                    : Mathf.MoveTowards(VerticalVelocity, 0f, c.AnchorHaltRate * dt);
                return VerticalVelocity * dt;
            }
            float vy = VerticalVelocity;
            float dy = JumpMath.Step(ref vy, held, profile, dt);
            VerticalVelocity = vy;
            return dy;
        }

        // ------------------------------------------------------- integration

        /// <summary>
        /// The standard move: own drive along travel, plus carried momentum, plus
        /// force-field drift, plus <paramref name="verticalDisplacement"/>.
        ///
        /// Field velocity rides on top EXCEPT vertically while grounded, where the
        /// floor gets the whole vertical component: gravity outgrows any damped
        /// field within frames, and adding it there only made a bounce cycle (and
        /// a phantom acceleration in the Fluxion Beam's reading). A well that can
        /// genuinely pluck him off the ground is a design change, not a patch.
        /// </summary>
        internal void MoveStandard(float dt, float verticalDisplacement)
        {
            var c = Config;
            _field.Integrate(_frameFieldAccel, dt, c.FieldDamping, c.MaxFieldSpeed);

            Vector3 own = _travel * Speed + Momentum;
            own.y = 0f;
            Vector3 field = _field.Velocity;
            if (IsGrounded) field.y = 0f;

            float vy = dt > 0f ? verticalDisplacement / dt : 0f;
            Vector3 requested = own + field + Vector3.up * vy;
            CollisionFlags flags = _cc.Move((own + field) * dt + Vector3.up * verticalDisplacement);
            _field.Attribute(requested, field, IsGrounded, _cc.velocity);

            // Head bonk: a ceiling ends the rise instead of pinning him to it for
            // the whole (deliberately long) hang time.
            if ((flags & CollisionFlags.Above) != 0 && VerticalVelocity > 0f) VerticalVelocity = 0f;

            float drag = IsGrounded ? c.MomentumGroundDrag : c.MomentumAirDrag;
            Momentum = Vector3.MoveTowards(Momentum, Vector3.zero, drag * dt);
        }

        /// <summary>Cut loose from every force field (tether, dash). See
        /// <see cref="ForceFieldChannel.Clear"/>.</summary>
        internal void ClearField() => _field.Clear();

        // =========================================================== lifecycle

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (input == null) input = GetComponent<PlayerMovementInput>();
            if (abilities == null) abilities = GetComponent<PlayerAbilities>();
            _standHeight = _cc.height;
            _standCenter = _cc.center;
            _standRadius = _cc.radius;
            _travel = transform.forward;
        }

        void OnEnable() => EnsureRuntime();

        /// <summary>Builds the plain-C# runtime objects. Called from OnEnable, so a
        /// script reload in Play mode (which drops non-serialized references)
        /// rebuilds them instead of null-referencing.</summary>
        void EnsureRuntime()
        {
            _field ??= new ForceFieldChannel();
            _sprint ??= new SprintRamp();
            _chain ??= new JumpChain();
            _spin ??= new SpinGesture();
            if (_states == null)
            {
                _states = new MoveState[]
                {
                    new GroundMove(), new SprintMove(), new RunStopMove(), new CrouchMove(),
                    new SkidTurnMove(), new JumpMove(), new FallMove(), new AirJumpMove(),
                    new DiveMove(), new BellySlideMove(), new SpinJumpMove(), new DashMove(),
                    new TetherMove(), new SkateMove(),
                };
                for (int i = 0; i < _states.Length; i++)
                    Debug.Assert((int)_states[i].Kind == i, $"MoveState order: {_states[i].Kind} at {i}");
            }
            _current = _states[(int)_currentKind];
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            EnsureRuntime();
            var c = Config;

            _intent = input != null ? input.Read() : default;
            if (_intent.JumpPressed) _jumpPressedAt = Time.time;
            NeutralTime = _intent.HasMove ? 0f : NeutralTime + dt;

            IsGrounded = CheckGrounded();
            if (IsGrounded)
            {
                LastGroundedTime = Time.time;
                AirJumpsUsed = 0;
                AirDashUsed = false;
            }

            // Fields: drain the frame's accumulated acceleration exactly once,
            // whichever move owns movement — the dash bends with it, the tether
            // discards it, the rest integrate it. Callers re-add every frame they
            // still act, so a well that stops existing stops at once.
            _frameFieldAccel = _externalAccel;
            _externalAccel = Vector3.zero;

            // Ticked up here so a lock or anchor applied just before a dash or a
            // latch still expires.
            if (_moveLockLeft > 0f) _moveLockLeft -= dt;
            TickAnchor(dt);
            _spin.Tick(_intent.RawStick, c.SpinStickMin, dt);

            for (int hop = 0; hop < 4; hop++)
            {
                MoveKind next = Interrupt() ?? _current.Next(this);
                if (next == _currentKind) break;
                Switch(next);
            }
            _current.Tick(this, dt);
            _timeInMove += dt;
            UpdateCapsule();
        }

        /// <summary>The ability moves that can cut into (almost) any other: mount,
        /// tether latch, dash — in the order ChibiLocomotion checked them.</summary>
        MoveKind? Interrupt()
        {
            var c = Config;
            if (_intent.MountPressed && _current.AllowsMount && IsGrounded && HasAbility(AbilityKind.Skateboard))
            {
                _intent.MountPressed = false;
                return MoveKind.Skate;
            }
            if (_intent.TetherPressed && _current.AllowsTether && HasAbility(AbilityKind.Tether)
                && tether != null && tether.Best != null)
            {
                _intent.TetherPressed = false;
                TetherAnchor = tether.Best;
                return MoveKind.Tether;
            }
            if (_intent.DashPressed && _current.AllowsDash && HasAbility(AbilityKind.Dash)
                && Time.time >= DashReadyAt && (IsGrounded || !AirDashUsed))
            {
                _intent.DashPressed = false;
                return MoveKind.Dash;
            }
            return null;
        }

        void Switch(MoveKind next)
        {
            MoveKind from = _currentKind;
            _current.Exit(this, next);
            if (from == MoveKind.Jump && next != MoveKind.Jump) CurrentJump = JumpKind.None;
            _previousKind = from;
            _currentKind = next;
            _current = _states[(int)next];
            _timeInMove = 0f;
            _current.Enter(this, from);
            MoveChanged?.Invoke(from, next);
        }

        /// <summary>G toggles the Gravity Anchor once Newton's pickup is collected.
        /// Ticked every frame, so it expires even while the dash or tether runs.</summary>
        void TickAnchor(float dt)
        {
            var c = Config;
            if (_anchorLeft > 0f)
            {
                _anchorLeft -= dt;
                if (_anchorLeft <= 0f) EndAnchor();
            }
            if (!_intent.AnchorPressed || !HasAbility(AbilityKind.GravityAnchor)) return;
            _intent.AnchorPressed = false;

            if (_anchorLeft > 0f) { EndAnchor(); return; }
            if (Time.time < _anchorReadyAt || !_current.AllowsAnchor) return;

            _anchorLeft = c.AnchorDuration;
            // Halting starts from where he is: a fall in progress is caught.
            if (VerticalVelocity < 0f) VerticalVelocity *= c.AnchorCatchKeep;
        }

        void EndAnchor()
        {
            _anchorLeft = 0f;
            _anchorReadyAt = Time.time + Config.AnchorCooldown;
        }

        // ------------------------------------------------------------ capsule

        void UpdateCapsule()
        {
            float wanted = Mathf.Clamp(_wantedCapsule, 0.2f, 1f);
            _wantedCapsule = 1f;   // each move re-requests every frame
            if (Mathf.Approximately(wanted, _capsuleFraction)) return;
            // Refuse to grow under a ceiling, or the capsule pops through it.
            if (wanted > _capsuleFraction && !CanStandTo(wanted)) return;
            _capsuleFraction = wanted;
            float h = _standHeight * wanted;
            _cc.height = h;
            _cc.center = new Vector3(_standCenter.x, h * 0.5f, _standCenter.z);
        }

        /// <summary>True if the capsule could be <paramref name="fraction"/> of standing
        /// height here without overlapping anything.</summary>
        internal bool CanStandTo(float fraction = 1f)
        {
            float h = _standHeight * fraction;
            Vector3 bottom = transform.position + Vector3.up * (_standRadius + 0.02f);
            Vector3 top = transform.position + Vector3.up * Mathf.Max(h - _standRadius, _standRadius + 0.03f);
            // OverlapCapsule rather than CheckCapsule: the character's own
            // controller sits inside this volume and would otherwise report a hit
            // every frame, trapping him crouched forever.
            var hits = Physics.OverlapCapsule(bottom, top, _standRadius * 0.95f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h2 in hits)
                if (h2.transform != transform && !h2.transform.IsChildOf(transform))
                    return false;
            return true;
        }

        // -------------------------------------------------------------- ground

        /// <summary>
        /// Ground test by spherecast rather than CharacterController.isGrounded,
        /// which is only refreshed by Move() and reports true spuriously while
        /// airborne (the landing animation used to play in mid-air). Rising is
        /// never grounded: that one rule replaces the old 0.15 s post-jump latch,
        /// and is what lets the jump chain trust a landing.
        /// </summary>
        bool CheckGrounded()
        {
            if (VerticalVelocity > 0.01f) return false;

            float radius = Mathf.Max(_cc.radius * 0.9f, 0.05f);
            Vector3 origin = transform.position + Vector3.up * (_cc.radius + 0.05f);
            float distance = _cc.radius + 0.05f + Config.GroundProbe;

            var hits = Physics.SphereCastAll(origin, radius, Vector3.down, distance, ~0,
                                             QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.transform == transform || h.transform.IsChildOf(transform)) continue;
                if (h.distance <= 0f) continue;   // started inside a collider
                if (Vector3.Angle(h.normal, Vector3.up) > _cc.slopeLimit) continue;
                return true;
            }
            // The cast can miss while he is plainly standing (on the scanned floor
            // it starts overlapping a bump, or clips past a crease, and he loops
            // Fall on flat ground). The controller's own last Move knows better.
            return (_cc.collisionFlags & CollisionFlags.Below) != 0;
        }
    }
}
