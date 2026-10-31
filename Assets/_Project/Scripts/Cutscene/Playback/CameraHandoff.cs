using System;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// Takes the main camera from gameplay for a cutscene and gives it back, the way
    /// <c>DialogueCamera</c> does.
    /// <list type="bullet">
    /// <item><b>Take:</b> disables <see cref="SandboxOrbitCamera"/> (so nothing fights the timeline
    /// for the transform), records the field of view, and enables every behaviour in
    /// <c>cutsceneOnly</c>. The cameras workstream puts its CinemachineBrain there from its setup
    /// menu, so it is on only during cutscenes; this class never references Cinemachine.</item>
    /// <item><b>Release:</b> disables <c>cutsceneOnly</c>, puts the field of view back, asks the
    /// orbit camera to recenter behind the player, and re-enables it on the first
    /// <see cref="Tick"/> whose scaled delta time is above zero: at dt 0 the orbit camera's
    /// SmoothDampAngle divides by zero and a NaN yaw never recovers.</item>
    /// </list>
    /// Serializable plain class owned by <see cref="CutscenePlayer"/>; the virtual hooks are the
    /// extension point for a camera system that needs more than enabling behaviours.
    /// </summary>
    [Serializable]
    public class CameraHandoff
    {
        [Tooltip("The main camera the cutscene drives.")]
        [SerializeField] private Camera camera;

        [Tooltip("Gameplay camera rig, disabled for the length of a cutscene.")]
        [SerializeField] private SandboxOrbitCamera orbitCamera;

        [Tooltip("The player root; the orbit camera recenters behind its forward on hand-back.")]
        [SerializeField] private Transform followTarget;

        [Tooltip("Behaviours enabled only while a cutscene plays (e.g. a CinemachineBrain). The " +
                 "cameras setup menu fills this.")]
        [SerializeField] private Behaviour[] cutsceneOnly = new Behaviour[0];

        bool _taken;
        bool _disabledOrbit;
        bool _orbitPending;
        float _fov;

        /// <summary>The main camera.</summary>
        public Camera Camera => camera;

        /// <summary>True between <see cref="Take"/> and <see cref="Release"/>.</summary>
        public bool IsTaken => _taken;

        /// <summary>True while the orbit camera waits for a frame with dt above zero.</summary>
        public bool OrbitPending => _orbitPending;

        /// <summary>Takes the camera. Idempotent.</summary>
        public virtual void Take()
        {
            if (_taken) return;
            _taken = true;
            if (orbitCamera != null && orbitCamera.enabled)
            {
                orbitCamera.enabled = false;
                _disabledOrbit = true;
            }
            _orbitPending = false;
            if (camera != null) _fov = camera.fieldOfView;
            SetCutsceneOnly(true);
        }

        /// <summary>Gives the camera back; the orbit camera returns on the next frame with dt above zero.</summary>
        public virtual void Release()
        {
            if (!_taken) return;
            _taken = false;
            SetCutsceneOnly(false);
            if (camera != null && _fov > 0f) camera.fieldOfView = _fov;
            if (_disabledOrbit && orbitCamera != null)
            {
                if (followTarget != null) orbitCamera.RecenterBehind(followTarget.forward);
                _orbitPending = true;
            }
        }

        /// <summary>One frame: re-enables the orbit camera once the scaled delta time is above zero.</summary>
        public void Tick(float deltaTime)
        {
            if (_orbitPending && deltaTime > 0f) EnableOrbit();
        }

        /// <summary>Release and hand the orbit camera back now (teardown, script reload).</summary>
        public void ForceRestore()
        {
            Release();
            EnableOrbit();
        }

        /// <summary>Called when <c>cutsceneOnly</c> behaviours switch; override to do more.</summary>
        protected virtual void SetCutsceneOnly(bool on)
        {
            if (cutsceneOnly == null) return;
            for (int i = 0; i < cutsceneOnly.Length; i++)
                if (cutsceneOnly[i] != null) cutsceneOnly[i].enabled = on;
        }

        void EnableOrbit()
        {
            _orbitPending = false;
            if (_disabledOrbit && orbitCamera != null)
            {
                orbitCamera.enabled = true;
                // The orbit camera never saw an Escape pressed while it was off; capture again to match.
                if (orbitCamera.CursorCaptured && Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            _disabledOrbit = false;
        }
    }
}
