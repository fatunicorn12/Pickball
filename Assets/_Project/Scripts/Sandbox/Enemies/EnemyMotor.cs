using UnityEngine;

namespace Pickleball.Sandbox.Enemies
{
    /// <summary>
    /// Moves an enemy's body: owns the CharacterController, so it slides along walls,
    /// climbs small steps and falls off ledges without a Rigidbody, as the player does.
    ///
    /// A juggle moves the root itself. While <c>carried</c> the height the juggle
    /// reports is applied directly and gravity is off, so the collider, lock-on and
    /// hit points all simply follow the body and <see cref="Health.BodyOffset"/> stays
    /// zero. (The training dummy has to fake this because its root must stay planted;
    /// an enemy's need not.)
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class EnemyMotor : MonoBehaviour
    {
        /// <summary>Downward speed held while grounded, m/s, so it stays stuck to a
        /// slope it is running down instead of skipping off it.</summary>
        const float GroundStick = 2f;

        CharacterController _controller;
        float _fallSpeed;

        CharacterController Controller =>
            _controller != null ? _controller : (_controller = GetComponent<CharacterController>());

        /// <summary>Horizontal speed actually achieved last step, m/s: zero when it is
        /// running into a wall. For presentation.</summary>
        public float PlanarSpeed { get; private set; }

        /// <summary>
        /// One frame of movement.
        /// </summary>
        /// <param name="velocity">Horizontal velocity wanted, m/s.</param>
        /// <param name="carried">True while a juggle is flying the body.</param>
        /// <param name="carriedRise">Metres the juggle moved it this frame.</param>
        /// <param name="gravity">Falling gravity, m/s².</param>
        /// <param name="maxFallSpeed">Terminal fall speed, m/s.</param>
        /// <param name="dt">Frame time, s.</param>
        public void Step(Vector3 velocity, bool carried, float carriedRise, float gravity,
                         float maxFallSpeed, float dt)
        {
            var controller = Controller;
            if (dt <= 0f || !controller.enabled)
            {
                PlanarSpeed = 0f;
                return;
            }

            float rise;
            if (carried)
            {
                _fallSpeed = 0f;
                rise = carriedRise;
            }
            else
            {
                _fallSpeed = controller.isGrounded
                    ? GroundStick
                    : Mathf.Min(_fallSpeed + gravity * dt, maxFallSpeed);
                rise = -_fallSpeed * dt;
            }

            Vector3 before = transform.position;
            controller.Move(new Vector3(velocity.x * dt, rise, velocity.z * dt));
            Vector3 moved = transform.position - before;
            PlanarSpeed = new Vector2(moved.x, moved.z).magnitude / dt;
        }

        /// <summary>Turn toward a horizontal direction, frame-rate independently.</summary>
        /// <param name="direction">Horizontal direction; ignored when zero.</param>
        /// <param name="rate">Smoothing rate per second.</param>
        /// <param name="dt">Frame time, s.</param>
        public void Face(Vector3 direction, float rate, float dt)
        {
            if (direction.sqrMagnitude < 1e-6f) return;
            var wanted = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-rate * dt));
        }

        /// <summary>Stop being solid: a defeated body must not block the player.</summary>
        public void BecomeIntangible()
        {
            PlanarSpeed = 0f;
            Controller.enabled = false;
        }
    }
}
