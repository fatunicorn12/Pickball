using UnityEngine;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// How far a reduced baked clip may stray from its per-frame bake, curve by curve (see
    /// <see cref="CurveReducer"/>). Every baked curve is reduced with these; a curve that stays
    /// within its tolerance of one value keeps a single key.
    /// </summary>
    /// <remarks>
    /// The reduction took the clips from about 166 MB of YAML (a key every frame on all 116 curves,
    /// written twice by Unity, once as runtime and once as editor curves) to a few MB, with every
    /// humanoid bone within 2 mm and 0.5° of the unreduced bake (ClipReductionTests).
    /// </remarks>
    public static class ClipReduction
    {
        /// <summary>Muscle curves: tolerance in muscle units (−1..1 over the muscle's range).</summary>
        [Tooltip("Muscle curves, muscle units (−1..1 spans the muscle's whole range). 0.0018 is about 0.2° on a 100° muscle. The brief asked for 0.002; at 0.002 errors added up along the arm to 2.006 mm at a fingertip, just over the 2 mm budget.")]
        public const float MuscleTolerance = 0.0018f;

        /// <summary>
        /// Arm-chain muscles (shoulder, arm, forearm, hand): tolerance in muscle units. Their errors
        /// add up along the arm to the fingertips: with every muscle at <see cref="MuscleTolerance"/>
        /// a fingertip still drifted 2.003 mm, just over the 2 mm budget.
        /// </summary>
        [Tooltip("Arm-chain muscles (shoulder, arm, forearm, hand), muscle units. Tighter than the rest because their errors add up to the fingertips.")]
        public const float ArmMuscleTolerance = 0.0013f;

        /// <summary>The tolerance for one muscle, by its Unity name.</summary>
        public static float MuscleToleranceFor(string muscleName)
        {
            bool finger = muscleName.Contains("Stretched") || muscleName.Contains("Spread");
            bool arm = !finger && (muscleName.Contains("Shoulder") || muscleName.Contains("Arm") || muscleName.Contains("Forearm")
                                   || muscleName.Contains("Hand"));
            return arm ? ArmMuscleTolerance : MuscleTolerance;
        }

        /// <summary>Stored key values are rounded to this step (curve units): shorter YAML, far below every tolerance.</summary>
        [Tooltip("Key values are rounded to this step before writing: about 5 % smaller files, 100x below the tightest tolerance.")]
        public const float ValueQuantum = 1e-5f;

        /// <summary>Stored key slopes are rounded to this step (curve units per second).</summary>
        [Tooltip("Key slopes are rounded to this step (per second). Over a one-second segment the curve moves at most a few 1e-5.")]
        public const float SlopeQuantum = 1e-4f;

        /// <summary>
        /// Rounds to a multiple of <paramref name="quantum"/> (a power of ten) as the float nearest
        /// that decimal, so the YAML writes it short ("0.01472", not "0.014719999").
        /// </summary>
        public static float Quantise(float v, float quantum)
        {
            int decimals = Mathf.Clamp(Mathf.RoundToInt(-Mathf.Log10(quantum)), 0, 15);
            return (float)System.Math.Round((double)v, decimals, System.MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// The body (RootT, RootQ) moves every bone, so its share of the 2 mm per-bone budget is
        /// halved: at 1 mm and 0.1°, a toe 0.55 m from the body could already drift 1.96 mm before
        /// any muscle error (MC_FlatStare's toe reached 2.02 mm).
        /// </summary>
        [Tooltip("Body (RootT/RootQ) tolerances as a fraction of the position and rotation tolerances. The body moves every bone, so it gets half.")]
        public const float BodyShare = 0.5f;

        /// <summary>Body (RootT) and IK goal (LeftFootT/RightFootT) positions: tolerance in metres.</summary>
        [Tooltip("Body and IK goal positions, metres (converted to the clip's human-scale units at bake time).")]
        public const float PositionToleranceMetres = 0.001f;

        /// <summary>Body (RootQ) and IK goal (…FootQ) rotations: tolerance in degrees.</summary>
        [Tooltip("Body and IK goal rotations, degrees. Applied per quaternion component as θ/4 radians, which bounds the angle error.")]
        public const float RotationToleranceDegrees = 0.1f;

        /// <summary>Per-component quaternion tolerance that keeps the rotation within <see cref="RotationToleranceDegrees"/>.</summary>
        public static float QuaternionComponentTolerance => RotationToleranceDegrees * Mathf.Deg2Rad / 4f;

        /// <summary>Position tolerance in a clip's normalised (human scale) units.</summary>
        public static float PositionTolerance(float humanScale) => PositionToleranceMetres / Mathf.Max(humanScale, 1e-3f);
    }
}
