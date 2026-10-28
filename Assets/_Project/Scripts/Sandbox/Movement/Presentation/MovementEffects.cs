using UnityEngine;

namespace Pickleball.Sandbox.Movement.Presentation
{
    /// <summary>
    /// The non-Animator side of movement presentation: the feather's cloud puff,
    /// the dash ghosts and arc, the tether rope and reticle, and the board under
    /// his feet. All driven from <see cref="IMovementState.MoveChanged"/> — the
    /// motor plays nothing itself.
    ///
    /// Every reference is optional: a missing effect is simply not played.
    /// </summary>
    public class MovementEffects : MonoBehaviour
    {
        [SerializeField] private MovementMotor motor;
        [Tooltip("Puff spawned at the feet on the feather's air jump.")]
        [SerializeField] private ParticleSystem doubleJumpEffect;
        [Tooltip("Dash ghosts, arc and FOV punch.")]
        [SerializeField] private DashEffects dashEffects;
        [Tooltip("Draws the rope and the lock-in reticle while tethered.")]
        [SerializeField] private TetherSystem tether;
        [Tooltip("Shows the board while riding. Found in children if empty.")]
        [SerializeField] private SkateboardMount board;

        void Awake()
        {
            if (motor == null) motor = GetComponentInParent<MovementMotor>();
            if (board == null) board = GetComponentInChildren<SkateboardMount>(true);
        }

        void OnEnable()
        {
            if (motor == null) return;
            motor.MoveChanged += OnMoveChanged;
            // Resync the persistent effects with whatever move is current. After a
            // script reload the motor may already have handed a tether or skate
            // over (QA MV8) before this listener re-subscribed, and the change
            // event would be missed. Both calls are idempotent.
            if (board != null) board.SetRidden(motor.Current == MoveKind.Skate);
            if (tether != null && motor.Current != MoveKind.Tether) tether.Detach();
        }

        void OnDisable()
        {
            if (motor != null) motor.MoveChanged -= OnMoveChanged;
        }

        void OnMoveChanged(MoveKind from, MoveKind to)
        {
            if (from == MoveKind.Skate && board != null) board.SetRidden(false);
            if (from == MoveKind.Tether && tether != null) tether.Detach();

            switch (to)
            {
                case MoveKind.AirJump:
                    if (doubleJumpEffect != null)
                    {
                        doubleJumpEffect.transform.position = motor.transform.position;
                        doubleJumpEffect.Play();
                    }
                    break;
                case MoveKind.Dash:
                    if (dashEffects != null) dashEffects.Play(motor.DashDirection, motor.Config.DashDuration);
                    break;
                case MoveKind.Tether:
                    if (tether != null && motor.TetherAnchor != null) tether.Attach(motor.TetherAnchor);
                    break;
                case MoveKind.Skate:
                    if (board != null) board.SetRidden(true);
                    break;
            }
        }
    }
}
