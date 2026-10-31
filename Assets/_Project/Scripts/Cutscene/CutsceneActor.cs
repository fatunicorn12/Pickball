using UnityEngine;

namespace Pickleball.Cutscene
{
    /// <summary>
    /// A character's stand-in slot in a cutscene: an id that matches the script's speaker, the
    /// named anchor transforms, the voice source, and the character's measured size. Timeline
    /// tracks bind to this component, not to a model, so swapping the model under it (or putting
    /// it on the gameplay Player) changes nothing in the shots.
    /// </summary>
    /// <remarks>
    /// Sizes are measured in the rest pose by the setup menu that builds the actor
    /// (<c>Tools ▸ Pickleball ▸ Cutscene</c>) and stored here, because a posed body (lying down,
    /// mid-tumble) has no usable height. Cameras read them at runtime, so a different-size model
    /// stays in frame after a re-measure. Anchors are wired by the same menu; nothing here
    /// searches the hierarchy at runtime.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CutsceneActor : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Speaker id this slot plays, exactly as the script writes it (\"MC\", \"BIRDY\"). " +
                 "Subtitles, audio and mouth flap route lines by this id.")]
        [SerializeField] private string actorId = CutsceneSpeakers.MC;

        [Header("Anchors")]
        [Tooltip("Anchor transforms indexed by CutsceneAnchor (Head, Eyes, Hand_R, Feet, " +
                 "PropellerSocket, Blade). Wired by the setup menu; unused slots stay empty.")]
        [SerializeField] private Transform[] anchors = new Transform[CutsceneAnchors.Count];

        [Header("Voice")]
        [Tooltip("AudioSource that plays this character's lines. The audio track binds here and " +
                 "the mouth flap reads its level. Optional until audio exists.")]
        [SerializeField] private AudioSource voice;

        [Header("Measured size (rest pose, written by the setup menu)")]
        [Tooltip("Rest-pose height from the feet to the top of the model, in metres. Shots scale " +
                 "their offsets by this, so it must be re-measured after a model swap.")]
        [Min(0.01f)]
        [SerializeField] private float height = 1.6f;

        [Tooltip("Rest-pose head-box height, in metres: twice the distance from the Head anchor to " +
                 "the top of the model. Close-up rules (Birdy's TCU) are written as a share of the " +
                 "frame filled by this.")]
        [Min(0.01f)]
        [SerializeField] private float headBox = 0.4f;

        /// <summary>Speaker id this slot plays.</summary>
        public string ActorId => actorId;

        /// <summary>Rest-pose height in metres (H for MC, h for Birdy in the shot list).</summary>
        public float Height => height;

        /// <summary>Rest-pose head-box height in metres (HB in the shot list).</summary>
        public float HeadBox => headBox;

        /// <summary>The voice source, or null before audio is wired.</summary>
        public AudioSource Voice => voice;

        /// <summary>
        /// The anchor transform, or null when this actor doesn't provide it (MC has no
        /// PropellerSocket). Callers fall back to <see cref="CutsceneAnchor.Head"/> or the root.
        /// </summary>
        public Transform GetAnchor(CutsceneAnchor anchor)
        {
            int i = (int)anchor;
            return anchors != null && i >= 0 && i < anchors.Length ? anchors[i] : null;
        }

        /// <summary>World position of an anchor, or of the actor root if the anchor is missing.</summary>
        public Vector3 AnchorPosition(CutsceneAnchor anchor)
        {
            Transform t = GetAnchor(anchor);
            return t != null ? t.position : transform.position;
        }

        /// <summary>The shot list's <c>Mid</c>: halfway between Feet and Head.</summary>
        public Vector3 MidPosition =>
            Vector3.Lerp(AnchorPosition(CutsceneAnchor.Feet), AnchorPosition(CutsceneAnchor.Head), 0.5f);

        /// <summary>
        /// Editor and setup-menu entry point: set the identity, anchors and measured size in one
        /// call so a builder never half-wires a slot.
        /// </summary>
        public void Configure(string id, Transform[] anchorTransforms, AudioSource voiceSource,
            float restHeight, float restHeadBox)
        {
            actorId = id;
            anchors = new Transform[CutsceneAnchors.Count];
            if (anchorTransforms != null)
            {
                for (int i = 0; i < anchors.Length && i < anchorTransforms.Length; i++)
                    anchors[i] = anchorTransforms[i];
            }
            voice = voiceSource;
            height = Mathf.Max(0.01f, restHeight);
            headBox = Mathf.Max(0.01f, restHeadBox);
        }
    }
}
