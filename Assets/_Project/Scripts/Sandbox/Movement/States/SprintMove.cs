namespace Pickleball.Sandbox.Movement.States
{
    /// <summary>
    /// Running at sprint speed. Identical rules to <see cref="GroundMove"/> — the
    /// ramp, top speed and turn rate all come from the motor's sprint ramp — but
    /// its own kind, so anything listening can react to "now sprinting" (the
    /// sprint clip, a dust trail, a sprint slash) without polling speed.
    ///
    /// <see cref="MovementMotor.GroundKind"/> moves between the two: entered once
    /// the ramp's delay has elapsed, left when the ramp resets (stick released
    /// for longer than the grace, crouch, skateboard).
    /// </summary>
    public sealed class SprintMove : GroundMove
    {
        public override MoveKind Kind => MoveKind.Sprint;
    }
}
