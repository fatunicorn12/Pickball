using System;
using UnityEngine;

namespace Pickleball.Cutscene.Script
{
    /// <summary>
    /// One voiced line as stored in <see cref="CutsceneScript"/>: everything the parser derived plus
    /// the AudioClip slot a person fills in. The importer rewrites the text fields on every run and
    /// keeps <see cref="Audio"/> by id, so re-importing after a script edit never loses voice work.
    /// </summary>
    [Serializable]
    public sealed class CutsceneScriptLine
    {
        [Tooltip("Stable id OC_S<scene>_<nn>. Audio, subtitles and timelines key on it.")]
        [SerializeField] private string id = string.Empty;

        [Tooltip("Scene number.")]
        [SerializeField] private int scene;

        [Tooltip("Speaker id (MC, BIRDY).")]
        [SerializeField] private string speaker = string.Empty;

        [Tooltip("Text after the colon, tags included. Paste this into ElevenLabs.")]
        [TextArea(1, 4)]
        [SerializeField] private string rawText = string.Empty;

        [Tooltip("Tag-free text the subtitles show.")]
        [TextArea(1, 4)]
        [SerializeField] private string displayText = string.Empty;

        [Tooltip("Audio tags, lower case, with their display-text position.")]
        [SerializeField] private ScriptTag[] tags = new ScriptTag[0];

        [Tooltip("Subtitle cards (≤ 84 characters each).")]
        [SerializeField] private string[] cards = new string[0];

        [Tooltip("Card index a mid-line hold follows (OC_S2_09: 0), or -1.")]
        [SerializeField] private int midHoldAfterCard = -1;

        [Header("Voice")]
        [Tooltip("The generated voice line. Empty uses the placeholder duration " +
                 "max(1.5, chars / 15) s. Trim ElevenLabs' lead-in silence: holds are timeline gaps. " +
                 "Run Rebuild Timelines after assigning.")]
        [SerializeField] private AudioClip audio;

        /// <summary>Line id.</summary>
        public string Id => id;

        /// <summary>Scene number.</summary>
        public int Scene => scene;

        /// <summary>Speaker id.</summary>
        public string Speaker => speaker;

        /// <summary>Raw text with tags.</summary>
        public string RawText => rawText;

        /// <summary>Display text.</summary>
        public string DisplayText => displayText;

        /// <summary>Tags in script order.</summary>
        public ScriptTag[] Tags => tags;

        /// <summary>Subtitle cards.</summary>
        public string[] Cards => cards;

        /// <summary>Card a mid-line hold follows, or -1.</summary>
        public int MidHoldAfterCard => midHoldAfterCard;

        /// <summary>The assigned voice clip, or null.</summary>
        public AudioClip Audio
        {
            get => audio;
            set => audio = value;
        }

        /// <summary>Clip length in seconds, or -1 when no clip is assigned.</summary>
        public float AudioSeconds => audio != null ? audio.length : -1f;

        /// <summary>Copies the parsed fields, leaving <see cref="Audio"/> alone.</summary>
        public void SetParsed(ParsedLine line)
        {
            id = line.Id;
            scene = line.Scene;
            speaker = line.Speaker;
            rawText = line.RawText;
            displayText = line.DisplayText;
            tags = new ScriptTag[line.Tags.Count];
            for (int i = 0; i < tags.Length; i++) tags[i] = line.Tags[i];
            cards = (string[])line.Cards.Clone();
            midHoldAfterCard = line.MidHoldAfterCard;
        }
    }
}
