using Pickleball.Sandbox.Boss.Hypatia.Rings;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Draws the live Celestial Rings: each one a wall standing on the plate at the
    /// ring's radius with its gap cut out, in the Ring Wall shader. Low rings end in
    /// a crisp line at the height they really hit (Low Ring Height), the height to
    /// clear. Tall rings hit at ANY height, so they have no top: they dissolve upward
    /// into an aurora, which says "you cannot jump this". Phase-2 burning rings do
    /// the same in fire. Whatever the ring, its gap edges are the brightest thing on
    /// it and throw sparks, in the cyan of the floor's gap forecast.
    ///
    /// Presentation only. It reads <see cref="CelestialRingEmitter.TryGetRing"/>
    /// every frame (value snapshots, no allocation), listens to
    /// <see cref="CelestialRingEmitter.RingHitPlayer"/> for the impact, and never
    /// calls into the simulation. One wall mesh per gap width, shared by every ring
    /// and turned to each ring's gap azimuth; the walls come from a
    /// <see cref="ViewPool"/>.
    /// </summary>
    public class CelestialRingView : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int HeightId = Shader.PropertyToID("_Height");
        static readonly int SolidHeightId = Shader.PropertyToID("_SolidHeight");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int GapDegreesId = Shader.PropertyToID("_GapDegrees");
        static readonly int TallId = Shader.PropertyToID("_Tall");
        static readonly int FireId = Shader.PropertyToID("_Fire");
        static readonly int SeedId = Shader.PropertyToID("_Seed");

        [Header("Source")]
        [Tooltip("The rings to draw.")]
        [SerializeField] private CelestialRingEmitter emitter;

        [Header("Pool")]
        [Tooltip("Inactive template: a root with a child 'Wall' (MeshFilter + MeshRenderer).")]
        [SerializeField] private GameObject ringTemplate;
        [Tooltip("Where ring clones live (emptied on enable).")]
        [SerializeField] private Transform poolRoot;
        [Tooltip("Walls made up front, so the first rings never allocate.")]
        [SerializeField, Min(0)] private int prewarm = 8;

        [Header("Look")]
        [ColorUsage(false, true)]
        [Tooltip("Colour of a low ring (jump it). Values over 1 bloom.")]
        [SerializeField] private Color lowColour = new(0.3f, 0.48f, 1.1f);
        [ColorUsage(false, true)]
        [Tooltip("Colour of a tall ring (only the gap gets through). Values over 1 bloom.")]
        [SerializeField] private Color tallColour = new(1.05f, 0.72f, 0.3f);
        [ColorUsage(false, true)]
        [Tooltip("Ember colour of a burning ring (phase 2; acts tall). The base runs hotter by itself.")]
        [SerializeField] private Color burningColour = new(1.3f, 0.36f, 0.08f);
        [ColorUsage(false, true)]
        [Tooltip("Gap edge colour on every ring. Matches the floor's gap forecast, and must stay the ring's brightest thing.")]
        [SerializeField] private Color gapEdgeColour = new(0.7f, 2.2f, 2.6f);
        [Tooltip("How far a tall ring's aurora carries on above its wall, m. It is a hint, not a hit area: a tall ring hits at any height anyway.")]
        [SerializeField, Min(0f)] private float auroraHeight = 8f;
        [Tooltip("Segments round a full wall. Higher = rounder rings, more vertices.")]
        [SerializeField, Range(24, 256)] private int segments = 120;
        [Tooltip("Wall lifted this far off the plate, m, so it never z-fights the floor.")]
        [SerializeField, Min(0f)] private float floorLift = 0.01f;

        [Header("Sparks")]
        [Tooltip("Shared world-space sparks: emitted at the gap edges, and in a burst where a ring hits the player.")]
        [SerializeField] private ParticleSystem sparks;
        [Tooltip("Sparks per second from each ring's gap edges. Higher = busier edges, easier to spot.")]
        [SerializeField, Min(0f)] private float edgeSparksPerSecond = 26f;
        [Tooltip("Sparks in the burst when a ring hits the player.")]
        [SerializeField, Min(0)] private int hitSparks = 36;

        [Header("Impact")]
        [Tooltip("The chase camera, for the kick when a ring hits the player. Optional.")]
        [SerializeField] private SandboxOrbitCamera cameraRig;
        [Tooltip("Shake when a ring hits the player, m. Sword hits are about 0.15.")]
        [SerializeField, Min(0f)] private float shakeOnHit = 0.3f;
        [Tooltip("Seconds the shake lasts.")]
        [SerializeField, Min(0.01f)] private float shakeSeconds = 0.3f;

        ViewPool _pool;
        GameObject[] _views;
        Transform[] _walls;
        Renderer[] _wallRenderers;
        MaterialPropertyBlock _block;
        Mesh _wallMesh;
        float _meshGap = -1f;
        float _sparksOwed;
        CelestialRingEmitter _subscribed;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            _pool = new ViewPool(ringTemplate, poolRoot, 0);
            int capacity = Mathf.Max(1, prewarm);
            _views = new GameObject[capacity];
            _walls = new Transform[capacity];
            _wallRenderers = new Renderer[capacity];
            for (int i = 0; i < capacity; i++) Ensure(i);
            for (int i = 0; i < capacity; i++) Hide(i);
            _subscribed = emitter;
            if (_subscribed != null) _subscribed.RingHitPlayer += OnRingHitPlayer;
        }

        void OnDisable()
        {
            if (_subscribed != null) _subscribed.RingHitPlayer -= OnRingHitPlayer;
            _subscribed = null;
            if (_views == null) return;
            for (int i = 0; i < _views.Length; i++) Hide(i);
        }

        void OnDestroy()
        {
            if (_wallMesh == null) return;
            if (Application.isPlaying) Destroy(_wallMesh);
            else DestroyImmediate(_wallMesh);
            _wallMesh = null;
        }

        void OnRingHitPlayer(int id, Vector3 point)
        {
            if (cameraRig != null) cameraRig.Shake(shakeOnHit, shakeSeconds);
            if (sparks == null) return;
            for (int i = 0; i < hitSparks; i++)
                EmitSpark(point, new Vector3(Random.Range(-1f, 1f), Random.Range(0.1f, 1.2f), Random.Range(-1f, 1f)) * 7f,
                          gapEdgeColour);
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
                EnsureMesh(ring.GapWidthDegrees);

                var view = _views[slot];
                if (!view.activeSelf) view.SetActive(true);
                view.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, ring.GapCentreDegrees, 0f));

                float r = Mathf.Max(0.01f, ring.Radius);
                float solid = Mathf.Max(0.01f, ring.VisualHeight);
                float drawn = RingLook.DrawnHeight(solid, ring.Tall, auroraHeight);
                _walls[slot].localScale = new Vector3(r, drawn, r);

                _block.Clear();
                _block.SetColor(ColorId, ring.Burning ? burningColour : ring.Tall ? tallColour : lowColour);
                _block.SetColor(EdgeColorId, gapEdgeColour);
                _block.SetFloat(HeightId, drawn);
                _block.SetFloat(SolidHeightId, solid);
                _block.SetFloat(RadiusId, r);
                _block.SetFloat(GapDegreesId, ring.GapWidthDegrees);
                _block.SetFloat(TallId, ring.Tall ? 1f : 0f);
                _block.SetFloat(FireId, ring.Burning ? 1f : 0f);
                _block.SetFloat(SeedId, Mathf.Repeat(ring.Id * 0.618034f, 1f));
                _wallRenderers[slot].SetPropertyBlock(_block);

                EmitEdgeSparks(ring, centre, r, solid);
            }

            for (int slot = live; slot < _views.Length; slot++) Hide(slot);
        }

        void EmitEdgeSparks(in RingSnapshot ring, Vector3 centre, float radius, float solid)
        {
            if (sparks == null) return;
            int due = RingLook.SparksDue(ref _sparksOwed, edgeSparksPerSecond, Time.deltaTime, 8);
            for (int i = 0; i < due; i++)
            {
                float azimuth = RingLook.GapEdgeAzimuth(ring.GapCentreDegrees, ring.GapWidthDegrees, i % 2 == 0 ? -1 : 1);
                Vector3 outward = RingMath.DirectionFromAzimuth(azimuth);
                Vector3 at = centre + outward * radius + Vector3.up * Random.Range(0f, solid);
                Vector3 velocity = outward * Random.Range(0.5f, 4f) + Vector3.up * Random.Range(0.3f, 2.5f);
                EmitSpark(at, velocity, gapEdgeColour);
            }
        }

        void EmitSpark(Vector3 position, Vector3 velocity, Color colour)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startColor = new Color(Mathf.Clamp01(colour.r * 0.4f), Mathf.Clamp01(colour.g * 0.4f), Mathf.Clamp01(colour.b * 0.4f)),
                applyShapeToPosition = false,
            };
            sparks.Emit(emit, 1);
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
                System.Array.Resize(ref _wallRenderers, size);
            }
            if (_views[slot] != null) return true;
            var go = _pool.Get();
            if (go == null) return false;
            _views[slot] = go;
            _walls[slot] = go.transform.Find("Wall");
            if (_walls[slot] == null)
            {
                Debug.LogWarning($"[CelestialRingView] Ring template needs a 'Wall' child; " +
                                 "re-run Tools ▸ Pickleball ▸ Setup Hypatia Fight.", this);
                return false;
            }
            _wallRenderers[slot] = _walls[slot].GetComponent<Renderer>();
            if (_wallMesh != null) Assign(slot);
            return true;
        }

        void Hide(int slot)
        {
            if (_views[slot] != null && _views[slot].activeSelf) _views[slot].SetActive(false);
        }

        /// <summary>(Re)build the shared mesh when the gap width changes: once per
        /// fight in practice, since the width is a config value.</summary>
        void EnsureMesh(float gapDegrees)
        {
            if (_wallMesh != null && Mathf.Approximately(gapDegrees, _meshGap)) return;
            _meshGap = gapDegrees;
            if (_wallMesh == null) _wallMesh = new Mesh { name = "RingWall" };
            ArcMesh.Wall(_wallMesh, gapDegrees, segments, bottomAlpha: 1f, topAlpha: 1f, edgeAlpha: 1f);
            for (int i = 0; i < _views.Length; i++)
                if (_views[i] != null && _walls[i] != null) Assign(i);
        }

        void Assign(int slot)
        {
            if (_walls[slot].TryGetComponent(out MeshFilter wall)) wall.sharedMesh = _wallMesh;
        }
    }
}
