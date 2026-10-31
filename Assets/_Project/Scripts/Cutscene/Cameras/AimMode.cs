namespace Pickleball.Cutscene.Cameras
{
    /// <summary>How a shot's camera rotation is produced.</summary>
    public enum AimMode
    {
        /// <summary>CinemachineRotationComposer on the aim keys (the shot list's "aim X at (sx, sy)").</summary>
        Composer = 0,
        /// <summary>Straight down at the first aim key's target, the frame's up along MC's body axis
        /// (head at the top). The composer can't look straight down.</summary>
        TopDown = 1,
        /// <summary>The gameplay orbit camera's own rotation: Euler(pitch, yaw, 0).</summary>
        Gameplay = 2,
    }
}
