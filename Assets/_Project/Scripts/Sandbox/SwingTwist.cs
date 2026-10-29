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
    ///
    /// The sampling, crossfade and correction maths live in
    /// <see cref="SwingTwistMath"/> and are EditMode-tested there. The lookup
    /// and bones are rebuilt in OnEnable, so a script reload in Play mode keeps
    /// the twist working (QA ST3: Awake does not run again after a reload).
    ///
    /// The cost: the legs stay with locomotion, so the clip's whole hip turn
    /// (yaw and tilt) is taken up at the waist. Measured over the idle stance,
    /// the chest's rotation relative to the hips peaks at 82-180° away from
    /// idle, depending on the swing. In the clips themselves it peaks at 22-50°,
    /// because the hips turn too. <see cref="LastCorrection"/> reports the turn
    /// applied each frame. See Docs/Systems/SwordAbilities.md §5.
    ///
    /// A step flagged Full Body When Standing plays on SwordAnimatorDriver's
    /// AttackBody layer when he stands still. There the hips come from the clip, so
    /// this correction falls to zero by itself and nothing is corrected twice.
    /// Measured: under 0.5° at full layer weight.
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

        // Built in OnEnable, never in a readonly initializer + Awake: a script
        // reload in Play mode re-creates these empty and calls OnEnable, not
        // Awake, which used to leave the twist dead until the scene reloaded.
        Dictionary<int, Quaternion[]> _byHash;
        int _layer = -1;
        bool _layerResolved;
        Transform _hips, _spine;

        /// <summary>The turn applied to the spine chain this frame, in degrees.
        /// Zero when no swing is playing or the state has no baked track. For
        /// debugging: read it while a swing plays to see how much of the pose is
        /// this correction rather than the clip.</summary>
        public float LastCorrection { get; private set; }

        void OnEnable() => Resolve();

        void Resolve()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _hips = _spine = null;
            if (animator != null && animator.isHuman)
            {
                _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            }

            _byHash ??= new Dictionary<int, Quaternion[]>();
            _byHash.Clear();
            if (tracks != null)
                foreach (var t in tracks)
                    if (t != null && t.hips is { Length: > 1 })
                        _byHash[Animator.StringToHash(t.state)] = t.hips;

            _layerResolved = false;
            _layer = -1;
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
            LastCorrection = 0f;
            if (_byHash == null) Resolve();
            if (_hips == null || _spine == null || _byHash.Count == 0) return;
            int layer = Layer();
            if (layer < 0) return;

            float weight = animator.GetLayerWeight(layer) * strength;
            if (weight <= 0.001f) return;

            // Hip rotation the clip wants, blended across a chain crossfade so
            // the correction moves with the swing rather than popping per state.
            bool haveCur = TrySample(animator.GetCurrentAnimatorStateInfo(layer), out var local);
            if (animator.IsInTransition(layer) &&
                TrySample(animator.GetNextAnimatorStateInfo(layer), out var next))
            {
                float b = animator.GetAnimatorTransitionInfo(layer).normalizedTime;
                if (haveCur) local = SwingTwistMath.BlendStates(local, next, b);
                else { local = next; weight *= b; }      // fading in from None
                haveCur = true;
            }
            if (!haveCur) return;

            Quaternion turn = SwingTwistMath.Correction(animator.transform.rotation, local,
                                                        _hips.rotation, weight);
            _spine.rotation = turn * _spine.rotation;
            LastCorrection = Quaternion.Angle(Quaternion.identity, turn);
        }

        bool TrySample(AnimatorStateInfo info, out Quaternion rot)
        {
            if (!_byHash.TryGetValue(info.shortNameHash, out var samples))
            {
                rot = Quaternion.identity;
                return false;
            }
            rot = SwingTwistMath.SampleTrack(samples, info.normalizedTime);
            return true;
        }

#if UNITY_EDITOR
        /// <summary>Editor hook: SwingTwistSetup writes the baked tracks in.</summary>
        public void SetTracks(Animator target, List<Track> baked)
        {
            animator = target;
            tracks = baked;
            _byHash = null;     // rebuilt from the new tracks on next use
        }
#endif
    }
}
