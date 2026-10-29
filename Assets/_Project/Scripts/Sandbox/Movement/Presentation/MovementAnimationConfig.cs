using UnityEngine;

namespace Pickleball.Sandbox.Movement.Presentation
{
    /// <summary>
    /// Presentation tunables for the player's movement: how the Animator is fed
    /// and how the model sits on the capsule. Nothing here changes how he moves —
    /// gameplay values are on <see cref="MovementConfig"/>.
    ///
    /// The stride and ride-height fields are MEASURED by the setup menu
    /// (Tools ▸ Pickleball ▸ Setup Movement) and written here, so re-running it
    /// keeps them in step with the clips.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Movement Animation Config", fileName = "MovementAnimationConfig")]
    public class MovementAnimationConfig : ScriptableObject
    {
        [Header("Model placement")]
        [Tooltip("Vertical nudge for the model against the capsule, m. Humanoid playback pins " +
                 "the root at the body's centre of mass, so the model sinks by about its hip " +
                 "height and this lifts it back. Live in the inspector.")]
        [SerializeField, Range(-0.3f, 0.7f)] private float modelYOffset = 0.64f;
        [Tooltip("How far the model rises onto the deck when riding, m (measured off the " +
                 "board by the setup).")]
        [SerializeField, Min(0f)] private float skateRideHeight = 0.2f;
        [Tooltip("How fast the model eases onto and off the deck, m/s.")]
        [SerializeField, Min(0.01f)] private float rideLiftRate = 1.2f;

        [Header("Stride matching")]
        [Tooltip("Nudge if the feet still slide — scales clip playback against travel.")]
        [SerializeField, Range(0.5f, 2f)] private float strideScale = 1f;
        [Tooltip("Ground speed the RUN clip strides at on this character, m/s (measured). " +
                 "Above the locomotion tree's top clip, playback is sped up by speed / its " +
                 "stride instead, or the feet skate.")]
        [SerializeField, Min(0.01f)] private float runClipStride = 2.57f;
        [Tooltip("Same for the SPRINT clip at the top of the tree, m/s. 0 = the tree has no " +
                 "sprint clip and the run clip is the top.")]
        [SerializeField, Min(0f)] private float sprintClipStride;
        [Tooltip("Same for the crouch-walk clip, m/s.")]
        [SerializeField, Min(0.01f)] private float crouchClipStride = 0.69f;
        [Tooltip("Damping on the Speed parameter, s. Raw speed jumps (dash exit, skid push-off, " +
                 "landing) would otherwise pop the blend tree between clips in one frame.")]
        [SerializeField, Min(0f)] private float speedDampTime = 0.08f;
        [Tooltip("Once he is at rest, a damped Speed below this snaps to exactly 0 — the damper " +
                 "only approaches its target and otherwise holds the walk clip at a sliver of " +
                 "weight for seconds.")]
        [SerializeField, Min(0f)] private float speedRestSnap = 0.01f;

        [Header("Triggers and timing")]
        [Tooltip("A one-shot trigger (Jump, Stop, Flip, Dash) the Animator has not consumed " +
                 "within this long is withdrawn, s. Unity latches an unconsumed trigger forever, " +
                 "so a stray flip would otherwise replay seconds later.")]
        [SerializeField, Min(0.01f)] private float triggerLifetime = 0.2f;
        [Tooltip("Grounded is held false this long after any takeoff, s, so a ground probe that " +
                 "still sees the floor for a frame cannot fire the landing.")]
        [SerializeField, Min(0f)] private float airborneLatch = 0.15f;
        [Tooltip("Airborne length of the trimmed Front Flip (frames 5–41), s. Flips are " +
                 "time-scaled to the real airtime so they never over- or under-run.")]
        [SerializeField, Min(0.05f)] private float flipAirborneSeconds = 1.2f;
        [Tooltip("Clamp on that time scale (min, max).")]
        [SerializeField] private Vector2 flipSpeedRange = new(0.4f, 2.5f);
        [Tooltip("How long the hook-throw gesture is blended over the upper body, s (the " +
                 "trimmed throw clip: frames 55–80).")]
        [SerializeField, Min(0f)] private float gestureSeconds = 0.87f;
        [Tooltip("Gesture layer fade rate, weight per second.")]
        [SerializeField, Min(0.1f)] private float gestureFadeRate = 6f;

        [Header("Procedural poses (placeholders until dedicated clips)")]
        [Tooltip("Tilt the model for the dive and belly slide, lower it onto the floor for the " +
                 "slide, and spin it for the spin jump. Leave on: the dive and slide clips set " +
                 "their pitch to 0 but still need the slide offset.")]
        [SerializeField] private bool proceduralPoses = true;
        [Tooltip("Forward pitch of the model while diving, °. 0 with the Run To Dive clip, " +
                 "which is already flat; about 60 if the dive falls back to the dash pose.")]
        [SerializeField, Range(0f, 90f)] private float divePitch = 0f;
        [Tooltip("Forward pitch of the model while belly sliding, °. 0 with the Run To Dive " +
                 "clip, which is already flat; about 82 if the slide falls back to the fall loop.")]
        [SerializeField, Range(0f, 90f)] private float bellySlidePitch = 0f;
        [Tooltip("Model offset while belly sliding, m (local), so the body lies on the floor " +
                 "instead of floating at standing hip height. -0.45 puts Belly Slide Pose Wide's " +
                 "lowest point about 1 cm above the floor.")]
        [SerializeField] private Vector3 bellySlideOffset = new(0f, -0.45f, 0f);
        [Tooltip("How fast poses blend in and out, 1/s (exponential).")]
        [SerializeField, Min(0.1f)] private float poseBlendRate = 14f;
        [Tooltip("Corkscrew spin speed of the model during the spin jump, °/s.")]
        [SerializeField] private float spinYawSpeed = 1080f;

        public float ModelYOffset => modelYOffset;
        public float SkateRideHeight => skateRideHeight;
        public float RideLiftRate => rideLiftRate;
        public float StrideScale => strideScale;
        public float RunClipStride => runClipStride;
        public float SprintClipStride => sprintClipStride;
        public float CrouchClipStride => crouchClipStride;
        public float SpeedDampTime => speedDampTime;
        public float SpeedRestSnap => speedRestSnap;
        public float TriggerLifetime => triggerLifetime;
        public float AirborneLatch => airborneLatch;
        public float FlipAirborneSeconds => flipAirborneSeconds;
        public Vector2 FlipSpeedRange => flipSpeedRange;
        public float GestureSeconds => gestureSeconds;
        public float GestureFadeRate => gestureFadeRate;
        public bool ProceduralPoses => proceduralPoses;
        public float DivePitch => divePitch;
        public float BellySlidePitch => bellySlidePitch;
        public Vector3 BellySlideOffset => bellySlideOffset;
        public float PoseBlendRate => poseBlendRate;
        public float SpinYawSpeed => spinYawSpeed;

        /// <summary>The fastest clip at the top of the ground tree, m/s.</summary>
        public float TopClipStride => sprintClipStride > 0.01f ? sprintClipStride : runClipStride;
    }
}
