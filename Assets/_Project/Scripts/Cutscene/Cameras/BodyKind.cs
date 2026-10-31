namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// How a shot's camera position is produced. The shot list's camera types map onto these:
    /// Locked, push, pedestal and truck are <see cref="Keyed"/>; Crane is <see cref="Crane"/>;
    /// Orbit is Cinemachine's OrbitalFollow; Tracking is Cinemachine's Follow; the side-on scene-4
    /// dolly is Cinemachine's SplineDolly on Cam_Track_S4; the hand-off is <see cref="Gameplay"/>.
    /// </summary>
    public enum BodyKind
    {
        /// <summary>No Cinemachine body: the position is the keyed path, straight-line between keys.</summary>
        Keyed = 0,
        /// <summary>No Cinemachine body: a Catmull-Rom spline through the keys (anchor-relative
        /// knots re-resolved every frame), travelled with one ease over the keys' span.</summary>
        Crane = 1,
        /// <summary>CinemachineOrbitalFollow round Birdy.Head, its horizontal axis animated.</summary>
        Orbit = 2,
        /// <summary>CinemachineFollow on an anchor, offset in the anchor's yaw frame, with damping.</summary>
        Follow = 3,
        /// <summary>CinemachineSplineDolly on the Cam_Track_S4 spline, kept abeam MC.Mid.</summary>
        Track = 4,
        /// <summary>The gameplay orbit camera's resting pose behind MC (pose G), pushed in along its view axis.</summary>
        Gameplay = 5,
    }
}
