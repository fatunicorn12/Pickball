using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The protractor flash: a 45° arc struck at the launch point, lying in the
    /// vertical plane the dash travels through, with the angle read off from
    /// the ground line up to the dash vector.
    ///
    /// It is the mechanic annotating itself — the dash is *exactly* 45° by
    /// construction, so the diagram is telling the truth rather than decorating.
    /// Left in world space on purpose: it marks where the dash began and stays
    /// there while the character streaks away from it.
    /// </summary>
    public class DashArc : MonoBehaviour
    {
        [SerializeField] private MeshRenderer arcRenderer;
        [SerializeField] private float lifetime = 0.45f;
        [SerializeField] private float startScale = 0.6f;
        [SerializeField] private float endScale = 1.35f;
        [SerializeField] private Color tint = new(0.6f, 0.9f, 1f, 0.9f);
        [Tooltip("Lift off the ground slightly so it does not z-fight the floor.")]
        [SerializeField] private float groundClearance = 0.05f;

        Material _mat;
        float _age = -1f;

        void Awake()
        {
            if (arcRenderer == null) arcRenderer = GetComponentInChildren<MeshRenderer>();
            if (arcRenderer != null)
            {
                _mat = new Material(arcRenderer.sharedMaterial);
                arcRenderer.sharedMaterial = _mat;
            }
            SetVisible(false);
        }

        void SetVisible(bool on)
        {
            if (arcRenderer != null) arcRenderer.enabled = on;
        }

        public void Play(Vector3 origin, Vector3 direction)
        {
            var flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 1e-4f) flat = transform.forward;
            flat.Normalize();

            // Detach from the character so the diagram stays where the dash
            // started rather than being dragged along by it.
            transform.SetParent(null, true);
            transform.position = origin + Vector3.up * groundClearance;
            // Arc is authored in the XY plane; face it along the travel line.
            transform.rotation = Quaternion.LookRotation(Vector3.Cross(flat, Vector3.up), Vector3.up);

            _age = 0f;
            SetVisible(true);
        }

        void Update()
        {
            if (_age < 0f) return;
            _age += Time.deltaTime;
            float k = Mathf.Clamp01(_age / Mathf.Max(lifetime, 0.01f));

            transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, 1f - Mathf.Pow(1f - k, 3f));

            var c = tint;
            c.a *= 1f - k * k;                    // hold, then drop away
            if (_mat != null)
            {
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
                if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
            }

            if (k >= 1f)
            {
                _age = -1f;
                SetVisible(false);
            }
        }
    }
}
