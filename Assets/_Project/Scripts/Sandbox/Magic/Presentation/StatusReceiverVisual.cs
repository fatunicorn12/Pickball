using UnityEngine;

namespace Pickleball.Sandbox.Magic.Presentation
{
    /// <summary>
    /// Shows what a <see cref="StatusReceiver"/> is suffering: while slowed, a
    /// pale frost shell over the body, ice crusting up from the feet and cold
    /// motes, all stronger the harder the slow; while staggered, three stars
    /// circling the head. Sized from the body's collider once, when enabled.
    ///
    /// Presentation only: it polls the receiver every frame (a target can be
    /// slowed by several zones at once, and a poll can never miss an edge) and
    /// eases in and out so a target stepping across the rim does not flicker.
    /// Lives on a child of the target, built by Setup Magic.
    /// </summary>
    public class StatusReceiverVisual : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Source")]
        [Tooltip("The receiver shown. Found on a parent when empty.")]
        [SerializeField] private StatusReceiver receiver;
        [Tooltip("The body to wrap. Found on the receiver's object (or its children) when empty.")]
        [SerializeField] private Collider body;

        [Header("Frost")]
        [Tooltip("Additive shell (a sphere) scaled over the body's bounds.")]
        [SerializeField] private Renderer frostShell;
        [Tooltip("Parent of the ice crust crystals at the feet; scaled in with the slow.")]
        [SerializeField] private Transform iceCrust;
        [Tooltip("Optional looping cold motes round the body.")]
        [SerializeField] private ParticleSystem frostMotes;

        [Header("Stagger")]
        [Tooltip("Pivot of the stars over the head; spun and shown while staggered.")]
        [SerializeField] private Transform stars;

        [Header("Look")]
        [Tooltip("Frost tint; alpha is the shell's opacity at full strength.")]
        [SerializeField] private Color frostColour = new(0.6f, 0.9f, 1f, 0.45f);
        [Tooltip("HDR brightness multiplier on the shell (x).")]
        [Min(0f)] [SerializeField] private float intensity = 1.6f;
        [Tooltip("Seconds the frost takes to come and go.")]
        [Min(0.01f)] [SerializeField] private float easeSeconds = 0.2f;
        [Tooltip("Star orbit speed, in degrees per second.")]
        [SerializeField] private float starSpinDegreesPerSecond = 300f;
        [Tooltip("Motes per second at full frost.")]
        [Min(0f)] [SerializeField] private float moteRate = 24f;

        MaterialPropertyBlock _block;
        Vector3 _crustScale = Vector3.one;
        float _frost, _starShow;

        void OnEnable()
        {
            _block = new MaterialPropertyBlock();
            if (receiver == null) receiver = GetComponentInParent<StatusReceiver>();
            if (body == null && receiver != null)
            {
                body = receiver.GetComponent<Collider>();
                if (body == null) body = receiver.GetComponentInChildren<Collider>();
            }
            if (iceCrust != null && iceCrust.localScale.sqrMagnitude > 1e-6f) _crustScale = iceCrust.localScale;
            _frost = 0f;
            _starShow = 0f;
            Apply(0f);
        }

        void LateUpdate()
        {
            if (receiver == null) return;
            float dt = Time.deltaTime;

            // Strength follows how hard the slow is: 30% speed is a heavy frost.
            float goal = receiver.IsSlowed ? Mathf.Lerp(0.55f, 1f, 1f - receiver.SpeedMultiplier) : 0f;
            _frost = Mathf.MoveTowards(_frost, goal, dt / easeSeconds);
            _starShow = Mathf.MoveTowards(_starShow, receiver.IsStaggered ? 1f : 0f, dt / easeSeconds);
            Apply(dt);
        }

        void Apply(float dt)
        {
            Bounds bounds = body != null && body.enabled
                ? body.bounds
                : new Bounds(transform.position + Vector3.up, Vector3.one * 1.6f);

            if (frostShell != null)
            {
                bool on = _frost > 0.01f;
                frostShell.enabled = on;
                if (on)
                {
                    frostShell.transform.position = bounds.center;
                    frostShell.transform.localScale = bounds.size * 1.12f;
                    _block.SetColor(BaseColorId, new Color(frostColour.r * intensity, frostColour.g * intensity,
                                                           frostColour.b * intensity, frostColour.a * _frost));
                    frostShell.SetPropertyBlock(_block);
                }
            }
            if (iceCrust != null)
            {
                bool on = _frost > 0.01f;
                if (iceCrust.gameObject.activeSelf != on) iceCrust.gameObject.SetActive(on);
                iceCrust.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                iceCrust.localScale = _crustScale * _frost;
            }
            if (frostMotes != null)
            {
                var emission = frostMotes.emission;
                emission.rateOverTime = moteRate * _frost;
                if (_frost > 0.01f && !frostMotes.isPlaying) frostMotes.Play(true);
                frostMotes.transform.position = bounds.center;
            }
            if (stars != null)
            {
                bool on = _starShow > 0.01f;
                if (stars.gameObject.activeSelf != on) stars.gameObject.SetActive(on);
                if (on)
                {
                    stars.position = new Vector3(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z);
                    stars.localScale = Vector3.one * _starShow;
                    stars.Rotate(Vector3.up, starSpinDegreesPerSecond * dt, Space.World);
                }
            }
        }
    }
}
