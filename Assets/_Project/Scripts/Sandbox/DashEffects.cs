using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Visuals for the 45° dash: afterimage ghosts, the protractor arc, and a
    /// camera FOV punch.
    ///
    /// The ghosts are the part that sells it. A dash lasts ~0.22s, which is far
    /// too short for any animation to read — what the eye actually registers is
    /// the streak of bodies left behind. Each ghost is a one-off snapshot taken
    /// with SkinnedMeshRenderer.BakeMesh, which freezes the *posed* mesh into a
    /// static one; a plain material swap would just move with the character and
    /// leave nothing behind.
    /// </summary>
    public class DashEffects : MonoBehaviour
    {
        [Header("Afterimages")]
        [SerializeField] private SkinnedMeshRenderer[] sources;
        [SerializeField] private Material ghostMaterial;
        [Tooltip("Seconds between snapshots. ~0.04 gives 5-6 ghosts over a dash.")]
        [SerializeField] private float ghostInterval = 0.04f;
        [SerializeField] private float ghostLifetime = 0.35f;
        [SerializeField] private Color ghostTint = new(0.55f, 0.85f, 1f, 0.55f);

        [Header("Protractor arc")]
        [SerializeField] private DashArc arc;

        [Header("Camera")]
        [SerializeField] private SandboxOrbitCamera cameraRig;
        [SerializeField] private float fovPunch = 8f;
        [SerializeField] private float fovPunchSeconds = 0.28f;

        readonly List<Ghost> _ghosts = new();
        float _emitting;
        float _nextGhost;

        struct Ghost
        {
            public GameObject Go;
            public Material Mat;
            public float Born;
            public Mesh Mesh;
        }

        void Awake()
        {
            if (sources == null || sources.Length == 0)
                sources = GetComponentsInChildren<SkinnedMeshRenderer>();
        }

        /// <summary>Fire everything. <paramref name="direction"/> is the world-space
        /// dash vector, used to orient the arc.</summary>
        public void Play(Vector3 direction, float duration)
        {
            _emitting = duration;
            _nextGhost = 0f;
            if (arc != null) arc.Play(transform.position, direction);
            if (cameraRig != null) cameraRig.PunchFov(fovPunch, fovPunchSeconds);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;

            if (_emitting > 0f)
            {
                _emitting -= dt;
                _nextGhost -= dt;
                if (_nextGhost <= 0f)
                {
                    _nextGhost = ghostInterval;
                    SpawnGhost();
                }
            }

            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                var g = _ghosts[i];
                float age = (Time.time - g.Born) / Mathf.Max(ghostLifetime, 0.01f);
                if (age >= 1f || g.Go == null)
                {
                    if (g.Go != null) Destroy(g.Go);
                    if (g.Mesh != null) Destroy(g.Mesh);
                    if (g.Mat != null) Destroy(g.Mat);
                    _ghosts.RemoveAt(i);
                    continue;
                }
                var c = ghostTint;
                c.a *= 1f - age;                       // fade out
                if (g.Mat.HasProperty("_BaseColor")) g.Mat.SetColor("_BaseColor", c);
                if (g.Mat.HasProperty("_Color")) g.Mat.SetColor("_Color", c);
                g.Go.transform.localScale = Vector3.one * (1f + age * 0.06f);
            }
        }

        void SpawnGhost()
        {
            if (ghostMaterial == null || sources == null) return;

            foreach (var smr in sources)
            {
                if (smr == null || !smr.enabled) continue;

                var mesh = new Mesh { name = "DashGhost" };
                smr.BakeMesh(mesh, true);          // true = bake in local scale

                var go = new GameObject("DashGhost");
                go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;

                var mat = new Material(ghostMaterial);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                _ghosts.Add(new Ghost { Go = go, Mat = mat, Born = Time.time, Mesh = mesh });
            }
        }
    }
}
