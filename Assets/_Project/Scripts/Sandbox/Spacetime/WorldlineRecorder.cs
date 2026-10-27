using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Records where this object has been, so the 4D view can show its
    /// worldline. Add it to anything that moves: platforms, balls, apples,
    /// bosses. It writes its history into the <see cref="SpacetimeSettings"/>
    /// it is wired to, and <see cref="SpacetimeView"/> reads every recorder on
    /// that asset when the player flips into 4D. The two never reference each
    /// other.
    ///
    /// <b>Inert while the feature is off.</b> With no settings asset, or with
    /// the asset's switch off, it records nothing and holds no history.
    ///
    /// <b>A recorder's history outlives it.</b> When the object is destroyed or
    /// disabled, or teleports, the history so far is handed to
    /// <see cref="SpacetimeSettings.Ended"/> and keeps ageing out on its own.
    /// That matters most for short-lived things. Nearly every apple from a
    /// barrage has already splatted by the time you flip, and the frozen
    /// apple-rain is the whole point.
    /// </summary>
    public class WorldlineRecorder : MonoBehaviour
    {
        [Tooltip("The Spacetime Settings asset. Empty, or switched off, means this " +
                 "records nothing.")]
        [SerializeField] private SpacetimeSettings settings;

        [Tooltip("How many seconds of the past the worldline shows, s.")]
        [Min(0.1f)]
        [SerializeField] private float historySeconds = 4f;
        [Tooltip("Movement that forces a new sample, m. Smaller is smoother and " +
                 "costs more points.")]
        [Min(0f)]
        [SerializeField] private float sampleSpacing = 0.15f;
        [Tooltip("Time that forces a new sample even when standing still, s. Keeps " +
                 "a stationary object's worldline from ageing away to nothing.")]
        [Min(0.01f)]
        [SerializeField] private float sampleInterval = 0.1f;
        [Tooltip("A jump larger than this between frames is a teleport, not " +
                 "motion, m. The line breaks there instead of stretching a beam " +
                 "across the gap.")]
        [Min(0.1f)]
        [SerializeField] private float teleportDistance = 4f;
        [Tooltip("World-space offset from the pivot to the point that is traced, m. " +
                 "Use it to trace the top of a platform instead of its middle.")]
        [SerializeField] private Vector3 traceOffset = Vector3.zero;

        [Header("Shape (0 = use the settings default)")]
        [Tooltip("Beam width for this object, m.")]
        [Min(0f)]
        [SerializeField] private float width = 0f;
        [Tooltip("Beam thickness for this object, m.")]
        [Min(0f)]
        [SerializeField] private float thickness = 0f;

        [Tooltip("Hide this object's renderers and colliders while in 4D, so " +
                 "only its worldline is there. Off by default: seeing the object " +
                 "at the tip of its own line is what explains the line.")]
        [SerializeField] private bool hideWhileIn4D = false;

        /// <summary>The history being written, or null while inert.</summary>
        public WorldlineHistory History => _history;
        public float Width => width;
        public float Thickness => thickness;

        /// <summary>Whether this recorder is wired to a switched-on settings asset.</summary>
        public bool Active => settings != null && settings.Enabled;

        WorldlineHistory _history;
        SpacetimeSettings _registeredWith;
        Renderer[] _hiddenRenderers;
        Collider[] _hiddenColliders;

        /// <summary>Wires a recorder added at runtime, which has no serialized
        /// reference (the view attaches these to apples).</summary>
        public void Bind(SpacetimeSettings spacetime)
        {
            if (settings == spacetime) return;
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled) Unregister();
            settings = spacetime;
            if (wasEnabled) Register();
        }

        void OnEnable() => Register();

        void OnDisable() => Unregister();

        void LateUpdate() => Sample(transform.position + traceOffset, Time.time);

        /// <summary>
        /// Records one position. The per-frame body, public so EditMode tests can
        /// drive it without a game loop. Returns whether a sample was kept, which
        /// is always false while the feature is off: a switched-off recorder
        /// throws its history away, so turning the feature back on starts clean
        /// instead of drawing a stale line.
        /// </summary>
        public bool Sample(Vector3 point, float now)
        {
            if (!Active)
            {
                _history = null;
                return false;
            }

            if (_history == null)
            {
                _history = NewHistory();
            }
            else if (_history.Count > 0 &&
                     (point - _history.NewestPosition).sqrMagnitude > teleportDistance * teleportDistance)
            {
                RetireHistory();
                _history = NewHistory();
            }

            bool kept = _history.Record(point, now);
            _history.Prune(now);
            return kept;
        }

        void Register()
        {
            if (settings == null) return;
            _registeredWith = settings;
            settings.Live.Add(this);
            settings.Changed += OnSpacetimeChanged;
            if (settings.In4D) OnSpacetimeChanged(true);
        }

        void Unregister()
        {
            if (_registeredWith == null) return;
            _registeredWith.Changed -= OnSpacetimeChanged;
            _registeredWith.Live.Remove(this);
            RetireHistory();
            if (_hiddenRenderers != null) OnSpacetimeChanged(false);
            _registeredWith = null;
        }

        WorldlineHistory NewHistory() =>
            new(historySeconds, sampleSpacing, sampleInterval);

        /// <summary>Hands the current history to the settings' ended list, if it
        /// has enough in it to draw and the feature is on.</summary>
        void RetireHistory()
        {
            var target = _registeredWith != null ? _registeredWith : settings;
            if (_history != null && _history.Count >= 2 && target != null && target.Enabled)
            {
                target.AddEnded(new SpacetimeSettings.EndedLine
                {
                    History = _history,
                    Width = width,
                    Thickness = thickness,
                    Name = name,
                });
            }
            _history = null;
        }

        void OnSpacetimeChanged(bool in4D)
        {
            if (!hideWhileIn4D) return;
            if (in4D)
            {
                // Only touch what is switched on now, and remember it, so leaving
                // 4D does not switch on things another system had switched off.
                _hiddenRenderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(), r => r.enabled);
                // Never a CharacterController: it is a Collider too, and Move()
                // errors on a disabled one, so hiding a CC-driven boss would break
                // its movement for as long as 4D lasts.
                _hiddenColliders = System.Array.FindAll(GetComponentsInChildren<Collider>(),
                                                        c => c.enabled && c is not CharacterController);
                foreach (var r in _hiddenRenderers) r.enabled = false;
                foreach (var c in _hiddenColliders) c.enabled = false;
            }
            else
            {
                if (_hiddenRenderers != null)
                    foreach (var r in _hiddenRenderers) if (r != null) r.enabled = true;
                if (_hiddenColliders != null)
                    foreach (var c in _hiddenColliders) if (c != null) c.enabled = true;
                _hiddenRenderers = null;
                _hiddenColliders = null;
            }
        }
    }
}
