using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// Closes a hand's fingers around whatever its <see cref="HandGrip"/>
    /// holds, after the Animator has posed them, in every clip.
    ///
    /// Why a post-Animator pass and not a fist pose on a masked layer: the
    /// Mixamo clips already carry their own right-hand finger curves (all 24
    /// clips on the chibi controller do), and the upper-body mask passes them
    /// through on the Gesture and Attack layers. A fist layer would fight those
    /// layers for the fingers depending on layer order, would mean editing the
    /// controller that other setups rebuild, and only works on Humanoid rigs.
    /// This pass runs after every layer, so the fist holds through locomotion,
    /// the hook throw and every combo swing alike, and the curl is plain data on
    /// the <see cref="HandGripProfile"/>.
    ///
    /// Each joint is set to its rest pose turned toward the palm about the
    /// handle (see <see cref="FingerCurlMath"/>), then blended with the
    /// animated pose by the profile's weight. Building from the rest pose, not
    /// adding to the animated one, means a clip that already curls the fingers
    /// can't push them through the palm.
    ///
    /// Needs finger bones mapped on the Humanoid avatar. The Tripo rigs
    /// (chibi, Newton) have none; the weapon setup only adds this component
    /// when the rig has them, and it does nothing if it finds none.
    ///
    /// Order: after <see cref="SwingTwist"/> (-100) and before WeaponClearance
    /// (-50). It only turns finger bones, which neither of those reads, and the
    /// grab point hangs off the hand bone, not a finger, so the blade and its
    /// markers never move with the curl; the order is fixed only to keep frames
    /// deterministic.
    /// </summary>
    [DefaultExecutionOrder(-75)]
    public class FingerCurl : MonoBehaviour
    {
        [Tooltip("The Humanoid Animator whose finger bones are curled.")]
        [SerializeField] private Animator animator;
        [Tooltip("The hand's grab point. The fist closes while it holds something; " +
                 "its axes give the direction the fingers wrap.")]
        [SerializeField] private HandGrip grip;
        [Tooltip("Curl angles, weight and speed.")]
        [SerializeField] private HandGripProfile profile;
        [Tooltip("Which hand's fingers to curl. Must match the hand the grab point " +
                 "hangs off, or the fingers bend backwards.")]
        [SerializeField] private bool rightHand = true;

        static readonly HumanBodyBones[][] RightFingers =
        {
            new[] { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal },
            new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
            new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
            new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
            new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
        };

        static readonly HumanBodyBones[][] LeftFingers =
        {
            new[] { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal },
            new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal },
            new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
            new[] { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal },
            new[] { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal },
        };

        /// <summary>One finger: its joints, their rest rotations and curl axes.</summary>
        sealed class Chain
        {
            public Transform[] Joints;
            public Quaternion[] Rest;
            public Vector3[] Axes;
            public bool Thumb;
            public float Sign;
        }

        // Rebuilt in OnEnable, so a script reload in Play mode resolves them
        // again instead of leaving stale bone references.
        Chain[] _chains;
        float _weight;

        /// <summary>Number of fingers (thumb included) this hand can curl. Zero
        /// on a rig without finger bones.</summary>
        public int FingerCount => _chains?.Length ?? 0;

        /// <summary>How closed the fist is right now, 0-1.</summary>
        public float Closed => _weight;

        void OnEnable()
        {
            Build();
            _weight = Target();   // start in the held pose rather than closing on load
        }

        void OnValidate()
        {
            if (isActiveAndEnabled) Build();
        }

        float Target() => profile != null && grip != null && grip.IsHolding ? profile.CurlWeight : 0f;

        void Build()
        {
            _chains = null;
            if (animator == null || !animator.isHuman || grip == null) return;
            Transform hand = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (hand == null || !grip.transform.IsChildOf(hand)) return;

            // Rest pose by bone name, from the avatar's own skeleton description.
            var rest = new Dictionary<string, Quaternion>();
            if (animator.avatar != null)
                foreach (var bone in animator.avatar.humanDescription.skeleton)
                    rest.TryAdd(bone.name, bone.rotation);
            Quaternion RestOf(Transform t) => rest.TryGetValue(t.name, out var q) ? q : t.localRotation;

            // The grab point is parented under the hand, so its rotation relative
            // to the hand is fixed whatever pose the hand is in now.
            Quaternion gripInHand = Quaternion.Inverse(hand.rotation) * grip.transform.rotation;

            var chains = new List<Chain>();
            var path = new List<Quaternion>();
            var table = rightHand ? RightFingers : LeftFingers;
            for (int f = 0; f < table.Length; f++)
            {
                bool thumb = f == 0;
                var joints = new List<Transform>();
                foreach (var b in table[f])
                {
                    var t = animator.GetBoneTransform(b);
                    if (t != null) joints.Add(t);
                }
                if (joints.Count == 0) continue;

                Vector3 axisInHand = gripInHand * FingerCurlMath.AxisInGrip(thumb);
                var chain = new Chain
                {
                    Joints = joints.ToArray(),
                    Rest = new Quaternion[joints.Count],
                    Axes = new Vector3[joints.Count],
                    Thumb = thumb,
                    Sign = FingerCurlMath.Sign(rightHand, thumb),
                };

                bool valid = true;
                for (int j = 0; j < joints.Count && valid; j++)
                {
                    // Rest rotations from the hand's child down to this joint,
                    // including any unmapped bone (a metacarpal) in between.
                    path.Clear();
                    for (var t = joints[j]; t != hand; t = t.parent)
                    {
                        if (t == null) { valid = false; break; }
                        path.Insert(0, RestOf(t));
                    }
                    if (!valid) break;
                    chain.Rest[j] = RestOf(joints[j]);
                    chain.Axes[j] = FingerCurlMath.AxisInJoint(FingerCurlMath.RestInHand(path), axisInHand);
                }
                if (valid) chains.Add(chain);
            }
            if (chains.Count > 0) _chains = chains.ToArray();
        }

        void LateUpdate()
        {
            if (_chains == null || profile == null) return;

            float k = 1f - Mathf.Exp(-profile.CloseRate * Time.deltaTime);
            _weight += (Target() - _weight) * k;
            if (_weight < 1e-3f) return;

            Vector3 fingerAngles = profile.FingerCurl;
            Vector3 thumbAngles = profile.ThumbCurl;
            for (int c = 0; c < _chains.Length; c++)
            {
                Chain chain = _chains[c];
                Vector3 angles = chain.Thumb ? thumbAngles : fingerAngles;
                for (int j = 0; j < chain.Joints.Length; j++)
                {
                    Transform joint = chain.Joints[j];
                    if (joint == null) continue;
                    // A chain missing a middle bone uses the next angle for the next bone it has.
                    float degrees = chain.Sign * angles[Mathf.Min(j, 2)];
                    Quaternion curled = FingerCurlMath.Curled(chain.Rest[j], chain.Axes[j], degrees);
                    joint.localRotation = Quaternion.Slerp(joint.localRotation, curled, _weight);
                }
            }
        }
    }
}
