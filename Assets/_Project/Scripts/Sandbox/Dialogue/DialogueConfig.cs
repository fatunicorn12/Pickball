using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Every tunable of the talk system in one asset: who can be talked to, how
    /// fast text types, how the camera frames the conversation, and how the box
    /// and the prompt look.
    ///
    /// Read live: the interactor, director, camera and views read this asset
    /// when they need a value, so Inspector edits in Play mode are felt on the
    /// next prompt or line. Setup Dialogue seeds it once at
    /// <c>ScriptableObjects/Dialogue/DialogueConfig.asset</c> and never
    /// overwrites it, so hand tuning survives a re-run.
    ///
    /// Every time here is <b>unscaled</b> seconds: the conversation runs while
    /// <c>Time.timeScale</c> is 0.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Dialogue/Dialogue Config", fileName = "DialogueConfig")]
    public class DialogueConfig : ScriptableObject
    {
        // --------------------------------------------------------- interaction
        [Header("Interaction — who can be talked to")]
        [Tooltip("Furthest an NPC can be talked to by walking up to it, m, measured flat " +
                 "from the player to the NPC's focus point. Larger finds people from further " +
                 "away but makes the prompt pop up while just passing by.")]
        [SerializeField, Min(0.1f)] private float talkRange = 2.4f;
        [Tooltip("Widest angle off the player's facing an NPC can be in and still get the " +
                 "prompt, °. Smaller means you have to face who you want; 180 ignores facing.")]
        [SerializeField, Range(5f, 180f)] private float facingConeDegrees = 70f;
        [Tooltip("Furthest a LOCKED-ON NPC can be talked to, m. Lock-on is an explicit " +
                 "choice, so it reaches further than walking up and ignores facing.")]
        [SerializeField, Min(0.1f)] private float lockedTalkRange = 4.5f;
        [Tooltip("Largest height difference between the player's focus height and the NPC's " +
                 "focus point, m. Stops talking to someone on the balcony above.")]
        [SerializeField, Min(0.1f)] private float maxHeightDifference = 1.6f;
        [Tooltip("Hysteresis, m. The current target keeps the prompt this far past the range, " +
                 "and another NPC must be this much closer to take it. Stops the prompt " +
                 "flickering between two NPCs or at the range edge.")]
        [SerializeField, Min(0f)] private float stickyMargin = 0.35f;
        [Tooltip("Extra facing angle the current target keeps the prompt for, °.")]
        [SerializeField, Range(0f, 45f)] private float stickyConeDegrees = 12f;
        [Tooltip("Only offer a talk while the player is free: grounded, and not dashing, " +
                 "tethered, skating, swinging the sword or in 4D. Time freezes for the talk, " +
                 "so starting one mid-air or mid-move would hang the player in it.")]
        [SerializeField] private bool requireFree = true;
        [Tooltip("Seconds of being airborne that still count as grounded for the prompt. " +
                 "Covers one-frame ground-check flickers on slopes and steps.")]
        [SerializeField, Min(0f)] private float groundedGraceSeconds = 0.15f;
        [Tooltip("Seconds after a conversation closes before another can start. Stops the E " +
                 "press that closed it from opening it again.")]
        [SerializeField, Min(0f)] private float restartCooldownSeconds = 0.35f;
        [Tooltip("Seconds the world keeps running after the talk press, with the player's " +
                 "input blocked, before time stops. He slows to idle instead of freezing " +
                 "mid-stride, and the camera starts pushing in. 0 stops time at once.")]
        [SerializeField, Min(0f)] private float settleSeconds = 0.25f;

        // ---------------------------------------------------------------- text
        [Header("Text — the typewriter")]
        [Tooltip("Characters revealed per second. 40–50 reads like BotW; 0 shows each line " +
                 "at once.")]
        [SerializeField, Min(0f)] private float charactersPerSecond = 45f;
        [Tooltip("Extra pause after a comma, semicolon, colon or dash, s.")]
        [SerializeField, Min(0f)] private float commaPauseSeconds = 0.08f;
        [Tooltip("Extra pause after a full stop, ! or ?, s. Makes sentences breathe.")]
        [SerializeField, Min(0f)] private float sentencePauseSeconds = 0.22f;
        [Tooltip("A press within this long of a line starting or being revealed is ignored, " +
                 "s. Stops a double-tap from skipping a line unread.")]
        [SerializeField, Min(0f)] private float advanceLockoutSeconds = 0.12f;
        [Tooltip("Seconds between the conversation starting and the first letter typing. " +
                 "Lets the box fade in and the camera get going first.")]
        [SerializeField, Min(0f)] private float textStartDelaySeconds = 0.2f;
        [Tooltip("Pause the AudioListener during conversations, so the world goes quiet " +
                 "with time. Dialogue sounds must set AudioSource.ignoreListenerPause.")]
        [SerializeField] private bool pauseAudio = true;

        // -------------------------------------------------------------- camera
        [Header("Camera — the push-in")]
        [Tooltip("Height of the player's head above his root, m: the player end of the talk " +
                 "height check, and the camera pivot when the player has no humanoid Head bone " +
                 "(with one, the camera frames from the bone itself).")]
        [SerializeField, Min(0f)] private float playerFocusHeight = 1.35f;
        [Tooltip("How far behind the pivot the camera sits, along the line from the player " +
                 "to the NPC, m. Smaller pulls the camera in closer.")]
        [SerializeField, Min(0.2f)] private float pushInDistance = 1.7f;
        [Tooltip("Camera height above the pivot, m. A little up looks down on short NPCs.")]
        [SerializeField] private float cameraHeight = 0.3f;
        [Tooltip("Least sideways camera offset, m; the sign picks the shoulder. Negative is " +
                 "over the player's left shoulder (the sword is on the right), positive the " +
                 "right. Widened automatically when the NPC is close (Player Clearance).")]
        [SerializeField] private float sideOffset = -0.8f;
        [Tooltip("How far the sightline to the NPC's head must pass from the player's head, m. " +
                 "The chibi's hair is ~0.32 m round its skull, so below ~0.4 the hair starts " +
                 "to cover a short NPC. Larger swings the camera wider at close range.")]
        [SerializeField, Min(0f)] private float playerClearance = 0.45f;
        [Tooltip("Largest sideways offset, m, however close the NPC stands.")]
        [SerializeField, Min(0f)] private float maxSideOffset = 1.6f;

        [Header("Camera — composition")]
        [Tooltip("Screen height of the NPC's head (its focus point), 0 bottom to 1 top. About " +
                 "0.66 puts it on the upper third.")]
        [SerializeField, Range(0.3f, 0.95f)] private float npcHeadScreenHeight = 0.66f;
        [Tooltip("Screen height the NPC's feet should reach down to, 0–1. Keep it above the " +
                 "dialogue box (which covers the bottom ~0.27) so the whole NPC shows.")]
        [SerializeField, Range(0f, 0.9f)] private float npcFeetScreenHeight = 0.31f;
        [Tooltip("How far the NPC sits from the screen centre toward the open side, away from " +
                 "the player's shoulder, in screen widths. 0 is dead centre.")]
        [SerializeField, Range(0f, 0.4f)] private float npcScreenSide = 0.1f;
        [Tooltip("Narrowest field of view, °. The field of view is chosen so the NPC fills the " +
                 "space above the box; a very short NPC is not zoomed in past this.")]
        [SerializeField, Range(10f, 90f)] private float minFieldOfView = 30f;
        [Tooltip("Widest field of view, °. A tall NPC is cut off at the box rather than " +
                 "shrunk past this.")]
        [SerializeField, Range(10f, 90f)] private float maxFieldOfView = 50f;
        [Tooltip("Seconds to blend from the orbit camera into the conversation shot.")]
        [SerializeField, Min(0.01f)] private float blendInSeconds = 0.45f;
        [Tooltip("Seconds the close takes, time still paused: the box fades and the camera " +
                 "blends back to exactly where the orbit camera left it.")]
        [SerializeField, Min(0.01f)] private float closeSeconds = 0.35f;
        [Tooltip("Radius kept clear around the camera, m. Walls and props between the " +
                 "player's shoulder and the shot pull the camera in.")]
        [SerializeField, Min(0.01f)] private float cameraCollisionRadius = 0.2f;
        [Tooltip("Closest the camera may be pulled to the pivot, m.")]
        [SerializeField, Min(0f)] private float minCameraDistance = 0.4f;
        [Tooltip("Layers that block the camera.")]
        [SerializeField] private LayerMask cameraObstructionMask = ~0;

        // ----------------------------------------------------------------- box
        [Header("Dialogue box")]
        [Tooltip("Seconds for the box to fade and scale in.")]
        [SerializeField, Min(0.01f)] private float boxFadeInSeconds = 0.18f;
        [Tooltip("Seconds for the box to fade out. Capped at Close Seconds.")]
        [SerializeField, Min(0.01f)] private float boxFadeOutSeconds = 0.15f;
        [Tooltip("Box scale when it starts to appear, 0–1. Closer to 1 is subtler.")]
        [SerializeField, Range(0.5f, 1f)] private float boxScaleFrom = 0.94f;
        [Tooltip("Box fill. Dark and translucent so the world shows through.")]
        [SerializeField] private Color boxColour = new(0.04f, 0.045f, 0.06f, 0.8f);
        [Tooltip("Speaker name tab fill.")]
        [SerializeField] private Color nameTabColour = new(0.04f, 0.045f, 0.06f, 0.9f);
        [Tooltip("Speaker name text: the accent colour.")]
        [SerializeField] private Color nameColour = new(1f, 0.8f, 0.36f, 1f);
        [Tooltip("Body text colour.")]
        [SerializeField] private Color textColour = new(0.97f, 0.97f, 0.95f, 1f);
        [Tooltip("Colour of the ▼ shown when the line is fully revealed.")]
        [SerializeField] private Color continueColour = new(1f, 0.8f, 0.36f, 1f);
        [Tooltip("Pulses per second of the ▼.")]
        [SerializeField, Min(0.1f)] private float continuePulseHz = 1.6f;
        [Tooltip("How far the ▼ bobs, in canvas units (1920-wide reference).")]
        [SerializeField, Min(0f)] private float continueBob = 5f;

        // -------------------------------------------------------------- prompt
        [Header("Talk prompt")]
        [Tooltip("Height of the prompt above the NPC's focus point, m.")]
        [SerializeField] private float promptHeightOffset = 0.45f;
        [Tooltip("Seconds for the prompt to fade in or out.")]
        [SerializeField, Min(0.01f)] private float promptFadeSeconds = 0.12f;
        [Tooltip("The verb on the prompt.")]
        [SerializeField] private string promptLabel = "Talk";
        [Tooltip("Key cap shown while the keyboard was used last.")]
        [SerializeField] private string keyboardKeyLabel = "E";
        [Tooltip("Key cap shown while a gamepad was used last.")]
        [SerializeField] private string gamepadKeyLabel = "RB";

        // ---------------------------------------------------------- properties

        /// <summary>Walk-up talk range, m (flat).</summary>
        public float TalkRange => talkRange;
        /// <summary>Half-angle of the facing cone, °.</summary>
        public float FacingConeDegrees => facingConeDegrees;
        /// <summary>Talk range for the lock-on target, m (flat).</summary>
        public float LockedTalkRange => lockedTalkRange;
        /// <summary>Largest player-to-NPC focus height difference, m.</summary>
        public float MaxHeightDifference => maxHeightDifference;
        /// <summary>Range hysteresis for the current target, m.</summary>
        public float StickyMargin => stickyMargin;
        /// <summary>Facing hysteresis for the current target, °.</summary>
        public float StickyConeDegrees => stickyConeDegrees;
        /// <summary>Whether a talk needs the player free: grounded, not in an exclusive move,
        /// not attacking, not in 4D.</summary>
        public bool RequireFree => requireFree;
        /// <summary>Airborne time that still counts as grounded, s.</summary>
        public float GroundedGraceSeconds => groundedGraceSeconds;
        /// <summary>Unscaled seconds after a close before a new talk may start.</summary>
        public float RestartCooldownSeconds => restartCooldownSeconds;
        /// <summary>Seconds of running time, input blocked, between the talk press and the pause.</summary>
        public float SettleSeconds => settleSeconds;

        /// <summary>Typewriter speed, characters per second; 0 or less is instant.</summary>
        public float CharactersPerSecond => charactersPerSecond;
        /// <summary>Pause after , ; : and dashes, s.</summary>
        public float CommaPauseSeconds => commaPauseSeconds;
        /// <summary>Pause after . ! ?, s.</summary>
        public float SentencePauseSeconds => sentencePauseSeconds;
        /// <summary>Presses this soon after a line change are ignored, s.</summary>
        public float AdvanceLockoutSeconds => advanceLockoutSeconds;
        /// <summary>Delay before the first line types, s.</summary>
        public float TextStartDelaySeconds => textStartDelaySeconds;
        /// <summary>Whether the AudioListener pauses with time.</summary>
        public bool PauseAudio => pauseAudio;

        /// <summary>Player head height above his root, m (talk height check; camera fallback).</summary>
        public float PlayerFocusHeight => playerFocusHeight;
        /// <summary>Blend into the conversation shot, s.</summary>
        public float BlendInSeconds => blendInSeconds;
        /// <summary>Length of the close (box out, camera back), s.</summary>
        public float CloseSeconds => closeSeconds;
        /// <summary>Camera clearance radius, m.</summary>
        public float CameraCollisionRadius => cameraCollisionRadius;
        /// <summary>Closest the camera may be pulled to the pivot, m.</summary>
        public float MinCameraDistance => minCameraDistance;
        /// <summary>Layers that block the camera.</summary>
        public LayerMask CameraObstructionMask => cameraObstructionMask;

        /// <summary>Box fade-in time, s.</summary>
        public float BoxFadeInSeconds => boxFadeInSeconds;
        /// <summary>Box fade-out time, s, never longer than the close.</summary>
        public float BoxFadeOutSeconds => Mathf.Min(boxFadeOutSeconds, closeSeconds);
        /// <summary>Box scale at the start of its fade-in.</summary>
        public float BoxScaleFrom => boxScaleFrom;
        /// <summary>Box fill colour.</summary>
        public Color BoxColour => boxColour;
        /// <summary>Name tab fill colour.</summary>
        public Color NameTabColour => nameTabColour;
        /// <summary>Speaker name colour.</summary>
        public Color NameColour => nameColour;
        /// <summary>Body text colour.</summary>
        public Color TextColour => textColour;
        /// <summary>Continue arrow colour.</summary>
        public Color ContinueColour => continueColour;
        /// <summary>Continue arrow pulse rate, Hz.</summary>
        public float ContinuePulseHz => continuePulseHz;
        /// <summary>Continue arrow bob height, canvas units.</summary>
        public float ContinueBob => continueBob;

        /// <summary>Prompt height above the focus point, m.</summary>
        public float PromptHeightOffset => promptHeightOffset;
        /// <summary>Prompt fade time, s.</summary>
        public float PromptFadeSeconds => promptFadeSeconds;
        /// <summary>Prompt verb.</summary>
        public string PromptLabel => promptLabel;
        /// <summary>Key cap text for keyboard players.</summary>
        public string KeyboardKeyLabel => keyboardKeyLabel;
        /// <summary>Key cap text for gamepad players.</summary>
        public string GamepadKeyLabel => gamepadKeyLabel;

        /// <summary>The selection rules for <see cref="TalkTargetSelector"/>.</summary>
        public TalkSelectionRules SelectionRules => new()
        {
            TalkRange = talkRange,
            FacingConeDegrees = facingConeDegrees,
            LockedTalkRange = lockedTalkRange,
            MaxHeightDifference = maxHeightDifference,
            StickyMargin = stickyMargin,
            StickyConeDegrees = stickyConeDegrees,
        };

        /// <summary>The camera framing for <see cref="DialogueFraming"/>.</summary>
        public DialogueFramingSettings Framing => new()
        {
            PushInDistance = pushInDistance,
            CameraHeight = cameraHeight,
            SideOffset = sideOffset,
            PlayerClearance = playerClearance,
            MaxSideOffset = maxSideOffset,
            NpcHeadScreenHeight = npcHeadScreenHeight,
            NpcFeetScreenHeight = npcFeetScreenHeight,
            NpcScreenSide = npcScreenSide,
            MinFieldOfView = minFieldOfView,
            MaxFieldOfView = Mathf.Max(minFieldOfView, maxFieldOfView),
        };

        /// <summary>The typewriter timing for <see cref="DialogueRunner"/>.</summary>
        public TypewriterTiming Typewriter => new()
        {
            CharactersPerSecond = charactersPerSecond,
            CommaPause = commaPauseSeconds,
            SentencePause = sentencePauseSeconds,
            AdvanceLockout = advanceLockoutSeconds,
        };
    }
}
