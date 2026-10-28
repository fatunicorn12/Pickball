using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// Presents an NPC's head rotor (Birdy's): it spins while the NPC lives,
    /// faster on Excited lines, and when the NPC dies it is knocked off the
    /// head, somersaults through the air and lands upright on the ground beyond
    /// it, winding down to a stop.
    ///
    /// <b>Why it flies off.</b> On the head, the rotor can only follow the head,
    /// and a body lying on its back turns the head's up horizontal, so the
    /// blades stood upright out of the floor whatever the death clip. Knocked
    /// off just before the head hits the ground, it can lie the right way up,
    /// and "his hat falls off" suits the character.
    ///
    /// The rotor is its own bone (BirdyModelPostprocessor adds it under Head,
    /// with its origin at the bottom of the mast and its up along the spin
    /// axis), which nothing in the Animator's clips drives, so this component
    /// owns it. While alive it only sets the bone's local rotation (rest pose
    /// times the spin about its up); after the knock it sets its world pose
    /// every frame from <see cref="NpcRotorFlight"/>. It runs in LateUpdate,
    /// after the Animator has posed the head it would otherwise follow.
    ///
    /// Presentation only (CodingStandards §1): it reads
    /// <see cref="NpcAnimatorDriver"/>'s read-only state (talking, speaking,
    /// emote, dead, death progress) and changes nothing but the rotor bone and,
    /// once it has flown, its renderer's off-screen update (its bounds follow
    /// the body, which it has left).
    ///
    /// <b>Time.</b> Conversations run at <c>Time.timeScale</c> 0, so while the
    /// NPC talks the spin uses unscaled time, like the Animator; otherwise
    /// scaled game time, which also times the fall.
    ///
    /// Hot-reload safe: the rest pose is captured once; OnDisable puts the
    /// rotor back on the head and drops the flight, and a re-enabled dead NPC
    /// re-knocks it, straight to its resting spot if the death has finished.
    /// Time is read only in LateUpdate and passed to <see cref="Tick"/>, the
    /// test seam.
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcRotor : MonoBehaviour
    {
        [Tooltip("The rotor's own bone (Rotor, under Head, added by BirdyModelPostprocessor). " +
                 "Its local up is the spin axis and its origin the bottom of the mast.")]
        [SerializeField] private Transform rotor;

        [Tooltip("The rotor's mesh. Once the rotor flies off, it is set to update off screen, " +
                 "because its bounds follow the body it has left. Optional.")]
        [SerializeField] private SkinnedMeshRenderer rotorRenderer;

        [Tooltip("Whose talk and death to follow. Empty: the NpcAnimatorDriver on this object.")]
        [SerializeField] private NpcAnimatorDriver driver;

        [Tooltip("The model's Animator, for the head and hips that aim the fall. Empty: the " +
                 "first Animator in the children.")]
        [SerializeField] private Animator animator;

        [Tooltip("Spin and fall tunables (the Rotor fields). Empty: built-in defaults.")]
        [SerializeField] private NpcAnimationConfig config;

        /// <summary>How far above and below the NPC's feet the landing looks
        /// for ground, m: about a step. A hit further off (a wall top, the
        /// player) is not the floor the body lies on.</summary>
        const float GroundSearch = 0.5f;

        bool _hasRest;
        Vector3 _restLocalPosition;
        Quaternion _restLocalRotation;
        float _angle;
        float _speed;

        NpcRotorFlight _flight;
        float _flightTime;
        Vector3 _landing;
        bool _rendererOverridden;
        bool _restUpdateWhenOffscreen;
        RaycastHit[] _hits;
        NpcAnimationConfig _defaultConfig;

        /// <summary>Whether the rotor has been knocked off (it never goes back on).</summary>
        public bool HasFlown => _flight != null;

        /// <summary>Whether the knocked-off rotor has come to rest.</summary>
        public bool IsSettled => _flight != null && _flightTime >= _flight.SettledTime;

        /// <summary>Current spin, °/s.</summary>
        public float SpinSpeed => _flight != null ? _flight.SpinAt(_flightTime) : _speed;

        /// <summary>The rotor bone this drives.</summary>
        public Transform Rotor => rotor;

        NpcAnimationConfig Config
        {
            get
            {
                if (config != null) return config;
                if (_defaultConfig == null)
                {
                    _defaultConfig = ScriptableObject.CreateInstance<NpcAnimationConfig>();
                    _defaultConfig.hideFlags = HideFlags.HideAndDontSave;
                }
                return _defaultConfig;
            }
        }

        void OnEnable()
        {
            if (driver == null) driver = GetComponent<NpcAnimatorDriver>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            CaptureRest();
        }

        void OnDisable()
        {
            if (rotor != null && _hasRest)
            {
                rotor.localPosition = _restLocalPosition;
                rotor.localRotation = _restLocalRotation;
            }
            if (_rendererOverridden && rotorRenderer != null) rotorRenderer.updateWhenOffscreen = _restUpdateWhenOffscreen;
            _rendererOverridden = false;
            _flight = null;
        }

        void OnDestroy()
        {
            if (_defaultConfig == null) return;
            if (Application.isPlaying) Destroy(_defaultConfig);
            else DestroyImmediate(_defaultConfig);
        }

        // The only place this component reads the clocks.
        void LateUpdate() => Tick(Time.deltaTime, Time.unscaledDeltaTime, Time.unscaledTime);

        /// <summary>
        /// One frame, after the Animator: spin the rotor on the head, or knock
        /// it off once the death reaches the config's rotorPopAt, or fly it.
        /// Public so EditMode tests and the setup's pose report can drive time
        /// explicitly.
        /// </summary>
        /// <param name="scaledDt">Game frame time, s: idling, and the fall.</param>
        /// <param name="unscaledDt">Unscaled frame time, s: while talking (time is paused).</param>
        /// <param name="unscaledNow">Unscaled time, s: whether a line's gesture is playing.</param>
        public void Tick(float scaledDt, float unscaledDt, float unscaledNow)
        {
            if (rotor == null) return;
            CaptureRest();
            var cfg = Config;
            bool talking = driver != null && driver.IsTalking;
            float dt = talking ? unscaledDt : scaledDt;

            if (_flight == null && driver != null && driver.IsDead && driver.DeathProgress >= cfg.RotorPopAt)
                Knock(cfg, finished: driver.DeathProgress >= 1f);

            if (_flight != null)
            {
                Fly(cfg, dt);
                return;
            }

            bool excited = talking && driver.CurrentEmote == DialogueEmote.Excited && driver.IsSpeakingAt(unscaledNow);
            float target = NpcRotorSpin.Target(excited, cfg.RotorIdleSpin, cfg.RotorExcitedSpin);
            _speed = NpcRotorSpin.Approach(_speed, target, cfg.RotorSpinResponse, dt);
            _angle = NpcRotorSpin.Advance(_angle, _speed, dt);
            rotor.localPosition = _restLocalPosition;
            rotor.localRotation = _restLocalRotation * Quaternion.AngleAxis(_angle, Vector3.up);
        }

        void CaptureRest()
        {
            if (_hasRest || rotor == null) return;
            _restLocalPosition = rotor.localPosition;
            _restLocalRotation = rotor.localRotation;
            _hasRest = true;
        }

        // ----------------------------------------------------------- the fall

        void Knock(NpcAnimationConfig cfg, bool finished)
        {
            var parent = rotor.parent;
            Vector3 from = parent != null ? parent.TransformPoint(_restLocalPosition) : rotor.position;
            Quaternion tilt = parent != null ? parent.rotation * _restLocalRotation : rotor.rotation;
            _landing = LandingSpot(cfg);
            _flight = new NpcRotorFlight(from, tilt, _landing, cfg.RotorFlightSeconds, cfg.RotorPopHeight,
                                         cfg.RotorTumbleTurns, cfg.RotorBounceHeight, cfg.RotorBounceSeconds,
                                         cfg.RotorPopSpin, cfg.RotorSpinDownSeconds);
            // Re-enabled after the death finished (a script reload): no replay.
            _flightTime = finished ? _flight.SettledTime : 0f;

            if (rotorRenderer != null && !_rendererOverridden)
            {
                _restUpdateWhenOffscreen = rotorRenderer.updateWhenOffscreen;
                rotorRenderer.updateWhenOffscreen = true;
                _rendererOverridden = true;
            }
        }

        void Fly(NpcAnimationConfig cfg, float dt)
        {
            // The head is still settling while the rotor is in the air, so the
            // spot follows it until touchdown, then stays put.
            if (!_flight.HasLanded(_flightTime)) _landing = LandingSpot(cfg);
            _flightTime += dt;
            _flight.Sample(_flightTime, _landing, out var position, out var tilt);
            _angle = NpcRotorSpin.Advance(_angle, _flight.SpinAt(_flightTime), dt);
            rotor.SetPositionAndRotation(position, tilt * Quaternion.AngleAxis(_angle, Vector3.up));
        }

        /// <summary>
        /// Where the rotor comes to rest: from where its mast would be on the
        /// head, rotorLandDistance further along the fall (hips to head, level)
        /// and rotorLandSide to the side, on the ground.
        /// </summary>
        Vector3 LandingSpot(NpcAnimationConfig cfg)
        {
            var parent = rotor.parent;
            Vector3 hub = parent != null ? parent.TransformPoint(_restLocalPosition) : rotor.position;
            Vector3 fall = FallDirection();
            Vector3 left = new Vector3(fall.z, 0f, -fall.x);
            Vector3 spot = hub + fall * cfg.RotorLandDistance + left * cfg.RotorLandSide;
            spot.y = GroundHeight(spot);
            return spot;
        }

        /// <summary>Level direction from the hips to the head; the NPC's back
        /// if the body is still too upright to tell.</summary>
        Vector3 FallDirection()
        {
            if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (head != null && hips != null)
                {
                    Vector3 d = head.position - hips.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.01f) return d.normalized;
                }
            }
            Vector3 back = -transform.forward;
            back.y = 0f;
            return back.sqrMagnitude > 1e-6f ? back.normalized : Vector3.back;
        }

        /// <summary>
        /// Height of the floor under <paramref name="at"/>: the nearest
        /// collider below a step above the NPC's feet that isn't the NPC itself
        /// or something that moves, within a step of the feet. Falls back to
        /// the feet's height (the floor the body lies on).
        /// </summary>
        float GroundHeight(Vector3 at)
        {
            float feet = transform.position.y;
            _hits ??= new RaycastHit[8];
            var origin = new Vector3(at.x, feet + GroundSearch, at.z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, GroundSearch * 2f,
                                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = feet, nearest = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var hit = _hits[i];
                var c = hit.collider;
                if (c == null || c.attachedRigidbody != null || c is CharacterController) continue;
                if (c.transform.IsChildOf(transform)) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    best = hit.point.y;
                }
            }
            return best;
        }
    }
}
