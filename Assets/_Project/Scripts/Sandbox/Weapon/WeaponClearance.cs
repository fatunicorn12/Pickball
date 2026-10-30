using UnityEngine;

namespace Pickleball.Sandbox.Weapon
{
    /// <summary>
    /// Keeps the hand-held weapon out of the wielder's own head, in every
    /// animation, by bending the arm after the Animator has posed it.
    ///
    /// The Mixamo sword-and-shield clips were authored on an adult-proportioned
    /// body. On the chibi the head is about 0.7 m across, so a blade held where
    /// those clips hold it cuts through the hair: in idle it rises behind the
    /// head, and in run, jump, the front flip and the combo swings it passes
    /// straight through. Re-gripping the weapon would fix one clip and break the
    /// swings tuned around the current grip, and editing ~20 clips would have to
    /// be repeated for every new one. Instead this runs one pass after the pose
    /// is final, so it covers any clip, including ones added later.
    ///
    /// Each frame, for each volume in the <see cref="WeaponClearanceProfile"/>,
    /// <see cref="ClearanceSolver"/> finds the smallest turn that takes the
    /// blade clear. That turn is split between the shoulder (a ball joint, which
    /// lifts the whole arm away) and the wrist. The correction is the minimum
    /// needed, so it is zero whenever the animation already clears and fades in
    /// and out without popping.
    ///
    /// Order: after <see cref="SwingTwist"/> (-100), which restores the swings'
    /// hip turn and so moves the arm, and before the default-order LateUpdates
    /// (BladeTrail, SpringCloth), so the trail draws the corrected blade. Foot
    /// IK and look-at run in OnAnimatorIK, which comes before every LateUpdate.
    ///
    /// MeleeCombat's hit sweep is not one of those LateUpdates: it runs in
    /// Update, before this frame's animation pass, so it reads the bones as
    /// this component left them last frame. The sweep therefore hits with the
    /// corrected blade, one frame late, which is the same lag it has always had
    /// against the Animator. That holds while the Animator uses the Normal
    /// update mode. Under Animate Physics the Animator would overwrite the bones
    /// in the physics step before Update, and the sweep would see the raw clip
    /// on frames that have one.
    ///
    /// Presentation only: it moves bones, never gameplay state, and nothing
    /// else depends on it. Disable the component to see the raw clips.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class WeaponClearance : MonoBehaviour
    {
        [Tooltip("The wielder's humanoid Animator; the volumes' bones and the " +
                 "arm are looked up through its avatar.")]
        [SerializeField] private Animator animator;
        [Tooltip("Grip end of the blade (the point the fist closes on).")]
        [SerializeField] private Transform bladeBase;
        [Tooltip("Far end of the blade.")]
        [SerializeField] private Transform bladeTip;
        [Tooltip("Which hand holds the weapon.")]
        [SerializeField] private bool rightHanded = true;
        [SerializeField] private WeaponClearanceProfile profile;

        // Resolved from the avatar in OnEnable, so a script reload in Play mode
        // rebuilds them rather than leaving them null.
        Transform _shoulder, _hand;
        Transform[] _volumeBones;

        /// <summary>Total turn applied this frame, °. Zero when the animation
        /// clears by itself; useful for debugging which clips need help.</summary>
        public float LastCorrection { get; private set; }

        /// <summary>The profile in use. A weapon swap may change it.</summary>
        public WeaponClearanceProfile Profile => profile;

        /// <summary>
        /// Swaps the clearance profile, for a weapon with a different blade
        /// (width, length). Null is ignored so a weapon without its own profile
        /// keeps the current one. Re-resolves the volume bones at once.
        /// </summary>
        public void SetProfile(WeaponClearanceProfile newProfile)
        {
            if (newProfile == null || newProfile == profile) return;
            profile = newProfile;
            Resolve();
        }

        void OnEnable() => Resolve();

        void OnValidate()
        {
            if (isActiveAndEnabled) Resolve();
        }

        void Resolve()
        {
            _shoulder = _hand = null;
            _volumeBones = null;
            if (animator == null || !animator.isHuman || profile == null) return;

            _shoulder = animator.GetBoneTransform(rightHanded
                ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            _hand = animator.GetBoneTransform(rightHanded
                ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);

            var volumes = profile.Volumes;
            _volumeBones = new Transform[volumes.Count];
            for (int i = 0; i < volumes.Count; i++)
                _volumeBones[i] = animator.GetBoneTransform(volumes[i].bone);
        }

        void LateUpdate()
        {
            LastCorrection = 0f;
            if (profile == null || bladeBase == null || bladeTip == null) return;
            if (_hand == null || _volumeBones == null
                || _volumeBones.Length != profile.Volumes.Count)
            {
                Resolve();
                if (_hand == null || _volumeBones == null) return;
            }

            var volumes = profile.Volumes;
            float pad = profile.BladeRadius + profile.Margin;
            float share = _shoulder != null ? profile.ShoulderShare : 0f;
            float total = 0f;

            for (int pass = 0; pass < profile.Iterations; pass++)
            {
                Vector3 grip = bladeBase.position;
                Vector3 blade = bladeTip.position - grip;
                float length = blade.magnitude;
                if (length < 1e-4f) break;
                Vector3 dir = blade / length;

                // Worst offender only: clearing it usually clears the others,
                // and the next pass catches any that remain.
                float worst = 0f;
                Vector3 worstCentre = Vector3.zero;
                for (int i = 0; i < volumes.Count; i++)
                {
                    Transform bone = _volumeBones[i];
                    if (bone == null) continue;
                    Vector3 centre = bone.TransformPoint(volumes[i].centre);
                    float radius = volumes[i].radius * bone.lossyScale.x + pad;
                    float needed = ClearanceSolver.RequiredAngle(grip, dir, length, centre, radius);
                    if (needed > worst) { worst = needed; worstCentre = centre; }
                }

                float step = Mathf.Min(worst, profile.MaxCorrection - total);
                if (step <= 0.01f) break;

                // Fallback for a blade aimed dead at the centre: swing it out
                // to the weapon side of the body.
                Vector3 body = animator.transform.right;
                Vector3 outward = rightHanded ? body : -body;
                Vector3 axis = ClearanceSolver.AwayAxis(grip, dir, worstCentre, outward);

                // Both rotations share one world axis, so the blade turns by
                // exactly `step` whatever the split; the shoulder's share also
                // carries the grip outward, which the next pass measures.
                if (share > 0f)
                    _shoulder.rotation = Quaternion.AngleAxis(step * share, axis) * _shoulder.rotation;
                _hand.rotation = Quaternion.AngleAxis(step * (1f - share), axis) * _hand.rotation;
                total += step;
            }

            LastCorrection = total;
        }

        void OnDrawGizmosSelected()
        {
            if (profile == null || animator == null || !animator.isHuman) return;
            float pad = profile.BladeRadius + profile.Margin;
            foreach (var v in profile.Volumes)
            {
                Transform bone = animator.GetBoneTransform(v.bone);
                if (bone == null) continue;
                Vector3 centre = bone.TransformPoint(v.centre);
                Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
                Gizmos.DrawWireSphere(centre, v.radius * bone.lossyScale.x);
                Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.3f);
                Gizmos.DrawWireSphere(centre, v.radius * bone.lossyScale.x + pad);
            }
            if (bladeBase != null && bladeTip != null)
            {
                Gizmos.color = LastCorrection > 0f ? Color.red : Color.green;
                Gizmos.DrawLine(bladeBase.position, bladeTip.position);
            }
        }
    }
}
