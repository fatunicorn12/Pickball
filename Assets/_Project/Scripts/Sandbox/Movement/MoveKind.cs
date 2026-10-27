namespace Pickleball.Sandbox.Movement
{
    /// <summary>
    /// Every move the player's movement state machine can be in. Exactly one is
    /// current at a time; <see cref="IMovementState.MoveChanged"/> fires on every
    /// change.
    ///
    /// The numeric values are written to the Animator's int <c>Move</c>
    /// parameter, so <b>append new kinds at the end</b> — renumbering silently
    /// re-targets every transition the setup menu built.
    /// </summary>
    public enum MoveKind
    {
        /// <summary>Standing, walking or running on the ground.</summary>
        Ground = 0,
        /// <summary>On the ground at sprint speed (reached after about a second of running).</summary>
        Sprint = 1,
        /// <summary>Released the stick at speed: the planted run-to-stop.</summary>
        RunStop = 2,
        /// <summary>Crouched (idle or crouch-walk).</summary>
        Crouch = 3,
        /// <summary>The snap-back redirect: a hard reversal at speed that keeps momentum.</summary>
        SkidTurn = 4,
        /// <summary>Any jump off the ground — see <see cref="IMovementState.CurrentJump"/> for which.</summary>
        Jump = 5,
        /// <summary>Airborne without a jump in progress (walked off a ledge, past a jump's arc).</summary>
        Fall = 6,
        /// <summary>The feather's mid-air jump.</summary>
        AirJump = 7,
        /// <summary>Sunshine's forward dive, airborne.</summary>
        Dive = 8,
        /// <summary>Sliding on the belly after a dive lands.</summary>
        BellySlide = 9,
        /// <summary>The corkscrew spin jump and its slow descent.</summary>
        SpinJump = 10,
        /// <summary>The protractor's fixed 45° dash.</summary>
        Dash = 11,
        /// <summary>Swinging on the tether.</summary>
        Tether = 12,
        /// <summary>Riding the skateboard.</summary>
        Skate = 13,
    }
}
