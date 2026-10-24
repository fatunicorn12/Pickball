using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Grounds each foot independently by raycasting the world.
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
    /// Requires "IK Pass" on the Animator layer, which the setup script enables;
    /// without it Unity never calls OnAnimatorIK and this silently does nothing.
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

        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private bool alignToSlope = true;

        static readonly int GroundedHash = Animator.StringToHash("Grounded");

        Animator _anim;
        Transform _root;
        bool _hasGroundedParam;
        float _bodyDrop;
        float _float;
        float _weight;
        float _referenceY;

        void Awake()
        {
            _anim = GetComponent<Animator>();
            // The character root (the CharacterController) is the ground
            // reference and the thing the casts must never hit.
            var cc = GetComponentInParent<CharacterController>();
            _root = cc != null ? cc.transform : transform;
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (_anim == null || layerIndex != 0) return;

            // Looked up once per controller rather than reading
            // Animator.parameters every frame, which allocates a fresh array.
            if (!_hasGroundedParam)
            {
                foreach (var p in _anim.parameters)
                    if (p.nameHash == GroundedHash) { _hasGroundedParam = true; break; }
                if (!_hasGroundedParam) return;
            }

            // Fade IK out entirely while airborne — planting feet on the ground
            // during a jump is worse than no IK at all.
            bool grounded = _anim.GetBool(GroundedHash);
            _weight = Mathf.MoveTowards(_weight, grounded ? 1f : 0f, smoothing * Time.deltaTime);
            if (_weight <= 0.001f)
            {
                _bodyDrop = 0f;
                _float = 0f;
                return;
            }

            // The surface the animation is standing on: straight under the
            // character. Each foot is corrected by its OWN surface's offset
            // from this one.
            _referenceY = _root.position.y;
            Vector3 centre = _root.position + Vector3.up * castAbove;
            if (Physics.Raycast(centre, Vector3.down, out var under, castAbove + castBelow,
                                groundMask, QueryTriggerInteraction.Ignore) &&
                !under.transform.IsChildOf(_root))
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
            _float = Mathf.Lerp(_float, hover, 1f - Mathf.Exp(-floatSettleRate * Time.deltaTime));

            float dropL = okL ? Apply(AvatarIKGoal.LeftFoot, left) : 0f;
            float dropR = okR ? Apply(AvatarIKGoal.RightFoot, right) : 0f;

            // Body follows the foot that needs to go furthest down.
            float wanted = Mathf.Clamp(Mathf.Min(dropL, dropR), -maxBodyDrop, 0f);
            _bodyDrop = Mathf.Lerp(_bodyDrop, wanted, 1f - Mathf.Exp(-smoothing * Time.deltaTime));

            float shift = _bodyDrop - _float;
            if (Mathf.Abs(shift) > 0.0005f)
            {
                var body = _anim.bodyPosition;
                body.y += shift * _weight;
                _anim.bodyPosition = body;
            }
        }

        struct FootProbe
        {
            public Vector3 target;
            public RaycastHit hit;
            public float sole;
            /// <summary>Height above its own surface after terrain correction.</summary>
            public float gap;
        }

        /// <summary>Cast under one foot. False if there is no ground in reach.</summary>
        bool Probe(AvatarIKGoal goal, float soleHeight, out FootProbe probe)
        {
            probe = default;
            probe.target = _anim.GetIKPosition(goal);
            Vector3 origin = probe.target + Vector3.up * castAbove;
            if (!Physics.Raycast(origin, Vector3.down, out probe.hit,
                                 castAbove + castBelow, groundMask,
                                 QueryTriggerInteraction.Ignore))
                return false;
            if (probe.hit.transform.IsChildOf(_root)) return false;
            probe.sole = soleHeight + extraSoleClearance;
            // The animated height above the surface the animation assumed (the
            // one under the character), which is what this foot should keep
            // above its own surface.
            probe.gap = probe.target.y - _referenceY - probe.sole;
            return true;
        }

        /// <summary>Correct one foot for the surface under it and the hover
        /// removal. Returns the correction beyond the hover removal (negative =
        /// the foot had to reach further down).</summary>
        float Apply(AvatarIKGoal goal, FootProbe f)
        {
            float lowered = f.target.y - _float;
            // Keep the animated lift (swing foot, flip, step-up), shift it by
            // the terrain's height difference, and never let it end up inside
            // the surface.
            float solvedY = lowered + (f.hit.point.y - _referenceY);
            solvedY = Mathf.Max(solvedY, f.hit.point.y + f.sole);

            _anim.SetIKPositionWeight(goal, _weight);
            _anim.SetIKPosition(goal, new Vector3(f.target.x, solvedY, f.target.z));

            // Only a foot that is actually down gets rolled onto the slope; a
            // foot mid-swing keeps the clip's own rotation.
            if (alignToSlope)
            {
                float lift = solvedY - (f.hit.point.y + f.sole);
                float plant = Mathf.Clamp01(1f - lift / 0.15f);
                Quaternion current = _anim.GetIKRotation(goal);
                Quaternion toNormal = Quaternion.FromToRotation(Vector3.up, f.hit.normal);
                _anim.SetIKRotationWeight(goal, _weight * plant);
                _anim.SetIKRotation(goal, toNormal * current);
            }
            return solvedY - lowered;
        }
    }
}
