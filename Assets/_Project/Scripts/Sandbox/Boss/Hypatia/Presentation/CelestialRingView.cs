using Pickleball.Sandbox.Boss.Hypatia.Rings;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Draws the live Celestial Rings: each one a thin wall standing on the plate
    /// at the ring's radius with its gap cut out. Low rings are as tall as they
    /// really hit (Low Ring Height); tall rings are drawn to Tall Ring Visual
    /// Height with a faint column of light above, because a tall ring hits at
    /// ANY height and the column says "you cannot jump this"; phase-2 burning
    /// rings use fire colours.
    ///
    /// Presentation only. It reads <see cref="CelestialRingEmitter.TryGetRing"/>
    /// every frame (value snapshots, no allocation) and never calls into the
    /// simulation. One wall mesh per gap width, shared by every ring and turned
    /// to each ring's gap azimuth; the walls come from a <see cref="ViewPool"/>.
    /// </summary>
    public class CelestialRingView : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Source")]
        [Tooltip("The rings to draw.")]
        [SerializeField] private CelestialRingEmitter emitter;

        [Header("Pool")]
        [Tooltip("Inactive template: a root with children 'Wall' and 'Column' (MeshFilter + MeshRenderer).")]
        [SerializeField] private GameObject ringTemplate;
        [Tooltip("Where ring clones live (emptied on enable).")]
        [SerializeField] private Transform poolRoot;
        [Tooltip("Walls made up front, so the first rings never allocate.")]
        [SerializeField, Min(0)] private int prewarm = 8;

        [Header("Look")]
        [Tooltip("Colour of a low ring (jump it). Alpha scales the whole wall.")]
        [SerializeField] private Color lowColour = new(0.55f, 0.7f, 1f, 0.8f);
        [Tooltip("Colour of a tall ring (only the gap gets through).")]
        [SerializeField] private Color tallColour = new(1f, 0.86f, 0.52f, 0.4f);
        [Tooltip("Colour of a burning ring (phase 2; acts tall).")]
        [SerializeField] private Color burningColour = new(1f, 0.42f, 0.12f, 0.45f);
        [Tooltip("Colour of the light column above a tall ring. Keep alpha low: it is a hint, not a wall.")]
        [SerializeField] private Color columnColour = new(1f, 0.92f, 0.7f, 0.07f);
        [Tooltip("Height of the light column above a tall ring's wall, m.")]
        [SerializeField, Min(0f)] private float columnHeight = 8f;
        [Tooltip("Segments round a full wall. Higher = rounder rings, more vertices.")]
        [SerializeField, Range(24, 256)] private int segments = 120;
        [Tooltip("Wall lifted this far off the plate, m, so it never z-fights the floor.")]
        [SerializeField, Min(0f)] private float floorLift = 0.01f;

        ViewPool _pool;
        GameObject[] _views;
        Transform[] _walls, _columns;
        Renderer[] _wallRenderers, _columnRenderers;
        MaterialPropertyBlock _block;
        Mesh _wallMesh, _columnMesh;
        float _meshGap = -1f;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            _pool = new ViewPool(ringTemplate, poolRoot, 0);
            int capacity = Mathf.Max(1, prewarm);
            _views = new GameObject[capacity];
            _walls = new Transform[capacity];
            _columns = new Transform[capacity];
            _wallRenderers = new Renderer[capacity];
            _columnRenderers = new Renderer[capacity];
            for (int i = 0; i < capacity; i++) Ensure(i);
            for (int i = 0; i < capacity; i++) Hide(i);
        }

        void OnDisable()
        {
            if (_views == null) return;
            for (int i = 0; i < _views.Length; i++) Hide(i);
        }

        void OnDestroy()
        {
            DestroyMesh(ref _wallMesh);
            DestroyMesh(ref _columnMesh);
        }

        void LateUpdate()
        {
            if (emitter == null || _views == null) return;
            int live = emitter.LiveRings;
            Vector3 centre = emitter.Centre + Vector3.up * floorLift;

            for (int slot = 0; slot < live; slot++)
            {
                if (!emitter.TryGetRing(slot, out RingSnapshot ring)) continue;
                if (!Ensure(slot)) break;
                EnsureMeshes(ring.GapWidthDegrees);

                var view = _views[slot];
                if (!view.activeSelf) view.SetActive(true);
                view.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, ring.GapCentreDegrees, 0f));

                float r = Mathf.Max(0.01f, ring.Radius);
                float h = Mathf.Max(0.01f, ring.VisualHeight);
                _walls[slot].localScale = new Vector3(r, h, r);

                Color wall = ring.Burning ? burningColour : ring.Tall ? tallColour : lowColour;
                Tint(_wallRenderers[slot], wall);

                bool column = ring.Tall && columnHeight > 0f;
                var columnGo = _columns[slot].gameObject;
                if (columnGo.activeSelf != column) columnGo.SetActive(column);
                if (column)
                {
                    _columns[slot].localPosition = new Vector3(0f, h, 0f);
                    _columns[slot].localScale = new Vector3(r, columnHeight, r);
                    Color c = ring.Burning
                        ? new Color(burningColour.r, burningColour.g, burningColour.b, columnColour.a)
                        : columnColour;
                    Tint(_columnRenderers[slot], c);
                }
            }

            for (int slot = live; slot < _views.Length; slot++) Hide(slot);
        }

        /// <summary>Make sure slot <paramref name="slot"/> has a wall; grows the
        /// slot arrays only if more rings are live than were prewarmed.</summary>
        bool Ensure(int slot)
        {
            if (slot >= _views.Length)
            {
                int size = Mathf.Max(slot + 1, _views.Length * 2);
                System.Array.Resize(ref _views, size);
                System.Array.Resize(ref _walls, size);
                System.Array.Resize(ref _columns, size);
                System.Array.Resize(ref _wallRenderers, size);
                System.Array.Resize(ref _columnRenderers, size);
            }
            if (_views[slot] != null) return true;
            var go = _pool.Get();
            if (go == null) return false;
            _views[slot] = go;
            _walls[slot] = go.transform.Find("Wall");
            _columns[slot] = go.transform.Find("Column");
            if (_walls[slot] == null || _columns[slot] == null)
            {
                Debug.LogWarning($"[CelestialRingView] Ring template needs 'Wall' and 'Column' children; " +
                                 "re-run Tools ▸ Pickleball ▸ Setup Hypatia Fight.", this);
                return false;
            }
            _wallRenderers[slot] = _walls[slot].GetComponent<Renderer>();
            _columnRenderers[slot] = _columns[slot].GetComponent<Renderer>();
            if (_wallMesh != null) Assign(slot);
            return true;
        }

        void Hide(int slot)
        {
            if (_views[slot] != null && _views[slot].activeSelf) _views[slot].SetActive(false);
        }

        /// <summary>(Re)build the shared meshes when the gap width changes:
        /// once per fight in practice, since the width is a config value.</summary>
        void EnsureMeshes(float gapDegrees)
        {
            if (_wallMesh != null && Mathf.Approximately(gapDegrees, _meshGap)) return;
            _meshGap = gapDegrees;
            if (_wallMesh == null) _wallMesh = new Mesh { name = "RingWall" };
            if (_columnMesh == null) _columnMesh = new Mesh { name = "RingColumn" };
            ArcMesh.Wall(_wallMesh, gapDegrees, segments, bottomAlpha: 0.9f, topAlpha: 0.12f, edgeAlpha: 1f);
            ArcMesh.Wall(_columnMesh, gapDegrees, segments, bottomAlpha: 0.6f, topAlpha: 0f, edgeAlpha: 0.6f);
            for (int i = 0; i < _views.Length; i++)
                if (_views[i] != null && _walls[i] != null) Assign(i);
        }

        void Assign(int slot)
        {
            if (_walls[slot].TryGetComponent(out MeshFilter wall)) wall.sharedMesh = _wallMesh;
            if (_columns[slot].TryGetComponent(out MeshFilter column)) column.sharedMesh = _columnMesh;
        }

        void Tint(Renderer renderer, Color colour)
        {
            if (renderer == null) return;
            _block.SetColor(BaseColorId, colour);
            renderer.SetPropertyBlock(_block);
        }

        static void DestroyMesh(ref Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }
    }
}
