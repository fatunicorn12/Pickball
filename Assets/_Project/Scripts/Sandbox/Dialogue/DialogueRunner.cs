using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The conversation state machine: which node is showing, how much of its
    /// line has typed out, which options are offered, and what a press of the
    /// talk button does. Plain C# with time passed in, so it is unit-tested
    /// without a scene and knows nothing about input, cameras or UI.
    ///
    /// <b>The graph.</b> It walks a <see cref="DialogueDefinition"/>'s nodes
    /// from the start id. A <see cref="DialogueNodeKind.Line"/> types out and
    /// waits for a press. A <see cref="DialogueNodeKind.Branch"/> is resolved
    /// at once against the <see cref="IChoiceStore"/> and never shows. A
    /// <see cref="DialogueNodeKind.Choice"/> shows its options as soon as the
    /// line before it (or its own prompt) has typed out, as in Breath of the
    /// Wild: no extra press. A choice with nothing pickable is skipped to its
    /// fallback. An empty link ends the conversation. A definition with no
    /// nodes plays its linear lines as an implicit chain, exactly as before
    /// branching existed: same lines, same indices, same events.
    ///
    /// <b>Choosing.</b> While options are up the cursor rests on an enabled
    /// option (<see cref="ChoiceSelection"/>). Picking one records it
    /// (<see cref="IChoiceStore.RecordChoice"/>), applies its effects, raises
    /// <see cref="ChoiceMade"/> and moves on. Only a pick writes to the store;
    /// <see cref="Stop"/> (an abort) mid-choice records nothing. Without a
    /// store, conditions read an empty store and effects are dropped.
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
    /// The next press moves to the next node. A press on a fully shown line
    /// whose link is empty finishes the conversation. While options are up a
    /// press picks the highlighted one. Presses within
    /// <see cref="TypewriterTiming.AdvanceLockout"/> of a line starting or being
    /// revealed are ignored, so a hurried double-tap cannot skip a line unread;
    /// options take <see cref="TypewriterTiming.ChoiceLockout"/> of quiet first.
    ///
    /// No allocations per tick: the line's text is the definition's own string,
    /// and progress is two indices. Options are gathered into a reused list once
    /// per choice.
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
            /// <summary>The conversation ended: the last line was dismissed, or a pick or
            /// link led nowhere.</summary>
            Finished,
            /// <summary>Options are up, under the line just shown; waiting for a pick.</summary>
            Choosing,
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
            /// <summary>The highlighted option was picked.</summary>
            Chose,
        }

        /// <summary>Most branch and skipped-choice hops resolved in one go before the
        /// runner gives up and ends the talk (the validator reports such loops).</summary>
        const int MaxHops = 64;

        TypewriterTiming _timing;
        DialogueDefinition _definition;
        IChoiceStore _store;
        DialogueNode _node;            // the node whose line or options are showing
        DialogueNode _after;           // Revealed: where a press goes (null ends)
        DialogueLine _line;
        List<PresentedOption> _options;
        int _highlight = -1;
        string _text = string.Empty;
        int _cursor;          // index in _text of the next character not yet shown
        int _visible;         // characters shown, tags excluded
        int _total;           // characters in the line, tags excluded
        float _budget;        // seconds banked toward the next character
        float _pendingPause;  // pause owed to the next whitespace character
        float _sinceChange;   // seconds since the line started or was revealed, or the options appeared

        /// <summary>Creates an idle runner.</summary>
        public DialogueRunner(TypewriterTiming timing) => _timing = timing;

        /// <summary>Typing speed and lockouts. Changes apply from the next tick.</summary>
        public TypewriterTiming Timing
        {
            get => _timing;
            set => _timing = value;
        }

        /// <summary>Current phase.</summary>
        public Phase State { get; private set; } = Phase.Idle;

        /// <summary>True while a line is typing or waiting for a press, or options are up.</summary>
        public bool IsActive => State is Phase.Typing or Phase.Revealed or Phase.Choosing;

        /// <summary>How many lines this conversation has shown, minus one: the index
        /// of the current line in showing order, or -1 before the first. For a
        /// linear definition it is the line's index in its list.</summary>
        public int LineIndex { get; private set; } = -1;

        /// <summary>The line showing (text and emote); default before the first.</summary>
        public DialogueLine CurrentLine => _line;

        /// <summary>Full text of the current line (tags included). Empty when idle.</summary>
        public string CurrentText => _text;

        /// <summary>Characters of the current line showing, as TMP counts them.</summary>
        public int VisibleCharacters => _visible;

        /// <summary>Characters in the current line, as TMP counts them.</summary>
        public int TotalCharacters => _total;

        /// <summary>The conversation's id (the definition's), empty when idle. Never null.</summary>
        public string DialogueId => _definition != null ? _definition.Id : string.Empty;

        /// <summary>Id of the node showing: a line, or the choice whose options are
        /// up. Empty when idle. Never null.</summary>
        public string CurrentNodeId => _node != null ? _node.Id : string.Empty;

        /// <summary>The options showing, top to bottom; empty unless <see cref="Phase.Choosing"/>.</summary>
        public IReadOnlyList<PresentedOption> Options => _options ??= new List<PresentedOption>(DialogueValidator.MaxOptions);

        /// <summary>Index into <see cref="Options"/> of the highlighted option, or -1.</summary>
        public int HighlightedOption => _highlight;

        /// <summary>Line <c>index</c> started typing. <see cref="CurrentText"/> is already its text.</summary>
        public event Action<int> LineStarted;

        /// <summary>The number of characters showing changed. Argument: the new count.</summary>
        public event Action<int> CharactersRevealed;

        /// <summary>Line <c>index</c> is fully shown (typed out or skipped).</summary>
        public event Action<int> LineRevealed;

        /// <summary>Options are up. <see cref="Options"/>, <see cref="HighlightedOption"/>
        /// and <see cref="CurrentNodeId"/> describe them.</summary>
        public event Action ChoicesShown;

        /// <summary>The cursor moved to option <c>index</c>.</summary>
        public event Action<int> ChoiceHighlighted;

        /// <summary>The player picked option <c>choiceId</c> at choice node <c>nodeId</c>.
        /// Raised after the pick is recorded and its effects applied, before the
        /// talk moves on.</summary>
        public event Action<string, string> ChoiceMade;

        /// <summary>The conversation ended normally.</summary>
        public event Action Finished;

        bool Instant => !(_timing.CharactersPerSecond > 0f) || float.IsInfinity(_timing.CharactersPerSecond);

        /// <summary>Starts <paramref name="definition"/> with no choice store.</summary>
        public bool Begin(DialogueDefinition definition) => Begin(definition, null);

        /// <summary>
        /// Starts <paramref name="definition"/> at its start node, reading and
        /// writing <paramref name="store"/> (null reads as empty and drops
        /// effects). Returns false, and stays idle, for a null definition, one
        /// with nothing to play, or one whose start resolves straight to the end.
        /// Raises <see cref="LineStarted"/> (and <see cref="LineRevealed"/> at once
        /// for an empty first line or instant text), or <see cref="ChoicesShown"/>
        /// for a start on a choice with no prompt.
        /// </summary>
        public bool Begin(DialogueDefinition definition, IChoiceStore store)
        {
            Stop();
            if (definition == null || !definition.IsPlayable) return false;
            _definition = definition;
            _store = store;
            var first = Resolve(definition.StartNodeId);
            if (first == null)
            {
                Stop();
                return false;
            }
            Show(first);
            return true;
        }

        /// <summary>Drops the conversation without raising <see cref="Finished"/>. Options
        /// that are up are dropped unpicked: nothing is recorded.</summary>
        public void Stop()
        {
            _definition = null;
            _store = null;
            _node = _after = null;
            _line = default;
            _options?.Clear();
            _highlight = -1;
            State = Phase.Idle;
            LineIndex = -1;
            _text = string.Empty;
            _cursor = _visible = _total = 0;
            _budget = _pendingPause = _sinceChange = 0f;
        }

        /// <summary>Advances the typewriter and the lockouts by <paramref name="unscaledDeltaTime"/> seconds.</summary>
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
            if (!IsActive) return AdvanceResult.Ignored;

            if (State == Phase.Choosing)
            {
                // A debounce: a press inside the lockout restarts it, so mashing
                // through the text can never pick an option the player hasn't read.
                if (_sinceChange < _timing.ChoiceLockout)
                {
                    _sinceChange = 0f;
                    return AdvanceResult.Ignored;
                }
                return Choose(_highlight) ? AdvanceResult.Chose : AdvanceResult.Ignored;
            }

            if (_sinceChange < _timing.AdvanceLockout) return AdvanceResult.Ignored;

            if (State == Phase.Typing)
            {
                RevealAll();
                return AdvanceResult.RevealedLine;
            }

            var next = _after;
            _after = null;
            if (next == null)
            {
                Finish();
                return AdvanceResult.Finished;
            }
            Show(next);
            return AdvanceResult.NextLine;
        }

        /// <summary>Moves the option cursor <paramref name="step"/> enabled options down
        /// (negative: up), wrapping. Only while options are up.</summary>
        public void MoveHighlight(int step)
        {
            if (State != Phase.Choosing || step == 0) return;
            SetHighlight(ChoiceSelection.Step(Options, _highlight, step));
        }

        /// <summary>Puts the cursor on option <paramref name="index"/>. False, and no
        /// change, unless options are up and that one is enabled.</summary>
        public bool Highlight(int index)
        {
            if (!CanPick(index)) return false;
            SetHighlight(index);
            return true;
        }

        /// <summary>
        /// Picks option <paramref name="index"/> (a number key, a click, or the
        /// talk button on the highlighted one). False when options aren't up or
        /// that one is greyed out or missing. Inside
        /// <see cref="TypewriterTiming.ChoiceLockout"/> it only moves the cursor
        /// there and returns false. Otherwise: records the pick, applies its
        /// effects, raises <see cref="ChoiceMade"/>, and goes to its next node
        /// (an empty one finishes the conversation).
        /// </summary>
        public bool Choose(int index)
        {
            if (!CanPick(index)) return false;
            if (_sinceChange < _timing.ChoiceLockout)
            {
                SetHighlight(index);
                return false;
            }

            var picked = _options[index];
            var node = _node;
            // Leave Choosing before anything can call back in.
            _options.Clear();
            _highlight = -1;
            State = Phase.Revealed;
            _after = null;

            if (_store != null)
            {
                // The store's listeners are someone else's: one that throws must not
                // stall the talk. Record first, then effects, as the contract says.
                try { _store.RecordChoice(DialogueId, node.Id, picked.OptionId); }
                catch (Exception e) { Debug.LogException(e); }
                try { FactEffect.ApplyAll(picked.Option.Effects, _store); }
                catch (Exception e) { Debug.LogException(e); }
            }
            ChoiceMade?.Invoke(node.Id, picked.OptionId);

            // A handler stopped the talk or moved it on.
            if (State != Phase.Revealed || _node != node || _definition == null) return true;
            MoveTo(Resolve(picked.Option.Next));
            return true;
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

        // ------------------------------------------------------------ the graph

        /// <summary>
        /// The first node at or after <paramref name="id"/> that shows something:
        /// branches are decided against the store, and a choice with nothing
        /// pickable is replaced by its fallback. Null means the talk ends there
        /// (an empty id, a missing node, or a loop that never shows anything).
        /// </summary>
        DialogueNode Resolve(string id)
        {
            for (int hops = 0; hops <= MaxHops; hops++)
            {
                if (string.IsNullOrEmpty(id)) return null;
                if (!_definition.TryGetNode(id, out var node))
                {
                    Debug.LogWarning($"Dialogue '{DialogueId}': no node '{id}', so the talk ends there. " +
                                     "Its validator lists every broken link.");
                    return null;
                }
                switch (node.Kind)
                {
                    case DialogueNodeKind.Branch:
                        id = FactCondition.AllMet(node.Conditions, _store, DialogueId) ? node.Next : node.ElseNext;
                        continue;
                    case DialogueNodeKind.Choice:
                        if (AnyPickable(node)) return node;
                        id = node.Next;
                        continue;
                    default:
                        return node;
                }
            }
            Debug.LogWarning($"Dialogue '{DialogueId}': {MaxHops} branches in a row without a line or a choice; " +
                             "the talk ends. Its validator reports the loop.");
            return null;
        }

        bool AnyPickable(DialogueNode choice)
        {
            foreach (var option in choice.Options)
                if (option != null && option.IsAvailable(_store, DialogueId)) return true;
            return false;
        }

        /// <summary>Shows a resolved node: types its line (a choice's prompt
        /// included), or puts up a prompt-less choice's options at once.</summary>
        void Show(DialogueNode node)
        {
            _node = node;
            _after = null;
            if (node.Kind == DialogueNodeKind.Choice && !node.ShowsLine)
            {
                PresentChoices();
                return;
            }
            StartLine(node.Line);
        }

        void MoveTo(DialogueNode next)
        {
            if (next == null) Finish();
            else Show(next);
        }

        void Finish()
        {
            _after = null;
            State = Phase.Finished;
            Finished?.Invoke();
        }

        /// <summary>Gathers the current choice node's options (hidden ones left out,
        /// disabled ones greyed), puts the cursor on the first enabled one and
        /// raises <see cref="ChoicesShown"/>. With nothing pickable (the store
        /// changed under us), goes to the fallback instead.</summary>
        void PresentChoices()
        {
            var options = _options ??= new List<PresentedOption>(DialogueValidator.MaxOptions);
            options.Clear();
            string dialogueId = DialogueId;
            foreach (var option in _node.Options)
            {
                if (option == null) continue;
                bool met = option.IsAvailable(_store, dialogueId);
                if (!met && option.WhenUnmet == UnmetOptionMode.Hide) continue;
                options.Add(new PresentedOption(option, met));
            }
            _highlight = ChoiceSelection.FirstEnabled(options);
            if (_highlight < 0)
            {
                options.Clear();
                MoveTo(Resolve(_node.Next));
                return;
            }
            State = Phase.Choosing;
            _sinceChange = 0f;
            ChoicesShown?.Invoke();
        }

        bool CanPick(int index)
            => State == Phase.Choosing && _options != null && index >= 0 && index < _options.Count
               && _options[index].IsEnabled;

        void SetHighlight(int index)
        {
            if (index == _highlight) return;
            _highlight = index;
            ChoiceHighlighted?.Invoke(index);
        }

        // ------------------------------------------------------------ typewriter

        void StartLine(DialogueLine line)
        {
            int index = LineIndex + 1;
            var node = _node;
            LineIndex = index;
            _line = line;
            _text = line.Text;
            _cursor = _visible = 0;
            _budget = _pendingPause = _sinceChange = 0f;
            _total = CountVisible(_text);
            State = Phase.Typing;
            LineStarted?.Invoke(index);

            // The handler may have stopped us; only carry on if this line is still current.
            if (State == Phase.Typing && LineIndex == index && _node == node && (_total == 0 || Instant))
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

        /// <summary>The line is fully shown. If options come next (this node's own,
        /// or a prompt-less choice it leads to) they go up at once; otherwise the
        /// next node is decided now and waits for a press.</summary>
        void Reveal()
        {
            State = Phase.Revealed;
            _sinceChange = 0f;
            _budget = 0f;
            int index = LineIndex;
            var node = _node;
            LineRevealed?.Invoke(index);
            if (State != Phase.Revealed || LineIndex != index || _node != node || node == null) return;

            if (node.Kind == DialogueNodeKind.Choice)
            {
                PresentChoices();
                return;
            }
            var next = Resolve(node.Next);
            if (next != null && next.Kind == DialogueNodeKind.Choice && !next.ShowsLine)
            {
                _node = next;
                PresentChoices();
                return;
            }
            _after = next;
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
