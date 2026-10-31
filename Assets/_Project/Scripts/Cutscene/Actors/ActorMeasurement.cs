using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// The size rules a cutscene slot reports (<see cref="CutsceneActor.Height"/>,
    /// <see cref="CutsceneActor.HeadBox"/>, the anchor points), as pure functions. The setup menu
    /// feeds them rest-pose numbers read from the skinned meshes' bind poses, so the result is
    /// the same whatever pose the scene happens to hold; keeping the rules here means the shot
    /// list's definitions are tested in one place.
    /// </summary>
    public static class ActorMeasurement
    {
        /// <summary>Smallest size reported, metres (the slot clamps to this too).</summary>
        public const float MinSize = 0.01f;

        /// <summary>Shot list H / h: top of the rest-pose bounds minus the Feet anchor's height.</summary>
        public static float Height(float topY, float feetY)
        {
            return Mathf.Max(MinSize, topY - feetY);
        }

        /// <summary>
        /// Shot list HB: twice the distance from the Head anchor (head centre) up to the top of
        /// the model. Birdy is mostly head, so HB is most of his height.
        /// </summary>
        public static float HeadBox(float topY, float headY)
        {
            return Mathf.Max(MinSize, 2f * (topY - headY));
        }

        /// <summary>
        /// The Feet anchor: the point between the two feet, dropped to the ground the actor
        /// stands on (its root's height).
        /// </summary>
        public static Vector3 FeetPoint(Vector3 leftFoot, Vector3 rightFoot, float groundY)
        {
            Vector3 mid = (leftFoot + rightFoot) * 0.5f;
            mid.y = groundY;
            return mid;
        }

        /// <summary>
        /// Height of the eye line, from the head bone's height above the ground and a per-model
        /// ratio (eye height / head-bone height, measured once per model; ~1.1 for a chibi).
        /// </summary>
        public static float EyeHeight(float groundY, float headBoneY, float eyeToHeadBoneRatio)
        {
            return groundY + (headBoneY - groundY) * eyeToHeadBoneRatio;
        }

        /// <summary>Centre of an axis-aligned box given its two corners.</summary>
        public static Vector3 BoxCentre(Vector3 min, Vector3 max)
        {
            return (min + max) * 0.5f;
        }
    }
}
