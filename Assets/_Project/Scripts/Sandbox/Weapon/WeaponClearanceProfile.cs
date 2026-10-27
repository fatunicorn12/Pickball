using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Weapon
{
    /// <summary>
    /// The body volumes a hand-held weapon must stay out of, and how the arm is
    /// allowed to move to keep it out. Read by <see cref="WeaponClearance"/>.
    ///
    /// An asset rather than component fields so one tuned profile can be shared
    /// by every character on the same rig, and so retuning it in Play mode
    /// survives leaving Play.
    ///
    /// Created by Tools ▸ Pickleball ▸ Setup Weapon Clearance, which fits the
    /// head sphere to the skinned mesh. The setup only creates the asset when it
    /// is missing, so hand tuning here is never overwritten.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Weapon Clearance Profile", fileName = "WeaponClearanceProfile")]
    public class WeaponClearanceProfile : ScriptableObject
    {
        /// <summary>A sphere riding on a humanoid bone.</summary>
        [Serializable]
        public struct Volume
        {
            [Tooltip("Bone the sphere follows.")]
            public HumanBodyBones bone;
            [Tooltip("Sphere centre in the bone's local space, m.")]
            public Vector3 centre;
            [Tooltip("Sphere radius, m. Size it to the silhouette (hair included), " +
                     "not the skull: what reads as clipping is the blade crossing " +
                     "anything the player sees as head.")]
            [Min(0f)] public float radius;
        }

        [Header("Volumes")]
        [Tooltip("Spheres the blade is kept out of. The head alone covers the " +
                 "reported clipping; add chest or shoulder spheres the same way " +
                 "if a clip pushes the blade into the body.")]
        [SerializeField] private List<Volume> volumes = new();

        [Header("Blade")]
        [Tooltip("Half the blade's thickness, m. Added to every radius so the " +
                 "blade's edge clears, not just its centre line.")]
        [Min(0f)] [SerializeField] private float bladeRadius = 0.02f;
        [Tooltip("Visible gap kept between the blade's edge and the volumes, m. " +
                 "Zero lets the blade brush the hair; larger reads as the " +
                 "character consciously holding it away. 0.06 keeps every " +
                 "measured clip 6-11 cm off the hair from any camera angle.")]
        [Min(0f)] [SerializeField] private float margin = 0.06f;

        [Header("Arm")]
        [Tooltip("Share of each correction taken by the shoulder (0-1); the wrist " +
                 "takes the rest. The shoulder is a ball joint, so it carries a " +
                 "large push naturally by lifting the arm away; a wrist bent past " +
                 "~45° reads as broken. Higher keeps the wrist straighter.")]
        [Range(0f, 1f)] [SerializeField] private float shoulderShare = 0.5f;
        [Tooltip("Largest total turn applied in one frame, °. Caps the correction " +
                 "in poses where the hand itself is inside the head (the tuck of " +
                 "a front flip), where no reasonable arm pose clears the blade.")]
        [Range(0f, 120f)] [SerializeField] private float maxCorrection = 75f;
        [Tooltip("Solver passes per frame. Turning the shoulder also moves the " +
                 "grip, so each pass re-measures; 3 converges to well under a " +
                 "degree on every clip measured.")]
        [Range(1, 6)] [SerializeField] private int iterations = 3;

        /// <summary>Spheres the blade is kept out of.</summary>
        public IReadOnlyList<Volume> Volumes => volumes;
        /// <summary>Half the blade's thickness, m.</summary>
        public float BladeRadius => bladeRadius;
        /// <summary>Visible gap kept between blade and body, m.</summary>
        public float Margin => margin;
        /// <summary>Share of each correction taken by the shoulder, 0-1.</summary>
        public float ShoulderShare => shoulderShare;
        /// <summary>Largest total correction per frame, °.</summary>
        public float MaxCorrection => maxCorrection;
        /// <summary>Solver passes per frame.</summary>
        public int Iterations => iterations;

        /// <summary>Replace the volume list. Used by the setup when it fits the
        /// head to a newly created profile.</summary>
        public void SetVolumes(IEnumerable<Volume> fitted)
        {
            volumes.Clear();
            volumes.AddRange(fitted);
        }
    }
}
