using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Gives the masked sword swings back the hip turn the upper-body mask
    /// throws away.
    ///
    /// The combo plays on an arms/spine/head layer so the legs keep running,
    /// which means the hips always come from locomotion. But every Mixamo
    /// sword-and-shield clip is authored side-on: the hips are turned 50-110°
    /// away and the spine counter-twists back to face the target. Humanoid spine
    /// muscles are relative to the hips, so keeping the clip's counter-twist
    /// while dropping the hip turn wrenched the chest ~80° the wrong way, and
    /// the sword arm wrapped round in front of the face instead of sweeping out
    /// in front — the "swatting flies" look.
    ///
    /// Fix: after the Animator writes the pose, rotate the whole spine chain
    /// about its base by (where the clip's hips face) / (where locomotion's
    /// hips face). The chest, arms and blade then end up exactly where the clip
    /// authored them, while the hips and legs stay under locomotion.
    ///
    /// The clip's hip rotation cannot be read back off the Animator — the mask
    /// discards it before we see it — so it is baked per state by
    /// SwingTwistSetup and looked up from the Attack layer's state info here.
    ///
    /// Runs before every other LateUpdate (BladeTrail, SpringCloth) so they
    /// sample the corrected pose.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SwingTwist : MonoBehaviour
    {
        [System.Serializable]
        public class Track
        {
            [Tooltip("Attack-layer state this track belongs to.")]
            public string state;
            [Tooltip("The clip's hip rotation relative to the Animator's transform, " +
                     "sampled evenly from start to end of the clip.")]
            public Quaternion[] hips;
        }

        [SerializeField] private Animator animator;
        [Tooltip("Masked upper-body layer the combo states live on. Must match " +
                 "WeaponSetup.AttackLayer.")]
        [SerializeField] private string attackLayer = "Attack";
        [Tooltip("1 restores the authored hip turn exactly; lower keeps some of " +
                 "the locomotion facing.")]
        [Range(0f, 1f)] [SerializeField] private float strength = 1f;
        [Tooltip("Baked by Tools ▸ Pickleball ▸ Bake Swing Twist (also run by " +
                 "Setup Weapon).")]
        [SerializeField] private List<Track> tracks = new();

        readonly Dictionary<int, Quaternion[]> _byHash = new();
        int _layer = -1;
        bool _layerResolved;
        Transform _hips, _spine;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            }
            foreach (var t in tracks)
                if (t.hips is { Length: > 1 })
                    _byHash[Animator.StringToHash(t.state)] = t.hips;
        }

        /// <summary>Resolved on first use, not in Awake — the Animator is on the
        /// model child and has not built its graph when this root wakes (see
        /// MeleeCombat.AttackLayerIndex).</summary>
        int Layer()
        {
            if (_layerResolved) return _layer;
            if (animator == null || animator.runtimeAnimatorController == null) return -1;
            _layerResolved = true;
            _layer = animator.GetLayerIndex(attackLayer);
            return _layer;
        }

        void LateUpdate()
        {
            if (_hips == null || _spine == null || _byHash.Count == 0) return;
            int layer = Layer();
            if (layer < 0) return;

            float weight = animator.GetLayerWeight(layer) * strength;
            if (weight <= 0.001f) return;

            // Hip rotation the clip wants, blended across a chain crossfade so
            // the correction moves with the swing rather than popping per state.
            bool haveCur = TrySample(animator.GetCurrentAnimatorStateInfo(layer), out var cur);
            Quaternion local = cur;
            if (animator.IsInTransition(layer) &&
                TrySample(animator.GetNextAnimatorStateInfo(layer), out var next))
            {
                float b = animator.GetAnimatorTransitionInfo(layer).normalizedTime;
                if (haveCur) local = Quaternion.Slerp(cur, next, b);
                else { local = next; weight *= b; }      // fading in from None
                haveCur = true;
            }
            if (!haveCur) return;

            Quaternion want = animator.transform.rotation * local;
            Quaternion delta = want * Quaternion.Inverse(_hips.rotation);
            _spine.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * _spine.rotation;
        }

        bool TrySample(AnimatorStateInfo info, out Quaternion rot)
        {
            rot = Quaternion.identity;
            if (!_byHash.TryGetValue(info.shortNameHash, out var samples)) return false;

            // Swings are one-shots that hold their last frame while the layer
            // fades out, so clamp rather than wrap.
            float f = Mathf.Clamp01(info.normalizedTime) * (samples.Length - 1);
            int i = Mathf.Min((int)f, samples.Length - 2);
            rot = Quaternion.Slerp(samples[i], samples[i + 1], f - i);
            return true;
        }

#if UNITY_EDITOR
        /// <summary>Editor hook: SwingTwistSetup writes the baked tracks in.</summary>
        public void SetTracks(Animator target, List<Track> baked)
        {
            animator = target;
            tracks = baked;
        }
#endif
    }
}
