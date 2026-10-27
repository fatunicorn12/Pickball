namespace Pickleball.Sandbox.Movement
{
    /// <summary>
    /// Which jump a <see cref="MoveKind.Jump"/> is. Written to the Animator's int
    /// <c>JumpKind</c> parameter — append only, as for <see cref="MoveKind"/>.
    /// </summary>
    public enum JumpKind
    {
        /// <summary>Not jumping.</summary>
        None = 0,
        /// <summary>First jump of the chain.</summary>
        Single = 1,
        /// <summary>Second, higher jump, chained off a quick re-jump on landing.</summary>
        Double = 2,
        /// <summary>Third, highest jump — a flip; needs running speed.</summary>
        Triple = 3,
        /// <summary>Jump out of a skid turn: tall, short, facing the new way.</summary>
        SideFlip = 4,
        /// <summary>Jump out of a belly slide: a low forward hop back onto the feet.</summary>
        Rollout = 5,
    }
}
