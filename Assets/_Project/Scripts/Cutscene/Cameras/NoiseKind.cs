namespace Pickleball.Cutscene.Cameras
{
    /// <summary>Which Perlin noise profile a shot's CinemachineBasicMultiChannelPerlin uses.</summary>
    public enum NoiseKind
    {
        /// <summary>No noise component.</summary>
        None = 0,
        /// <summary>Gentle handheld drift (Cinemachine's Handheld_normal_mild).</summary>
        Handheld = 1,
        /// <summary>6D shake for impacts (Cinemachine's 6D Shake).</summary>
        Shake6D = 2,
    }
}
