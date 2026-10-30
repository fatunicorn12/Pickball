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
    /// Each joint is set to its rest pose turned toward the palm, then blended
    /// with the animated pose by the profile's weight: the fingers about the
    /// handle, the thumb about its own bend axis after its first joint swings
    /// it across the palm (see <see cref="FingerCurlMath"/>). Building from
    /// the rest pose, not adding to the animated one, means a clip that already
    /// curls the fingers can't push them through the palm.
    ///
    /// Needs finger bones mapped on the Humanoid avatar. The chibi has them
    /// from a Blender pass (Tools/Blender/chibi_add_fingers.py); Newton's Tripo
    /// rig still has none. The weapon setup only adds this component when the
    /// rig has them, and it does nothing if it finds none.
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
            /// <summary>Thumb only: the first joint's swing axis, in its rest frame.</summary>
            public Vector3 SwingAxis;
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
            var restPosition = new Dictionary<string, Vector3>();
            if (animator.avatar != null)
                foreach (var bone in animator.avatar.humanDescription.skeleton)
                {
                    rest.TryAdd(bone.name, bone.rotation);
                    restPosition.TryAdd(bone.name, bone.position);
                }
            Quaternion RestOf(Transform t) => rest.TryGetValue(t.name, out var q) ? q : t.localRotation;
            Vector3 RestPositionOf(Transform t) => restPosition.TryGetValue(t.name, out var p) ? p : t.localPosition;

            // The grab point is parented under the hand, so its rotation relative
            // to the hand is fixed whatever pose the hand is in now.
            Quaternion gripInHand = Quaternion.Inverse(hand.rotation) * grip.transform.rotation;
            Vector3 palmInHand = gripInHand * (rightHand ? Vector3.left : Vector3.right);
            Vector3 knuckleInHand = gripInHand * Vector3.forward;
            Vector3 thumbSideInHand = gripInHand * Vector3.up;

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

                var chain = new Chain
                {
                    Joints = joints.ToArray(),
                    Rest = new Quaternion[joints.Count],
                    Axes = new Vector3[joints.Count],
                    Thumb = thumb,
                    Sign = thumb ? 1f : FingerCurlMath.FingerSign(rightHand),
                };

                Vector3 axisInHand = gripInHand * FingerCurlMath.FingerAxisInGrip;
                Vector3 swingInHand = Vector3.zero;
                if (thumb)
                {
                    // Without a second joint to aim at, assume the T-pose convention
                    // (the thumb along the handle), whose flex axis is the knuckles'.
                    Vector3 thumbDirection = joints.Count >= 2
                        ? RestPositionInHand(joints[1], hand, RestOf, RestPositionOf)
                          - RestPositionInHand(joints[0], hand, RestOf, RestPositionOf)
                        : thumbSideInHand;
                    axisInHand = FingerCurlMath.ThumbFlexAxis(thumbDirection, palmInHand);
                    if (axisInHand == Vector3.zero) axisInHand = FingerCurlMath.ThumbFlexAxis(thumbSideInHand, palmInHand);
                    swingInHand = FingerCurlMath.ThumbSwingAxis(thumbDirection, palmInHand, knuckleInHand,
                                                                thumbSideInHand);
                }

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
                    Quaternion restInHand = FingerCurlMath.RestInHand(path);
                    chain.Rest[j] = RestOf(joints[j]);
                    chain.Axes[j] = FingerCurlMath.AxisInJoint(restInHand, axisInHand);
                    if (thumb && j == 0) chain.SwingAxis = FingerCurlMath.AxisInJoint(restInHand, swingInHand);
                }
                if (valid) chains.Add(chain);
            }
            if (chains.Count > 0) _chains = chains.ToArray();
        }

        /// <summary>A joint's rest position in the hand bone's space, chained
        /// down from the hand through the rest pose. Build only, never per frame.</summary>
        static Vector3 RestPositionInHand(Transform joint, Transform hand,
                                          System.Func<Transform, Quaternion> restOf,
                                          System.Func<Transform, Vector3> restPositionOf)
        {
            var path = new List<Transform>();
            for (var t = joint; t != null && t != hand; t = t.parent) path.Insert(0, t);
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            foreach (var t in path)
            {
                position += rotation * restPositionOf(t);
                rotation *= restOf(t);
            }
            return position;
        }

        void LateUpdate()
        {
            if (_chains == null || profile == null) return;

            float k = 1f - Mathf.Exp(-profile.CloseRate * Time.deltaTime);
            _weight += (Target() - _weight) * k;
            if (_weight < 1e-3f) return;

            // A handle fatter than the one the curl was tuned on gets less bend
            // (and a thinner one more), so the fingers wrap it rather than
            // passing through it. The thumb's swing across the palm doesn't
            // depend on the handle, only its bend.
            HeldItem held = grip != null ? grip.Held : null;
            float scale = FingerCurlMath.CurlScale(profile.GripRadius, held != null ? held.GripRadius : 0f,
                                                   profile.FingerHalfThickness);
            Vector3 fingerAngles = profile.FingerCurl * scale;
            Vector3 thumbAngles = profile.ThumbCurl * scale;
            float thumbSwing = profile.ThumbSwing;
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
                    Quaternion curled = chain.Thumb && j == 0
                        ? FingerCurlMath.Opposed(chain.Rest[j], chain.SwingAxis, thumbSwing, chain.Axes[j], degrees)
                        : FingerCurlMath.Curled(chain.Rest[j], chain.Axes[j], degrees);
                    joint.localRotation = Quaternion.Slerp(joint.localRotation, curled, _weight);
                }
            }
        }
    }
}
