using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Zelda / Kingdom Hearts lock-on, as a TOGGLE: one press locks, the next
    /// press releases. Nothing has to be held.
    ///
    ///  - Press with an enemy in view: lock the one nearest the centre of the
    ///    screen. The camera swings round to keep it framed beyond the player.
    ///  - Press with nothing in view: Zelda's fallback — the camera recentres
    ///    behind the player instead.
    ///  - Flick the mouse (or right stick) sideways while locked to hop to the
    ///    next enemy on that side, Kingdom Hearts style.
    ///  - The lock drops by itself when the target dies, goes too far away, or
    ///    stays out of view for a moment. A target that dies hands the lock to
    ///    the next enemy in view, if there is one.
    ///  - While the <see cref="PlayerInputGate"/> blocks <see cref="InputBlock.LockOn"/>
    ///    (any claimant: a conversation, an aim), the lock is released and no
    ///    marker shows. Breath of the Wild: talking ends targeting.
    ///
    /// Any <see cref="Health"/> that is not the player's own is a candidate, so a
    /// new enemy is targetable without any extra wiring. This component only
    /// decides WHAT is locked; <see cref="SandboxOrbitCamera"/> reads it to aim
    /// and <see cref="ChibiLocomotion"/> reads it to face the target.
    ///
    /// One answer to "where is the enemy": <see cref="BodyOf"/>, measured live from
    /// what the sword can hit (<see cref="LockOnAim"/>). Scoring, the hint, the sight
    /// line, the reticle, the camera and swing homing all read it. One answer to "is
    /// it in view": the aim point is inside the camera's viewport and the camera has a
    /// clear line to it (<see cref="InView"/>). A press, the hint, a target switch and
    /// a death hand-off share that check, and a held lock must keep passing it.
    ///
    /// Movement stays free-running while locked (Kingdom Hearts): the stick is
    /// camera-relative and the camera faces the target, so forward closes in and
    /// sideways circles it. The character turns to face the target when he
    /// stops and whenever he swings. Zelda's sidestep-while-facing needs strafe
    /// clips in the locomotion tree, which this deliberately does not fake.
    /// </summary>
    public class LockOnTargeting : MonoBehaviour
    {
        [Header("Aim")]
        [Tooltip("Where on the hittable body the lock sits, as a fraction of the way from its " +
                 "middle (0) to its top (1). Higher sits the ring and the camera's aim nearer the head.")]
        [SerializeField, Range(0f, 0.9f)] private float aimChestBias = LockOnAim.DefaultChestBias;

        [Header("Acquire")]
        [Tooltip("Furthest a target can be locked from, in metres.")]
        [SerializeField] private float acquireRange = 25f;
        [Tooltip("Widest angle off the camera's aim a target can be picked from. " +
                 "Roughly the visible screen — locking something behind you reads " +
                 "as the game choosing for you.")]
        [SerializeField] private float acquireConeDegrees = 55f;
        [Tooltip("How far inside the screen's edges the aim point must be for a press to lock it, " +
                 "as a fraction of the screen. Larger stops things at the very edge being picked; " +
                 "a held lock is only judged against the edge itself.")]
        [SerializeField, Range(0f, 0.2f)] private float viewportInset = 0.03f;
        [Tooltip("Layers that block the camera's view of a target. Triggers, the player and " +
                 "anything with a Health never block.")]
        [SerializeField] private LayerMask lineOfSightMask = ~0;

        [Header("Hold")]
        [Tooltip("The lock breaks beyond this distance. Larger than acquireRange " +
                 "so backing off a little does not drop it.")]
        [SerializeField] private float breakRange = 32f;
        [Tooltip("Seconds the target may stay out of view (off the screen, or behind a " +
                 "pillar, a well, an apple) before the lock lets go.")]
        [SerializeField] private float lostSightGrace = 1.5f;

        [Header("Switch")]
        [Tooltip("Horizontal mouse travel, in pixels within flickWindow, that " +
                 "counts as a flick to the next target.")]
        [SerializeField] private float mouseFlickPixels = 70f;
        [SerializeField] private float flickWindow = 0.15f;
        [Tooltip("Right-stick deflection that counts as a flick.")]
        [SerializeField, Range(0.2f, 1f)] private float stickFlickThreshold = 0.65f;
        [Tooltip("Minimum time between switches, so one long flick cannot skip " +
                 "past several enemies.")]
        [SerializeField] private float switchCooldown = 0.3f;

        [Header("Wiring")]
        [Tooltip("Where targets are judged from. Defaults to the main camera.")]
        [SerializeField] private Transform aimSource;
        [SerializeField] private SandboxOrbitCamera cameraRig;
        [Tooltip("Optional. While LockOn is blocked (a conversation, an aim) the toggle and " +
                 "target flicks are dropped, not saved for later, a held lock is released and " +
                 "no hint shows.")]
        [SerializeField] private PlayerInputGate inputGate;

        [Header("Reticle")]
        [Tooltip("Material for the reticle lines. Built by Setup Lock-On; a plain " +
                 "unlit fallback is made at runtime if empty.")]
        [SerializeField] private Material reticleMaterial;
        [SerializeField] private Color lockedColour = new(1f, 0.55f, 0.15f);
        [SerializeField] private Color hintColour = new(1f, 0.9f, 0.35f);
        [Tooltip("Show a small arrow over the enemy a press WOULD lock, Zelda's " +
                 "targetable marker.")]
        [SerializeField] private bool showHint = true;

        /// <summary>The locked enemy, or null.</summary>
        public Health Target { get; private set; }
        public bool IsLocked => Target != null;

        /// <summary>Where the camera, the reticle and the character aim: the chest of
        /// the region the sword can hit, this frame. Stays on a body that flies or
        /// hovers relative to its root.</summary>
        public Vector3 AimPoint => Target != null ? BodyOf(Target).AimPoint : transform.position;

        /// <summary>Horizontal radius of the target's hittable region, m: how far short
        /// of <see cref="AimPoint"/> a swing can stop and still connect.</summary>
        public float TargetRadius => Target != null ? BodyOf(Target).Radius : 0f;

        InputAction _toggle;
        bool _togglePressed;
        Health _own;
        MeleeCombat _melee;
        Health _frozenFor;
        Vector3 _frozenOffset, _frozenSize;
        List<Collider> _colliderScratch;
        List<Renderer> _rendererScratch;
        float _lostSightFor;
        float _flickAccum, _flickAge, _switchReadyAt;
        bool _stickArmed = true;
        LockOnReticle _reticle;
        Camera _aimCamera;
        readonly List<Health> _candidates = new();

        /// <summary>Raycast results for the sight line. Buffered so the per-frame hint doesn't
        /// allocate; 32 is far more than a line through a boss arena crosses.</summary>
        static readonly RaycastHit[] SightHits = new RaycastHit[32];

        void Awake()
        {
            if (aimSource == null && Camera.main != null) aimSource = Camera.main.transform;
            if (cameraRig == null && Camera.main != null)
                cameraRig = Camera.main.GetComponent<SandboxOrbitCamera>();
        }

        /// <summary>
        /// The reticle, created on first use. Self-heals after a hot reload:
        /// a script recompile during Play mode resets non-serialized fields,
        /// and an Awake-only reference then stays null for the rest of the
        /// session. Rebuilding lazily means a reload costs one frame, not every
        /// lock for the rest of the run.
        /// </summary>
        LockOnReticle Reticle
        {
            get
            {
                if (_reticle != null) return _reticle;
                var go = new GameObject("LockOnReticle");
                _reticle = go.AddComponent<LockOnReticle>();
                _reticle.Build(reticleMaterial);
                return _reticle;
            }
        }

        /// <summary>The player's own Health, never a candidate. Lazy for the
        /// same hot-reload reason as <see cref="Reticle"/>.</summary>
        Health Own => _own != null ? _own : (_own = GetComponent<Health>());

        void OnDestroy()
        {
            if (_reticle != null) Destroy(_reticle.gameObject);
        }

        void OnEnable()
        {
            // Zelda puts this on Z / ZL, Kingdom Hearts on R1. Both of those are
            // taken here (sprint, dash), so the right trigger stands in.
            _toggle = new InputAction("LockOn", InputActionType.Button);
            _toggle.AddBinding("<Keyboard>/tab");
            _toggle.AddBinding("<Mouse>/middleButton");
            _toggle.AddBinding("<Gamepad>/rightTrigger");
            // Latched from the callback rather than polled with
            // WasPressedThisFrame. Polling only sees a press made in the same
            // update Update runs in; input processed elsewhere (an unfocused
            // Editor, simulated keys, a different update mode) performs the
            // action but is never "this frame", and a toggle that silently
            // eats a press reads as broken.
            _toggle.performed += OnTogglePerformed;
            _toggle.Enable();
        }

        void OnDisable()
        {
            if (_toggle != null) _toggle.performed -= OnTogglePerformed;
            _toggle?.Disable();
            _toggle?.Dispose();
            _togglePressed = false;
            Release();
        }

        void OnTogglePerformed(InputAction.CallbackContext _) => _togglePressed = true;

        void Update()
        {
            bool blocked = inputGate != null && inputGate.IsBlocked(InputBlock.LockOn);
            if (PlayerInputGate.TakeLatched(ref _togglePressed, inputGate, InputBlock.LockOn))
            {
                if (IsLocked) Release();
                else if (!TryLock(FindBest(null, 0)) && cameraRig != null)
                    cameraRig.RecenterBehind(transform.forward);
            }

            // Any LockOn claim ends targeting (BotW: starting a conversation
            // does). Let go the way a toggle does, so the camera leaves lock
            // mode; the ring's release fade runs on unscaled time, so it
            // finishes even once a conversation stops time.
            if (blocked && IsLocked) Release();

            if (IsLocked)
            {
                ValidateLock();
                if (IsLocked && !blocked) HandleSwitch();
            }
            if (blocked) { _flickAccum = 0f; _flickAge = 0f; _stickArmed = false; }

            UpdateReticle(blocked);
        }

        // ------------------------------------------------------------- locking

        bool TryLock(Health target)
        {
            if (target == null) return false;
            Target = target;
            _lostSightFor = 0f;
            _frozenFor = null;
            var body = BodyOf(target);
            Reticle.SetSize(Mathf.Max(0.5f, Mathf.Min(body.Radius, body.HalfHeight) * 0.9f));
            Reticle.PlayLockIn();
            return true;
        }

        public void Release()
        {
            Target = null;
            _frozenFor = null;
            _flickAccum = 0f;
        }

        /// <summary>The one answer to where a target is: see the class notes. Not cached, so
        /// it tracks the target on the frame it moves. A target with no hittable collider
        /// (its hurtbox is off) falls back to its renderers; those bounds breathe with the
        /// animation, which the camera would amplify, so a held lock freezes them against the
        /// root and <see cref="Health.BodyOffset"/> until a hittable collider comes back.</summary>
        LockOnBody BodyOf(Health target)
        {
            _colliderScratch ??= new List<Collider>();
            if (LockOnAim.TryMeasureHittable(target, HittableMask, aimChestBias, _colliderScratch, out var body))
            {
                if (target == _frozenFor) _frozenFor = null;
                return body;
            }

            Vector3 root = target.transform.position + target.BodyOffset;
            if (target == _frozenFor)
                return new LockOnBody(new Bounds(root + _frozenOffset, _frozenSize), aimChestBias, false);

            _rendererScratch ??= new List<Renderer>();
            body = LockOnAim.MeasureFallback(target, aimChestBias, _rendererScratch, out _);
            if (target == Target)
            {
                _frozenFor = target;
                _frozenOffset = body.Bounds.center - root;
                _frozenSize = body.Bounds.size;
            }
            return body;
        }

        /// <summary>The blade's mask, so "hittable" can't drift from what a swing connects
        /// with. Everything on a rig with no sword.</summary>
        int HittableMask
        {
            get
            {
                if (_melee == null) _melee = GetComponent<MeleeCombat>();
                return _melee != null ? _melee.HittableLayers.value : ~0;
            }
        }

        /// <summary>
        /// A target's aim point (as an offset from its root, without
        /// <see cref="Health.BodyOffset"/>) and size, measured as <see cref="AimPoint"/> is, on
        /// any layer. False (and a default chest offset of 1.2 m) when neither a hittable
        /// collider nor a renderer is left to measure.
        /// </summary>
        public static bool MeasureAim(Health target, out Vector3 aimOffset, out float radius,
                                      out float halfHeight)
        {
            bool measured = LockOnAim.TryMeasureHittable(target, ~0, LockOnAim.DefaultChestBias,
                                                         new List<Collider>(), out var body);
            if (!measured)
                body = LockOnAim.MeasureFallback(target, LockOnAim.DefaultChestBias, new List<Renderer>(), out measured);
            if (!measured)
            {
                aimOffset = Vector3.up * LockOnAim.DefaultChestHeight;
                radius = LockOnAim.DefaultSize;
                halfHeight = LockOnAim.DefaultSize;
                return false;
            }
            aimOffset = body.AimPoint - target.transform.position - target.BodyOffset;
            radius = body.Radius;
            halfHeight = body.HalfHeight;
            return true;
        }

        void ValidateLock()
        {
            if (Target == null || !Target.isActiveAndEnabled || Target.IsDead)
            {
                // Kingdom Hearts hands the lock straight to the next enemy when
                // one goes down, rather than leaving the camera loose mid-fight.
                var previous = Target;
                Release();
                TryLock(FindBest(previous, 0));
                return;
            }

            Vector3 aim = AimPoint;
            float dist = Vector3.Distance(transform.position, aim);
            if (dist > breakRange) { Release(); return; }

            // Judged against the screen's edge, not the press's inset: a target the
            // camera is still framing shouldn't start the clock by nearing a margin.
            _lostSightFor = InView(Target, aim, 0f) ? 0f : _lostSightFor + Time.deltaTime;
            if (_lostSightFor > lostSightGrace) Release();
        }

        // ----------------------------------------------------------- selection

        /// <summary>
        /// What an attack should close on, for a swing that homes: the locked target,
        /// or with no lock the best enemy in reach, scored as a lock-on press would
        /// score it. Only enemies in reach compete: a further one nearer the middle of
        /// the screen must not stop a swing homing on the one beside you. A friendly
        /// (one that pays no XP) is never homed on unless it is locked on purpose.
        /// Scans the scene when not locked, so call it per swing, not per frame.
        /// </summary>
        /// <param name="reach">How far away an unlocked target may be, m. A locked one is
        /// returned at any distance; the caller judges whether it can reach it.</param>
        /// <param name="point">Aim point of the target, world space.</param>
        /// <param name="radius">Horizontal radius of its hittable region, m.</param>
        public bool TryGetAttackTarget(float reach, out Vector3 point, out float radius)
        {
            if (IsLocked)
            {
                point = AimPoint;
                radius = TargetRadius;
                return true;
            }
            var best = FindBest(null, 0, reach, enemiesOnly: true);
            if (best == null)
            {
                point = default;
                radius = 0f;
                return false;
            }
            var body = BodyOf(best);
            point = body.AimPoint;
            radius = body.Radius;
            return true;
        }

        void GatherCandidates()
        {
            _candidates.Clear();
            foreach (var h in FindObjectsByType<Health>(FindObjectsSortMode.None))
            {
                if (h == null || h == Own || h.IsDead || !h.isActiveAndEnabled) continue;
                if (h.transform.IsChildOf(transform)) continue;
                _candidates.Add(h);
            }
        }

        /// <summary>
        /// Best target to lock. With <paramref name="side"/> 0 it is the one
        /// nearest the centre of the camera's view, the same angle-first scoring
        /// the tether uses: what you are looking at beats what is closest.
        /// With side ±1 it is the nearest enemy to that side of
        /// <paramref name="from"/>, measured across the screen.
        /// </summary>
        /// <param name="from">The target being moved off, or null.</param>
        /// <param name="side">0 for the best overall, or ±1 for that side of <paramref name="from"/>.</param>
        /// <param name="maxRange">Furthest a candidate may be, m; below 0 means
        /// <see cref="acquireRange"/>.</param>
        /// <param name="enemiesOnly">Leave out friendlies, which pay no XP.</param>
        Health FindBest(Health from, int side, float maxRange = -1f, bool enemiesOnly = false)
        {
            float range = maxRange >= 0f ? maxRange : acquireRange;
            GatherCandidates();
            Vector3 eye = aimSource != null ? aimSource.position : transform.position;
            Vector3 aim = aimSource != null ? aimSource.forward : transform.forward;
            Vector3 right = aimSource != null ? aimSource.right : transform.right;
            Vector3 fromPoint = from != null ? BodyOf(from).AimPoint : Vector3.zero;

            Health best = null;
            float bestScore = float.MaxValue;
            foreach (var h in _candidates)
            {
                if (h == from) continue;
                if (enemiesOnly && !h.GrantsXp) continue;
                Vector3 p = BodyOf(h).AimPoint;
                float dist = Vector3.Distance(transform.position, p);
                if (dist > range) continue;

                float angle = Vector3.Angle(aim, p - eye);
                if (angle > acquireConeDegrees) continue;
                // The cone alone is not "on screen": at 16:9 the horizontal
                // view is narrower than the cone, so an enemy just off the left
                // edge could be locked. Picking something you cannot see reads
                // as the game choosing for you.
                if (!InView(h, p, viewportInset)) continue;

                float score;
                if (side == 0)
                {
                    // Angle dominates; distance only breaks near-ties.
                    score = angle + dist * 0.2f;
                }
                else
                {
                    float across = Vector3.Dot(p - fromPoint, right) * side;
                    if (across <= 0.1f) continue;   // wrong side
                    score = across + dist * 0.1f;
                }
                if (score < bestScore) { bestScore = score; best = h; }
            }
            return best;
        }

        /// <summary>The one answer to "can the player see it": see the class notes.</summary>
        /// <param name="aim">The target's aim point this frame.</param>
        /// <param name="inset">Margin inside the screen's edges, as a fraction of the screen.</param>
        bool InView(Health h, Vector3 aim, float inset) => OnScreen(aim, inset) && HasClearLine(h, aim);

        bool OnScreen(Vector3 point, float inset)
        {
            if (aimSource == null) return true;
            if (_aimCamera == null) _aimCamera = aimSource.GetComponent<Camera>();
            if (_aimCamera == null) return true;
            return LockOnView.InsideViewport(_aimCamera.WorldToViewportPoint(point), inset);
        }

        /// <summary>Where the line of sight starts: the lens, except when the camera rig says
        /// the lens is shoved through a wall (see <see cref="SandboxOrbitCamera.SightOrigin"/>).
        /// The player when there's no camera at all, on a bare test rig.</summary>
        Vector3 SightOrigin => cameraRig != null ? cameraRig.SightOrigin
                             : aimSource != null ? aimSource.position
                             : transform.position;

        bool HasClearLine(Health h, Vector3 to)
        {
            Vector3 from = SightOrigin;
            Vector3 line = to - from;
            float length = line.magnitude;
            if (length < 1e-3f) return true;
            int count = Physics.RaycastNonAlloc(from, line / length, SightHits, length,
                                                lineOfSightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = SightHits[i].transform;
                if (hit.IsChildOf(transform)) continue;
                if (hit.IsChildOf(h.transform)) continue;
                // Other enemies do not hide each other: a crowd stays lockable.
                if (hit.GetComponentInParent<Health>() != null) continue;
                return false;
            }
            return true;
        }

        void HandleSwitch()
        {
            int side = 0;

            // Mouse: only while the camera actually has the cursor, or moving the
            // pointer around the Editor after Escape would hop targets.
            var mouse = Mouse.current;
            if (mouse != null && cameraRig != null && cameraRig.CursorCaptured)
            {
                _flickAge += Time.deltaTime;
                if (_flickAge > flickWindow) { _flickAccum = 0f; _flickAge = 0f; }
                _flickAccum += mouse.delta.ReadValue().x;
                if (Mathf.Abs(_flickAccum) >= mouseFlickPixels)
                {
                    side = _flickAccum > 0f ? 1 : -1;
                    _flickAccum = 0f;
                    _flickAge = 0f;
                }
            }

            // Stick: edge-triggered, re-armed once it comes back near centre.
            var pad = Gamepad.current;
            if (pad != null)
            {
                float x = pad.rightStick.ReadValue().x;
                if (_stickArmed && Mathf.Abs(x) >= stickFlickThreshold)
                {
                    side = x > 0f ? 1 : -1;
                    _stickArmed = false;
                }
                else if (Mathf.Abs(x) < 0.3f) _stickArmed = true;
            }

            if (side == 0 || Time.time < _switchReadyAt) return;
            var next = FindBest(Target, side);
            if (next == null) return;
            _switchReadyAt = Time.time + switchCooldown;
            TryLock(next);
        }

        // ------------------------------------------------------------- reticle

        /// <summary>How far above the top of the body the hint floats, m.</summary>
        const float HintClearance = 0.5f;

        void UpdateReticle(bool blocked)
        {
            // No hint while blocked: a press could not lock it.
            if (blocked) { Reticle.Hide(); return; }

            Transform cam = aimSource;
            if (IsLocked)
            {
                Reticle.ShowLocked(AimPoint, cam, lockedColour);
                return;
            }

            // The hint is cheap to compute only because Health is rare in the
            // scene; scanning every frame is fine for a boss arena, not a horde.
            Health hint = showHint ? FindBest(null, 0) : null;
            if (hint != null)
            {
                var body = BodyOf(hint);
                Vector3 centre = body.Bounds.center;
                Vector3 p = new(centre.x, body.Bounds.max.y + HintClearance, centre.z);
                // A hint means a press would lock, so it must be seen: on a tall target
                // near the top of the screen, mark the aim point instead of the air above.
                if (!OnScreen(p, 0f)) p = body.AimPoint;
                Reticle.ShowHint(p, cam, hintColour);
            }
            else Reticle.Hide();
        }
    }
}
