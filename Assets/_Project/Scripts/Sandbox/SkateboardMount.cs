using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The board the player rides. Lives as a child of the character, hidden
    /// until mounted, so it inherits movement and turning for free — no
    /// follow logic and no chance of the board drifting away from the feet.
    ///
    /// The wheels spin from the character's actual travel speed rather than on
    /// a timer, so they stay honest when he speeds up, slows or stops.
    /// </summary>
    public class SkateboardMount : MonoBehaviour
    {
        [Tooltip("The visible board. Toggled with the mount.")]
        [SerializeField] private GameObject boardVisual;

        [Tooltip("Wheel transforms, spun about their local X while riding. " +
                 "Each must be pivoted at its own axle or it will orbit the deck.")]
        [SerializeField] private Transform[] wheels;

        [Tooltip("Wheel radius in metres; sets how fast they spin per metre travelled.")]
        [SerializeField] private float wheelRadius = 0.035f;

        [Tooltip("How quickly the board tilts into turns.")]
        [SerializeField] private float leanSmoothing = 6f;
        [SerializeField] private float maxLeanDegrees = 14f;

        CharacterController _cc;
        float _lean;

        public bool IsRidden { get; private set; }

        void Awake()
        {
            _cc = GetComponentInParent<CharacterController>();
            SetRidden(false);
        }

        public void SetRidden(bool ridden)
        {
            IsRidden = ridden;
            if (boardVisual != null) boardVisual.SetActive(ridden);
            if (!ridden) _lean = 0f;
        }

        void LateUpdate()
        {
            if (!IsRidden || _cc == null) return;

            Vector3 v = _cc.velocity;
            v.y = 0f;
            float speed = v.magnitude;

            if (wheels != null && wheelRadius > 0.0001f)
            {
                // arc length = radius * angle, so degrees = distance / radius in deg
                float degrees = speed * Time.deltaTime / wheelRadius * Mathf.Rad2Deg;
                foreach (var w in wheels)
                    if (w != null) w.Rotate(Vector3.right, degrees, Space.Self);
            }

            // Lean into the turn, judged by how far the travel direction sits
            // off the board's forward.
            float lean = 0f;
            if (speed > 0.2f)
            {
                float side = Vector3.Dot(transform.right, v.normalized);
                lean = -side * maxLeanDegrees;
            }
            _lean = Mathf.Lerp(_lean, lean, 1f - Mathf.Exp(-leanSmoothing * Time.deltaTime));

            if (boardVisual != null)
                boardVisual.transform.localRotation = Quaternion.Euler(0f, 0f, _lean);
        }
    }
}
