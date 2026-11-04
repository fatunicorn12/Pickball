using Pickleball.Sandbox.Movement.Logic;
using UnityEngine;

namespace Pickleball.Sandbox.Movement.Presentation
{
    /// <summary>
    /// Shows the side somersault's window: sparks fly from the planted foot for
    /// exactly as long as Jump would give a side flip. That window is the whole
    /// of <see cref="MoveKind.SkidTurn"/>, so this follows
    /// <see cref="IMovementState.MoveChanged"/> rather than keeping a timer of its
    /// own that could drift from the move.
    ///
    /// The spot is taken once, as the skid starts, and then held still. The body
    /// swings 180° over its feet in the first few frames of a skid, so following a
    /// foot bone would whip the sparks round to the new side and they would stop
    /// marking where he braked.
    ///
    /// Emitted from code (the system's own rate is 0), because the system's
    /// transform follows the player between emissions and only the moment of
    /// emission should decide where a spark starts.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class SideFlipSparks : MonoBehaviour
    {
        [Tooltip("The move being watched. Found in the parents if empty.")]
        [SerializeField] private MovementMotor motor;
        [Tooltip("Where the spot goes and how many sparks come out of it.")]
        [SerializeField] private MovementAnimationConfig config;
        [Tooltip("The humanoid rig whose feet are measured. Found on the player if empty.")]
        [SerializeField] private Animator animator;

        ParticleSystem _sparks;
        Transform _leftFoot, _leftToes, _rightFoot, _rightToes;
        Vector3 _spot;
        Quaternion _spray;
        float _owed;
        bool _emitting;

        void OnEnable()
        {
            // Rebuilt here, not Awake: a script reload re-runs OnEnable only.
            _sparks = GetComponent<ParticleSystem>();
            if (motor == null) motor = GetComponentInParent<MovementMotor>();
            if (motor == null) return;
            if (animator == null) animator = motor.GetComponentInChildren<Animator>();
            FindFeet();
            motor.MoveChanged += OnMoveChanged;
        }

        void OnDisable()
        {
            if (motor != null) motor.MoveChanged -= OnMoveChanged;
            _emitting = false;
        }

        void FindFeet()
        {
            bool human = animator != null && animator.isHuman;
            _leftFoot = human ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
            _rightFoot = human ? animator.GetBoneTransform(HumanBodyBones.RightFoot) : null;
            _leftToes = human ? animator.GetBoneTransform(HumanBodyBones.LeftToes) : null;
            _rightToes = human ? animator.GetBoneTransform(HumanBodyBones.RightToes) : null;
        }

        void OnMoveChanged(MoveKind from, MoveKind to)
        {
            if (to == MoveKind.SkidTurn) Begin();
            else if (from == MoveKind.SkidTurn) _emitting = false;
        }

        /// <summary>The motor raises this before it moves him or the Animator poses
        /// him, so the feet are still where his last running frame left them.</summary>
        void Begin()
        {
            if (config == null || _sparks == null || _leftFoot == null || _rightFoot == null) return;

            // The skid has already read this as the direction it brakes along.
            Vector3 travel = motor.TravelDirection;
            Vector3 edge = FurtherFootEdge.Find(
                _leftFoot.position, Toes(_leftToes, _leftFoot),
                _rightFoot.position, Toes(_rightToes, _rightFoot),
                travel, config.SparkEdgeOutset);
            edge.y = motor.transform.position.y + config.SparkFloorLift;
            _spot = edge;

            // Thrown on the way he was going and up off the floor.
            float lift = config.SparkSprayAngle * Mathf.Deg2Rad;
            _spray = Quaternion.LookRotation(travel * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift));

            _owed = 0f;
            _emitting = true;
            // The burst goes out now, so a jump buffered into the skid still shows
            // sparks even though that window closes on this same frame.
            Emit(config.SparkBurst);
        }

        static Vector3 Toes(Transform toes, Transform foot) => (toes != null ? toes : foot).position;

        void LateUpdate()
        {
            if (!_emitting) return;
            _owed += config.SparkRate * Time.deltaTime;
            int count = (int)_owed;
            if (count <= 0) return;
            _owed -= count;
            Emit(count);
        }

        void Emit(int count)
        {
            transform.SetPositionAndRotation(_spot, _spray);
            _sparks.Emit(count);
        }
    }
}
