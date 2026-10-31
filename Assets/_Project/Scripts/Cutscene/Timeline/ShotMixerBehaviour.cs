using UnityEngine;
using UnityEngine.Playables;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Drives the bound Camera to the placeholder framing of the shot under the playhead, when the
    /// track says to (<see cref="ShotTrack.DrivePlaceholderCamera"/>). Once the cameras workstream
    /// owns the camera, it turns that off and this does nothing. No allocation per frame.
    /// </summary>
    public sealed class ShotMixerBehaviour : PlayableBehaviour
    {
        /// <summary>Whether to move the bound camera at all.</summary>
        public bool Drive;

        /// <inheritdoc />
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!Drive) return;
            var cam = playerData as Camera;
            if (cam == null) return;

            int count = playable.GetInputCount();
            for (int i = 0; i < count; i++)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                var input = (ScriptPlayable<ShotBehaviour>)playable.GetInput(i);
                var shot = input.GetBehaviour();
                if (shot.Subject == null) return;
                double duration = input.GetDuration();
                float t = duration > 1e-6 ? Mathf.Clamp01((float)(input.GetTime() / duration)) : 1f;
                Transform root = shot.SubjectRoot != null ? shot.SubjectRoot : shot.Subject;
                Pose pose = PlaceholderFraming.Solve(shot.Subject.position, ActorMarkMath.YawOf(root.rotation), shot.Distance);
                cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
                cam.fieldOfView = Mathf.Lerp(shot.LensStart, shot.LensEnd, t);
                return;
            }
        }
    }
}
