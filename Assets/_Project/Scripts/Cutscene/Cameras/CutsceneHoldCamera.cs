using Unity.Cinemachine;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// A freeze-frame camera for the moments no Cinemachine track drives the brain: the frame a
    /// timeline ends before the next starts or the hand-back runs. Without it the brain falls back to
    /// its highest-priority live camera, which is an arbitrary shot (seen in Play mode: S4_SH01's
    /// track camera flashed for a frame at the end of scene 2). This camera has the top priority, so
    /// the brain picks it whenever the timeline lets go, and it shows the last pose and lens the
    /// brain output (<see cref="CutsceneCameraHandback"/> feeds it each frame). A track's override
    /// always wins over priority, so it never shows during a shot.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class CutsceneHoldCamera : CinemachineExtension
    {
        Pose _pose;
        float _fov;
        float _near;
        bool _has;

        /// <summary>Records the pose and lens to hold.</summary>
        public void Hold(Pose pose, float fov, float near)
        {
            _pose = pose;
            _fov = fov;
            _near = near;
            _has = true;
        }

        /// <inheritdoc />
        public override void PrePipelineMutateCameraStateCallback(
            CinemachineVirtualCameraBase vcam, ref CameraState state, float deltaTime)
        {
            if (!_has) return;
            state.RawPosition = _pose.position;
            state.Lens.FieldOfView = _fov;
            if (_near > 0f) state.Lens.NearClipPlane = _near;
        }

        /// <inheritdoc />
        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (_has && stage == CinemachineCore.Stage.Aim) state.RawOrientation = _pose.rotation;
        }
    }
}
