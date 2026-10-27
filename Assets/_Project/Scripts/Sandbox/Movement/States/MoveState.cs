namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// One move of the player's movement state machine.
    ///
    /// The motor runs each frame as: <see cref="Next"/> on the current state
    /// (and on whatever it hands over to, a few hops at most), then
    /// <see cref="Tick"/> on the state that is current at the end. Deciding and
    /// integrating are kept apart so a state that changes on a button press —
    /// Ground to Jump — has the new state move the character that same frame,
    /// with no frame of the old behaviour.
    ///
    /// States hold only their own per-move data. Everything shared (speed,
    /// heading, vertical velocity, input, the sprint ramp, the jump chain) lives
    /// on the motor, so a move can hand over without either side knowing the
    /// other's internals. Plain C#: the motor rebuilds them in OnEnable, which
    /// keeps a script reload in Play mode safe.
    /// </summary>
    public abstract class MoveState
    {
        /// <summary>The move this state implements.</summary>
        public abstract MoveKind Kind { get; }

        /// <summary>The dash may interrupt this move.</summary>
        public virtual bool AllowsDash => true;
        /// <summary>The tether may latch from this move.</summary>
        public virtual bool AllowsTether => true;
        /// <summary>The skateboard may be mounted from this move (grounded moves only).</summary>
        public virtual bool AllowsMount => false;
        /// <summary>The Gravity Anchor may be switched on during this move.</summary>
        public virtual bool AllowsAnchor => true;

        /// <summary>Called once when the move begins. <paramref name="from"/> is the move it replaced.</summary>
        public virtual void Enter(MovementMotor m, MoveKind from) { }

        /// <summary>Called once when the move ends, before the next one's Enter.</summary>
        public virtual void Exit(MovementMotor m, MoveKind to) { }

        /// <summary>The move that should be current now: <see cref="Kind"/> to stay.
        /// Decisions only — no integration here.</summary>
        public abstract MoveKind Next(MovementMotor m);

        /// <summary>Integrates one frame of this move.</summary>
        public abstract void Tick(MovementMotor m, float dt);
    }
}
