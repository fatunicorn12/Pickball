using Pickleball.Sandbox.Boss.Hypatia.Burning;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Alexandria's Burning on the floor: a band that starts to smoulder
    /// (<see cref="ArenaBurn.BandSmouldering"/>) flickers ember-red over its
    /// whole annulus; when it ignites (<see cref="ArenaBurn.BandIgnited"/>) it
    /// turns a steady fire tint and flames rise across it. When the fight ends
    /// (<see cref="ArenaBurn.Extinguished"/>) the flames die and burned bands
    /// stay as ash, matching ArenaBurn's "burned but inert" rule.
    ///
    /// One slot per band, built by Setup Hypatia Fight: a floor overlay
    /// (MeshFilter + MeshRenderer, its annulus mesh made here from the event's
    /// radii) and a ParticleSystem whose circle shape is fitted to the annulus.
    /// Presentation only: it reads <see cref="ArenaBurn.GetBandState"/> to redraw
    /// after a reload and never calls into the burn.
    /// </summary>
    public class BandFireView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Source")]
        [Tooltip("The floor fire to draw.")]
        [SerializeField] private ArenaBurn burn;

        [Header("Slots (index = band)")]
        [Tooltip("Floor overlay per band; the annulus mesh is built when the band first smoulders.")]
        [SerializeField] private MeshRenderer[] overlays;
        [Tooltip("Flames per band, a ParticleSystem lying flat (circle shape).")]
        [SerializeField] private ParticleSystem[] flames;

        [Header("Look")]
        [Tooltip("Smoulder colour; alpha is the peak of the flicker.")]
        [SerializeField] private Color smoulderColour = new(0.85f, 0.08f, 0.05f, 0.55f);
        [Tooltip("Burned band colour while the fire is live.")]
        [SerializeField] private Color burningColour = new(1f, 0.42f, 0.08f, 0.4f);
        [Tooltip("Burned band colour after the fight (inert ash).")]
        [SerializeField] private Color ashColour = new(0.18f, 0.16f, 0.2f, 0.6f);
        [Tooltip("Smoulder flicker speed, 1/s. Higher = more agitated warning.")]
        [SerializeField, Min(0f)] private float flickerRate = 9f;
        [Tooltip("Flame particles per second per square metre of band.")]
        [SerializeField, Min(0f)] private float flamesPerSquareMetre = 3f;
        [Tooltip("Cap on flame particles per second per band.")]
        [SerializeField, Min(0f)] private float maxFlameRate = 450f;
        [Tooltip("Overlay lifted this far off the plate, m.")]
        [SerializeField, Min(0f)] private float floorLift = 0.02f;
        [Tooltip("Segments round each annulus.")]
        [SerializeField, Range(24, 256)] private int segments = 128;

        ArenaBurn _subscribed;
        MaterialPropertyBlock _block;
        BandState[] _states;
        Mesh[] _meshes;
        bool _extinguished;

        /// <summary>The drawn state of <paramref name="band"/> (Unburned when out of range).</summary>
        public BandState DrawnState(int band) =>
            _states != null && band >= 0 && band < _states.Length ? _states[band] : BandState.Unburned;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            int count = overlays != null ? overlays.Length : 0;
            _states = new BandState[count];
            _meshes ??= new Mesh[count];
            if (_meshes.Length != count) System.Array.Resize(ref _meshes, count);
            _extinguished = false;
            for (int i = 0; i < count; i++) Show(i, BandState.Unburned);

            _subscribed = burn;
            if (_subscribed == null) return;
            _subscribed.BandSmouldering += OnSmouldering;
            _subscribed.BandIgnited += OnIgnited;
            _subscribed.Extinguished += OnExtinguished;

            // Redraw what we missed (a reload mid phase 2).
            for (int i = 0; i < _subscribed.BandCount && i < count; i++)
            {
                var state = _subscribed.GetBandState(i);
                if (state == BandState.Unburned) continue;
                Build(i, _subscribed.GetBandInnerRadius(i), _subscribed.GetBandOuterRadius(i));
                Show(i, state);
            }
            if (_subscribed.IsStopped) OnExtinguished();
        }

        void OnDisable()
        {
            if (_subscribed == null) return;
            _subscribed.BandSmouldering -= OnSmouldering;
            _subscribed.BandIgnited -= OnIgnited;
            _subscribed.Extinguished -= OnExtinguished;
            _subscribed = null;
        }

        void OnDestroy()
        {
            if (_meshes == null) return;
            for (int i = 0; i < _meshes.Length; i++)
            {
                if (_meshes[i] == null) continue;
                if (Application.isPlaying) Destroy(_meshes[i]);
                else DestroyImmediate(_meshes[i]);
            }
        }

        void OnSmouldering(int band, float inner, float outer, float lead)
        {
            if (!InRange(band)) return;
            Build(band, inner, outer);
            Show(band, BandState.Smouldering);
        }

        void OnIgnited(int band, float inner, float outer)
        {
            if (!InRange(band)) return;
            Build(band, inner, outer);
            Show(band, BandState.Burned);
        }

        void OnExtinguished()
        {
            _extinguished = true;
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == BandState.Smouldering) Show(i, BandState.Unburned);
                if (flames != null && i < flames.Length && flames[i] != null)
                    flames[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (_states[i] == BandState.Burned) Tint(i, ashColour);
            }
        }

        bool InRange(int band) => _states != null && band >= 0 && band < _states.Length;

        void Build(int band, float inner, float outer)
        {
            var overlay = overlays[band];
            if (overlay == null) return;
            if (_meshes[band] == null) _meshes[band] = new Mesh { name = $"BandOverlay{band}" };
            ArcMesh.Annulus(_meshes[band], inner, outer, segments);
            if (overlay.TryGetComponent(out MeshFilter filter)) filter.sharedMesh = _meshes[band];
            overlay.transform.localPosition = new Vector3(0f, floorLift, 0f);

            if (flames == null || band >= flames.Length || flames[band] == null) return;
            var ps = flames[band];
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.01f, outer);
            shape.radiusThickness = outer > 0f ? Mathf.Clamp01((outer - inner) / outer) : 1f;
            var emission = ps.emission;
            float area = Mathf.PI * (outer * outer - inner * inner);
            emission.rateOverTime = Mathf.Min(maxFlameRate, area * flamesPerSquareMetre);
        }

        void Show(int band, BandState state)
        {
            _states[band] = state;
            var overlay = overlays[band];
            if (overlay != null) overlay.enabled = state != BandState.Unburned;
            if (state == BandState.Burned) Tint(band, _extinguished ? ashColour : burningColour);

            if (flames == null || band >= flames.Length || flames[band] == null) return;
            var ps = flames[band];
            bool on = state == BandState.Burned && !_extinguished;
            if (on && !ps.isPlaying) ps.Play(true);
            else if (!on && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void Tint(int band, Color c)
        {
            var overlay = overlays[band];
            if (overlay == null) return;
            _block.SetColor(BaseColorId, c);
            overlay.SetPropertyBlock(_block);
        }

        void LateUpdate()
        {
            if (_states == null) return;
            float t = Time.time;
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == BandState.Smouldering)
                {
                    float n = Mathf.PerlinNoise(t * flickerRate, i * 7.31f);
                    Tint(i, new Color(smoulderColour.r, smoulderColour.g, smoulderColour.b,
                                      smoulderColour.a * Mathf.Lerp(0.2f, 1f, n)));
                }
                else if (_states[i] == BandState.Burned && !_extinguished)
                {
                    float n = Mathf.PerlinNoise(t * 1.7f, i * 3.1f);
                    Tint(i, new Color(burningColour.r, burningColour.g * Mathf.Lerp(0.8f, 1.1f, n),
                                      burningColour.b, burningColour.a * Mathf.Lerp(0.8f, 1f, n)));
                }
            }
        }
    }
}
