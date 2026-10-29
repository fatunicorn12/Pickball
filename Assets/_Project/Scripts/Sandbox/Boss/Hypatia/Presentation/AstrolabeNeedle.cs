using TMPro;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// The astrolabe's needle: the fight's one tell for which conic is coming.
    /// On <see cref="HypatiaBoss.ConicTelegraphed"/> it swings to the
    /// eccentricity e on a dial marked 0, 1 and 2 (an ellipse lands left of 1, a
    /// parabola exactly on it, a hyperbola right of it, as the player faces her),
    /// tints by kind and shows "e = 0.62" beneath the dial. It returns to rest
    /// when the volley finishes (the recalibration window opens), or when she is
    /// defeated.
    ///
    /// The needle swings on HER clock (<see cref="HypatiaContext.BossTimeScale"/>):
    /// freeze her and the needle freezes mid-swing with the rest of her.
    ///
    /// <b>Dial geometry.</b> The needle pivot turns about its local Z. Angle 0
    /// points up (+Y); the dial spans ±<see cref="dialHalfDegrees"/>, e = 0 at
    /// −half and e = 2 at +half, which Setup Hypatia Fight marks with ticks at
    /// the same angles (<see cref="AngleForEccentricity"/>). A positive Z angle
    /// turns the needle toward −X, the viewer's right when she faces them.
    /// </summary>
    public class AstrolabeNeedle : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Source")]
        [Tooltip("Her brain, whose telegraph and window events drive the needle.")]
        [SerializeField] private HypatiaBoss boss;

        [Header("Parts")]
        [Tooltip("Pivot the needle turns about (local Z).")]
        [SerializeField] private Transform pivot;
        [Tooltip("The needle's renderer, tinted by conic kind.")]
        [SerializeField] private Renderer needleRenderer;
        [Tooltip("World-space text under the dial showing e.")]
        [SerializeField] private TMP_Text readout;

        [Header("Dial")]
        [Tooltip("Half the dial's sweep, °: e = 0 sits this far left of e = 1, e = 2 this far right.")]
        [SerializeField, Range(30f, 170f)] private float dialHalfDegrees = 100f;
        [Tooltip("Where the needle rests between volleys, ° (180 = hanging straight down).")]
        [SerializeField] private float restDegrees = 180f;
        [Tooltip("How fast the needle swings to its target, 1/s (exponential, on her clock). " +
                 "Higher snaps it there.")]
        [SerializeField, Min(0.1f)] private float swingRate = 9f;

        [Header("Colours")]
        [Tooltip("Needle colour at rest.")]
        [SerializeField] private Color restColour = new(0.78f, 0.62f, 0.32f);
        [Tooltip("Needle and text colour for a parabola (e = 1).")]
        [SerializeField] private Color parabolaColour = new(1f, 0.82f, 0.3f);
        [Tooltip("Needle and text colour for an ellipse (e < 1).")]
        [SerializeField] private Color ellipseColour = new(0.62f, 0.85f, 1f);
        [Tooltip("Needle and text colour for a hyperbola (e > 1).")]
        [SerializeField] private Color hyperbolaColour = new(1f, 0.35f, 0.55f);
        [Tooltip("Emission multiplier on the needle while it shows a reading.")]
        [SerializeField, Min(0f)] private float glow = 2.2f;

        HypatiaBoss _subscribed;
        MaterialPropertyBlock _block;
        float _angle, _target;
        bool _showing;

        /// <summary>The dial angle for eccentricity <paramref name="e"/> (clamped
        /// to 0-2), °. e = 1 is exactly 0 (straight up).</summary>
        public static float AngleForEccentricity(float e, float dialHalfDegrees)
            => (Mathf.Clamp(float.IsNaN(e) ? 1f : e, 0f, 2f) - 1f) * dialHalfDegrees;

        /// <summary>The colour this needle uses for <paramref name="kind"/>.</summary>
        public Color ColourFor(ConicKind kind) => kind switch
        {
            ConicKind.Parabola => parabolaColour,
            ConicKind.Ellipse => ellipseColour,
            _ => hyperbolaColour,
        };

        /// <summary>True while a reading is shown (from telegraph to the end of the volley).</summary>
        public bool IsShowing => _showing;

        /// <summary>The needle's current angle, ° (0 = e of 1).</summary>
        public float Angle => _angle;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            _subscribed = boss;
            if (_subscribed != null)
            {
                _subscribed.ConicTelegraphed += OnTelegraphed;
                _subscribed.RecalibrationStarted += OnVolleyOver;
                _subscribed.Defeated += Rest;
            }
            Rest();
            _angle = _target;
            Apply();
        }

        void OnDisable()
        {
            if (_subscribed == null) return;
            _subscribed.ConicTelegraphed -= OnTelegraphed;
            _subscribed.RecalibrationStarted -= OnVolleyOver;
            _subscribed.Defeated -= Rest;
            _subscribed = null;
        }

        void OnTelegraphed(ConicKind kind, float e, float lead)
        {
            _showing = true;
            _target = AngleForEccentricity(e, dialHalfDegrees);
            Color c = ColourFor(kind);
            Tint(c, glow);
            if (readout != null)
            {
                readout.gameObject.SetActive(true);
                readout.color = c;
                readout.SetText("e = {0:2}", e);
            }
        }

        void OnVolleyOver(float duration) => Rest();

        void Rest()
        {
            _showing = false;
            _target = restDegrees;
            Tint(restColour, 0f);
            if (readout != null) readout.gameObject.SetActive(false);
        }

        void Update()
        {
            float scale = boss != null && boss.Context != null ? boss.Context.BossTimeScale : 1f;
            float dt = Time.deltaTime * scale;
            _angle = Mathf.LerpAngle(_angle, _target, 1f - Mathf.Exp(-swingRate * dt));
            Apply();
        }

        void Apply()
        {
            if (pivot != null) pivot.localRotation = Quaternion.Euler(0f, 0f, _angle);
        }

        void Tint(Color colour, float emission)
        {
            if (needleRenderer == null) return;
            _block.SetColor(BaseColorId, colour);
            _block.SetColor(EmissionColorId, colour * emission);
            needleRenderer.SetPropertyBlock(_block);
        }
    }
}
