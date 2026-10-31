using System;
using UnityEngine;

namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// How one speaker's name tab looks: the name shown and its colour. Keyed on the script's
    /// speaker id (<see cref="CutsceneSpeakers"/>), so adding a speaker to the script only needs an
    /// entry here, and a missing entry still shows (in the fallback colour).
    /// </summary>
    [Serializable]
    public struct SubtitleSpeakerStyle
    {
        [Tooltip("Speaker id exactly as the script writes it before the colon (MC, BIRDY). " +
                 "Matched ignoring case.")]
        [SerializeField] private string speakerId;

        [Tooltip("Name shown in the tab, in small caps. Lower-case letters render as small " +
                 "capitals, so 'Birdy' reads B-IRDY with a tall B. Empty shows the id.")]
        [SerializeField] private string displayName;

        [Tooltip("Colour of the name text. Warm and saturated reads as lively, grey as reserved; " +
                 "keep it light enough to read on the dark tab.")]
        [SerializeField] private Color colour;

        /// <summary>The script speaker id this entry styles.</summary>
        public string SpeakerId => speakerId;

        /// <summary>Name shown in the tab; falls back to the id when empty.</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? speakerId : displayName;

        /// <summary>Colour of the speaker's name.</summary>
        public Color Colour => colour;

        /// <summary>Creates an entry (used when the setup menu seeds the style asset).</summary>
        public SubtitleSpeakerStyle(string speakerId, string displayName, Color colour)
        {
            this.speakerId = speakerId;
            this.displayName = displayName;
            this.colour = colour;
        }
    }
}
