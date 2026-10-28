using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Short tint on this body's renderers when its <see cref="Health"/> takes a
    /// discrete hit. Continuous ticks (beam, well core) do not flash, so a long
    /// burn does not strobe. Uses a MaterialPropertyBlock built once, reuses the
    /// <see cref="FlashTimer"/> from the sword's hit flash, and sleeps (disabled)
    /// whenever idle, so a hit allocates nothing and costs nothing afterwards.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class HurtFlash : MonoBehaviour
    {
        [Tooltip("Tint applied to every renderer under this object while flashing.")]
        [SerializeField] private Color flashColor = new(1f, 0.25f, 0.2f, 1f);

        [Tooltip("Seconds the tint lasts. 0 = off.")]
        [Range(0f, 0.6f)] [SerializeField] private float flashSeconds = 0.18f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Health _health;
        Renderer[] _renderers;
        MaterialPropertyBlock _block;
        Combat.FlashTimer _timer;

        void Awake()
        {
            _health = GetComponent<Health>();
            _renderers = GetComponentsInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
            // Subscribed for the object's lifetime, even while this component sleeps.
            _health.Hurt += OnHurt;
            enabled = false;           // only ticks while a flash is running
        }

        void OnDestroy()
        {
            if (_health != null) _health.Hurt -= OnHurt;
        }

        void OnHurt(float amount, Vector3 at)
        {
            if (flashSeconds <= 0f || !_health.LastHitDiscrete) return;
            _timer.Trigger(flashSeconds);
            Apply();
            enabled = true;
        }

        void Update()
        {
            if (_timer.Tick(Time.unscaledDeltaTime)) return;
            Clear();
            enabled = false;
        }

        void Apply()
        {
            _block.SetColor(BaseColorId, flashColor);
            _block.SetColor(ColorId, flashColor);
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].SetPropertyBlock(_block);
        }

        void Clear()
        {
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].SetPropertyBlock(null);
        }
    }
}
