using UnityEngine;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// The stand-in camera used before the cameras workstream's shots exist: a front three-quarter
    /// view of the shot's subject at the shot's distance, slightly above, aimed at the subject. It
    /// only has to make a placeholder build watchable and reviewable, not match the shot list.
    /// </summary>
    public static class PlaceholderFraming
    {
        /// <summary>Degrees off the subject's forward the camera stands (front three-quarter).</summary>
        public const float SideAngle = 25f;

        /// <summary>Camera height above the subject as a share of the distance.</summary>
        public const float Lift = 0.12f;

        /// <summary>The camera pose for a subject at <paramref name="subject"/> facing <paramref name="subjectYaw"/>.</summary>
        public static Pose Solve(Vector3 subject, float subjectYaw, float distance)
        {
            distance = Mathf.Max(0.05f, distance);
            Vector3 dir = Quaternion.Euler(0f, subjectYaw + SideAngle, 0f) * Vector3.forward;
            Vector3 position = subject + dir * distance + Vector3.up * (Lift * distance);
            Vector3 look = subject - position;
            return new Pose(position, look.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity);
        }
    }
}
