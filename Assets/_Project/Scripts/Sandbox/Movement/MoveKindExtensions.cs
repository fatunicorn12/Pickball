namespace Pickleball.Sandbox.Movement
{
    /// <summary>Helpers that classify moves, so listeners never hard-code lists.</summary>
    public static class MoveKindExtensions
    {
        /// <summary>True for the moves that happen with the feet (or belly) on the ground.</summary>
        public static bool IsGrounded(this MoveKind kind) => kind switch
        {
            MoveKind.Ground or MoveKind.Sprint or MoveKind.RunStop or MoveKind.Crouch
                or MoveKind.SkidTurn or MoveKind.BellySlide or MoveKind.Skate => true,
            _ => false,
        };

        /// <summary>True for moves that own the CharacterController outright, during
        /// which attacks are refused (the old <c>ChibiLocomotion.IsBusy</c>).</summary>
        public static bool IsExclusive(this MoveKind kind)
            => kind is MoveKind.Dash or MoveKind.Tether or MoveKind.Skate;
    }
}
