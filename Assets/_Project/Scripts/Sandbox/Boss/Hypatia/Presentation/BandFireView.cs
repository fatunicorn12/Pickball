using Pickleball.Sandbox.Boss.Hypatia.Burning;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Alexandria's Burning, the Library's end drawn on the floor. A band that starts
    /// to smoulder (<see cref="ArenaBurn.BandSmouldering"/>) tints red all at once,
    /// and a burn front creeps in from its outer edge, driven by the real countdown,
    /// so it arrives exactly at ignition. When it ignites
    /// (<see cref="ArenaBurn.BandIgnited"/>) the band flashes and burns: char with
    /// molten veins, flames, embers and scraps of burning pages. When the
    /// fight ends (<see cref="ArenaBurn.Extinguished"/>) the fire dies, the veins cool
    /// and the bands stay as ash, matching ArenaBurn's "burned but inert" rule.
    ///
    /// One slot per band, built by Setup Hypatia Fight: a floor overlay (its annulus
    /// mesh made here from the event's radii, so what looks dangerous is exactly what
    /// the fire rule hurts) and three particle systems fitted to that annulus, kept
    /// clear of the safe ground. Presentation only: it reads
    /// <see cref="ArenaBurn.GetBandState"/> to redraw after a reload and never calls
    /// into the burn. The rules are in <see cref="BandLook"/>. No per-frame allocation.
    /// </summary>
    public class BandFireView : MonoBehaviour
    {
        static readonly int InnerId = Shader.PropertyToID("_Inner");
        static readonly int OuterId = Shader.PropertyToID("_Outer");
        static readonly int WarningId = Shader.PropertyToID("_Warning");
        static readonly int BurningId = Shader.PropertyToID("_Burning");
        static readonly int AshId = Shader.PropertyToID("_Ash");
        static readonly int FrontierId = Shader.PropertyToID("_Frontier");
        static readonly int IgniteId = Shader.PropertyToID("_Ignite");

        [Header("Source")]
        [Tooltip("The floor fire to draw.")]
        [SerializeField] private ArenaBurn burn;

        [Header("Slots (index = band)")]
        [Tooltip("Floor overlay per band; the annulus mesh is built when the band first smoulders.")]
        [SerializeField] private MeshRenderer[] overlays;
        [Tooltip("Flames per band (upright quads, Fire Flame shader).")]
        [SerializeField] private ParticleSystem[] flames;
        [Tooltip("Embers per band.")]
        [SerializeField] private ParticleSystem[] embers;
        [Tooltip("Burning scraps of page per band: this is the Library burning.")]
        [SerializeField] private ParticleSystem[] pages;

        [Header("Parts")]
        [Tooltip("Point light over the arena: warms everything as more of the floor burns. Optional.")]
        [SerializeField] private Light fireLight;
        [Tooltip("The chase camera, for the kick when a band ignites. Optional.")]
        [SerializeField] private SandboxOrbitCamera cameraRig;

        [Header("Density")]
        [Tooltip("Flames per second per m² of band. Higher = a denser blaze, more overdraw.")]
        [SerializeField, Min(0f)] private float flamesPerSquareMetre = 1.15f;
        [Tooltip("Cap on flames per second per band.")]
        [SerializeField, Min(0f)] private float maxFlameRate = 170f;
        [Tooltip("Embers per second per m² of band.")]
        [SerializeField, Min(0f)] private float embersPerSquareMetre = 0.3f;
        [Tooltip("Page scraps per second per m² of band. Keep it low: it is a detail you notice, not a blizzard.")]
        [SerializeField, Min(0f)] private float pagesPerSquareMetre = 0.035f;
        [Tooltip("Flames stay this far inside the band's inner edge, m: about half the width of a flame's visible body, so none stands over safe ground.")]
        [SerializeField, Min(0f)] private float flameEdgeMargin = 0.8f;

        [Header("Ignition")]
        [Tooltip("Seconds the band's ignition flash takes to die.")]
        [SerializeField, Min(0.01f)] private float igniteFlashSeconds = 0.45f;
        [Tooltip("Embers thrown up when a band ignites.")]
        [SerializeField, Min(0)] private int igniteEmbers = 90;
        [Tooltip("Shake when a band ignites, m. Sword hits are about 0.15.")]
        [SerializeField, Min(0f)] private float igniteShake = 0.16f;
        [Tooltip("Seconds that shake lasts.")]
        [SerializeField, Min(0.01f)] private float igniteShakeSeconds = 0.4f;

        [Header("Light")]
        [Tooltip("Light intensity added per burning band.")]
        [SerializeField, Min(0f)] private float lightPerBand = 6f;
        [Tooltip("Extra intensity at the moment a band ignites, fading over the flash.")]
        [SerializeField, Min(0f)] private float lightIgniteBoost = 40f;
        [Tooltip("How much the light flickers, 0..1 of its brightness.")]
        [SerializeField, Range(0f, 1f)] private float lightFlicker = 0.3f;
        [Tooltip("Flicker speed, 1/s. Higher = more agitated.")]
        [SerializeField, Min(0f)] private float lightFlickerRate = 7f;

        [Header("Ash")]
        [Tooltip("Seconds the veins take to cool once the fight ends.")]
        [SerializeField, Min(0.01f)] private float ashSeconds = 2.5f;

        [Header("Placement")]
        [Tooltip("Overlay lifted this far off the plate, m.")]
        [SerializeField, Min(0f)] private float floorLift = 0.02f;
        [Tooltip("Segments round each annulus.")]
        [SerializeField, Range(24, 256)] private int segments = 128;

        ArenaBurn _subscribed;
        MaterialPropertyBlock _block;
        BandState[] _states;
        Mesh[] _meshes;
        float[] _inner, _outer, _lead, _igniteAge;
        bool _extinguished;
        float _ashAge, _lastIgniteAge = float.MaxValue;

        /// <summary>The drawn state of <paramref name="band"/> (Unburned when out of range).</summary>
        public BandState DrawnState(int band) =>
            _states != null && band >= 0 && band < _states.Length ? _states[band] : BandState.Unburned;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            int count = overlays != null ? overlays.Length : 0;
            _states = new BandState[count];
            _inner = new float[count];
            _outer = new float[count];
            _lead = new float[count];
            _igniteAge = new float[count];
            _meshes ??= new Mesh[count];
            if (_meshes.Length != count) System.Array.Resize(ref _meshes, count);
            _extinguished = false;
            _ashAge = 0f;
            _lastIgniteAge = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                _igniteAge[i] = float.MaxValue;
                Show(i, BandState.Unburned);
            }
            if (fireLight != null) fireLight.enabled = false;

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
                _lead[i] = Mathf.Max(0.01f, _subscribed.GetSmoulderRemaining(i));
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
            _lead[band] = lead;
            Build(band, inner, outer);
            Show(band, BandState.Smouldering);
        }

        void OnIgnited(int band, float inner, float outer)
        {
            if (!InRange(band)) return;
            Build(band, inner, outer);
            Show(band, BandState.Burned);
            _igniteAge[band] = 0f;
            _lastIgniteAge = 0f;

            if (cameraRig != null) cameraRig.Shake(igniteShake, igniteShakeSeconds);
            if (embers != null && band < embers.Length && embers[band] != null) embers[band].Emit(igniteEmbers);
        }

        void OnExtinguished()
        {
            _extinguished = true;
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == BandState.Smouldering) Show(i, BandState.Unburned);
                StopEmitting(flames, i);
                StopEmitting(embers, i);
                StopEmitting(pages, i);
            }
        }

        bool InRange(int band) => _states != null && band >= 0 && band < _states.Length;

        void Build(int band, float inner, float outer)
        {
            _inner[band] = inner;
            _outer[band] = outer;
            var overlay = overlays[band];
            if (overlay == null) return;
            if (_meshes[band] == null) _meshes[band] = new Mesh { name = $"BandOverlay{band}" };
            ArcMesh.Annulus(_meshes[band], inner, outer, segments);
            if (overlay.TryGetComponent(out MeshFilter filter)) filter.sharedMesh = _meshes[band];
            overlay.transform.localPosition = new Vector3(0f, floorLift, 0f);

            Fit(flames, band, inner, outer, flameEdgeMargin, flamesPerSquareMetre, maxFlameRate);
            Fit(embers, band, inner, outer, 0.1f, embersPerSquareMetre, float.MaxValue);
            Fit(pages, band, inner, outer, 0.1f, pagesPerSquareMetre, float.MaxValue);
        }

        static void Fit(ParticleSystem[] systems, int band, float inner, float outer, float margin, float density, float cap)
        {
            if (systems == null || band >= systems.Length || systems[band] == null) return;
            var ps = systems[band];
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.01f, outer);
            shape.radiusThickness = BandLook.RadiusThickness(inner, outer, margin);
            var emission = ps.emission;
            emission.rateOverTime = BandLook.Rate(inner, outer, density, cap);
        }

        void Show(int band, BandState state)
        {
            _states[band] = state;
            var overlay = overlays[band];
            if (overlay != null) overlay.enabled = state != BandState.Unburned;

            bool on = state == BandState.Burned && !_extinguished;
            SetPlaying(flames, band, on);
            SetPlaying(embers, band, on);
            SetPlaying(pages, band, on);
        }

        static void SetPlaying(ParticleSystem[] systems, int band, bool on)
        {
            if (systems == null || band >= systems.Length || systems[band] == null) return;
            var ps = systems[band];
            if (on && !ps.isPlaying) ps.Play(true);
            else if (!on && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static void StopEmitting(ParticleSystem[] systems, int band)
        {
            if (systems == null || band >= systems.Length || systems[band] == null) return;
            systems[band].Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void LateUpdate()
        {
            if (_states == null) return;
            float dt = Time.deltaTime;
            if (_extinguished) _ashAge += dt;
            float ash = _extinguished ? Mathf.Clamp01(_ashAge / ashSeconds) : 0f;
            float safe = burn != null ? burn.SafeRadius : 0f;
            float projected = burn != null ? burn.ProjectedSafeRadius : 0f;

            int burning = 0;
            for (int i = 0; i < _states.Length; i++)
            {
                if (_states[i] == BandState.Unburned) continue;
                if (_states[i] == BandState.Burned) burning++;
                if (_igniteAge[i] < float.MaxValue) _igniteAge[i] += dt;

                float warning = _states[i] == BandState.Burned
                    ? 1f
                    : burn != null ? BandLook.WarningProgress(burn.GetSmoulderRemaining(i), _lead[i]) : 0f;

                var overlay = overlays[i];
                if (overlay == null) continue;
                _block.Clear();
                _block.SetFloat(InnerId, _inner[i]);
                _block.SetFloat(OuterId, _outer[i]);
                // Above zero from the first frame: the shader treats 0 as "no warning".
                _block.SetFloat(WarningId, Mathf.Max(0.001f, warning));
                _block.SetFloat(BurningId, _states[i] == BandState.Burned ? 1f : 0f);
                _block.SetFloat(AshId, ash);
                _block.SetFloat(FrontierId, BandLook.IsFrontier(_inner[i], safe, projected) ? 1f : 0f);
                _block.SetFloat(IgniteId, BandLook.Flash(_igniteAge[i], igniteFlashSeconds));
                overlay.SetPropertyBlock(_block);
            }

            if (_lastIgniteAge < float.MaxValue) _lastIgniteAge += dt;
            if (fireLight != null)
            {
                float flicker = 1f - lightFlicker * Mathf.PerlinNoise(Time.time * lightFlickerRate, 0.37f);
                float intensity = burning * lightPerBand * flicker * (1f - ash);
                intensity += lightIgniteBoost * BandLook.Flash(_lastIgniteAge, igniteFlashSeconds) * (1f - ash);
                bool lit = intensity > 0.01f;
                if (fireLight.enabled != lit) fireLight.enabled = lit;
                fireLight.intensity = intensity;
            }
        }
    }
}
