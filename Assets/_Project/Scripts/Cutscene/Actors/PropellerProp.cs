using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Birdy's propeller as its own object: it pops off mid-tumble, arcs onto the ledge, spins
    /// down, glints, is carried back and seated on his head. The mesh is Birdy's own propeller
    /// part, extracted with its pivot on the hub axis at the foot of the mast and +Y along the
    /// spin axis, so seating it on a socket is just parenting with an identity local pose.
    /// </summary>
    /// <remarks>
    /// <para>Flight paths are the timeline's job (it moves this object's root); this component
    /// owns only the spin and the socket. It spins its <see cref="Spinner"/> child, never the
    /// root, so a track that animates the root and this spin never fight.</para>
    /// <para>Seated on Birdy's <c>PropellerSocket</c> it sits exactly where his skinned rotor
    /// sits (the socket is the Rotor bone's rest pose), so the cutscene can swap between the two
    /// (<see cref="CutsceneRotor.SetAttached"/>) without a visible pop.</para>
    /// <para>Runs in LateUpdate in Play mode only.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PropellerProp : MonoBehaviour
    {
        [Header("Wiring (setup menu)")]
        [Tooltip("Spin speeds, response and wind-down time shared with Birdy's own rotor.")]
        [SerializeField] private PropellerConfig config;

        [Tooltip("The child that spins about its local +Y (the model sits under it).")]
        [SerializeField] private Transform spinner;

        [Header("Playback (Timeline may animate this)")]
        [Tooltip("Spin the propeller is easing toward, in °/s. Ignored while winding down.")]
        [SerializeField] private float targetSpin;

        private float _speed;
        private float _angle;
        private bool _windingDown;
        private float _windElapsed;
        private float _windFrom;

        /// <summary>The spinning child.</summary>
        public Transform Spinner => spinner;

        /// <summary>The current spin, °/s.</summary>
        public float CurrentSpin => _speed;

        /// <summary>True between <see cref="SpinDown"/> and the next <see cref="SpinTo"/>.</summary>
        public bool IsWindingDown => _windingDown;

        /// <summary>The config, or null before the setup menu ran.</summary>
        public PropellerConfig Config => config;

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(PropellerConfig spinConfig, Transform spinChild)
        {
            config = spinConfig;
            spinner = spinChild;
        }

        /// <summary>Ease toward a spin, °/s (seat and spin up, the spin loop).</summary>
        public void SpinTo(float degreesPerSecond)
        {
            targetSpin = degreesPerSecond;
            _windingDown = false;
        }

        /// <summary>
        /// Start winding down from the current spin to rest over the config's
        /// <see cref="PropellerConfig.SpinDownSeconds"/> (the ledge landing).
        /// </summary>
        public void SpinDown()
        {
            _windingDown = true;
            _windElapsed = 0f;
            _windFrom = _speed;
            targetSpin = 0f;
        }

        /// <summary>
        /// Seat the propeller on a socket: parented, its pivot (the mast foot) on the socket and
        /// its spin axis along the socket's up.
        /// </summary>
        public void AttachTo(Transform socket)
        {
            if (socket == null) return;
            transform.SetParent(socket, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Seat on an actor's anchor: <see cref="CutsceneAnchor.PropellerSocket"/> on Birdy's
        /// helmet, or <see cref="CutsceneAnchor.HandR"/> to carry it. Falls back to the Head
        /// anchor, then the actor root, when the actor doesn't provide the anchor.
        /// </summary>
        public void AttachTo(CutsceneActor actor, CutsceneAnchor anchor)
        {
            if (actor == null) return;
            Transform socket = actor.GetAnchor(anchor);
            if (socket == null) socket = actor.GetAnchor(CutsceneAnchor.Head);
            AttachTo(socket != null ? socket : actor.transform);
        }

        /// <summary>Let go: keep the world pose, reparent (null = scene root).</summary>
        public void Detach(Transform newParent = null)
        {
            transform.SetParent(newParent, true);
        }

        private void OnEnable()
        {
            _speed = targetSpin;
            _windingDown = false;
        }

        private void LateUpdate()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>Advance one frame. The test seam; LateUpdate passes Time.deltaTime.</summary>
        public void Tick(float dt)
        {
            if (_windingDown)
            {
                float seconds = config != null ? config.SpinDownSeconds : 1.5f;
                _windElapsed += dt;
                _speed = PropellerSpinMath.WindDown(_windFrom, _windElapsed, seconds);
            }
            else
            {
                float response = config != null ? config.SpinResponse : 4f;
                _speed = PropellerSpinMath.Approach(_speed, targetSpin, response, dt);
            }
            _angle = PropellerSpinMath.Advance(_angle, _speed, dt);
            if (spinner != null) spinner.localRotation = Quaternion.AngleAxis(_angle, Vector3.up);
        }
    }
}
