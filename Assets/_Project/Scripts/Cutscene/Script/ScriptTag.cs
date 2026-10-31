using System;
using UnityEngine;

namespace Pickleball.Cutscene.Script
{
    /// <summary>
    /// One square-bracket audio tag from a script line (<c>[pause, staring at MC's back]</c>),
    /// lower-cased, with where it sat in the display text. Tags are kept (rather than thrown away
    /// with the brackets) because some of them are timing: a mid-line <c>[pause]</c> forces a
    /// subtitle card break and a hold, and the voice director reads the rest.
    /// </summary>
    [Serializable]
    public struct ScriptTag
    {
        [Tooltip("The tag's content without brackets, lower case (\"pause, then excited\").")]
        [SerializeField] private string text;

        [Tooltip("Character index in the line's display text where the tag sat. 0 for a leading " +
                 "tag; a mid-line tag points at the first character after it.")]
        [SerializeField] private int position;

        /// <summary>Creates a tag.</summary>
        public ScriptTag(string text, int position)
        {
            this.text = text;
            this.position = position;
        }

        /// <summary>The tag's content, lower case, without brackets.</summary>
        public string Text => text;

        /// <summary>Index in the display text the tag sat at.</summary>
        public int Position => position;

        /// <summary>True when the tag is not at the start of the line (OC_S2_09's <c>[pause]</c>).</summary>
        public bool IsMidLine => position > 0;

        /// <summary>
        /// True when one of the tag's comma-separated parts is exactly <paramref name="directive"/>,
        /// so <c>[pause, softer]</c> has "pause" but <c>[long pause]</c> does not.
        /// </summary>
        public bool HasDirective(string directive)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(directive)) return false;
            int start = 0;
            while (start <= text.Length)
            {
                int comma = text.IndexOf(',', start);
                int end = comma < 0 ? text.Length : comma;
                if (string.Equals(text.Substring(start, end - start).Trim(), directive, StringComparison.Ordinal))
                    return true;
                if (comma < 0) break;
                start = comma + 1;
            }
            return false;
        }

        /// <summary>True for the tags that are timing holds: pause, beat, long pause.</summary>
        public bool IsHold => HasDirective("pause") || HasDirective("beat") || HasDirective("long pause");
    }
}
