using UnityEngine;

namespace Pickleball.Cutscene.Script
{
    /// <summary>
    /// The shot list's duration rule (§1): a line without audio lasts
    /// <c>max(1.5, chars / 15) + 0.3</c> seconds, where the 0.3 s is the gap after it and nothing
    /// else separates consecutive lines. With an AudioClip the spoken part is the clip's length and
    /// the same gap follows. Everything that times lines goes through here so the placeholder cut
    /// and the voiced cut follow one rule.
    /// </summary>
    public static class LineTiming
    {
        /// <summary>Silence after every line, in seconds.</summary>
        public const float Gap = 0.3f;

        /// <summary>Shortest placeholder speech, in seconds, so a one-word line is readable.</summary>
        public const float MinSpeech = 1.5f;

        /// <summary>Placeholder reading speed, in characters per second.</summary>
        public const float CharsPerSecond = 15f;

        /// <summary>Placeholder speech time for a display text of <paramref name="chars"/> characters (no gap).</summary>
        public static float PlaceholderSpeech(int chars) => Mathf.Max(MinSpeech, chars / CharsPerSecond);

        /// <summary>The shot list's placeholder duration: speech plus the gap.</summary>
        public static float PlaceholderDuration(int chars) => PlaceholderSpeech(chars) + Gap;

        /// <summary>
        /// The spoken part of a line: the clip's length when audio is assigned
        /// (<paramref name="audioSeconds"/> ≥ 0), otherwise the placeholder speech.
        /// </summary>
        public static float SpeechSeconds(int chars, float audioSeconds) =>
            audioSeconds >= 0f ? audioSeconds : PlaceholderSpeech(chars);

        /// <summary>The line's slot on the timeline: speech plus the gap.</summary>
        public static float SlotSeconds(int chars, float audioSeconds) => SpeechSeconds(chars, audioSeconds) + Gap;

        /// <summary>
        /// Shortest time a voiced line's subtitle stays up, in seconds, when no config says otherwise
        /// (<c>CutscenePlaybackConfig.MinCardSeconds</c>). Real clips can be far shorter than a card
        /// is readable (OC_S1_02 is 0.43 s).
        /// </summary>
        public const float DefaultMinCardSeconds = 1.0f;

        /// <summary>
        /// The voiced part of a line's slot: the clip's length, but never under
        /// <paramref name="minCardSeconds"/>, so a very short clip still leaves its card up long enough
        /// to read. The clip plays at its real length from the slot's start; the rest reads as a beat.
        /// Placeholder lines are unaffected (their 1.5 s floor is already longer).
        /// </summary>
        public static float VoicedSlotSpeech(float audioSeconds, float minCardSeconds) =>
            Mathf.Max(audioSeconds, Mathf.Max(0f, minCardSeconds));
    }
}
