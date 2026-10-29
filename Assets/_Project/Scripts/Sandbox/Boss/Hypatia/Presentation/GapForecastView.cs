using System.Collections.Generic;
using Pickleball.Sandbox.Boss.Hypatia.Rings;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// The floor's warning for each Celestial Ring: a glowing wedge along the
    /// coming ring's gap, from where rings are born out to the rim, shown from
    /// <see cref="CelestialRingEmitter.GapForecast"/> until that ring launches
    /// (or is retired). It brightens as the launch nears, read from the
    /// forecast's own countdown, so a slowed Hypatia's forecast visibly holds.
    ///
    /// The wedge is the gap's angular width exactly. Close to her the capsule
    /// has to fit through, so the truly safe part is narrower than the wedge
    /// there (RingMath.CapsuleFitsGap); the edges are drawn brighter so the
    /// player aims for the middle.
    ///
    /// Presentation only: subscribes in OnEnable, unsubscribes in OnDisable, and
    /// redraws any pending forecasts it missed (after a reload) from
    /// <see cref="CelestialRingEmitter.TryGetForecast"/>.
    /// </summary>
    public class GapForecastView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        struct Entry
        {
            public int Id;
            public GameObject View;
            public Renderer Renderer;
            public float Lead;
        }

        [Header("Source")]
        [Tooltip("The rings whose gaps to forecast.")]
        [SerializeField] private CelestialRingEmitter emitter;
        [Tooltip("Her brain, for the arena radius the wedge reaches out to.")]
        [SerializeField] private HypatiaBoss boss;

        [Header("Pool")]
        [Tooltip("Inactive template with a MeshFilter + MeshRenderer; the wedge mesh is built at runtime.")]
        [SerializeField] private GameObject wedgeTemplate;
        [Tooltip("Where wedge clones live (emptied on enable).")]
        [SerializeField] private Transform poolRoot;

        [Header("Look")]
        [Tooltip("Wedge colour at launch. Alpha is the brightest it gets.")]
        [SerializeField] private Color colour = new(0.55f, 0.95f, 1f, 0.75f);
        [Tooltip("Fraction (0-1) of full brightness when the forecast first appears.")]
        [SerializeField, Range(0f, 1f)] private float startBrightness = 0.35f;
        [Tooltip("Pulses per second while the forecast waits.")]
        [SerializeField, Min(0f)] private float pulseHz = 3f;
        [Tooltip("Wedge lifted this far off the plate, m.")]
        [SerializeField, Min(0f)] private float floorLift = 0.025f;

        ViewPool _pool;
        List<Entry> _entries;
        MaterialPropertyBlock _block;
        Mesh _mesh;
        float _meshWidth = -1f, _meshInner = -1f, _meshOuter = -1f;
        CelestialRingEmitter _subscribed;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            _entries ??= new List<Entry>(8);
            _entries.Clear();
            _pool = new ViewPool(wedgeTemplate, poolRoot, 4);

            _subscribed = emitter;
            if (_subscribed == null) return;
            _subscribed.GapForecast += OnForecast;
            _subscribed.RingLaunched += OnGone;
            _subscribed.RingRetired += OnGone;

            for (int i = 0; i < _subscribed.PendingForecasts; i++)
                if (_subscribed.TryGetForecast(i, out RingSnapshot pending)) OnForecast(pending);
        }

        void OnDisable()
        {
            if (_subscribed != null)
            {
                _subscribed.GapForecast -= OnForecast;
                _subscribed.RingLaunched -= OnGone;
                _subscribed.RingRetired -= OnGone;
            }
            _subscribed = null;
            _pool?.ReleaseAll();
            _entries?.Clear();
        }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }

        void OnForecast(RingSnapshot forecast)
        {
            var go = _pool.Get();
            if (go == null) return;

            float outer = boss != null && boss.Config != null ? boss.Config.ArenaRadius : 13f;
            EnsureMesh(forecast.GapWidthDegrees, forecast.Radius, outer);
            if (go.TryGetComponent(out MeshFilter filter)) filter.sharedMesh = _mesh;

            go.transform.SetPositionAndRotation(emitter.Centre + Vector3.up * floorLift,
                                                Quaternion.Euler(0f, forecast.GapCentreDegrees, 0f));
            var entry = new Entry
            {
                Id = forecast.Id,
                View = go,
                Renderer = go.GetComponent<Renderer>(),
                Lead = Mathf.Max(0.01f, forecast.LeadSeconds),
            };
            _entries.Add(entry);
            Tint(entry, startBrightness);
        }

        void OnGone(int id)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Id != id) continue;
                _pool.Release(_entries[i].View);
                _entries.RemoveAt(i);
            }
        }

        void LateUpdate()
        {
            if (_entries == null || emitter == null) return;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * pulseHz * Mathf.PI * 2f);
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                float left = emitter.TryGetRingById(e.Id, out RingSnapshot s) ? s.LeadSeconds : 0f;
                float ramp = 1f - Mathf.Clamp01(left / e.Lead);
                Tint(e, Mathf.Lerp(startBrightness, 1f, ramp) * pulse);
            }
        }

        void Tint(Entry entry, float brightness)
        {
            if (entry.Renderer == null) return;
            _block.SetColor(BaseColorId, new Color(colour.r, colour.g, colour.b, colour.a * brightness));
            entry.Renderer.SetPropertyBlock(_block);
        }

        void EnsureMesh(float width, float inner, float outer)
        {
            if (_mesh != null && Mathf.Approximately(width, _meshWidth) && Mathf.Approximately(inner, _meshInner)
                && Mathf.Approximately(outer, _meshOuter)) return;
            _meshWidth = width;
            _meshInner = inner;
            _meshOuter = outer;
            if (_mesh == null) _mesh = new Mesh { name = "GapWedge" };
            ArcMesh.Sector(_mesh, inner, outer, width, 24, centreAlpha: 0.45f, edgeAlpha: 1f);
        }
    }
}
