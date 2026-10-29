using System.Collections.Generic;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// The parabola's landing markers: a ring the size of the blast at each
    /// aimed point, filling in as impact nears, from
    /// <see cref="ConicCaster.LobAimed"/> until <see cref="ConicCaster.LobLanded"/>
    /// at the same point; then a short flash. The fill follows the time the
    /// caster promised, and the marker is cleared by the landing event rather
    /// than by that timer, because a slowed lob lands later than promised.
    ///
    /// Presentation only; pooled; cleared on <see cref="ConicCaster.Stopped"/>.
    /// </summary>
    public class LobMarkerView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        struct Marker
        {
            public GameObject View;
            public Transform Fill;
            public Vector3 Point;
            public float Start, Duration;
        }

        struct Flash
        {
            public GameObject View;
            public Renderer Renderer;
            public float Start, Radius;
        }

        [Header("Source")]
        [Tooltip("The caster whose lobs to mark.")]
        [SerializeField] private ConicCaster caster;
        [Tooltip("Her brain, for the blast radius and the floor height.")]
        [SerializeField] private HypatiaBoss boss;

        [Header("Pool")]
        [Tooltip("Inactive marker template: a root (scaled to the blast diameter) with a 'Fill' child.")]
        [SerializeField] private GameObject markerTemplate;
        [Tooltip("Inactive flash template: a flat disc with a Renderer.")]
        [SerializeField] private GameObject flashTemplate;
        [Tooltip("Where clones live (emptied on enable).")]
        [SerializeField] private Transform poolRoot;

        [Header("Look")]
        [Tooltip("Markers lifted this far off the plate, m.")]
        [SerializeField, Min(0f)] private float floorLift = 0.03f;
        [Tooltip("Seconds the landing flash lasts.")]
        [SerializeField, Min(0.05f)] private float flashSeconds = 0.4f;
        [Tooltip("Landing flash colour.")]
        [SerializeField] private Color flashColour = new(1f, 0.85f, 0.45f, 0.9f);

        ViewPool _markers, _flashes;
        List<Marker> _live;
        List<Flash> _flashing;
        MaterialPropertyBlock _block;
        ConicCaster _subscribed;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            _live ??= new List<Marker>(4);
            _flashing ??= new List<Flash>(4);
            _live.Clear();
            _flashing.Clear();
            var markerRoot = Container("Markers");
            var flashRoot = Container("Flashes");
            _markers = new ViewPool(markerTemplate, markerRoot, 2);
            _flashes = new ViewPool(flashTemplate, flashRoot, 2);

            _subscribed = caster;
            if (_subscribed == null) return;
            _subscribed.LobAimed += OnAimed;
            _subscribed.LobLanded += OnLanded;
            _subscribed.Stopped += ClearAll;
        }

        void OnDisable()
        {
            if (_subscribed != null)
            {
                _subscribed.LobAimed -= OnAimed;
                _subscribed.LobLanded -= OnLanded;
                _subscribed.Stopped -= ClearAll;
            }
            _subscribed = null;
            ClearAll();
        }

        Transform Container(string name)
        {
            if (poolRoot == null) return null;
            var t = poolRoot.Find(name);
            if (t != null) return t;
            t = new GameObject(name).transform;
            t.SetParent(poolRoot, false);
            return t;
        }

        float BlastRadius => boss != null && boss.Config != null ? boss.Config.LobBlastRadius : 1.6f;

        void OnAimed(Vector3 point, float secondsToImpact)
        {
            var go = _markers.Get();
            if (go == null) return;
            float d = BlastRadius * 2f;
            go.transform.SetPositionAndRotation(point + Vector3.up * floorLift, Quaternion.identity);
            go.transform.localScale = new Vector3(d, 1f, d);
            var fill = go.transform.Find("Fill");
            if (fill != null) fill.localScale = Vector3.zero;
            _live.Add(new Marker
            {
                View = go,
                Fill = fill,
                Point = point,
                Start = Time.time,
                Duration = Mathf.Max(0.05f, secondsToImpact),
            });
        }

        void OnLanded(Vector3 at, float radius)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _live.Count; i++)
            {
                float d = (_live[i].Point - at).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            if (best >= 0)
            {
                _markers.Release(_live[best].View);
                _live.RemoveAt(best);
            }

            var flash = _flashes.Get();
            if (flash == null) return;
            flash.transform.SetPositionAndRotation(at + Vector3.up * (floorLift + 0.01f), Quaternion.identity);
            _flashing.Add(new Flash
            {
                View = flash,
                Renderer = flash.GetComponentInChildren<Renderer>(),
                Start = Time.time,
                Radius = Mathf.Max(0.1f, radius),
            });
        }

        void ClearAll()
        {
            if (_live != null)
            {
                for (int i = 0; i < _live.Count; i++) _markers?.Release(_live[i].View);
                _live.Clear();
            }
            if (_flashing != null)
            {
                for (int i = 0; i < _flashing.Count; i++) _flashes?.Release(_flashing[i].View);
                _flashing.Clear();
            }
        }

        void LateUpdate()
        {
            if (_live == null) return;
            float now = Time.time;
            for (int i = 0; i < _live.Count; i++)
            {
                Marker m = _live[i];
                if (m.Fill == null) continue;
                float t = Mathf.Clamp01((now - m.Start) / m.Duration);
                m.Fill.localScale = new Vector3(t, 1f, t);
            }

            for (int i = _flashing.Count - 1; i >= 0; i--)
            {
                Flash f = _flashing[i];
                float t = (now - f.Start) / flashSeconds;
                if (t >= 1f)
                {
                    _flashes.Release(f.View);
                    _flashing.RemoveAt(i);
                    continue;
                }
                float d = f.Radius * 2f * Mathf.Lerp(0.6f, 1.25f, t);
                f.View.transform.localScale = new Vector3(d, 1f, d);
                if (f.Renderer != null)
                {
                    _block.SetColor(BaseColorId, new Color(flashColour.r, flashColour.g, flashColour.b,
                                                           flashColour.a * (1f - t)));
                    f.Renderer.SetPropertyBlock(_block);
                }
            }
        }
    }
}
