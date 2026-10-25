using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Records where this object has been, so the 4D view can show its
    /// worldline. Add it to anything that moves: platforms, balls, apples,
    /// bosses. It needs no references and talks to nothing. It writes its
    /// history, and <see cref="SpacetimeView"/> reads every live recorder (and
    /// every ended one) when the player flips into 4D.
    ///
    /// <b>A recorder's history outlives it.</b> When the object is destroyed or
    /// disabled, or teleports, the history so far is moved to <see cref="Ended"/>
    /// and keeps ageing out on its own. That matters most for short-lived
    /// things. Nearly every apple from a barrage has already splatted by the time
    /// you flip, and the frozen apple-rain is the whole point.
    /// </summary>
    public class WorldlineRecorder : MonoBehaviour
    {
        [Tooltip("How many seconds of the past the worldline shows.")]
        [SerializeField] private float historySeconds = 4f;
        [Tooltip("Movement that forces a new sample, in metres.")]
        [SerializeField] private float sampleSpacing = 0.15f;
        [Tooltip("Time that forces a new sample even when standing still. Keeps " +
                 "a stationary object's worldline from ageing away to nothing.")]
        [SerializeField] private float sampleInterval = 0.1f;
        [Tooltip("A jump larger than this between frames is a teleport, not " +
                 "motion. The line breaks there instead of stretching a beam " +
                 "across the gap.")]
        [SerializeField] private float teleportDistance = 4f;
        [Tooltip("World-space offset from the pivot to the point that is traced. " +
                 "Use it to trace the top of a platform instead of its middle.")]
        [SerializeField] private Vector3 traceOffset = Vector3.zero;

        [Header("Shape (0 = use the SpacetimeView default)")]
        [SerializeField] private float width = 0f;
        [SerializeField] private float thickness = 0f;

        [Tooltip("Hide this object's renderers and colliders while in 4D, so " +
                 "only its worldline is there. Off by default: seeing the object " +
                 "at the tip of its own line is what explains the line.")]
        [SerializeField] private bool hideWhileIn4D = false;

        /// <summary>A history whose owner is gone, plus the shape it should be
        /// drawn with.</summary>
        public struct EndedLine
        {
            public WorldlineHistory History;
            public float Width, Thickness;
            public string Name;
        }

        /// <summary>Every recorder currently writing.</summary>
        public static readonly List<WorldlineRecorder> Live = new();

        /// <summary>Histories of objects that died, were disabled or teleported.
        /// <see cref="SpacetimeView"/> prunes these as they age out.</summary>
        public static readonly List<EndedLine> Ended = new();

        /// <summary>Hard cap on <see cref="Ended"/>. A full apple barrage ends a
        /// lot of lines in a few seconds, and nothing else bounds the list if no
        /// SpacetimeView is in the scene to prune it.</summary>
        const int MaxEnded = 256;

        public WorldlineHistory History => _history;
        public float Width => width;
        public float Thickness => thickness;

        WorldlineHistory _history;
        Renderer[] _hiddenRenderers;
        Collider[] _hiddenColliders;

        /// <summary>Statics survive Play mode restarts when domain reload is off.
        /// Clear them so a new session does not inherit ghosts from the last.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Live.Clear();
            Ended.Clear();
        }

        void OnEnable()
        {
            // Built here rather than in Awake or a field initialiser, so a script
            // reload in Play mode (which drops non-serialised fields) comes back
            // with a working recorder instead of a null one.
            _history = NewHistory();
            Live.Add(this);
            SpacetimeView.Changed += OnSpacetimeChanged;
            if (SpacetimeView.In4D) OnSpacetimeChanged(true);
        }

        void OnDisable()
        {
            SpacetimeView.Changed -= OnSpacetimeChanged;
            Live.Remove(this);
            RetireHistory();
            if (_hiddenRenderers != null) OnSpacetimeChanged(false);
        }

        void LateUpdate()
        {
            Vector3 point = transform.position + traceOffset;
            float now = Time.time;

            if (_history.Count > 0 &&
                (point - _history.NewestPosition).sqrMagnitude > teleportDistance * teleportDistance)
            {
                RetireHistory();
                _history = NewHistory();
            }

            _history.Record(point, now);
            _history.Prune(now);
        }

        WorldlineHistory NewHistory() =>
            new(historySeconds, sampleSpacing, sampleInterval);

        /// <summary>Moves the current history to <see cref="Ended"/>, if it has
        /// enough in it to draw.</summary>
        void RetireHistory()
        {
            if (_history == null || _history.Count < 2) return;
            if (Ended.Count >= MaxEnded) Ended.RemoveAt(0);
            Ended.Add(new EndedLine
            {
                History = _history,
                Width = width,
                Thickness = thickness,
                Name = name,
            });
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
