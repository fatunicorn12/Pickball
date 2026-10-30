using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// How a character's hand holds things: where its grab point sits and how
    /// far its fingers close. Read by the weapon setup (the grab point fields)
    /// and by <see cref="FingerCurl"/> (the curl fields).
    ///
    /// An asset rather than component fields so one tuned profile is shared by
    /// every character on the same rig, and so Play-mode tuning of the curl
    /// survives leaving Play.
    ///
    /// Created by Tools ▸ Pickleball ▸ Setup Weapon / Setup Weapon Grip only
    /// when missing, so hand tuning here is never overwritten.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Hand Grip Profile", fileName = "HandGripProfile")]
    public class HandGripProfile : ScriptableObject
    {
        [Header("Grab point (baked by Setup Weapon Grip)")]
        [Tooltip("Turns the held handle so its blade end leans toward the knuckles " +
                 "(positive) or toward the wrist (negative), °. 0 holds it square " +
                 "across the palm. A real sword grip crosses the palm diagonally, " +
                 "about 15-20°, which angles the blade forward in a thrust. Baked " +
                 "into the grab point: re-run Tools ▸ Pickleball ▸ Setup Weapon Grip " +
                 "after changing it.")]
        [Range(-45f, 45f)] [SerializeField] private float handleTilt = 0f;

        [Tooltip("Radius of the handle a closing fist wraps, m. Only used on rigs " +
                 "with finger bones: it sets how far in front of the palm the grab " +
                 "point sits. Larger opens the fist around a fatter grip. Baked by " +
                 "Setup Weapon Grip.")]
        [Min(0f)] [SerializeField] private float gripRadius = DefaultGripRadius;

        [Header("Finger curl (rigs with finger bones only)")]
        [Tooltip("How far each finger joint bends around the handle, °: proximal " +
                 "(knuckle), intermediate, distal. Measured from the rig's rest " +
                 "pose, so the clips' own finger poses don't add to it. Too high " +
                 "pushes fingertips into the palm; too low leaves a claw.")]
        [SerializeField] private Vector3 fingerCurl = new(75f, 95f, 55f);

        [Tooltip("How far each thumb joint bends toward the palm, °: proximal, " +
                 "intermediate, distal, about the thumb's own bend axis. Smaller " +
                 "than the fingers': a thumb wraps only part-way round a handle. " +
                 "Too high points the thumb straight down out of the fist.")]
        [SerializeField] private Vector3 thumbCurl = new(25f, 25f, 30f);

        [Tooltip("How far the thumb swings across the palm toward the little " +
                 "finger at its first joint, °, before it bends. More lays the " +
                 "thumb over the curled fingers; 0 leaves it beside the index " +
                 "finger. A thumb that points along the handle at rest (Mixamo " +
                 "T-pose) wants little; the chibi's points along its fingers and " +
                 "wants about 80.")]
        [Range(0f, 120f)] [SerializeField] private float thumbSwing = 0f;

        [Tooltip("How fully the fist replaces the clips' finger poses while the " +
                 "hand holds something (0-1). 1 is a locked fist in every clip; " +
                 "lower lets the clips' finger motion show through.")]
        [Range(0f, 1f)] [SerializeField] private float curlWeight = 1f;

        [Tooltip("How fast the fist closes on a grab and opens on release, 1/s " +
                 "(exponential rate). 18 closes in about 0.15 s; lower reads as a " +
                 "slow, deliberate grip.")]
        [Min(0f)] [SerializeField] private float closeRate = 18f;

        [Tooltip("Half a finger's thickness, m. Only used to scale the curl for a " +
                 "handle fatter or thinner than gripRadius: the finger's centre line " +
                 "wraps a circle of handle radius plus this, so each joint bends by " +
                 "(gripRadius + this) / (handle radius + this) of the curl above. " +
                 "Larger makes the curl change less with handle size.")]
        [Min(0.001f)] [SerializeField] private float fingerHalfThickness = 0.009f;

        /// <summary>The grip radius a new profile starts with, m; also what a
        /// hand without a profile assumes it was solved for.</summary>
        public const float DefaultGripRadius = 0.015f;

        /// <summary>Handle lean toward the knuckles, °.</summary>
        public float HandleTilt => handleTilt;
        /// <summary>Radius of the handle a closing fist wraps, m.</summary>
        public float GripRadius => gripRadius;
        /// <summary>Finger bend per joint (proximal, intermediate, distal), °.</summary>
        public Vector3 FingerCurl => fingerCurl;
        /// <summary>Thumb bend per joint (proximal, intermediate, distal), °.</summary>
        public Vector3 ThumbCurl => thumbCurl;
        /// <summary>Thumb swing across the palm at its first joint, °.</summary>
        public float ThumbSwing => thumbSwing;
        /// <summary>How fully the fist replaces the clips' fingers, 0-1.</summary>
        public float CurlWeight => curlWeight;
        /// <summary>Close/open rate, 1/s.</summary>
        public float CloseRate => closeRate;
        /// <summary>Half a finger's thickness, m, for scaling the curl to a handle's size.</summary>
        public float FingerHalfThickness => fingerHalfThickness;
    }
}
