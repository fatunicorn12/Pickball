namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>Where a finished dash hands control: back to the ground moves when
    /// he is on, or right at, the floor; otherwise the fall.</summary>
    public static class DashExitRule
    {
        public static bool ReturnsToGround(bool grounded, bool floorWithinProbe)
            => grounded || floorWithinProbe;
    }
}
