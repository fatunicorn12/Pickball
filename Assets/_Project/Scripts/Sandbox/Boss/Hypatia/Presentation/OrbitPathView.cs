using System.Collections.Generic;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// A faint ellipse traced on the plate for each live orbiter, so the player
    /// can read the whole orbit (and where it whips round her at periapsis)
    /// instead of only the ball. Drawn once from the orbiter's
    /// <see cref="ConicOrbiter.Orbit"/> and <see cref="ConicOrbiter.Plane"/> on
    /// <see cref="ConicCaster.OrbiterSpawned"/>, faded out over the orbiter's last
    /// seconds, released on <see cref="ConicCaster.OrbiterRetired"/> or
    /// <see cref="ConicCaster.Stopped"/>.
    ///
    /// The ellipse is the orbit's true shape, r = a(1 − e²)/(1 + e cos θ) about
    /// the focus where she stood at spawn, laid on the floor under the orbit
    /// plane. Presentation only; pooled LineRenderers; no per-frame allocation.
    /// </summary>
    public class OrbitPathView : MonoBehaviour
    {
        struct Path
        {
            public ConicOrbiter Orbiter;
            public LineRenderer Line;
        }

        [Header("Source")]
        [Tooltip("The caster whose orbiters to trace.")]
        [SerializeField] private ConicCaster caster;
        [Tooltip("Her brain, for the floor height.")]
        [SerializeField] private HypatiaBoss boss;

        [Header("Pool")]
        [Tooltip("Inactive template with a LineRenderer (loop, world space).")]
        [SerializeField] private GameObject lineTemplate;
        [Tooltip("Where clones live (emptied on enable).")]
        [SerializeField] private Transform poolRoot;

        [Header("Look")]
        [Tooltip("Points round each ellipse. Higher = smoother at a sharp periapsis.")]
        [SerializeField, Range(16, 256)] private int points = 96;
        [Tooltip("Line colour; alpha is the brightest it gets.")]
        [SerializeField] private Color colour = new(0.62f, 0.85f, 1f, 0.45f);
        [Tooltip("Seconds over which the line fades before the orbiter expires.")]
        [SerializeField, Min(0f)] private float fadeSeconds = 1.5f;
        [Tooltip("Line lifted this far off the plate, m.")]
        [SerializeField, Min(0f)] private float floorLift = 0.04f;

        ViewPool _pool;
        List<Path> _paths;
        ConicCaster _subscribed;

        void OnEnable()
        {
            _paths ??= new List<Path>(8);
            _paths.Clear();
            _pool = new ViewPool(lineTemplate, poolRoot, 5);

            _subscribed = caster;
            if (_subscribed == null) return;
            _subscribed.OrbiterSpawned += OnSpawned;
            _subscribed.OrbiterRetired += OnRetired;
            _subscribed.Stopped += ClearAll;

            var registry = _subscribed.Registry;
            if (registry == null) return;
            for (int i = 0; i < registry.Count; i++)
                if (registry.At(i) != null) OnSpawned(registry.At(i));
        }

        void OnDisable()
        {
            if (_subscribed != null)
            {
                _subscribed.OrbiterSpawned -= OnSpawned;
                _subscribed.OrbiterRetired -= OnRetired;
                _subscribed.Stopped -= ClearAll;
            }
            _subscribed = null;
            ClearAll();
        }

        void OnSpawned(ConicOrbiter orbiter)
        {
            var go = _pool.Get();
            if (go == null || !go.TryGetComponent(out LineRenderer line)) return;

            KeplerOrbit orbit = orbiter.Orbit;
            ConicPlane plane = orbiter.Plane;
            float floor = (boss != null && boss.Context != null ? boss.Context.FloorY : plane.Focus.y - 1f) + floorLift;
            float a = orbit.SemiMajor, e = orbit.Eccentricity;
            float p = a * (1f - e * e);

            line.loop = true;
            line.useWorldSpace = true;
            line.positionCount = points;
            for (int i = 0; i < points; i++)
            {
                float theta = i * Mathf.PI * 2f / points;
                float r = p / (1f + e * Mathf.Cos(theta));
                Vector3 w = plane.ToWorld(new Vector2(r * Mathf.Cos(theta), r * Mathf.Sin(theta)));
                line.SetPosition(i, new Vector3(w.x, floor, w.z));
            }
            line.startColor = line.endColor = colour;
            _paths.Add(new Path { Orbiter = orbiter, Line = line });
        }

        void OnRetired(ConicOrbiter orbiter)
        {
            for (int i = _paths.Count - 1; i >= 0; i--)
            {
                if (_paths[i].Orbiter != orbiter) continue;
                _pool.Release(_paths[i].Line.gameObject);
                _paths.RemoveAt(i);
            }
        }

        void ClearAll()
        {
            if (_paths == null) return;
            for (int i = 0; i < _paths.Count; i++)
                if (_paths[i].Line != null) _pool?.Release(_paths[i].Line.gameObject);
            _paths.Clear();
        }

        void LateUpdate()
        {
            if (_paths == null) return;
            for (int i = _paths.Count - 1; i >= 0; i--)
            {
                Path path = _paths[i];
                if (path.Orbiter == null)
                {
                    // Destroyed without a retire event (scene teardown): just drop it.
                    if (path.Line != null) _pool.Release(path.Line.gameObject);
                    _paths.RemoveAt(i);
                    continue;
                }
                float left = path.Orbiter.Lifetime - path.Orbiter.Age;
                float alpha = fadeSeconds > 0f ? Mathf.Clamp01(left / fadeSeconds) : 1f;
                var c = new Color(colour.r, colour.g, colour.b, colour.a * alpha);
                path.Line.startColor = c;
                path.Line.endColor = c;
            }
        }
    }
}
