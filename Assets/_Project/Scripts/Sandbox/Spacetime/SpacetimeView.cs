using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The 3D ↔ 4D flip.
    ///
    /// In 4D, every recorded object's recent past turns into a solid beam you can
    /// stand on: its worldline. A lobbed ball leaves a bridge, a patrolling
    /// platform leaves a walkway, and the apple barrage becomes a forest of
    /// pillars. In 3D you change things. In 4D you walk on the results.
    ///
    /// <b>The beams are a snapshot.</b> They are built once, from the histories
    /// as they stand at the moment of the flip, and stay put while the world
    /// carries on moving. Nothing else in the game has to know 4D exists, and
    /// the player gets solid, predictable footing instead of a bridge that grows
    /// at one end and erodes at the other. Flipping back removes them, and
    /// anyone standing on one falls, as in Super Paper Mario.
    ///
    /// <b>A beam that appears through the player is intangible</b> until they
    /// step clear of it. A slab materialising around a CharacterController
    /// otherwise gets resolved by the controller's overlap recovery, which shoves
    /// him out in whatever direction it likes. A beam only as far into his feet
    /// as his step height is left solid, because there the recovery does the
    /// right thing and steps him up onto it.
    ///
    /// <b>4D is on a meter.</b> Time in 4D drains it and time in 3D refills it,
    /// so a beam is a crossing you have to commit to, not a floor you can live
    /// on. Set <see cref="maxDuration"/> to 0 to turn the limit off while
    /// building levels.
    /// </summary>
    public class SpacetimeView : MonoBehaviour
    {
        [Header("Worldline shape")]
        [SerializeField] private float width = 1.1f;
        [SerializeField] private float thickness = 0.2f;

        [Tooltip("The spacetime-diagram projection, in metres per second of age. " +
                 "Each moment of an object's past is drawn this far from where " +
                 "it actually was, so time becomes a direction in space. Upward " +
                 "turns a swinging platform into a zigzag ramp, an orbiting one " +
                 "into a spiral staircase, and a still one into a pillar. Zero " +
                 "draws the plain path through space.")]
        [SerializeField] private Vector3 timeAxis = new(0f, 0.5f, 0f);

        [Tooltip("Longer histories are thinned to this many points per line. " +
                 "Bounds the cost of a flip, which builds every line at once.")]
        [SerializeField] private int maxPointsPerLine = 96;

        [Tooltip("Assign one for builds, so its shader is guaranteed to ship. Left " +
                 "empty, a vertex-coloured unlit material is made at runtime.")]
        [SerializeField] private Material worldlineMaterial;
        [SerializeField] private Color pastColor = new(0.35f, 0.2f, 0.85f, 1f);
        [SerializeField] private Color presentColor = new(0.55f, 0.95f, 1f, 1f);

        [Header("Budget")]
        [Tooltip("Seconds you can stay in 4D on a full meter. 0 = unlimited.")]
        [SerializeField] private float maxDuration = 6f;
        [Tooltip("Seconds of 4D regained per second spent in 3D.")]
        [SerializeField] private float rechargeRate = 1f;
        [Tooltip("Minimum meter needed to flip in, so an empty meter cannot be " +
                 "used to flicker in and out for a frame at a time.")]
        [SerializeField] private float minChargeToEnter = 0.5f;
        [SerializeField] private float flipCooldown = 0.25f;

        [Header("Player")]
        [Tooltip("Optional. Left empty, the chibi's controller is found on the " +
                 "first flip. Used to keep beams that appear through the player " +
                 "intangible until they step clear.")]
        [SerializeField] private CharacterController player;

        [Header("Scene dressing")]
        [Tooltip("Shown only in 4D: hints, alternate geometry, a sky tint.")]
        [SerializeField] private GameObject[] onlyIn4D = Array.Empty<GameObject>();
        [Tooltip("Shown only in 3D.")]
        [SerializeField] private GameObject[] onlyIn3D = Array.Empty<GameObject>();

        /// <summary>Whether the world is currently in 4D.</summary>
        public static bool In4D { get; private set; }

        /// <summary>Raised on every flip, with the new state.</summary>
        public static event Action<bool> Changed;

        /// <summary>Remaining 4D time as 0–1, for a HUD meter. Always 1 when the
        /// meter is off.</summary>
        public float Charge01 => maxDuration > 0f ? _charge / maxDuration : 1f;

        /// <summary>
        /// Longest frame the meter will charge for. Building every beam makes the
        /// frame after a flip long, and without this cap that one frame would
        /// drain a third of a second of 4D before the player can do anything.
        /// </summary>
        const float MaxMeterStep = 0.1f;

        /// <summary>The one view that owns the flip. <see cref="In4D"/> is global,
        /// so two views each toggling it would leave one's beams orphaned with the
        /// state saying 3D.</summary>
        static SpacetimeView _active;

        /// <summary>One built worldline.</summary>
        class Beam
        {
            public GameObject Object;
            public Mesh Mesh;
            public MeshCollider Collider;
            /// <summary>The recorded object's own colliders, null for an ended
            /// line. The newest end of a live line sits inside its owner, so
            /// without ignoring them a physics-driven owner gets kicked out of its
            /// own worldline the moment you flip.</summary>
            public Collider[] Owner;
            /// <summary>Waiting for the player to step out of it.</summary>
            public bool Intangible;
        }

        InputAction _toggle;
        readonly List<Beam> _beams = new();
        readonly List<Vector3> _path = new();
        readonly Collider[] _overlaps = new Collider[64];
        Material _runtimeMaterial;
        bool _warnedNoShader;
        float _charge;
        float _lastFlip = float.NegativeInfinity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            In4D = false;
            Changed = null;
            _active = null;
        }

        void Awake()
        {
            _charge = maxDuration;
            ApplyDressing(false);
        }

        void OnEnable()
        {
            if (_active != null && _active != this)
            {
                Debug.LogWarning($"{name}: a SpacetimeView is already active on " +
                                 $"{_active.name}. Only one may run; disabling this one.", this);
                enabled = false;
                return;
            }
            _active = this;

            _toggle = new InputAction("Spacetime", InputActionType.Button);
            _toggle.AddBinding("<Keyboard>/t");
            _toggle.AddBinding("<Gamepad>/dpad/up");
            _toggle.Enable();
        }

        void OnDisable()
        {
            // A rejected duplicate never set anything up, and must not tear down
            // the active view's state.
            if (_active != this) return;

            _toggle?.Disable();
            _toggle?.Dispose();
            _toggle = null;
            // Do not leave the world stuck in 4D, with its beams, if the view
            // goes away (scene unload, script reload, someone disabling it).
            if (In4D) Exit();
            _active = null;
        }

        void OnDestroy()
        {
            if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
        }

        void Update()
        {
            if (_toggle != null && _toggle.WasPressedThisFrame() &&
                Time.time - _lastFlip >= flipCooldown)
            {
                if (In4D) Exit();
                else if (maxDuration <= 0f || _charge >= minChargeToEnter) Enter();
            }

            if (maxDuration > 0f)
            {
                float dt = Mathf.Min(Time.deltaTime, MaxMeterStep);
                if (In4D)
                {
                    _charge -= dt;
                    if (_charge <= 0f) { _charge = 0f; Exit(); }
                }
                else
                {
                    _charge = Mathf.Min(maxDuration, _charge + rechargeRate * dt);
                }
            }

            if (In4D) SolidifyClearedBeams();

            // Age out the histories of things that no longer exist. Live
            // recorders prune their own.
            var ended = WorldlineRecorder.Ended;
            for (int i = ended.Count - 1; i >= 0; i--)
            {
                ended[i].History.Prune(Time.time);
                if (ended[i].History.Count < 2) ended.RemoveAt(i);
            }
        }

        /// <summary>Flips into 4D. Public so a trigger, cutscene or test can force it.</summary>
        public void Enter()
        {
            if (In4D || _active != this) return;
            // Without a material there is nothing to see, and invisible floors
            // are worse than none.
            if (Material == null) return;
            _lastFlip = Time.time;

            float now = Time.time;
            foreach (var recorder in WorldlineRecorder.Live)
                if (recorder != null && recorder.History != null)
                    AddBeam(recorder.History, recorder.Width, recorder.Thickness,
                            recorder.name, now, recorder.GetComponentsInChildren<Collider>());
            foreach (var line in WorldlineRecorder.Ended)
                AddBeam(line.History, line.Width, line.Thickness, line.Name, now, null);
            AddColliders();

            In4D = true;
            ApplyDressing(true);
            Changed?.Invoke(true);
        }

        /// <summary>Flips back to 3D and removes every beam.</summary>
        public void Exit()
        {
            if (!In4D || _active != this) return;
            _lastFlip = Time.time;

            foreach (var beam in _beams)
            {
                if (beam.Object != null) Destroy(beam.Object);
                if (beam.Mesh != null) Destroy(beam.Mesh);
            }
            _beams.Clear();

            In4D = false;
            ApplyDressing(false);
            Changed?.Invoke(false);
        }

        void AddBeam(WorldlineHistory history, float lineWidth, float lineThickness,
                     string label, float now, Collider[] owner)
        {
            history.Project(now, timeAxis, maxPointsPerLine, _path);

            var mesh = new Mesh { name = $"Worldline ({label})" };
            bool built = WorldlineMesh.Build(_path,
                                             lineWidth > 0f ? lineWidth : width,
                                             lineThickness > 0f ? lineThickness : thickness,
                                             pastColor, presentColor, mesh);
            if (!built) { Destroy(mesh); return; }

            // A root object at the origin, because the vertices are already in
            // world space. Not parented to the recorder: the beam is the object's
            // past and must stay where that past was while the object moves on.
            // Not parented to this view either, so moving or scaling the view's
            // object cannot drag the beams off the paths they trace.
            var go = new GameObject(mesh.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Material;
            _beams.Add(new Beam { Object = go, Mesh = mesh, Owner = owner });
        }

        /// <summary>
        /// Gives every beam its collider, all at once.
        ///
        /// Assigning a mesh to a MeshCollider cooks it synchronously, and a busy
        /// moment (a barrage's worth of ended lines) can mean a couple of hundred
        /// beams on one frame. Cooking them in parallel first, with
        /// Physics.BakeMesh, turns that into one wide job. Each assignment then
        /// reuses the cooked data instead of cooking again.
        /// </summary>
        void AddColliders()
        {
            if (_beams.Count == 0) return;

            var meshIds = new NativeArray<int>(_beams.Count, Allocator.TempJob);
            for (int i = 0; i < _beams.Count; i++) meshIds[i] = _beams[i].Mesh.GetInstanceID();
            new BakeJob { MeshIds = meshIds }.Schedule(meshIds.Length, 1).Complete();
            meshIds.Dispose();

            foreach (var beam in _beams)
            {
                // Non-convex is fine for a static collider, and needed. A convex
                // hull of a spiral is a solid cylinder.
                var col = beam.Object.AddComponent<MeshCollider>();
                col.sharedMesh = beam.Mesh;
                beam.Collider = col;
                IgnoreOwner(beam);
            }

            ResolvePlayer();
            if (player == null) return;
            Physics.SyncTransforms();
            foreach (var beam in _beams)
            {
                if (!OverlapsPlayer(beam.Collider)) continue;
                beam.Intangible = true;
                beam.Collider.enabled = false;
            }
        }

        struct BakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> MeshIds;
            // convex: false, and default cooking options, to match the
            // MeshColliders the cooked data is for. A mismatch silently cooks
            // again on assignment.
            public void Execute(int i) => Physics.BakeMesh(MeshIds[i], false);
        }

        /// <summary>Re-tests the intangible beams and makes solid the ones the
        /// player has stepped out of.</summary>
        void SolidifyClearedBeams()
        {
            bool any = false;
            foreach (var beam in _beams)
                if (beam.Intangible && beam.Collider != null) { beam.Collider.enabled = true; any = true; }
            if (!any) return;

            // The overlap query only sees enabled colliders, so switch the
            // candidates on, test, and switch back off the ones still in him.
            Physics.SyncTransforms();
            foreach (var beam in _beams)
            {
                if (!beam.Intangible || beam.Collider == null) continue;
                if (OverlapsPlayer(beam.Collider)) { beam.Collider.enabled = false; continue; }
                beam.Intangible = false;
                // Re-applied because toggling a collider can drop its ignore
                // pairs, and this one has been switched off and on.
                IgnoreOwner(beam);
            }
        }

        static void IgnoreOwner(Beam beam)
        {
            if (beam.Owner == null) return;
            foreach (var c in beam.Owner)
                if (c != null) Physics.IgnoreCollision(beam.Collider, c, true);
        }

        void ResolvePlayer()
        {
            if (player != null) return;
            var chibi = FindFirstObjectByType<ChibiLocomotion>();
            if (chibi != null) player = chibi.GetComponent<CharacterController>();
        }

        /// <summary>
        /// Whether <paramref name="beam"/> cuts into the player above step height.
        /// The test capsule is the controller's own, with its bottom raised by the
        /// step offset. A beam only that far into his feet is a step, and the
        /// controller climbs it without help.
        /// </summary>
        bool OverlapsPlayer(Collider beam)
        {
            if (player == null || !player.enabled) return false;

            Transform t = player.transform;
            float scale = Mathf.Abs(t.lossyScale.y);
            float radius = player.radius * scale;
            float half = Mathf.Max(player.height * scale * 0.5f, radius);
            Vector3 centre = t.TransformPoint(player.center);
            Vector3 top = centre + Vector3.up * (half - radius);
            Vector3 bottom = centre + Vector3.up * (radius - half + player.stepOffset);
            if (bottom.y > top.y) bottom = top;

            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, _overlaps,
                                                   ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (_overlaps[i] == beam) return true;
            return false;
        }

        Material Material
        {
            get
            {
                if (worldlineMaterial != null) return worldlineMaterial;
                if (_runtimeMaterial != null) return _runtimeMaterial;
                // URP's particle shader, because it multiplies in vertex colour
                // and the past-to-present gradient lives in the vertex colours.
                // Plain Unlit is a last resort: it renders, but flat white.
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    // Stripped from a build. Say so once, not on every press.
                    if (!_warnedNoShader)
                        Debug.LogError($"{name}: no worldline shader in this build. " +
                                       "Assign worldlineMaterial so it ships.", this);
                    _warnedNoShader = true;
                    return null;
                }
                _runtimeMaterial = new Material(shader) { name = "Worldline (runtime)" };
                _runtimeMaterial.SetColor("_BaseColor", Color.white);
                return _runtimeMaterial;
            }
        }

        void ApplyDressing(bool in4D)
        {
            foreach (var go in onlyIn4D) if (go != null) go.SetActive(in4D);
            foreach (var go in onlyIn3D) if (go != null) go.SetActive(!in4D);
        }
    }
}
