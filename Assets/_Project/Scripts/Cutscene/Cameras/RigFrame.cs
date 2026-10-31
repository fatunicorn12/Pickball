namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// The axes an offset is written in. The shot list writes offsets "in character A's local
    /// frame: x = A's right, y = up, z = A's forward"; only the yaw is used, so a lying or tumbling
    /// body never tilts a camera. Two frames are not in the shot list's notation but are needed to
    /// make some of its shots hold together with the built blocking (see each shot's notes).
    /// </summary>
    public enum RigFrame
    {
        /// <summary>World axes: x east, y up, z north.</summary>
        World = 0,
        /// <summary>MC's yaw frame.</summary>
        MC = 1,
        /// <summary>Birdy's yaw frame.</summary>
        Birdy = 2,
        /// <summary>z = Pair.Side (the side the point's <see cref="SideRule"/> picks), x to its right.</summary>
        PairSide = 3,
        /// <summary>The yaw frame of the marker named by <see cref="RigPoint.frameMarker"/>.</summary>
        Marker = 4,
        /// <summary>z = the flattened direction from the point's basis toward the marker named by
        /// <see cref="RigPoint.frameMarker"/>. Used where MC turns during a shot but the camera
        /// must stay put relative to where he ends up looking (the black sun).</summary>
        Toward = 5,
        /// <summary>z = the flattened direction from MC.Feet to Birdy.Feet.</summary>
        TowardBirdy = 6,
    }
}
