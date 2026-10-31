using System.Collections.Generic;
using UnityEngine;
using M = Pickleball.Cutscene.Editor.Actors.HumanMuscles;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Whole-body building blocks for the hand-keyed clips, shared by MC's and Birdy's specs, with
    /// the sign conventions measured on the chibi avatar baked in (see <see cref="HumanMuscles"/>):
    /// head turn + is to the character's LEFT, head tilt + to the RIGHT, spine twist + to the
    /// RIGHT, arm front-back − is FORWARD.
    /// </summary>
    /// <remarks>
    /// Unity's body rotation is the centre-of-mass frame, so bending the spine alone swings the
    /// legs the other way. <see cref="Bend"/> adds the matching body pitch (half the bend) so the
    /// hips and legs stay put and only the upper body folds.
    /// </remarks>
    public static class PoseKit
    {
        /// <summary>Degrees of total spine bend per unit of <see cref="HumanMuscles.Torso"/> (40+40+20).</summary>
        public const float BendDegreesPerUnit = 100f;

        /// <summary>
        /// Share of the spine bend the body frame is counter-pitched by so the legs stay vertical
        /// (measured: Torso(0.5) alone swung the legs about 28° back).
        /// </summary>
        public const float BendCompensation = 0.55f;

        /// <summary>A key at a time (shorthand).</summary>
        public static PoseKey K(float t) => new PoseKey(t);

        /// <summary>Bend the torso forward (+) or back (−) with the legs kept still.</summary>
        public static PoseKey Bend(this PoseKey k, float frontBack, float twist = 0f)
        {
            k.S(M.Torso(frontBack, 0f, twist));
            k.Turn = new Vector3(k.Turn.x - frontBack * BendDegreesPerUnit * BendCompensation, k.Turn.y, k.Turn.z);
            return k;
        }

        /// <summary>Both arms hanging relaxed at the sides.</summary>
        public static PoseKey ArmsDown(this PoseKey k, float spread = 0f)
        {
            k.S(M.Arm(true, -0.45f + spread, 0.1f, 0.85f));
            k.S(M.Arm(false, -0.45f + spread, 0.1f, 0.85f));
            return k;
        }

        /// <summary>Both legs straight under the body (standing).</summary>
        public static PoseKey Stand(this PoseKey k, float spread = 0.05f)
        {
            k.S(M.Leg(true, 0.6f, spread, 1f));
            k.S(M.Leg(false, 0.6f, spread, 1f));
            return k;
        }

        /// <summary>Knees bent into a crouch (0 = straight, 1 = deep).</summary>
        public static PoseKey Crouch(this PoseKey k, float depth)
        {
            float thigh = Mathf.Lerp(0.6f, -0.2f, depth);
            float knee = Mathf.Lerp(1f, -0.1f, depth);
            k.S(M.Leg(true, thigh, 0.1f, knee, depth * 0.3f));
            k.S(M.Leg(false, thigh, 0.1f, knee, depth * 0.3f));
            return k;
        }

        /// <summary>Where the head looks: nod (+up), turn (+left), tilt (+right).</summary>
        public static PoseKey Look(this PoseKey k, float nod, float turn, float tilt = 0f)
        {
            return k.S(M.Look(nod, turn, tilt));
        }

        /// <summary>Fists on both hands (0 relaxed .. 1 clenched).</summary>
        public static PoseKey Fists(this PoseKey k, float closed = 1f)
        {
            return k.S(M.Fist(true, closed)).S(M.Fist(false, closed));
        }

        /// <summary>Lying on the back, head toward the actor's +Z, face up.</summary>
        public static Quaternion OnBack => Quaternion.LookRotation(Vector3.up, Vector3.forward);

        /// <summary>Upright, facing the actor's −Z (where a body lying head-to-+Z faces once it sits up).</summary>
        public static Quaternion FacingBack => Quaternion.LookRotation(Vector3.back, Vector3.up);

        /// <summary>Concatenate muscle lists.</summary>
        public static List<KeyValuePair<string, float>> Join(params List<KeyValuePair<string, float>>[] lists)
        {
            var all = new List<KeyValuePair<string, float>>();
            foreach (var l in lists) all.AddRange(l);
            return all;
        }
    }
}
