using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Target selection and rope rendering for the tether. The swing physics
    /// itself lives in ChibiLocomotion, which owns the CharacterController —
    /// this half only decides what you would hit and draws the line.
    ///
    /// Candidates are scored by ANGLE from the camera's aim, not by distance.
    /// Scoring by distance makes the hook grab whatever is nearest rather than
    /// what you are looking at, which reads as the game ignoring you.
    ///
    /// It also drives a selector marker, reusing the enemy lock-on's
    /// <see cref="LockOnReticle"/> in a cool cyan so it never reads as the
    /// orange enemy lock: a pre-lock hint on <see cref="Best"/> ("a tether
    /// press latches HERE") and a locked marker on the anchor the rope is on.
    /// The anchor glow says "reachable / aimed at"; the marker says exactly
    /// which one the button will take, which the glow alone cannot when two
    /// lit anchors sit close together.
    /// </summary>
    public class TetherSystem : MonoBehaviour
    {
        [Header("Targeting")]
        [Tooltip("Widest angle off the aim direction that can still be latched.")]
        [SerializeField] private float aimConeDegrees = 30f;
        [SerializeField] private LayerMask lineOfSightMask = ~0;
        [SerializeField] private Transform aimSource;

        [Header("Rope")]
        [SerializeField] private LineRenderer rope;
        [Tooltip("Where the rope leaves the character — roughly hand height.")]
        [SerializeField] private Transform ropeOrigin;
        [SerializeField] private int ropeSegments = 14;
        [Tooltip("How much the rope bows when it is not under load.")]
        [SerializeField] private float slackSag = 0.55f;
        [Tooltip("Seconds for the rope to visually fly out to the anchor.")]
        [SerializeField] private float fireTime = 0.09f;

        [Header("Selector Marker")]
        [Tooltip("Material for the selector marker. Optional: the reticle builds " +
                 "a plain unlit fallback at runtime if this is empty, so no setup " +
                 "step is needed.")]
        [SerializeField] private Material reticleMaterial;
        [Tooltip("Marker on the anchor a tether press WOULD latch. Cool teal so it " +
                 "never reads as the orange enemy lock-on.")]
        [SerializeField] private Color hintColour = new(0.45f, 1f, 0.9f, 1f);
        [Tooltip("Marker on the anchor the rope is attached to. A brighter, purer " +
                 "cyan than the hint so latching reads as a change of state.")]
        [SerializeField] private Color lockedColour = new(0.15f, 0.85f, 1f, 1f);
        [Tooltip("Marker radius around the anchor. Anchors are small hooks, not " +
                 "bosses — the enemy lock sizes itself to a body, this stays tight.")]
        [SerializeField, Range(0.1f, 1.5f)] private float markerRadius = 0.4f;
        [Tooltip("Vertical nudge for the pre-lock hint, in metres. 0 puts it right " +
                 "on the attach point, which is where the rope will meet.")]
        [SerializeField] private float hintHeightOffset = 0f;
        [Tooltip("Show the pre-lock hint at all. The locked marker is always shown.")]
        [SerializeField] private bool showHint = true;

        public TetherAnchor Best { get; private set; }
        public Vector3 RopeOrigin => ropeOrigin != null ? ropeOrigin.position : transform.position;

        float _fireProgress = 1f;
        TetherAnchor _attached;
        PlayerAbilities _abilities;
        LockOnReticle _reticle;

        void Awake()
        {
            if (aimSource == null && Camera.main != null) aimSource = Camera.main.transform;
            if (rope != null)
            {
                rope.positionCount = 0;
                rope.enabled = false;
            }

            // Same gate ChibiLocomotion uses for the button itself: no point
            // advertising a latch the player cannot perform yet.
            _abilities = GetComponentInParent<PlayerAbilities>();

            // Created at runtime and parked at the scene root (it must not
            // inherit the player's rotation), so the scene needs no wiring.
            var go = new GameObject("TetherReticle");
            _reticle = go.AddComponent<LockOnReticle>();
            _reticle.Build(reticleMaterial);
            _reticle.SetSize(markerRadius);
        }

        void OnDestroy()
        {
            if (_reticle != null) Destroy(_reticle.gameObject);
        }

        void OnDisable()
        {
            if (_reticle != null) _reticle.Hide();
        }

        void Update()
        {
            Best = null;
            float bestScore = float.MaxValue;
            Vector3 eye = aimSource != null ? aimSource.position : transform.position;
            Vector3 aim = aimSource != null ? aimSource.forward : transform.forward;

            foreach (var anchor in TetherAnchor.Anchors)
            {
                if (anchor == null) continue;

                // "In range" is judged on distance and line of sight ONLY, so an
                // anchor advertises itself as reachable without having to be
                // aimed at first. Aim decides which one you get, not whether
                // any are available.
                bool reachable = IsReachable(anchor, out float dist);
                var state = reachable ? TetherAnchor.State.InRange : TetherAnchor.State.Dormant;

                if (reachable)
                {
                    float angle = Vector3.Angle(aim, anchor.Position - eye);
                    if (angle <= aimConeDegrees)
                    {
                        // Angle dominates; distance only breaks ties between two
                        // anchors you are looking at equally.
                        float score = angle + dist * 0.05f;
                        if (score < bestScore) { bestScore = score; Best = anchor; }
                    }
                }
                anchor.SetState(state);
            }

            if (Best != null) Best.SetState(TetherAnchor.State.Targeted);
            if (_attached != null) _attached.SetState(TetherAnchor.State.Targeted);
        }

        bool IsReachable(TetherAnchor anchor, out float dist)
        {
            Vector3 to = anchor.Position - RopeOrigin;
            dist = to.magnitude;
            if (dist > anchor.MaxRange || dist < 0.5f) return false;

            // No latching through walls.
            if (Physics.Linecast(RopeOrigin, anchor.Position, out var hit,
                                 lineOfSightMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(anchor.transform) &&
                    !hit.transform.IsChildOf(transform)) return false;
            }
            return true;
        }

        public void Attach(TetherAnchor anchor)
        {
            _attached = anchor;
            _fireProgress = 0f;
            if (rope != null)
            {
                rope.enabled = true;
                rope.positionCount = ropeSegments;
            }

            // The lock-in snap is the "got it" beat; firing it here, at the
            // moment of latching, keeps it in step with the throw gesture.
            if (_reticle != null)
            {
                _reticle.SetSize(markerRadius);
                _reticle.PlayLockIn();
            }
        }

        public void Detach()
        {
            _attached = null;
            if (rope != null)
            {
                rope.enabled = false;
                rope.positionCount = 0;
            }
            // No explicit Hide: next LateUpdate falls straight back to the hint
            // on whatever Best is, so letting go and re-aiming is seamless.
        }

        void LateUpdate()
        {
            // Marker runs in LateUpdate so it sees this frame's Attach/Detach no
            // matter whether ChibiLocomotion's Update ran before or after ours.
            UpdateReticle();
            UpdateRope();
        }

        /// <summary>
        /// Locked beats hint: while the rope is on, the marker sits on the
        /// attached anchor even if the aim has drifted onto another one, because
        /// that is the one you are actually hanging from. Off the rope it tracks
        /// Best, snapping to the new anchor the frame Best changes.
        /// </summary>
        void UpdateReticle()
        {
            if (_reticle == null) return;
            if (aimSource == null && Camera.main != null) aimSource = Camera.main.transform;
            Transform cam = aimSource;

            if (_attached != null)
            {
                _reticle.ShowLocked(_attached.Position, cam, lockedColour);
                return;
            }

            if (showHint && Best != null && HasTetherAbility())
                _reticle.ShowHint(Best.Position + Vector3.up * hintHeightOffset, cam, hintColour);
            else
                _reticle.Hide();
        }

        bool HasTetherAbility() => _abilities != null && _abilities.Has(AbilityKind.Tether);

        /// <summary>0 = slack (rope bows), 1 = taut (dead straight).</summary>
        void UpdateRope()
        {
            if (_attached == null || rope == null) return;

            _fireProgress = Mathf.Min(1f, _fireProgress + Time.deltaTime / Mathf.Max(fireTime, 0.01f));

            Vector3 from = RopeOrigin;
            Vector3 to = Vector3.Lerp(from, _attached.Position, _fireProgress);

            float length = Vector3.Distance(from, to);
            float straight = Vector3.Distance(from, _attached.Position);
            // Bow the rope only while it is shorter than the gap it spans.
            float slack = Mathf.Clamp01(1f - length / Mathf.Max(straight, 0.001f));

            for (int i = 0; i < ropeSegments; i++)
            {
                float t = i / (float)(ropeSegments - 1);
                Vector3 p = Vector3.Lerp(from, to, t);
                // Catenary-ish: a sine bulge, deepest at the middle.
                p += Vector3.down * (Mathf.Sin(t * Mathf.PI) * slackSag * slack * length);
                rope.SetPosition(i, p);
            }
        }
    }
}
