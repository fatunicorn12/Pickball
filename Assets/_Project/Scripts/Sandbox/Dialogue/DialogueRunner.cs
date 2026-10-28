using System;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The conversation state machine: which line is showing, how much of it
    /// has typed out, and what a press of the talk button does. Plain C# with
    /// time passed in, so it is unit-tested without a scene and knows nothing
    /// about input, cameras or UI.
    ///
    /// <b>Typewriter.</b> Each character costs <c>1 / CharactersPerSecond</c>
    /// seconds; a speed of 0 or less (or NaN) shows the line at once. A
    /// whitespace character after punctuation costs the punctuation's pause on
    /// top, so the pause lands between words, never inside "3.14", and "..."
    /// pauses once, after the last dot. Closing quotes and brackets carry the
    /// pause through (<c>"Hi." He</c> still pauses). Unused time is banked, so
    /// how many characters are showing after T seconds does not depend on how
    /// those seconds were split into frames.
    ///
    /// <b>Counting matches TextMeshPro's <c>maxVisibleCharacters</c>.</b> Only a
    /// tag TMP would parse is a tag: '&lt;', an optional '/', a letter or '#',
    /// and a closing '&gt;' with no '&lt;' before it. So "a &lt; b &gt; c" is
    /// plain text. Formatting tags (<c>&lt;b&gt;</c>, <c>&lt;color=…&gt;</c>,
    /// <c>&lt;#ff0&gt;</c>) are free and not counted. Tags TMP turns into a
    /// character (<c>&lt;br&gt;</c>, <c>&lt;cr&gt;</c>, <c>&lt;nbsp&gt;</c>,
    /// <c>&lt;zwsp&gt;</c>, <c>&lt;zwj&gt;</c>, <c>&lt;shy&gt;</c>,
    /// <c>&lt;sprite…&gt;</c>) count as one, and <c>&lt;br&gt;</c> pauses like a
    /// line break. A surrogate pair (most emoji) is one character.
    ///
    /// <b>Advance.</b> The first press while a line is typing shows all of it.
    /// The next press moves to the next line. A press on the last, fully shown
    /// line finishes the conversation. Presses within
    /// <see cref="TypewriterTiming.AdvanceLockout"/> of a line starting or being
    /// revealed are ignored, so a hurried double-tap cannot skip a line unread.
    ///
    /// No allocations per tick: the line's text is the definition's own string,
    /// and progress is two indices.
    /// </summary>
    public sealed class DialogueRunner
    {
        /// <summary>Where the runner is in a conversation.</summary>
        public enum Phase
        {
            /// <summary>No conversation.</summary>
            Idle,
            /// <summary>The current line is typing out.</summary>
            Typing,
            /// <summary>The current line is fully shown and waits for a press.</summary>
            Revealed,
            /// <summary>The last line was dismissed.</summary>
            Finished,
        }

        /// <summary>What a call to <see cref="Advance"/> did.</summary>
        public enum AdvanceResult
        {
            /// <summary>Nothing: no conversation, or the press came inside the lockout.</summary>
            Ignored,
            /// <summary>The typing line was shown in full.</summary>
            RevealedLine,
            /// <summary>The next line started.</summary>
            NextLine,
            /// <summary>The conversation finished.</summary>
            Finished,
        }

        TypewriterTiming _timing;
        DialogueDefinition _definition;
        string _text = string.Empty;
        int _cursor;          // index in _text of the next character not yet shown
        int _visible;         // characters shown, tags excluded
        int _total;           // characters in the line, tags excluded
        float _budget;        // seconds banked toward the next character
        float _pendingPause;  // pause owed to the next whitespace character
        float _sinceChange;   // seconds since the line started or was revealed

        /// <summary>Creates an idle runner.</summary>
        public DialogueRunner(TypewriterTiming timing) => _timing = timing;

        /// <summary>Typing speed and lockout. Changes apply from the next tick.</summary>
        public TypewriterTiming Timing
        {
            get => _timing;
            set => _timing = value;
        }

        /// <summary>Current phase.</summary>
        public Phase State { get; private set; } = Phase.Idle;

        /// <summary>True while a line is typing or waiting for a press.</summary>
        public bool IsActive => State == Phase.Typing || State == Phase.Revealed;

        /// <summary>Index of the current line, or -1 when idle.</summary>
        public int LineIndex { get; private set; } = -1;

        /// <summary>Full text of the current line (tags included). Empty when idle.</summary>
        public string CurrentText => _text;

        /// <summary>Characters of the current line showing, as TMP counts them.</summary>
        public int VisibleCharacters => _visible;

        /// <summary>Characters in the current line, as TMP counts them.</summary>
        public int TotalCharacters => _total;

        /// <summary>Line <c>index</c> started typing. <see cref="CurrentText"/> is already its text.</summary>
        public event Action<int> LineStarted;

        /// <summary>The number of characters showing changed. Argument: the new count.</summary>
        public event Action<int> CharactersRevealed;

        /// <summary>Line <c>index</c> is fully shown (typed out or skipped).</summary>
        public event Action<int> LineRevealed;

        /// <summary>The last line was dismissed.</summary>
        public event Action Finished;

        bool Instant => !(_timing.CharactersPerSecond > 0f) || float.IsInfinity(_timing.CharactersPerSecond);

        /// <summary>
        /// Starts <paramref name="definition"/> at its first line. Returns false,
        /// and stays idle, for a null definition or one with no lines. Raises
        /// <see cref="LineStarted"/> (and <see cref="LineRevealed"/> at once for an
        /// empty first line or instant text).
        /// </summary>
        public bool Begin(DialogueDefinition definition)
        {
            Stop();
            if (definition == null || definition.LineCount == 0) return false;
            _definition = definition;
            StartLine(0);
            return true;
        }

        /// <summary>Drops the conversation without raising <see cref="Finished"/>.</summary>
        public void Stop()
        {
            _definition = null;
            State = Phase.Idle;
            LineIndex = -1;
            _text = string.Empty;
            _cursor = _visible = _total = 0;
            _budget = _pendingPause = _sinceChange = 0f;
        }

        /// <summary>Advances the typewriter by <paramref name="unscaledDeltaTime"/> seconds.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!IsActive || !(unscaledDeltaTime > 0f)) return;
            _sinceChange += unscaledDeltaTime;
            if (State != Phase.Typing) return;

            // Speed tuned to 0 (or NaN) mid-line: show the rest, as the tooltip says.
            if (Instant)
            {
                RevealAll();
                return;
            }

            _budget += unscaledDeltaTime;
            int before = _visible;
            while (true)
            {
                SkipTags();
                if (_cursor >= _text.Length) break;
                int length = UnitAt(_text, _cursor, out char shown);
                float cost = CostOf(shown);
                if (_budget < cost) break;
                _budget -= cost;
                _cursor += length;
                _visible++;
                AfterShowing(shown);
            }
            SkipTags();

            if (_visible != before) CharactersRevealed?.Invoke(_visible);
            if (_cursor >= _text.Length) Reveal();
        }

        /// <summary>The talk button was pressed. See the class summary for what it does when.</summary>
        public AdvanceResult Advance()
        {
            if (!IsActive || _sinceChange < _timing.AdvanceLockout) return AdvanceResult.Ignored;

            if (State == Phase.Typing)
            {
                RevealAll();
                return AdvanceResult.RevealedLine;
            }

            if (LineIndex + 1 < _definition.LineCount)
            {
                StartLine(LineIndex + 1);
                return AdvanceResult.NextLine;
            }

            State = Phase.Finished;
            Finished?.Invoke();
            return AdvanceResult.Finished;
        }

        /// <summary>Characters TextMeshPro would count in <paramref name="text"/>
        /// (see the class summary for what is and isn't a tag).</summary>
        public static int CountVisible(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            int i = 0;
            while (i < text.Length)
            {
                int tag = TagAt(text, i, out char tagCharacter);
                if (tag > 0)
                {
                    if (tagCharacter != '\0') count++;
                    i += tag;
                    continue;
                }
                i += UnitAt(text, i, out _);
                count++;
            }
            return count;
        }

        // ------------------------------------------------------------ internals

        void StartLine(int index)
        {
            LineIndex = index;
            _text = _definition.GetLine(index).Text;
            _cursor = _visible = 0;
            _budget = _pendingPause = _sinceChange = 0f;
            _total = CountVisible(_text);
            State = Phase.Typing;
            LineStarted?.Invoke(index);

            // The handler may have stopped us; only carry on if this line is still current.
            if (State == Phase.Typing && LineIndex == index && (_total == 0 || Instant))
                RevealAll();
        }

        void RevealAll()
        {
            _cursor = _text.Length;
            if (_visible != _total)
            {
                _visible = _total;
                CharactersRevealed?.Invoke(_visible);
            }
            Reveal();
        }

        void Reveal()
        {
            State = Phase.Revealed;
            _sinceChange = 0f;
            _budget = 0f;
            LineRevealed?.Invoke(LineIndex);
        }

        /// <summary>Steps over formatting tags, which take no time and show nothing.</summary>
        void SkipTags()
        {
            while (_cursor < _text.Length)
            {
                int tag = TagAt(_text, _cursor, out char tagCharacter);
                if (tag == 0 || tagCharacter != '\0') return;
                _cursor += tag;
            }
        }

        float CostOf(char shown)
        {
            float cost = 1f / _timing.CharactersPerSecond;
            if (char.IsWhiteSpace(shown)) cost += _pendingPause;
            return cost;
        }

        void AfterShowing(char shown)
        {
            if (IsSentenceEnd(shown)) _pendingPause = _timing.SentencePause;
            else if (IsClausePause(shown)) _pendingPause = Math.Max(_pendingPause, _timing.CommaPause);
            else if (!IsCloser(shown)) _pendingPause = 0f;
        }

        /// <summary>
        /// The visible unit at <paramref name="i"/> (not a formatting tag): its
        /// length in chars and the character it stands for. A character tag
        /// stands for its character, a surrogate pair is two chars long.
        /// </summary>
        static int UnitAt(string s, int i, out char shown)
        {
            int tag = TagAt(s, i, out char tagCharacter);
            if (tag > 0 && tagCharacter != '\0')
            {
                shown = tagCharacter;
                return tag;
            }
            shown = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) return 2;
            return 1;
        }

        /// <summary>
        /// Length of the rich-text tag TMP would parse at <paramref name="i"/>,
        /// or 0 if there is none. <paramref name="character"/> is the character a
        /// character tag stands for, '\0' for a formatting tag.
        /// </summary>
        static int TagAt(string s, int i, out char character)
        {
            character = '\0';
            if (s[i] != '<') return 0;
            int j = i + 1;
            bool closing = j < s.Length && s[j] == '/';
            if (closing) j++;
            if (j >= s.Length || !(char.IsLetter(s[j]) || s[j] == '#')) return 0;

            int nameStart = j;
            int nameEnd = -1;
            for (int k = j; k < s.Length; k++)
            {
                char c = s[k];
                if (c == '<') return 0;
                if (nameEnd < 0 && (c == '=' || c == ' ' || c == '>')) nameEnd = k;
                if (c != '>') continue;
                if (!closing) character = CharacterFor(s, nameStart, nameEnd - nameStart);
                return k - i + 1;
            }
            return 0;
        }

        /// <summary>The character a TMP character tag inserts, or '\0' for any other tag.</summary>
        static char CharacterFor(string s, int start, int length)
        {
            if (Is(s, start, length, "br")) return '\n';
            if (Is(s, start, length, "cr")) return '\r';
            if (Is(s, start, length, "nbsp")) return ' ';
            if (Is(s, start, length, "zwsp")) return '​';
            if (Is(s, start, length, "zwj")) return '‍';
            if (Is(s, start, length, "shy")) return '­';
            if (Is(s, start, length, "sprite")) return '￼';
            return '\0';
        }

        static bool Is(string s, int start, int length, string name)
            => length == name.Length && string.Compare(s, start, name, 0, length, StringComparison.OrdinalIgnoreCase) == 0;

        static bool IsSentenceEnd(char c) => c is '.' or '!' or '?' or '…';
        static bool IsClausePause(char c) => c is ',' or ';' or ':' or '—' or '–';
        static bool IsCloser(char c) => c is '"' or '\'' or ')' or ']' or '”' or '’' or '»';
    }
}
