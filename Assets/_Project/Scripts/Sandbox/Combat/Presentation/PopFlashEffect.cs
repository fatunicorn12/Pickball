using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// A camera-facing glyph that pops out of a hit point and shrinks away —
    /// the "+" of + C. The glyph is whatever children the prefab gives it
    /// (two crossed bars for a plus), so the same component draws a "!" for
    /// Factorial or "∴" for Corollary later.
    /// </summary>
    public class PopFlashEffect : PooledEffect
    {
        [Tooltip("Seconds from appearing to gone. ~0.2 reads as a flash, not a sign.")]
        [Min(0.02f)] [SerializeField] private float lifetime = 0.22f;

        [Tooltip("Peak scale as a multiple of the cue size (×). The overshoot is " +
                 "what makes it pop rather than just appear.")]
        [Min(0f)] [SerializeField] private float overshoot = 1.25f;

        [Tooltip("Spin while visible, in degrees per second about the view axis.")]
        [SerializeField] private float spinDegreesPerSecond = 120f;

        [Tooltip("Brightness multiplier on the tint (×). Above 1 blooms if bloom is on.")]
        [Min(0f)] [SerializeField] private float intensity = 2f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Renderer[] _renderers;
        MaterialPropertyBlock _block;
        Transform _camera;
        float _age, _size, _roll;

        public override void Play(in EffectCue cue)
        {
            _renderers ??= GetComponentsInChildren<Renderer>(true);
            _block ??= new MaterialPropertyBlock();
            if (Camera.main != null) _camera = Camera.main.transform;

            _age = 0f;
            _size = cue.Radius;
            _roll = Random.Range(-20f, 20f);

            var c = cue.Color * intensity;
            c.a = 1f;
            _block.SetColor(BaseColorId, c);
            foreach (var r in _renderers) r.SetPropertyBlock(_block);
            Apply();
        }

        void LateUpdate()
        {
            _age += Time.deltaTime;
            if (_age >= lifetime) { Finish(); return; }
            _roll += spinDegreesPerSecond * Time.deltaTime;
            Apply();
        }

        void Apply()
        {
            float t = Mathf.Clamp01(_age / lifetime);
            // Up past full size in the first third, then ease away to nothing.
            float s = t < 0.3f
                ? Mathf.Lerp(0f, overshoot, t / 0.3f)
                : Mathf.Lerp(overshoot, 0f, (t - 0.3f) / 0.7f * ((t - 0.3f) / 0.7f));
            transform.localScale = Vector3.one * (s * _size);
            if (_camera != null)
                transform.rotation = Quaternion.LookRotation(_camera.forward) * Quaternion.Euler(0f, 0f, _roll);
        }
    }
}
