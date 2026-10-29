using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// The hyperbola's warning: its two asymptotes as solid lines on the plate
    /// (the shot comes in near one and leaves near the other, never straying
    /// outside the wedge between them) and the exact path as a marching dashed
    /// line, from <see cref="ConicCaster.AsymptotesTelegraphed"/>. The asymptotes
    /// go when the shot is released; the path stays, fainter, until
    /// <see cref="ConicCaster.ShotFinished"/>. Everything clears on
    /// <see cref="ConicCaster.Stopped"/>.
    ///
    /// Everything is drawn on the floor (the shot flies at Hyperbola Height):
    /// from the chase camera a shape on the plate reads, a line in the air does not.
    /// Presentation only; three fixed LineRenderers; no per-frame allocation.
    /// </summary>
    public class AsymptoteTelegraphView : MonoBehaviour
    {
        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        [Header("Source")]
        [Tooltip("The caster whose flyby to telegraph.")]
        [SerializeField] private ConicCaster caster;
        [Tooltip("Her brain, for the floor height and the reach.")]
        [SerializeField] private HypatiaBoss boss;

        [Header("Parts")]
        [Tooltip("Incoming asymptote.")]
        [SerializeField] private LineRenderer incoming;
        [Tooltip("Outgoing asymptote.")]
        [SerializeField] private LineRenderer outgoing;
        [Tooltip("The dashed path (texture tiled along it).")]
        [SerializeField] private LineRenderer path;

        [Header("Look")]
        [Tooltip("Asymptote colour.")]
        [SerializeField] private Color asymptoteColour = new(1f, 0.35f, 0.55f, 0.85f);
        [Tooltip("Path colour while the shot waits.")]
        [SerializeField] private Color pathColour = new(1f, 0.7f, 0.8f, 0.95f);
        [Tooltip("Path alpha multiplier once the shot is out.")]
        [SerializeField, Range(0f, 1f)] private float pathAfterRelease = 0.35f;
        [Tooltip("Asymptote length past the hyperbola's centre, as a multiple of Hyperbola Reach.")]
        [SerializeField, Min(0.1f)] private float asymptoteReach = 1.3f;
        [Tooltip("Dashes per metre along the path.")]
        [SerializeField, Min(0.05f)] private float dashesPerMetre = 1.2f;
        [Tooltip("How fast the dashes march toward the exit, m/s.")]
        [SerializeField] private float marchSpeed = 6f;
        [Tooltip("Lines lifted this far off the plate, m.")]
        [SerializeField, Min(0f)] private float floorLift = 0.05f;

        ConicCaster _subscribed;
        MaterialPropertyBlock _block;
        float _pathLength;
        bool _pathShown, _released;

        /// <summary>True from the telegraph until the shot leaves (or a stop).</summary>
        public bool IsShowing => _pathShown;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            HideAll();
            _subscribed = caster;
            if (_subscribed == null) return;
            _subscribed.AsymptotesTelegraphed += OnTelegraphed;
            _subscribed.ShotReleased += OnReleased;
            _subscribed.ShotFinished += OnFinished;
            _subscribed.Stopped += HideAll;
        }

        void OnDisable()
        {
            if (_subscribed != null)
            {
                _subscribed.AsymptotesTelegraphed -= OnTelegraphed;
                _subscribed.ShotReleased -= OnReleased;
                _subscribed.ShotFinished -= OnFinished;
                _subscribed.Stopped -= HideAll;
            }
            _subscribed = null;
            HideAll();
        }

        float Floor(HyperbolaTelegraph t)
        {
            if (boss != null && boss.Context != null) return boss.Context.FloorY + floorLift;
            float height = boss != null && boss.Config != null ? boss.Config.HyperbolaHeight : 1f;
            return t.Focus.y - height + floorLift;
        }

        void OnTelegraphed(HyperbolaTelegraph t)
        {
            float y = Floor(t);
            float reach = (boss != null && boss.Config != null ? boss.Config.HyperbolaReach : 16f) * asymptoteReach;
            Vector3 c = new(t.Centre.x, y, t.Centre.z);
            SetRay(incoming, c, t.IncomingDirection, reach);
            SetRay(outgoing, c, t.OutgoingDirection, reach);

            _pathLength = 0f;
            ConicPolyline poly = t.Path;
            if (path != null && poly != null && poly.Count > 1)
            {
                path.positionCount = poly.Count;
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector3 p = poly[i];
                    path.SetPosition(i, new Vector3(p.x, y, p.z));
                }
                _pathLength = poly.Length;
                path.startColor = path.endColor = pathColour;
                path.enabled = true;
                _pathShown = true;
            }
            _released = false;
        }

        void SetRay(LineRenderer line, Vector3 from, Vector3 direction, float length)
        {
            if (line == null) return;
            Vector3 d = new Vector3(direction.x, 0f, direction.z).normalized;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, from + d * length);
            line.startColor = line.endColor = asymptoteColour;
            line.enabled = true;
        }

        void OnReleased(HyperbolicShot shot)
        {
            _released = true;
            if (incoming != null) incoming.enabled = false;
            if (outgoing != null) outgoing.enabled = false;
            if (path != null)
            {
                var c = new Color(pathColour.r, pathColour.g, pathColour.b, pathColour.a * pathAfterRelease);
                path.startColor = path.endColor = c;
            }
        }

        void OnFinished(HyperbolicShot shot) => HideAll();

        void HideAll()
        {
            if (incoming != null) incoming.enabled = false;
            if (outgoing != null) outgoing.enabled = false;
            if (path != null) path.enabled = false;
            _pathShown = false;
            _released = false;
        }

        void LateUpdate()
        {
            if (!_pathShown || path == null) return;
            // March the dashes toward the exit: tile by length, scroll by time.
            float tiles = Mathf.Max(1f, _pathLength * dashesPerMetre);
            float offset = -Time.time * marchSpeed * dashesPerMetre;
            _block.SetVector(BaseMapStId, new Vector4(tiles, 1f, offset, 0f));
            path.SetPropertyBlock(_block);
            if (!_released && incoming != null)
            {
                float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 12f);
                var c = new Color(asymptoteColour.r, asymptoteColour.g, asymptoteColour.b, asymptoteColour.a * pulse);
                incoming.startColor = incoming.endColor = c;
                if (outgoing != null) outgoing.startColor = outgoing.endColor = c;
            }
        }
    }
}
