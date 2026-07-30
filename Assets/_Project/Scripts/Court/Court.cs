using UnityEngine;

namespace Pickleball
{
    /// <summary>
    /// Builds the pickleball court from primitives at runtime and answers geometric
    /// zone/side queries. Coordinate convention:
    ///   X = court width (sideline to sideline)
    ///   Z = court length; player baseline at -Z, AI baseline at +Z, net at Z = 0
    ///   Y = up; the play surface top sits at Y = 0.
    /// Zone resolution is geometric (see <see cref="ResolveZone"/>) rather than relying on
    /// overlapping trigger colliders, which keeps bounce detection unambiguous. The matching
    /// tag names (Kitchen / CourtLeft / CourtRight / OutOfBounds) are registered by the editor
    /// scene builder and mirrored by the <see cref="CourtZone"/> enum.
    /// </summary>
    public class Court : MonoBehaviour
    {
        [Header("Dimensions (metres) — 44ft x 20ft pickleball court")]
        [SerializeField] private float length = 13.41f;
        [SerializeField] private float width = 6.10f;
        [SerializeField] private float netHeight = 0.86f;
        [SerializeField] private float kitchenDepth = 2.13f;

        [Tooltip("Build the court geometry automatically on Awake.")]
        [SerializeField] private bool buildOnAwake = true;

        public float Length => length;
        public float Width => width;
        public float NetHeight => netHeight;
        public float KitchenDepth => kitchenDepth;
        public float HalfLength => length * 0.5f;
        public float HalfWidth => width * 0.5f;

        /// <summary>Z of the player's baseline (the back of the player's court).</summary>
        public float PlayerBaselineZ => -HalfLength;
        /// <summary>Z of the AI's baseline.</summary>
        public float AiBaselineZ => HalfLength;

        private bool _built;

        private void Awake()
        {
            // Skip if geometry already exists (e.g. it was pre-built via the editor preview) to avoid duplicating it.
            if (buildOnAwake && transform.childCount == 0) Build();
        }

        // ---- Geometric queries -------------------------------------------------

        /// <summary>True if the (x,z) of a world position lies within the painted court rectangle.</summary>
        public bool IsInBounds(Vector3 worldPos)
        {
            const float tol = 0.02f;
            return Mathf.Abs(worldPos.x) <= HalfWidth + tol &&
                   Mathf.Abs(worldPos.z) <= HalfLength + tol;
        }

        /// <summary>Which half of the court a world position is on (by net side).</summary>
        public CourtSide SideOf(Vector3 worldPos) => worldPos.z < 0f ? CourtSide.Player : CourtSide.AI;

        /// <summary>Resolves the logical zone for a contact position.</summary>
        public CourtZone ResolveZone(Vector3 worldPos)
        {
            if (!IsInBounds(worldPos)) return CourtZone.OutOfBounds;
            if (Mathf.Abs(worldPos.z) <= kitchenDepth) return CourtZone.Kitchen;
            return worldPos.x < 0f ? CourtZone.CourtLeft : CourtZone.CourtRight;
        }

        // ---- Procedural construction ------------------------------------------

        public void Build()
        {
            if (_built) return;
            _built = true;

            BuildSurface();
            BuildKitchen();
            BuildLines();
            BuildNet();
            BuildGround();
        }

#if UNITY_EDITOR
        /// <summary>Editor helper: clears any existing geometry and rebuilds so you can preview the court without entering Play mode.</summary>
        [ContextMenu("Rebuild Court (Editor Preview)")]
        private void EditorRebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                PrimitiveFactory.SafeDestroy(transform.GetChild(i).gameObject);
            }
            _built = false;
            Build();
        }
#endif

        private void BuildSurface()
        {
            var surface = PrimitiveFactory.CreateBox("Surface",
                new Vector3(width, 0.1f, length), Palette.CourtSurface, transform);
            surface.transform.localPosition = new Vector3(0f, -0.05f, 0f);
        }

        private void BuildKitchen()
        {
            // Two non-volley-zone strips either side of the net, drawn just above the surface.
            CreateOverlay("Kitchen_Player", kitchenDepth, -kitchenDepth * 0.5f);
            CreateOverlay("Kitchen_AI", kitchenDepth, kitchenDepth * 0.5f);

            void CreateOverlay(string name, float depth, float centerZ)
            {
                var go = PrimitiveFactory.CreateBox(name,
                    new Vector3(width, 0.02f, depth), Palette.Kitchen, transform, withCollider: false);
                go.transform.localPosition = new Vector3(0f, 0.011f, centerZ);
            }
        }

        private void BuildLines()
        {
            const float t = 0.05f;     // line thickness
            const float h = 0.012f;    // line height above surface
            const float y = 0.025f;

            // Sidelines (run the full length along each X edge).
            CreateLine("Sideline_L", new Vector3(t, h, length), new Vector3(-HalfWidth, y, 0f));
            CreateLine("Sideline_R", new Vector3(t, h, length), new Vector3(HalfWidth, y, 0f));

            // Baselines.
            CreateLine("Baseline_Player", new Vector3(width, h, t), new Vector3(0f, y, -HalfLength));
            CreateLine("Baseline_AI", new Vector3(width, h, t), new Vector3(0f, y, HalfLength));

            // Kitchen (non-volley) lines.
            CreateLine("KitchenLine_Player", new Vector3(width, h, t), new Vector3(0f, y, -kitchenDepth));
            CreateLine("KitchenLine_AI", new Vector3(width, h, t), new Vector3(0f, y, kitchenDepth));

            // Centre service lines (only between kitchen line and baseline, as in real pickleball).
            float midZ = (HalfLength + kitchenDepth) * 0.5f;
            float segLen = HalfLength - kitchenDepth;
            CreateLine("Centerline_Player", new Vector3(t, h, segLen), new Vector3(0f, y, -midZ));
            CreateLine("Centerline_AI", new Vector3(t, h, segLen), new Vector3(0f, y, midZ));

            void CreateLine(string name, Vector3 size, Vector3 pos)
            {
                var go = PrimitiveFactory.CreateBox(name, size, Palette.Line, transform, withCollider: false);
                go.transform.localPosition = pos;
            }
        }

        private void BuildNet()
        {
            var net = PrimitiveFactory.CreateBox("Net",
                new Vector3(width, netHeight, 0.06f), Palette.Net, transform);
            net.transform.localPosition = new Vector3(0f, netHeight * 0.5f, 0f);

            // Posts just outside the sidelines.
            CreatePost("NetPost_L", -HalfWidth - 0.1f);
            CreatePost("NetPost_R", HalfWidth + 0.1f);

            void CreatePost(string name, float x)
            {
                var post = PrimitiveFactory.CreateBox(name,
                    new Vector3(0.08f, netHeight + 0.1f, 0.08f), Palette.NetPost, transform);
                post.transform.localPosition = new Vector3(x, (netHeight + 0.1f) * 0.5f, 0f);
            }
        }

        private void BuildGround()
        {
            // Ground keeps its collider so balls landing outside the court rectangle still
            // register a bounce, which ResolveZone reports as OutOfBounds.
            var ground = PrimitiveFactory.CreateBox("Ground",
                new Vector3(width * 3f, 0.1f, length * 1.6f), Palette.Ground, transform, withCollider: true);
            ground.transform.localPosition = new Vector3(0f, -0.12f, 0f);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f,
                new Vector3(width, 1f, length));
        }
#endif
    }
}
