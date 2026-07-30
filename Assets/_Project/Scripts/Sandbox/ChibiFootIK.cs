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
    /// This casts under each foot every frame and pins it to the real surface,
    /// then drops the whole body by the deeper of the two corrections so the
    /// legs are not forced to over-extend reaching down.
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

        [Tooltip("Sole thickness — how far the ankle sits above the contact point.")]
        [SerializeField] private float footOffset = 0.06f;

        [Tooltip("Largest downward body shift allowed when one foot is much lower.")]
        [SerializeField] private float maxBodyDrop = 0.35f;

        [Tooltip("How fast IK weights and body height follow changes.")]
        [SerializeField] private float smoothing = 12f;

        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private bool alignToSlope = true;

        static readonly int GroundedHash = Animator.StringToHash("Grounded");

        Animator _anim;
        float _bodyDrop;
        float _weight;

        void Awake() => _anim = GetComponent<Animator>();

        void OnAnimatorIK(int layerIndex)
        {
            if (_anim == null) return;

            // Fade IK out entirely while airborne — planting feet on the ground
            // during a jump is worse than no IK at all.
            bool grounded = !_anim.parameters.Length.Equals(0) && _anim.GetBool(GroundedHash);
            _weight = Mathf.MoveTowards(_weight, grounded ? 1f : 0f, smoothing * Time.deltaTime);
            if (_weight <= 0.001f)
            {
                _bodyDrop = 0f;
                return;
            }

            bool okL = Solve(AvatarIKGoal.LeftFoot, out float dropL);
            bool okR = Solve(AvatarIKGoal.RightFoot, out float dropR);

            // Body follows the foot that needs to go furthest down.
            float wanted = 0f;
            if (okL || okR) wanted = Mathf.Min(okL ? dropL : 0f, okR ? dropR : 0f);
            wanted = Mathf.Clamp(wanted, -maxBodyDrop, 0f);
            _bodyDrop = Mathf.Lerp(_bodyDrop, wanted, 1f - Mathf.Exp(-smoothing * Time.deltaTime));

            if (Mathf.Abs(_bodyDrop) > 0.0005f)
            {
                var body = _anim.bodyPosition;
                body.y += _bodyDrop * _weight;
                _anim.bodyPosition = body;
            }
        }

        /// <summary>Pin one foot to the surface. Returns the vertical correction
        /// applied (negative = the foot had to move down).</summary>
        bool Solve(AvatarIKGoal goal, out float drop)
        {
            drop = 0f;
            Vector3 target = _anim.GetIKPosition(goal);
            Vector3 origin = target + Vector3.up * castAbove;

            if (!Physics.Raycast(origin, Vector3.down, out var hit,
                                 castAbove + castBelow, groundMask,
                                 QueryTriggerInteraction.Ignore))
                return false;
            if (hit.transform.IsChildOf(transform)) return false;

            Vector3 solved = hit.point + Vector3.up * footOffset;
            drop = solved.y - target.y;

            _anim.SetIKPositionWeight(goal, _weight);
            _anim.SetIKPosition(goal, solved);

            if (alignToSlope)
            {
                Quaternion current = _anim.GetIKRotation(goal);
                // Keep the foot's facing, but roll it onto the surface normal.
                Quaternion toNormal = Quaternion.FromToRotation(Vector3.up, hit.normal);
                _anim.SetIKRotationWeight(goal, _weight);
                _anim.SetIKRotation(goal, toNormal * current);
            }
            return true;
        }
    }
}
