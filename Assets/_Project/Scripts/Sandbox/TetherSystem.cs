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

        public TetherAnchor Best { get; private set; }
        public Vector3 RopeOrigin => ropeOrigin != null ? ropeOrigin.position : transform.position;

        float _fireProgress = 1f;
        TetherAnchor _attached;

        void Awake()
        {
            if (aimSource == null && Camera.main != null) aimSource = Camera.main.transform;
            if (rope != null)
            {
                rope.positionCount = 0;
                rope.enabled = false;
            }
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
        }

        public void Detach()
        {
            _attached = null;
            if (rope != null)
            {
                rope.enabled = false;
                rope.positionCount = 0;
            }
        }

        /// <param name="tension">0 = slack (rope bows), 1 = taut (dead straight).</param>
        void LateUpdate()
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
