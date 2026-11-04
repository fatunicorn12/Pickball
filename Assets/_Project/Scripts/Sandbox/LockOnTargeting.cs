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
    ///    stays out of sight for a moment. A target that dies hands the lock to
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
    /// Movement stays free-running while locked (Kingdom Hearts): the stick is
    /// camera-relative and the camera faces the target, so forward closes in and
    /// sideways circles it. The character turns to face the target when he
    /// stops and whenever he swings. Zelda's sidestep-while-facing needs strafe
    /// clips in the locomotion tree, which this deliberately does not fake.
    /// </summary>
    public class LockOnTargeting : MonoBehaviour
    {
        [Header("Acquire")]
        [Tooltip("Furthest a target can be locked from.")]
        [SerializeField] private float acquireRange = 25f;
        [Tooltip("Widest angle off the camera's aim a target can be picked from. " +
                 "Roughly the visible screen — locking something behind you reads " +
                 "as the game choosing for you.")]
        [SerializeField] private float acquireConeDegrees = 55f;
        [SerializeField] private LayerMask lineOfSightMask = ~0;

        [Header("Hold")]
        [Tooltip("The lock breaks beyond this distance. Larger than acquireRange " +
                 "so backing off a little does not drop it.")]
        [SerializeField] private float breakRange = 32f;
        [Tooltip("Seconds the target may stay out of sight (behind a pillar, a " +
                 "well, an apple) before the lock lets go.")]
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
        [Tooltip("Chest height on the player, for line-of-sight checks.")]
        [SerializeField] private float eyeHeight = 1.2f;

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

        /// <summary>Where the camera and the character aim: the target's chest,
        /// not its feet.</summary>
        public Vector3 AimPoint => Target != null
            ? Target.transform.position + _aimOffset + Target.BodyOffset
            : transform.position;

        /// <summary>Approximate horizontal radius of the target, for framing.</summary>
        public float TargetRadius { get; private set; }

        InputAction _toggle;
        bool _togglePressed;
        Health _own;
        Vector3 _aimOffset;
        float _lostSightFor;
        float _flickAccum, _flickAge, _switchReadyAt;
        bool _stickArmed = true;
        LockOnReticle _reticle;
        Camera _aimCamera;
        readonly List<Health> _candidates = new();

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
            MeasureTarget(target);
            Reticle.PlayLockIn();
            return true;
        }

        public void Release()
        {
            Target = null;
            _flickAccum = 0f;
        }

        /// <summary>
        /// Aim point and size are measured once, at lock time, from the target's
        /// renderers. Re-reading skinned bounds every frame makes the aim breathe
        /// with the animation, which the camera then faithfully amplifies.
        /// </summary>
        void MeasureTarget(Health target)
        {
            if (!MeasureAim(target, out _aimOffset, out float radius, out float halfHeight))
            {
                TargetRadius = 0.6f;
                Reticle.SetSize(0.6f);
                return;
            }
            TargetRadius = radius;
            Reticle.SetSize(Mathf.Max(0.5f, Mathf.Min(radius, halfHeight) * 0.9f));
        }

        /// <summary>
        /// The aim point (as an offset from the target's root) and horizontal
        /// radius, from the bounds of the target's visible body: every enabled
        /// mesh renderer under its Health, except particles, lines, trails and
        /// anything under a <see cref="LockOnIgnore"/>. False (and a default chest
        /// offset of 1.2 m) when nothing is left to measure.
        /// </summary>
        public static bool MeasureAim(Health target, out Vector3 aimOffset, out float radius,
                                      out float halfHeight)
        {
            Vector3 root = target.transform.position;
            bool any = false;
            var b = new Bounds(root, Vector3.zero);
            foreach (var r in target.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.GetComponentInParent<LockOnIgnore>() != null) continue;
                if (any) b.Encapsulate(r.bounds);
                else { b = r.bounds; any = true; }
            }
            if (!any)
            {
                aimOffset = Vector3.up * 1.2f;
                radius = 0.6f;
                halfHeight = 0.6f;
                return false;
            }

            // Chest: a little above the middle of the body.
            Vector3 aim = b.center + Vector3.up * (b.extents.y * 0.2f);
            // Relative to the body's rest place: AimPoint adds its current offset
            // back, so locking on mid-juggle must not count that height twice.
            aimOffset = aim - root - target.BodyOffset;
            radius = Mathf.Max(b.extents.x, b.extents.z);
            halfHeight = b.extents.y;
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

            float dist = Vector3.Distance(transform.position, AimPoint);
            if (dist > breakRange) { Release(); return; }

            _lostSightFor = HasLineOfSight(Target) ? 0f : _lostSightFor + Time.deltaTime;
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
        /// <param name="point">Chest of the target, world space.</param>
        /// <param name="radius">Approximate horizontal radius of the target, m.</param>
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
            point = CentreOf(best);
            var col = best.GetComponentInChildren<Collider>();
            radius = col != null ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) : 0.6f;
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
            Vector3 fromPoint = from != null ? from.transform.position + _aimOffset : Vector3.zero;

            Health best = null;
            float bestScore = float.MaxValue;
            foreach (var h in _candidates)
            {
                if (h == from) continue;
                if (enemiesOnly && !h.GrantsXp) continue;
                Vector3 p = CentreOf(h);
                float dist = Vector3.Distance(transform.position, p);
                if (dist > range) continue;

                float angle = Vector3.Angle(aim, p - eye);
                if (angle > acquireConeDegrees) continue;
                // The cone alone is not "on screen": at 16:9 the horizontal
                // view is narrower than the cone, so an enemy just off the left
                // edge could be locked. Picking something you cannot see reads
                // as the game choosing for you.
                if (!OnScreen(p)) continue;
                if (!HasLineOfSight(h)) continue;

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

        bool OnScreen(Vector3 point)
        {
            if (aimSource == null) return true;
            if (_aimCamera == null) _aimCamera = aimSource.GetComponent<Camera>();
            if (_aimCamera == null) return true;
            Vector3 vp = _aimCamera.WorldToViewportPoint(point);
            return vp.z > 0f && vp.x > -0.02f && vp.x < 1.02f && vp.y > -0.02f && vp.y < 1.02f;
        }

        /// <summary>Rough chest point for a candidate that is not locked yet.
        /// The precise one is measured on lock.</summary>
        static Vector3 CentreOf(Health h)
        {
            var c = h.GetComponentInChildren<Collider>();
            return c != null ? c.bounds.center : h.transform.position + Vector3.up;
        }

        bool HasLineOfSight(Health h)
        {
            Vector3 from = transform.position + Vector3.up * eyeHeight;
            Vector3 to = h == Target ? AimPoint : CentreOf(h);
            var hits = Physics.RaycastAll(from, to - from, Vector3.Distance(from, to),
                                          lineOfSightMask, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (hit.transform.IsChildOf(h.transform)) continue;
                // Other enemies do not hide each other: a crowd stays lockable.
                if (hit.transform.GetComponentInParent<Health>() != null) continue;
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
                var c = hint.GetComponentInChildren<Collider>();
                float top = c != null ? c.bounds.max.y : hint.transform.position.y + 2f;
                Vector3 p = CentreOf(hint);
                Reticle.ShowHint(new Vector3(p.x, top + 0.5f, p.z), cam, hintColour);
            }
            else Reticle.Hide();
        }
    }
}
