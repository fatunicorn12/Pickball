using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Lip-sync for a model with painted mouths: each frame it turns the voice openness that
    /// <see cref="MouthFlap"/> measures (RMS, noise floor, attack/release) into a mouth shape with
    /// <see cref="MouthShapePicker"/> (thresholds with hysteresis and a minimum hold, so it doesn't
    /// flicker) and hands the shape to <see cref="EyeSwap"/>, which owns the face texture.
    /// </summary>
    /// <remarks>
    /// <para>While this component is present and the face has painted mouths, the setup menu turns
    /// the flap's head nod off: the nod is the fallback for a model without painted mouths (a
    /// swapped-in model gets the nod and no MouthSwap).</para>
    /// <para>Runs after MouthFlap's LateUpdate (execution order), in Play mode only. Closed while
    /// the voice is silent: the openness releases to 0 and the shape follows after its hold.</para>
    /// </remarks>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class MouthSwap : MonoBehaviour
    {
        [Header("Wiring (setup menu)")]
        [Tooltip("Thresholds, hysteresis and minimum hold.")]
        [SerializeField] private MouthSwapConfig config;

        [Tooltip("Where the voice openness comes from (the actor's MouthFlap).")]
        [SerializeField] private MouthFlap flap;

        [Tooltip("The face that shows the painted mouths (the model's EyeSwap).")]
        [SerializeField] private EyeSwap face;

        private MouthShapePicker _picker;

        /// <summary>The shape picked last frame (0 closed, 1 half, 2 open).</summary>
        public int Shape => _picker != null ? _picker.Shape : MouthShapePicker.Closed;

        /// <summary>The face this drives.</summary>
        public EyeSwap Face => face;

        /// <summary>The flap whose openness this reads.</summary>
        public MouthFlap Flap => flap;

        /// <summary>The thresholds.</summary>
        public MouthSwapConfig Config => config;

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(MouthSwapConfig swapConfig, MouthFlap source, EyeSwap target)
        {
            config = swapConfig;
            flap = source;
            face = target;
        }

        private void OnEnable()
        {
            if (_picker == null) _picker = new MouthShapePicker();
            _picker.Reset();
        }

        private void OnDisable()
        {
            if (face != null) face.SetMouth(MouthShapePicker.Closed);
        }

        private void LateUpdate()
        {
            Tick(flap != null ? flap.Openness : 0f, Time.deltaTime);
        }

        /// <summary>Advance one frame from an openness. The test seam; LateUpdate reads the flap.</summary>
        public void Tick(float openness, float dt)
        {
            if (config == null || face == null || !face.HasMouths) return;
            if (_picker == null) _picker = new MouthShapePicker();
            face.SetMouth(_picker.Step(openness, dt, config));
        }
    }
}
