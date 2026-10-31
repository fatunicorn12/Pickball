using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// Looks up a set marker's position and yaw by id. The scene's
    /// <see cref="CutsceneCameraContext"/> implements it over its wired markers; tests implement it
    /// over a dictionary.
    /// </summary>
    public interface IMarkerSource
    {
        /// <summary>False when no marker has this id.</summary>
        bool TryGetMarker(string id, out Vector3 position, out float yaw);
    }
}
