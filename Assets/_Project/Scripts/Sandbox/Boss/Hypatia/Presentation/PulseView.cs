using Pickleball.Sandbox.Boss.Hypatia.Pulse;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Shows the knockback pulse. While it charges, a glow swells on her (the
    /// warning: back off or be thrown). When it fires, a ring races out across
    /// the plate to exactly Pulse Reach and a faint, flattened dome swells with
    /// it, so the player sees how far it reached and whether they were inside.
    /// The dome is kept low and nearly transparent: the camera is usually inside
    /// it, and a bright sphere would fill the screen.
    ///
    /// Presentation only: listens to <see cref="KnockbackPulse.PulseCharging"/>,
    /// <see cref="KnockbackPulse.PulseFired"/> and
    /// <see cref="KnockbackPulse.PulseCancelled"/>. The charge glow follows her
    /// clock (it freezes with her); the shockwave, already released, plays in
    /// real time. No per-frame allocation.
    /// </summary>
    public class PulseView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Source")]
        [Tooltip("The pulse attack on her root.")]
        [SerializeField] private KnockbackPulse pulse;
        [Tooltip("Her brain: her position, the floor height and her clock.")]
        [SerializeField] private HypatiaBoss boss;

        [Header("Parts")]
        [Tooltip("Camera-facing glow on her body (carries LockOnIgnore). Shown while charging.")]
        [SerializeField] private Renderer chargeGlow;
        [Tooltip("Flat ring quad on the plate, scaled in XZ to the shockwave's diameter.")]
        [SerializeField] private Transform ring;
        [Tooltip("The ring's renderer, faded out after it reaches Pulse Reach.")]
        [SerializeField] private Renderer ringRenderer;
        [Tooltip("Translucent sphere, scaled to the shockwave (flattened by Dome Height).")]
        [SerializeField] private Transform dome;
        [Tooltip("The dome's renderer.")]
        [SerializeField] private Renderer domeRenderer;

        [Header("Look")]
        [Tooltip("Charge glow colour; its alpha is the brightest it gets.")]
        [SerializeField] private Color chargeColour = new(0.75f, 0.85f, 1f, 0.9f);
        [Tooltip("Charge glow size when the pulse fires, m.")]
        [SerializeField, Min(0.1f)] private float chargeGlowSize = 3.4f;
        [Tooltip("Charge glow flicker rate while charging, Hz.")]
        [SerializeField, Min(0f)] private float chargeFlicker = 9f;
        [Tooltip("Shockwave ring colour.")]
        [SerializeField] private Color ringColour = new(0.8f, 0.9f, 1f, 1f);
        [Tooltip("Dome colour; keep alpha low, the camera is usually inside it.")]
        [SerializeField] private Color domeColour = new(0.55f, 0.65f, 1f, 0.05f);
        [Tooltip("Dome height as a fraction of its radius (1 = a hemisphere).")]
        [SerializeField, Range(0.05f, 1f)] private float domeHeight = 0.3f;
        [Tooltip("Seconds the shockwave takes to reach Pulse Reach (real time).")]
        [SerializeField, Min(0.01f)] private float expandSeconds = 0.25f;
        [Tooltip("Seconds it then takes to fade out (real time).")]
        [SerializeField, Min(0.01f)] private float fadeSeconds = 0.35f;
        [Tooltip("Lift off the plate, m, so the ring does not z-fight it.")]
        [SerializeField, Min(0f)] private float floorLift = 0.05f;

        KnockbackPulse _subscribed;
        MaterialPropertyBlock _block;
        bool _charging, _waving;
        float _chargeFor, _charged, _waveAge, _reach;
        Vector3 _origin;

        /// <summary>True while the shockwave is showing.</summary>
        public bool IsShowingWave => _waving;

        /// <summary>Current shockwave radius, m (0 when not showing).</summary>
        public float WaveRadius => _waving ? _reach * Ease(_waveAge / expandSeconds) : 0f;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            _subscribed = pulse;
            if (_subscribed != null)
            {
                _subscribed.PulseCharging += OnCharging;
                _subscribed.PulseFired += OnFired;
                _subscribed.PulseCancelled += OnCancelled;
            }
            _charging = _subscribed != null && _subscribed.IsCharging;
            _waving = false;
            ShowGlow(_charging);
            ShowWave(false);
        }

        void OnDisable()
        {
            if (_subscribed != null)
            {
                _subscribed.PulseCharging -= OnCharging;
                _subscribed.PulseFired -= OnFired;
                _subscribed.PulseCancelled -= OnCancelled;
            }
            _subscribed = null;
        }

        void OnCharging(float seconds)
        {
            _charging = true;
            _chargeFor = Mathf.Max(0.01f, seconds);
            _charged = 0f;
            ShowGlow(true);
        }

        void OnCancelled()
        {
            _charging = false;
            ShowGlow(false);
        }

        void OnFired(float reach)
        {
            _charging = false;
            ShowGlow(false);
            _reach = Mathf.Max(0.01f, reach);
            _waveAge = 0f;
            Vector3 her = boss != null ? boss.transform.position : transform.position;
            float floor = boss != null && boss.Context != null ? boss.Context.FloorY : her.y;
            _origin = new Vector3(her.x, floor + floorLift, her.z);
            _waving = true;
            ShowWave(true);
            ApplyWave();
        }

        void LateUpdate()
        {
            if (_charging && chargeGlow != null)
            {
                float scale = boss != null && boss.Context != null ? boss.Context.BossTimeScale : 1f;
                _charged += Time.deltaTime * scale;
                float t = Mathf.Clamp01(_charged / _chargeFor);
                float flicker = 0.85f + 0.15f * Mathf.Sin(_charged * chargeFlicker * 2f * Mathf.PI);
                float size = Mathf.Lerp(0.6f, chargeGlowSize, t * t);
                chargeGlow.transform.localScale = new Vector3(size, size, 1f);
                Tint(chargeGlow, chargeColour, Mathf.Lerp(0.3f, 1f, t) * flicker);
            }

            if (!_waving) return;
            _waveAge += Time.deltaTime;
            if (_waveAge >= expandSeconds + fadeSeconds)
            {
                _waving = false;
                ShowWave(false);
                return;
            }
            ApplyWave();
        }

        void ApplyWave()
        {
            float r = _reach * Ease(_waveAge / expandSeconds);
            float fade = 1f - Mathf.Clamp01((_waveAge - expandSeconds) / fadeSeconds);
            float d = Mathf.Max(0.01f, 2f * r);
            if (ring != null)
            {
                ring.position = _origin;
                ring.localScale = new Vector3(d, d, 1f);   // a quad laid flat: its local XY is the plate
                Tint(ringRenderer, ringColour, fade);
            }
            if (dome != null)
            {
                dome.position = _origin;
                dome.localScale = new Vector3(d, d * domeHeight, d);
                Tint(domeRenderer, domeColour, fade);
            }
        }

        /// <summary>Fast out, gentle in: the wave hits hard and slows at the edge.</summary>
        static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        void ShowGlow(bool on)
        {
            if (chargeGlow != null && chargeGlow.gameObject.activeSelf != on) chargeGlow.gameObject.SetActive(on);
        }

        void ShowWave(bool on)
        {
            if (ring != null && ring.gameObject.activeSelf != on) ring.gameObject.SetActive(on);
            if (dome != null && dome.gameObject.activeSelf != on) dome.gameObject.SetActive(on);
        }

        void Tint(Renderer r, Color c, float alphaScale)
        {
            if (r == null) return;
            _block.Clear();
            _block.SetColor(BaseColorId, new Color(c.r, c.g, c.b, c.a * alphaScale));
            r.SetPropertyBlock(_block);
        }
    }
}
