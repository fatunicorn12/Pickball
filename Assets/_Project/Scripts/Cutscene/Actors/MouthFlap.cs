using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Flaps a character's beak or jaw from the loudness of its voice line, so every voiced line
    /// gets mouth movement without per-line keys. Neither MC nor Birdy has a jaw or beak bone
    /// (or blendshapes), so on them it nods the head a few degrees per syllable; a rig with a
    /// humanoid Jaw bone gets the jaw instead (the setup menu picks).
    /// </summary>
    /// <remarks>
    /// <para>Each LateUpdate, while the voice is playing, it reads the source's output into a
    /// buffer allocated in OnEnable, takes the RMS, maps it to an openness
    /// (<see cref="MouthFlapMath"/>) with attack/release smoothing, and rotates the bone about the
    /// actor's right axis on top of the pose the Animator wrote this frame (additive).</para>
    /// <para>If nothing re-poses the bone between frames (no Animator, or it's culled), the
    /// previous frame's nod is taken off first, so the nod never accumulates.</para>
    /// <para>Off, and costing nothing, while the source isn't playing: no audio exists yet, so
    /// until it does this is inert.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MouthFlap : MonoBehaviour
    {
        [Header("Wiring (setup menu)")]
        [Tooltip("Level, response and nod size.")]
        [SerializeField] private MouthFlapConfig config;

        [Tooltip("The voice whose output drives the flap (the CutsceneActor's voice).")]
        [SerializeField] private AudioSource voice;

        [Tooltip("The bone that nods: the humanoid Jaw if the rig has one, else the Head.")]
        [SerializeField] private Transform bone;

        [Tooltip("Whose right axis the nod turns about (the actor root), so the nod is a pitch " +
                 "whatever the bone's own axes are.")]
        [SerializeField] private Transform facing;

        private float[] _samples;
        private float _openness;
        private bool _wrote;
        private Quaternion _lastBase;
        private Quaternion _lastWritten;

        /// <summary>Current smoothed openness, 0..1.</summary>
        public float Openness => _openness;

        /// <summary>The voice source.</summary>
        public AudioSource Voice => voice;

        /// <summary>The bone that nods.</summary>
        public Transform Bone => bone;

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(MouthFlapConfig flapConfig, AudioSource voiceSource, Transform nodBone, Transform facingRoot)
        {
            config = flapConfig;
            voice = voiceSource;
            bone = nodBone;
            facing = facingRoot;
        }

        private void OnEnable()
        {
            int n = config != null ? config.SampleCount : 256;
            if (_samples == null || _samples.Length != n) _samples = new float[n];
            _openness = 0f;
            _wrote = false;
        }

        private void LateUpdate()
        {
            float rms = 0f;
            if (voice != null && voice.isPlaying && _samples != null)
            {
                voice.GetOutputData(_samples, 0);
                rms = MouthFlapMath.Rms(_samples, _samples.Length);
            }
            Apply(rms, Time.deltaTime);
        }

        /// <summary>
        /// Advance one frame from a measured RMS. The test seam; LateUpdate measures the voice.
        /// </summary>
        public void Apply(float rms, float dt)
        {
            if (config == null || bone == null) return;

            // Undo last frame's nod if nothing re-posed the bone since.
            if (_wrote && bone.localRotation == _lastWritten) bone.localRotation = _lastBase;
            _wrote = false;

            float target = MouthFlapMath.Openness(rms, config.NoiseFloor, config.FullOpen);
            _openness = MouthFlapMath.Smooth(_openness, target, config.AttackRate, config.ReleaseRate, dt);
            if (_openness < 1e-4f && target <= 0f)
            {
                _openness = 0f;
                return;
            }

            float angle = MouthFlapMath.Angle(_openness, config.MaxAngle);
            Vector3 axis = facing != null ? facing.right : transform.right;
            Quaternion before = bone.localRotation;
            bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
            _lastBase = before;
            _lastWritten = bone.localRotation;
            _wrote = true;
        }
    }
}
