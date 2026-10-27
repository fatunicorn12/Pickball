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
    /// <b>Inert while the feature is off.</b> Everything is gated on
    /// <see cref="SpacetimeSettings.Enabled"/>. Off, no input binding exists, so
    /// T and d-pad left do nothing, and the view never flips. Switching it off
    /// mid-play leaves 4D and removes the binding on the next frame.
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
    /// on. All the numbers are on <see cref="SpacetimeSettings"/>.
    /// </summary>
    public class SpacetimeView : MonoBehaviour
    {
        [Tooltip("The Spacetime Settings asset: the on/off switch and every tunable. " +
                 "Empty, or switched off, means this view does nothing.")]
        [SerializeField] private SpacetimeSettings settings;

        [Tooltip("The player's controller. Beams that appear through it stay " +
                 "intangible until the player steps clear. Wired by the setup menu.")]
        [SerializeField] private CharacterController player;

        [Header("Scene dressing")]
        [Tooltip("Shown only in 4D: hints, alternate geometry, a sky tint.")]
        [SerializeField] private GameObject[] onlyIn4D = Array.Empty<GameObject>();
        [Tooltip("Shown only in 3D.")]
        [SerializeField] private GameObject[] onlyIn3D = Array.Empty<GameObject>();

        /// <summary>Whether this view is wired to a switched-on settings asset.</summary>
        public bool Active => settings != null && settings.Enabled;

        /// <summary>Whether the world is in 4D, as far as this view's channel says.</summary>
        public bool In4D => settings != null && settings.In4D;

        /// <summary>Whether the T / d-pad left binding currently exists. False
        /// whenever the feature is off: a switched-off view claims no input.</summary>
        public bool InputRegistered => _toggle != null;

        /// <summary>How many beams are standing, for tests and debugging.</summary>
        public int BeamCount => _beams.Count;

        /// <summary>Remaining 4D time as 0–1, for a HUD meter. Always 1 when the
        /// meter is off.</summary>
        public float Charge01 =>
            settings != null && settings.MaxDuration > 0f ? _charge / settings.MaxDuration : 1f;

        /// <summary>
        /// Longest frame the meter will charge for, s. Building every beam makes
        /// the frame after a flip long, and without this cap that one frame would
        /// drain a third of a second of 4D before the player can do anything.
        /// </summary>
        const float MaxMeterStep = 0.1f;

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
        bool _warnedDuplicate;
        float _charge = -1f;
        float _lastFlip = float.NegativeInfinity;

        void OnEnable()
        {
            ApplyDressing(false);
            RefreshInput();
        }

        void OnDisable()
        {
            DisposeInput();
            // Do not leave the world stuck in 4D, with its beams, if the view
            // goes away (scene unload, script reload, someone disabling it).
            Exit();
            if (settings != null) settings.Release(this);
        }

        void OnDestroy()
        {
            if (_runtimeMaterial != null) Dispose(_runtimeMaterial);
        }

        void Update() =>
            Tick(_toggle != null && _toggle.WasPressedThisFrame(), Time.time, Time.deltaTime);

        /// <summary>
        /// One frame of the view: the switch, the flip, the meter and the
        /// bookkeeping. Public so EditMode tests can drive it without a game loop
        /// or a real key press.
        /// </summary>
        public void Tick(bool togglePressed, float now, float deltaTime)
        {
            RefreshInput();
            if (!Active)
            {
                Exit();
                return;
            }
            if (!settings.TryClaim(this))
            {
                if (!_warnedDuplicate)
                    Debug.LogWarning($"{name}: another SpacetimeView ({settings.Owner.name}) " +
                                     "already drives this settings asset. Only one may run.", this);
                _warnedDuplicate = true;
                return;
            }

            if (_charge < 0f) _charge = settings.MaxDuration;

            if (togglePressed && now - _lastFlip >= settings.FlipCooldown)
            {
                if (In4D) Exit(now);
                else if (settings.MaxDuration <= 0f || _charge >= settings.MinChargeToEnter) Enter(now);
            }

            if (settings.MaxDuration > 0f)
            {
                float dt = Mathf.Min(deltaTime, MaxMeterStep);
                if (In4D)
                {
                    _charge -= dt;
                    if (_charge <= 0f) { _charge = 0f; Exit(now); }
                }
                else
                {
                    _charge = Mathf.Min(settings.MaxDuration, _charge + settings.RechargeRate * dt);
                }
            }

            if (In4D) SolidifyClearedBeams();
            if (settings.RecordApples) RecordApples();

            // Age out the histories of things that no longer exist. Live
            // recorders prune their own.
            var ended = settings.Ended;
            for (int i = ended.Count - 1; i >= 0; i--)
            {
                ended[i].History.Prune(now);
                if (ended[i].History.Count < 2) ended.RemoveAt(i);
            }
        }

        /// <summary>Flips into 4D. Public so a trigger, cutscene or test can force
        /// it. Does nothing while the feature is off.</summary>
        public void Enter() => Enter(Time.time);

        void Enter(float now)
        {
            if (!Active || In4D || !settings.TryClaim(this)) return;
            // Without a material there is nothing to see, and invisible floors
            // are worse than none.
            if (Material == null) return;
            _lastFlip = now;

            foreach (var recorder in settings.Live)
                if (recorder != null && recorder.History != null)
                    AddBeam(recorder.History, recorder.Width, recorder.Thickness,
                            recorder.name, now, recorder.GetComponentsInChildren<Collider>());
            foreach (var line in settings.Ended)
                AddBeam(line.History, line.Width, line.Thickness, line.Name, now, null);
            AddColliders();

            ApplyDressing(true);
            settings.SetIn4D(true);
        }

        /// <summary>Flips back to 3D and removes every beam.</summary>
        public void Exit() => Exit(Time.time);

        void Exit(float now)
        {
            bool wasIn4D = In4D && settings.Owner == this;
            if (!wasIn4D && _beams.Count == 0) return;
            _lastFlip = now;

            foreach (var beam in _beams)
            {
                if (beam.Object != null) Dispose(beam.Object);
                if (beam.Mesh != null) Dispose(beam.Mesh);
            }
            _beams.Clear();

            ApplyDressing(false);
            if (wasIn4D) settings.SetIn4D(false);
        }

        /// <summary>Creates the T / d-pad left binding while the feature is on,
        /// and removes it while it is off.</summary>
        void RefreshInput()
        {
            // Only reached from OnEnable and Tick, so the component is enabled;
            // OnDisable removes the binding on the way out.
            if (Active)
            {
                if (_toggle != null) return;
                _toggle = new InputAction("Spacetime", InputActionType.Button);
                _toggle.AddBinding("<Keyboard>/t");
                // D-pad left: up/down is camera zoom and right is the skateboard.
                _toggle.AddBinding("<Gamepad>/dpad/left");
                _toggle.Enable();
            }
            else
            {
                DisposeInput();
            }
        }

        void DisposeInput()
        {
            if (_toggle == null) return;
            _toggle.Disable();
            _toggle.Dispose();
            _toggle = null;
        }

        /// <summary>Gives every apple in flight a recorder bound to this view's
        /// settings, so the barrage leaves its frozen rain. Apples are spawned at
        /// runtime from a prefab this feature does not own, so they are recorded
        /// from here rather than by editing that prefab.</summary>
        void RecordApples()
        {
            var airborne = Apple.Airborne;
            for (int i = 0; i < airborne.Count; i++)
            {
                var apple = airborne[i];
                if (apple == null || apple.TryGetComponent<WorldlineRecorder>(out _)) continue;
                apple.gameObject.AddComponent<WorldlineRecorder>().Bind(settings);
            }
        }

        void AddBeam(WorldlineHistory history, float lineWidth, float lineThickness,
                     string label, float now, Collider[] owner)
        {
            history.Project(now, settings.TimeAxis, settings.MaxPointsPerLine, _path);

            var mesh = new Mesh { name = $"Worldline ({label})" };
            bool built = WorldlineMesh.Build(_path,
                                             lineWidth > 0f ? lineWidth : settings.Width,
                                             lineThickness > 0f ? lineThickness : settings.Thickness,
                                             settings.PastColor, settings.PresentColor, mesh);
            if (!built) { Dispose(mesh); return; }

            // A root object at the origin, because the vertices are already in
            // world space. Not parented to the recorder: the beam is the object's
            // past and must stay where that past was while the object moves on.
            // Not parented to this view either, so moving or scaling the view's
            // object cannot drag the beams off the paths they trace.
            var go = new GameObject(mesh.name);
            // Outside Play mode (the EditMode tests) a beam must never be saved
            // into whatever scene is open. In play it is an ordinary object.
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
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

            var meshIds = new NativeArray<EntityId>(_beams.Count, Allocator.TempJob);
            for (int i = 0; i < _beams.Count; i++) meshIds[i] = _beams[i].Mesh.GetEntityId();
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
            [ReadOnly] public NativeArray<EntityId> MeshIds;
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
                if (settings != null && settings.WorldlineMaterial != null) return settings.WorldlineMaterial;
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
                                       "Assign a material on the Spacetime Settings asset.", this);
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

        /// <summary>Destroy that also works outside Play mode, where the EditMode
        /// tests drive this view.</summary>
        static void Dispose(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
