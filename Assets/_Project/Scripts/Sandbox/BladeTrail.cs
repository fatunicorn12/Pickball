using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Ribbon trail swept between the blade's base and tip.
    ///
    /// Built as a mesh rather than a TrailRenderer: a TrailRenderer follows a
    /// single point and produces a thin wire, whereas a slash needs the whole
    /// width of the blade smeared across its arc. Sampling both ends each frame
    /// and stitching quads between successive samples gives that.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class BladeTrail : MonoBehaviour
    {
        [SerializeField] private Transform bladeBase;
        [SerializeField] private Transform bladeTip;
        [Tooltip("How long each sample stays before fading out.")]
        [SerializeField] private float lifetime = 0.18f;
        [Tooltip("Cap on samples. The arc of one swing rarely needs more.")]
        [SerializeField] private int maxSamples = 24;
        [Tooltip("Skip samples closer together than this, so a slow swing does " +
                 "not pile up degenerate slivers.")]
        [SerializeField] private float minSampleDistance = 0.02f;

        struct Sample
        {
            public Vector3 A, B;
            public float Born;
        }

        readonly List<Sample> _samples = new();
        Mesh _mesh;
        MeshRenderer _renderer;
        bool _emitting;

        readonly List<Vector3> _verts = new();
        readonly List<Vector2> _uvs = new();
        readonly List<Color> _colors = new();
        readonly List<int> _tris = new();

        void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _mesh = new Mesh { name = "BladeTrail" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            // World space: the ribbon must stay where it was swung, not ride
            // along with the character.
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _renderer.enabled = false;
        }

        public void SetEmitting(bool on) => _emitting = on;

        void LateUpdate()
        {
            if (_emitting && bladeBase != null && bladeTip != null)
            {
                bool far = _samples.Count == 0 ||
                           Vector3.Distance(_samples[^1].B, bladeTip.position) > minSampleDistance;
                if (far)
                {
                    _samples.Add(new Sample
                    {
                        A = bladeBase.position,
                        B = bladeTip.position,
                        Born = Time.time,
                    });
                    if (_samples.Count > maxSamples) _samples.RemoveAt(0);
                }
            }

            for (int i = _samples.Count - 1; i >= 0; i--)
                if (Time.time - _samples[i].Born > lifetime)
                    _samples.RemoveAt(i);

            Rebuild();
        }

        void Rebuild()
        {
            _verts.Clear(); _uvs.Clear(); _colors.Clear(); _tris.Clear();

            if (_samples.Count < 2)
            {
                _mesh.Clear();
                _renderer.enabled = false;
                return;
            }
            _renderer.enabled = true;

            for (int i = 0; i < _samples.Count; i++)
            {
                var s = _samples[i];
                float age = Mathf.Clamp01((Time.time - s.Born) / Mathf.Max(lifetime, 0.001f));
                // Fade with age, and taper along the ribbon so the oldest end
                // thins out instead of ending in a hard edge.
                float alpha = (1f - age) * (i / (float)(_samples.Count - 1) * 0.6f + 0.4f);
                var c = new Color(1f, 1f, 1f, alpha);

                _verts.Add(s.A); _verts.Add(s.B);
                float u = i / (float)(_samples.Count - 1);
                _uvs.Add(new Vector2(u, 0f)); _uvs.Add(new Vector2(u, 1f));
                _colors.Add(c); _colors.Add(c);
            }

            for (int i = 0; i < _samples.Count - 1; i++)
            {
                int a = i * 2;
                _tris.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
                // Back faces too — a slash gets seen from both sides.
                _tris.AddRange(new[] { a, a + 3, a + 1, a, a + 2, a + 3 });
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }

        public void Bind(Transform bladeBaseTransform, Transform bladeTipTransform)
        {
            bladeBase = bladeBaseTransform;
            bladeTip = bladeTipTransform;
        }
    }
}
