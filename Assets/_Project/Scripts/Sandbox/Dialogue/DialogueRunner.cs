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
    /// seconds. A whitespace character after punctuation costs the punctuation's
    /// pause on top, so the pause lands between words, never inside "3.14", and
    /// "..." pauses once, after the last dot. Closing quotes and brackets carry
    /// the pause through (<c>"Hi." He</c> still pauses). Unused time is banked, so
    /// how many characters are showing after T seconds does not depend on how
    /// those seconds were split into frames. Rich-text tags (<c>&lt;b&gt;</c>,
    /// <c>&lt;color=…&gt;</c>) cost nothing and are not counted, which matches
    /// TextMeshPro's <c>maxVisibleCharacters</c>.
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

        /// <summary>The conversation being run, or null when idle.</summary>
        public DialogueDefinition Definition => _definition;

        /// <summary>Index of the current line, or -1 when idle.</summary>
        public int LineIndex { get; private set; } = -1;

        /// <summary>Full text of the current line (tags included). Empty when idle.</summary>
        public string CurrentText => _text;

        /// <summary>Gesture of the current line; <see cref="DialogueEmote.Talk"/> when idle.</summary>
        public DialogueEmote CurrentEmote
            => _definition != null && LineIndex >= 0 ? _definition.GetLine(LineIndex).Emote : DialogueEmote.Talk;

        /// <summary>Characters of the current line showing, tags excluded.</summary>
        public int VisibleCharacters => _visible;

        /// <summary>Characters in the current line, tags excluded.</summary>
        public int TotalCharacters => _total;

        /// <summary>Line <c>index</c> started typing. <see cref="CurrentText"/> is already its text.</summary>
        public event Action<int> LineStarted;

        /// <summary>The number of characters showing changed. Argument: the new count.</summary>
        public event Action<int> CharactersRevealed;

        /// <summary>Line <c>index</c> is fully shown (typed out or skipped).</summary>
        public event Action<int> LineRevealed;

        /// <summary>The last line was dismissed.</summary>
        public event Action Finished;

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

            _budget += unscaledDeltaTime;
            int before = _visible;
            while (true)
            {
                SkipTags();
                if (_cursor >= _text.Length) break;
                float cost = CostOf(_text[_cursor]);
                if (_budget < cost) break;
                _budget -= cost;
                Show(_text[_cursor]);
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

        /// <summary>
        /// Characters TextMeshPro would count in <paramref name="text"/>:
        /// everything except rich-text tags. A '&lt;' with no closing '&gt;' is text.
        /// </summary>
        public static int CountVisible(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        i = close;
                        continue;
                    }
                }
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
            if (State == Phase.Typing && LineIndex == index
                && (_total == 0 || !(_timing.CharactersPerSecond > 0f) || float.IsInfinity(_timing.CharactersPerSecond)))
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

        void SkipTags()
        {
            while (_cursor < _text.Length && _text[_cursor] == '<')
            {
                int close = _text.IndexOf('>', _cursor + 1);
                if (close < 0) return;
                _cursor = close + 1;
            }
        }

        float CostOf(char c)
        {
            float cost = 1f / _timing.CharactersPerSecond;
            if (char.IsWhiteSpace(c)) cost += _pendingPause;
            return cost;
        }

        void Show(char c)
        {
            _cursor++;
            _visible++;
            if (IsSentenceEnd(c)) _pendingPause = _timing.SentencePause;
            else if (IsClausePause(c)) _pendingPause = Math.Max(_pendingPause, _timing.CommaPause);
            else if (!IsCloser(c)) _pendingPause = 0f;
        }

        static bool IsSentenceEnd(char c) => c is '.' or '!' or '?' or '…';
        static bool IsClausePause(char c) => c is ',' or ';' or ':' or '—' or '–';
        static bool IsCloser(char c) => c is '"' or '\'' or ')' or ']' or '”' or '’' or '»';
    }
}
