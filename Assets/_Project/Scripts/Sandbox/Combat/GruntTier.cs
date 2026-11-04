namespace Pickleball.Sandbox.Combat
{
    /// <summary>How hard an attack grunt sounds. <see cref="Finisher"/> is reserved
    /// for a chain's finisher, whatever the chain's length; see <see cref="GruntLadder"/>.</summary>
    public enum GruntTier
    {
        Light,
        Medium,
        Heavy,
        Finisher,
    }
}
