using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Spins Birdy's own (skinned) propeller in cutscenes and hides it while the loose prop is
    /// off his head. The propeller mesh is skinned 100% to the <c>Rotor</c> bone that
    /// BirdyModelPostprocessor adds under Head, and no clip animates that bone, so this
    /// component owns it outright.
    /// </summary>
    /// <remarks>
    /// <para><b>Why not NpcRotor:</b> the gameplay NPC's rotor reads a conversation and a death;
    /// a cutscene needs a speed it is told and an on/off. The cutscene actor is its own prefab,
    /// so the two never share a bone.</para>
    /// <para><b>Detached</b> scales the Rotor bone to zero, which collapses the skinned propeller
    /// to a point inside the helmet: only the loose <see cref="PropellerProp"/> shows. The rest
    /// pose is serialized by the setup menu rather than read in OnEnable, so a script reload
    /// mid-spin can't capture a spun pose as the rest.</para>
    /// <para>Runs in LateUpdate (after the Animator poses the head), in Play mode only. Timeline
    /// drives it by animating <see cref="targetSpin"/> and <see cref="attached"/>, or through
    /// <see cref="SetSpin"/> and <see cref="SetAttached"/>.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CutsceneRotor : MonoBehaviour
    {
        [Header("Wiring (setup menu)")]
        [Tooltip("Spin speeds and response shared with the loose propeller.")]
        [SerializeField] private PropellerConfig config;

        [Tooltip("The Rotor bone the propeller mesh is skinned to.")]
        [SerializeField] private Transform rotor;

        [Tooltip("The Rotor bone's local rotation at rest (written by the setup menu).")]
        [SerializeField] private Quaternion restLocalRotation = Quaternion.identity;

        [Tooltip("The Rotor bone's local scale at rest (written by the setup menu).")]
        [SerializeField] private Vector3 restLocalScale = Vector3.one;

        [Header("Playback (Timeline may animate these)")]
        [Tooltip("Spin the rotor is easing toward, in °/s. Use the config's idle, hover or rev " +
                 "speeds; dip it for Birdy's cheer slip (the rotor pitch drops with his mood).")]
        [SerializeField] private float targetSpin = 180f;

        [Tooltip("On: the propeller shows on Birdy's head. Off: the Rotor bone is scaled to zero " +
                 "so only the loose prop shows (scene 2 after the pop-off, until it is seated).")]
        [SerializeField] private bool attached = true;

        private float _speed;
        private float _angle;

        /// <summary>The Rotor bone this drives.</summary>
        public Transform Rotor => rotor;

        /// <summary>True while the propeller shows on Birdy's head.</summary>
        public bool Attached => attached;

        /// <summary>The current (eased) spin, °/s.</summary>
        public float CurrentSpin => _speed;

        /// <summary>The spin being eased toward, °/s.</summary>
        public float TargetSpin => targetSpin;

        /// <summary>The config, or null before the setup menu ran.</summary>
        public PropellerConfig Config => config;

        /// <summary>Set the spin to ease toward, °/s.</summary>
        public void SetSpin(float degreesPerSecond) => targetSpin = degreesPerSecond;

        /// <summary>Show (true) or hide (false) the propeller on Birdy's head.</summary>
        public void SetAttached(bool on) => attached = on;

        /// <summary>Setup-menu entry point: the bone, its rest pose and the config.</summary>
        public void Configure(PropellerConfig spinConfig, Transform rotorBone)
        {
            config = spinConfig;
            rotor = rotorBone;
            if (rotorBone != null)
            {
                restLocalRotation = rotorBone.localRotation;
                restLocalScale = rotorBone.localScale;
            }
            if (spinConfig != null) targetSpin = spinConfig.IdleSpin;
        }

        private void OnEnable()
        {
            _speed = attached ? targetSpin : 0f;
        }

        private void OnDisable()
        {
            // Leave the bone at rest so the Editor (and a prefab apply) never sees a spun or
            // collapsed rotor.
            if (rotor == null) return;
            rotor.localRotation = restLocalRotation;
            rotor.localScale = restLocalScale;
        }

        private void LateUpdate()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>Advance one frame. The test seam; LateUpdate passes Time.deltaTime.</summary>
        public void Tick(float dt)
        {
            if (rotor == null) return;
            float response = config != null ? config.SpinResponse : 4f;
            _speed = PropellerSpinMath.Approach(_speed, attached ? targetSpin : 0f, response, dt);
            _angle = PropellerSpinMath.Advance(_angle, _speed, dt);
            rotor.localRotation = restLocalRotation * Quaternion.AngleAxis(_angle, Vector3.up);
            rotor.localScale = attached ? restLocalScale : Vector3.zero;
        }
    }
}
