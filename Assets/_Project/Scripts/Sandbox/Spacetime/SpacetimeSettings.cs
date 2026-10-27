using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The one switch for the 4D worldline feature, and every number that tunes it.
    ///
    /// <b>Off by default.</b> The feature has not been play-tested, so it ships
    /// switched off. With <see cref="Enabled"/> false, nothing that references this
    /// asset does anything: <see cref="SpacetimeView"/> registers no input binding
    /// and never flips, and <see cref="WorldlineRecorder"/> records nothing. Flip
    /// it with Tools ▸ Pickleball ▸ Spacetime ▸ Enable / Disable, or the checkbox
    /// on the asset.
    ///
    /// <b>It is also the channel between the view and the recorders.</b> Both are
    /// wired to the same asset, and it carries the runtime registry: which
    /// recorders are live, which histories have outlived their objects, and
    /// whether the world is in 4D. Neither side holds a reference to the other,
    /// and nothing static is involved. This is the same pattern as the project's
    /// event channel assets. The registry is runtime-only and never serialized.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Spacetime Settings", fileName = "SpacetimeSettings")]
    public class SpacetimeSettings : ScriptableObject
    {
        [Tooltip("Master switch. Off: no input binding, no recording, no beams, no " +
                 "behaviour change anywhere. On: T / d-pad left flips into 4D.")]
        [SerializeField] private bool enabled = false;

        [Header("Worldline shape")]
        [Tooltip("Beam width, m. Wider is easier to walk along; the chibi's capsule " +
                 "is about 0.6 m across.")]
        [Min(0.1f)]
        [SerializeField] private float width = 1.1f;

        [Tooltip("Beam thickness, m. Thin reads as a ribbon; thick reads as a girder " +
                 "and makes the steep stretches more obviously walls.")]
        [Min(0.02f)]
        [SerializeField] private float thickness = 0.2f;

        [Tooltip("The spacetime-diagram projection, m per second of age. Each moment " +
                 "of an object's past is drawn this far from where it actually was, so " +
                 "time becomes a direction in space. Upward turns a swinging platform " +
                 "into a ramp, an orbiting one into a spiral staircase, and a still one " +
                 "into a pillar. Zero draws the plain path through space.")]
        [SerializeField] private Vector3 timeAxis = new(0f, 0.5f, 0f);

        [Tooltip("Longer histories are thinned to this many points per beam. Bounds " +
                 "the cost of a flip, which builds every beam at once.")]
        [Range(8, 256)]
        [SerializeField] private int maxPointsPerLine = 96;

        [Header("Look")]
        [Tooltip("Vertex-coloured material for the beams. Left empty, one is made at " +
                 "runtime, but its shader may then be stripped from a build.")]
        [SerializeField] private Material worldlineMaterial;

        [Tooltip("Colour at the oldest end of a beam.")]
        [SerializeField] private Color pastColor = new(0.35f, 0.2f, 0.85f, 1f);

        [Tooltip("Colour at the present end of a beam, where the object is now.")]
        [SerializeField] private Color presentColor = new(0.55f, 0.95f, 1f, 1f);

        [Header("Budget")]
        [Tooltip("Seconds you can stay in 4D on a full meter. 0 = unlimited, for " +
                 "building levels.")]
        [Min(0f)]
        [SerializeField] private float maxDuration = 6f;

        [Tooltip("Seconds of 4D regained per second spent in 3D. 1 means a full " +
                 "meter takes as long to refill as it does to drain.")]
        [Min(0f)]
        [SerializeField] private float rechargeRate = 1f;

        [Tooltip("Meter needed to flip in, s. Stops an empty meter being used to " +
                 "flicker in and out for a frame at a time.")]
        [Min(0f)]
        [SerializeField] private float minChargeToEnter = 0.5f;

        [Tooltip("Minimum time between flips, s.")]
        [Min(0f)]
        [SerializeField] private float flipCooldown = 0.25f;

        [Header("What gets recorded")]
        [Tooltip("Attach a recorder to every apple from Newton's barrage, so the " +
                 "frozen apple rain shows up in 4D.")]
        [SerializeField] private bool recordApples = true;

        /// <summary>Most ended histories kept at once. A full apple barrage ends a
        /// lot of lines in a few seconds.</summary>
        public const int MaxEnded = 256;

        public bool Enabled => enabled;
        public float Width => width;
        public float Thickness => thickness;
        public Vector3 TimeAxis => timeAxis;
        public int MaxPointsPerLine => maxPointsPerLine;
        public Material WorldlineMaterial => worldlineMaterial;
        public Color PastColor => pastColor;
        public Color PresentColor => presentColor;
        public float MaxDuration => maxDuration;
        public float RechargeRate => rechargeRate;
        public float MinChargeToEnter => minChargeToEnter;
        public float FlipCooldown => flipCooldown;
        public bool RecordApples => recordApples;

        /// <summary>A history whose owner is gone, plus the shape to draw it with.</summary>
        public struct EndedLine
        {
            public WorldlineHistory History;
            public float Width, Thickness;
            public string Name;
        }

        // Runtime registry. NonSerialized so it is never written into the asset,
        // and rebuilt in OnEnable, which runs on every domain reload, so a new
        // Play session never inherits the last one's recorders or ghosts.
        [NonSerialized] List<WorldlineRecorder> _live;
        [NonSerialized] List<EndedLine> _ended;

        /// <summary>Every recorder currently writing to this channel.</summary>
        public List<WorldlineRecorder> Live => _live ??= new List<WorldlineRecorder>();

        /// <summary>Histories of objects that died, were disabled or teleported.
        /// <see cref="SpacetimeView"/> prunes them as they age out.</summary>
        public List<EndedLine> Ended => _ended ??= new List<EndedLine>();

        /// <summary>Whether the world is currently in 4D. Only
        /// <see cref="SpacetimeView"/> changes it.</summary>
        public bool In4D { get; private set; }

        /// <summary>Raised on every flip, with the new state.</summary>
        public event Action<bool> Changed;

        void OnEnable()
        {
            _live = new List<WorldlineRecorder>();
            _ended = new List<EndedLine>();
            In4D = false;
            Changed = null;
            Owner = null;
        }

        /// <summary>Adds an ended history, dropping the oldest past
        /// <see cref="MaxEnded"/>.</summary>
        public void AddEnded(EndedLine line)
        {
            if (Ended.Count >= MaxEnded) Ended.RemoveAt(0);
            Ended.Add(line);
        }

        /// <summary>Sets the 4D state and raises <see cref="Changed"/>. Called by
        /// <see cref="SpacetimeView"/> only.</summary>
        public void SetIn4D(bool value)
        {
            if (In4D == value) return;
            In4D = value;
            Changed?.Invoke(value);
        }

        /// <summary>Flips the master switch. For the editor menu and for tests;
        /// gameplay never changes it.</summary>
        public void SetEnabled(bool value) => enabled = value;

        /// <summary>The view that drives this channel. <see cref="In4D"/> is one
        /// state, so two views each toggling it would leave one's beams orphaned
        /// with the state saying 3D.</summary>
        public SpacetimeView Owner { get; private set; }

        /// <summary>Makes <paramref name="view"/> the driver, unless another view
        /// already is. Returns whether it is now the owner.</summary>
        public bool TryClaim(SpacetimeView view)
        {
            if (Owner != null && Owner != view) return false;
            Owner = view;
            return true;
        }

        public void Release(SpacetimeView view)
        {
            if (Owner == view) Owner = null;
        }
    }
}
