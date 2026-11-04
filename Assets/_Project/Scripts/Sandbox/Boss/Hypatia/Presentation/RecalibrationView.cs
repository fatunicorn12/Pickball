using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Shows the recalibration window, the fight's punish beat: while it is open
    /// her glow and the astrolabe dim (she has spent her light on the volley), her
    /// halo turns from cold starlight to a warm, exposed amber, and a ring on the
    /// floor under her shrinks as the window drains. The player should read
    /// "she is down, hit her now" without a HUD.
    ///
    /// Presentation only. It listens to <see cref="HypatiaBoss.RecalibrationStarted"/>
    /// for the window's length and polls <see cref="HypatiaBoss.IsRecalibrating"/>
    /// and <see cref="HypatiaBoss.RecalibrationRemaining"/> (read-only, on her
    /// clock, so a frozen window holds its ring still). Blends are exponential
    /// and frame-rate independent.
    /// </summary>
    public class RecalibrationView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Source")]
        [Tooltip("Her brain.")]
        [SerializeField] private HypatiaBoss boss;

        [Header("Parts")]
        [Tooltip("Her glow light (a child of her body).")]
        [SerializeField] private Light glow;
        [Tooltip("Additive halo around her (camera-facing quad).")]
        [SerializeField] private Renderer halo;
        [Tooltip("Astrolabe parts whose emission dims in the window (URP Lit with emission on).")]
        [SerializeField] private Renderer[] astrolabeParts;
        [Tooltip("Floor ring under her that shrinks as the window drains (scaled in XZ).")]
        [SerializeField] private Transform windowRing;
        [Tooltip("The window ring's renderer, faded in and out.")]
        [SerializeField] private Renderer windowRingRenderer;

        [Header("Look")]
        [Tooltip("Glow light intensity outside the window. She is lit from close up and her gown is " +
                 "pale, so much above this clips the gold to flat white.")]
        [SerializeField, Min(0f)] private float glowIntensity = 1f;
        [Tooltip("Fraction (0-1) of the glow left in the window.")]
        [SerializeField, Range(0f, 1f)] private float dimmedFraction = 0.2f;
        [Tooltip("Halo colour outside the window.")]
        [SerializeField] private Color calmHalo = new(0.62f, 0.68f, 1f, 0.3f);
        [Tooltip("Halo colour in the window.")]
        [SerializeField] private Color exposedHalo = new(1f, 0.62f, 0.25f, 0.6f);
        [Tooltip("Astrolabe emission outside the window.")]
        [SerializeField] private Color astrolabeEmission = new(0.9f, 0.62f, 0.25f);
        [Tooltip("Window ring colour.")]
        [SerializeField] private Color ringColour = new(1f, 0.7f, 0.3f, 0.85f);
        [Tooltip("Window ring diameter when the window opens, m.")]
        [SerializeField, Min(0.1f)] private float ringDiameter = 3.2f;
        [Tooltip("How fast the look blends in and out, 1/s (exponential).")]
        [SerializeField, Min(0.1f)] private float blendRate = 6f;

        HypatiaBoss _subscribed;
        MaterialPropertyBlock _block;
        float _blend;
        float _duration = 1f;

        /// <summary>0 outside the window, 1 fully in it (smoothed).</summary>
        public float Blend => _blend;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            _subscribed = boss;
            if (_subscribed != null) _subscribed.RecalibrationStarted += OnStarted;
            _blend = boss != null && boss.IsRecalibrating ? 1f : 0f;
            Apply();
        }

        void OnDisable()
        {
            if (_subscribed != null) _subscribed.RecalibrationStarted -= OnStarted;
            _subscribed = null;
        }

        void OnStarted(float duration) => _duration = Mathf.Max(0.01f, duration);

        void LateUpdate()
        {
            if (boss == null) return;
            float target = boss.IsRecalibrating ? 1f : 0f;
            _blend += (target - _blend) * (1f - Mathf.Exp(-blendRate * Time.deltaTime));
            Apply();
        }

        void Apply()
        {
            if (glow != null) glow.intensity = glowIntensity * Mathf.Lerp(1f, dimmedFraction, _blend);

            if (halo != null)
            {
                _block.Clear();
                _block.SetColor(BaseColorId, Color.Lerp(calmHalo, exposedHalo, _blend));
                halo.SetPropertyBlock(_block);
            }

            if (astrolabeParts != null)
            {
                _block.Clear();
                _block.SetColor(EmissionColorId, astrolabeEmission * Mathf.Lerp(1f, dimmedFraction, _blend));
                for (int i = 0; i < astrolabeParts.Length; i++)
                    if (astrolabeParts[i] != null) astrolabeParts[i].SetPropertyBlock(_block);
            }

            if (windowRing == null) return;
            bool open = boss != null && boss.IsRecalibrating;
            bool show = open || _blend > 0.02f;
            if (windowRing.gameObject.activeSelf != show) windowRing.gameObject.SetActive(show);
            if (!show) return;
            float fraction = open ? Mathf.Clamp01(boss.RecalibrationRemaining / _duration) : 0f;
            float d = Mathf.Max(0.01f, ringDiameter * fraction);
            windowRing.localScale = new Vector3(d, 1f, d);
            if (windowRingRenderer != null)
            {
                _block.Clear();
                _block.SetColor(BaseColorId, new Color(ringColour.r, ringColour.g, ringColour.b, ringColour.a * _blend));
                windowRingRenderer.SetPropertyBlock(_block);
            }
        }
    }
}
