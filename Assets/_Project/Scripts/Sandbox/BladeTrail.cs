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
    ///
    /// Colour — the per-step tint and the fade with age — goes out as vertex
    /// colour, so the material must be one that multiplies it in (URP
    /// Particles/Unlit; Setup Sword Abilities assigns one). Plain URP/Unlit
    /// ignores vertex colour, which is why the ribbon used to end in a hard edge.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class BladeTrail : MonoBehaviour
    {
        [SerializeField] private Transform bladeBase;
        [SerializeField] private Transform bladeTip;
        [Tooltip("Seconds each sample stays before fading out. Longer smears " +
                 "more of the arc; past ~0.25 s the ribbon lags the blade.")]
        [Min(0.01f)] [SerializeField] private float lifetime = 0.18f;
        [Tooltip("Cap on samples. The arc of one swing rarely needs more.")]
        [Min(2)] [SerializeField] private int maxSamples = 24;
        [Tooltip("Skip samples closer together than this, in metres, so a slow " +
                 "swing does not pile up degenerate slivers.")]
        [Min(0f)] [SerializeField] private float minSampleDistance = 0.02f;

        struct Sample
        {
            public Vector3 A, B;
            public float Born;
            public Color Tint;
        }

        // Buffers are (re)built in OnEnable, not readonly initializers: OnEnable
        // runs again after a script reload in Play mode, Awake does not.
        List<Sample> _samples;
        Mesh _mesh;
        MeshRenderer _renderer;
        bool _emitting;
        Color _tint = Color.white;

        List<Vector3> _verts;
        List<Vector2> _uvs;
        List<Color> _colors;
        List<int> _tris;

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

        void OnEnable()
        {
            _samples ??= new List<Sample>();
            _verts ??= new List<Vector3>();
            _uvs ??= new List<Vector2>();
            _colors ??= new List<Color>();
            _tris ??= new List<int>();
        }

        public void SetEmitting(bool on) => _emitting = on;

        /// <summary>Colour for samples laid from now on. Samples already in the
        /// ribbon keep theirs, so a chain's hits stay visibly different as they
        /// overlap.</summary>
        public void SetTint(Color tint) => _tint = tint;

        void LateUpdate()
        {
            if (_mesh == null || _samples == null) return;
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
                        Tint = _tint,
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
                var c = s.Tint;
                c.a *= alpha;

                _verts.Add(s.A); _verts.Add(s.B);
                float u = i / (float)(_samples.Count - 1);
                _uvs.Add(new Vector2(u, 0f)); _uvs.Add(new Vector2(u, 1f));
                _colors.Add(c); _colors.Add(c);
            }

            for (int i = 0; i < _samples.Count - 1; i++)
            {
                int a = i * 2;
                // Front faces, then back faces — a slash gets seen from both
                // sides. Added one by one: AddRange(new[] {…}) allocated two
                // arrays per segment per frame.
                _tris.Add(a); _tris.Add(a + 1); _tris.Add(a + 3);
                _tris.Add(a); _tris.Add(a + 3); _tris.Add(a + 2);
                _tris.Add(a); _tris.Add(a + 3); _tris.Add(a + 1);
                _tris.Add(a); _tris.Add(a + 2); _tris.Add(a + 3);
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
