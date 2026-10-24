using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Turns a quad to face the main camera every frame — halos and glows that
    /// would otherwise vanish edge-on when the orbit camera swings round.
    ///
    /// Copies the camera's rotation rather than looking at its position, so a
    /// row of billboards stays parallel instead of fanning toward the lens.
    /// </summary>
    public class FaceCamera : MonoBehaviour
    {
        Transform _camera;

        void LateUpdate()
        {
            if (_camera == null)
            {
                var main = Camera.main;
                if (main == null) return;
                _camera = main.transform;
            }
            transform.rotation = _camera.rotation;
        }
    }
}
