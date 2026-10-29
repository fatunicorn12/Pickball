using System;
using Pickleball.Sandbox.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// The Breath of the Wild dialogue box: a dark translucent rounded panel at
    /// the bottom centre, the speaker's name on a small tab above its top-left
    /// in the accent colour, white text typing out, and a pulsing ▼ once the
    /// line is fully shown. It fades and scales in and out.
    ///
    /// <b>Options.</b> When the director puts a choice up, a vertical list of
    /// dark plates appears above the box's right end, first option on top. The
    /// highlighted one gets the ► cursor, a clearly lighter plate and a slight
    /// lift about its centre, eased in unscaled time; greyed-out options are
    /// dimmed and skipped. The column is at most
    /// <see cref="DialogueConfig.OptionMaxWidth"/> wide (longer answers wrap),
    /// which keeps it clear of the NPC. The answers show dimmed until Interact
    /// can pick (<see cref="DialogueDirector.IsChoiceReady"/>), so a player
    /// tapping through text sees they aren't taken yet. The mouse can hover and
    /// click them: for a keyboard and mouse player the cursor is released while
    /// they are up (only if it was captured), and for a pad player only once
    /// the mouse moves; it is captured again once a pick is made or the talk
    /// ends, matching how the dialogue hands the cursor back to the orbit
    /// camera. Hover and click go to
    /// <see cref="DialogueDirector.HighlightChoice"/> and
    /// <see cref="DialogueDirector.ConfirmChoice"/>, so a click can only ever
    /// pick an option, never advance a line. Hover follows the mouse only when
    /// it moves, so a resting pointer never fights the keys or the stick.
    ///
    /// Presentation only. It listens to the <see cref="DialogueDirector"/> and
    /// never drives the conversation itself: the typewriter and the option
    /// logic live in the runner, and this just sets
    /// <c>maxVisibleCharacters</c> and the row styles when the director says
    /// something changed. Everything animates in unscaled time, because time is
    /// stopped while it is on screen. Colours and timings are read from the
    /// <see cref="DialogueConfig"/> each time the box opens, so they can be
    /// tuned without re-running the setup.
    ///
    /// The layout (built by Setup Dialogue) anchors the box to 20–80% of the
    /// screen width under a 1920-wide reference, so line wrapping is the same on
    /// a 16:9 monitor and a 20:9 phone; only the space above the box changes.
    /// </summary>
    public class DialogueView : MonoBehaviour
    {
        [Header("Sources")]
        [Tooltip("The director whose conversations this box shows.")]
        [SerializeField] private DialogueDirector director;
        [Tooltip("Colours and timings.")]
        [SerializeField] private DialogueConfig config;

        [Header("Parts")]
        [Tooltip("Fades the whole box, tab included.")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("Scaled during the fade.")]
        [SerializeField] private RectTransform panel;
        [Tooltip("The box fill.")]
        [SerializeField] private Image box;
        [Tooltip("The speaker name tab; hidden when the speaker has no name.")]
        [SerializeField] private Image nameTab;
        [Tooltip("The speaker's name, inside the tab, in the accent colour.")]
        [SerializeField] private TMP_Text nameText;
        [Tooltip("The line being typed. Revealed through maxVisibleCharacters, never by " +
                 "rebuilding the string.")]
        [SerializeField] private TMP_Text bodyText;
        [Tooltip("The ▼ shown once the line is fully revealed.")]
        [SerializeField] private Graphic continueMark;

        [Header("Options")]
        [Tooltip("The option list, above the box's right end; sized to its rows by a layout " +
                 "group. Set by Setup Dialogue.")]
        [SerializeField] private RectTransform optionList;
        [Tooltip("Fades the option list in once the line has typed out.")]
        [SerializeField] private CanvasGroup optionGroup;
        [Tooltip("One row per option the list can show, top to bottom. Set by Setup Dialogue.")]
        [SerializeField] private DialogueViewOptionRow[] optionRows = System.Array.Empty<DialogueViewOptionRow>();
        [Tooltip("The camera that frames the talk. When the shot is mirrored (over the right " +
                 "shoulder, the NPC on the right half of the screen) the list moves to the box's " +
                 "left end, above the name tab, so it never covers the NPC. Empty: always on the right.")]
        [SerializeField] private Camera worldCamera;

        [Header("Rest of the HUD")]
        [Tooltip("HUD canvases faded out while the box is up, as Breath of the Wild clears its " +
                 "HUD for a conversation. Frozen damage numbers and barks go with them. Set " +
                 "by Setup Dialogue; the box's own canvas must ignore parent groups.")]
        [SerializeField] private CanvasGroup[] hideWhileTalking = System.Array.Empty<CanvasGroup>();

        // The ▼ never fades out completely while pulsing, so it is always findable.
        const float ContinueMinAlpha = 0.35f;
        // Canvas units between the name tab and a column moved above it.
        const float MirroredTabGap = 24f;

        float _visibility;      // 0 hidden … 1 shown, before easing
        float _target;          // where _visibility is heading
        bool _continueShown;
        float _continueTime;
        Vector2 _continueHome;
        bool _hasContinueHome;

        bool _optionsShown;
        int _optionCount;
        int _highlight = -1;
        float _optionsVisibility;
        float _cursorTime;
        float[] _rowBlend;          // per row: 0 plain … 1 highlighted, eased
        Vector2[] _cursorHome;
        bool _releasedCursor;       // we freed a captured cursor for the options
        bool _gamepadLast;          // the last button pressed was a pad's
        IDisposable _anyButton;
        bool _warnedNoRows;
        Canvas _canvas;
        (Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position) _listHome;
        bool _hasListHome;

        /// <summary>True while the option list is up.</summary>
        public bool OptionsShown => _optionsShown;

        /// <summary>How many option rows are showing.</summary>
        public int OptionCount => _optionsShown ? _optionCount : 0;

        void OnEnable()
        {
            if (director != null)
            {
                director.ConversationStarted += OnStarted;
                director.LineStarted += OnLineStarted;
                director.CharactersRevealed += OnCharactersRevealed;
                director.LineRevealed += OnLineRevealed;
                director.ChoicesShown += OnChoicesShown;
                director.ChoiceHighlighted += OnChoiceHighlighted;
                director.ChoiceMade += OnChoiceMade;
                director.ConversationEnding += OnEnding;
                director.ConversationEnded += OnEnded;
            }
            _anyButton = InputSystem.onAnyButtonPress.Call(OnAnyButton);
            ApplyStyle();
            _target = 0f;
            _visibility = 0f;
            ShowContinue(false);
            HideOptions();
            Apply();
            // Re-enabled mid-choice (a script reload in Play mode): put the list back.
            if (director != null && director.IsChoosing)
            {
                _target = _visibility = 1f;
                Apply();
                OnChoicesShown(director.Current);
            }
        }

        void OnDisable()
        {
            // Never leave the rest of the HUD hidden, or the cursor freed, behind us.
            SetHudAlpha(1f);
            HideOptions();
            // Back to the setup placement, so a script reload re-reads the right one.
            if (_hasListHome && optionList != null) PlaceOptionList(false);
            _anyButton?.Dispose();
            _anyButton = null;
            if (director == null) return;
            director.ConversationStarted -= OnStarted;
            director.LineStarted -= OnLineStarted;
            director.CharactersRevealed -= OnCharactersRevealed;
            director.LineRevealed -= OnLineRevealed;
            director.ChoicesShown -= OnChoicesShown;
            director.ChoiceHighlighted -= OnChoiceHighlighted;
            director.ChoiceMade -= OnChoiceMade;
            director.ConversationEnding -= OnEnding;
            director.ConversationEnded -= OnEnded;
        }

        void OnStarted(Talkable speaker)
        {
            ApplyStyle();
            string speakerName = speaker != null ? speaker.DisplayName : string.Empty;
            if (nameText != null) nameText.text = speakerName;
            if (nameTab != null) nameTab.gameObject.SetActive(!string.IsNullOrEmpty(speakerName));
            if (bodyText != null)
            {
                bodyText.text = string.Empty;
                bodyText.maxVisibleCharacters = 0;
            }
            ShowContinue(false);
            HideOptions();
            _target = 1f;
        }

        void OnLineStarted(Talkable _, int index)
        {
            if (bodyText != null)
            {
                bodyText.text = director.CurrentText;
                bodyText.maxVisibleCharacters = 0;
            }
            ShowContinue(false);
            HideOptions();
        }

        void OnCharactersRevealed(int count)
        {
            if (bodyText != null) bodyText.maxVisibleCharacters = count;
        }

        void OnLineRevealed(Talkable _, int index)
        {
            if (bodyText != null) bodyText.maxVisibleCharacters = int.MaxValue;
            ShowContinue(true);
        }

        void OnEnding(Talkable _)
        {
            ShowContinue(false);
            HideOptions();
            _target = 0f;
        }

        void OnEnded(Talkable _)
        {
            HideOptions();
            _target = 0f;
            _visibility = 0f;
            Apply();
        }

        // ------------------------------------------------------------- options

        /// <summary>Fills the rows from the director's options and puts the list up.
        /// The ▼ goes: the options are what the player answers now.</summary>
        void OnChoicesShown(Talkable _)
        {
            ShowContinue(false);
            var choices = director != null ? director.Choices : null;
            int count = choices != null ? Mathf.Min(choices.Count, optionRows.Length) : 0;
            if (count == 0)
            {
                if (optionRows.Length == 0 && !_warnedNoRows)
                {
                    _warnedNoRows = true;
                    Debug.LogWarning("DialogueView: options are up but this box has no option rows. Re-run " +
                                     "Tools > Pickleball > Setup Dialogue to build the option list.", this);
                }
                HideOptions();
                return;
            }
            EnsureRowState();
            for (int i = 0; i < optionRows.Length; i++)
            {
                var row = optionRows[i];
                bool used = i < count;
                if (row.Row != null) row.Row.gameObject.SetActive(used);
                if (!used) continue;
                if (row.Label != null) row.Label.text = choices[i].Text;
                _rowBlend[i] = i == director.HighlightedChoice ? 1f : 0f;
            }
            _optionCount = count;
            _highlight = director.HighlightedChoice;
            _optionsShown = true;
            _optionsVisibility = 0f;
            _cursorTime = 0f;
            if (optionList != null)
            {
                PlaceOptionList(NpcOnRight(director.Current));
                SizeOptionList(count);
                optionList.gameObject.SetActive(true);
                // Lay the rows out now, so the mouse can hit them this frame.
                LayoutRebuilder.ForceRebuildLayoutImmediate(optionList);
            }
            ApplyOptions(0f);
            // A pad player gets no pointer in the middle of the screen; moving the
            // mouse frees it (PollPointer).
            if (!_gamepadLast) ReleaseCursor();
        }

        void OnAnyButton(InputControl control) => _gamepadLast = control != null && control.device is Gamepad;

        /// <summary>
        /// The column is as wide as its longest answer on one line, at least the
        /// rows' minimum and at most <see cref="DialogueConfig.OptionMaxWidth"/>;
        /// an answer longer than that wraps to a second line. Each row's height is
        /// set here from its text measured at the column's real inner width, so it
        /// never depends on when TextMeshPro last measured itself (a layout pass
        /// can ask before the label has its final width, and every row came out
        /// as tall as a wrapped one).
        /// </summary>
        void SizeOptionList(int count)
        {
            if (config == null) return;
            float widest = 0f, padding = 0f, minWidth = 0f;
            for (int i = 0; i < count; i++)
            {
                var row = optionRows[i];
                if (row.Label == null || row.Row == null) continue;
                widest = Mathf.Max(widest, row.Label.GetPreferredValues(row.Label.text, float.PositiveInfinity,
                                                                          float.PositiveInfinity).x);
                if (row.Row.TryGetComponent(out HorizontalOrVerticalLayoutGroup line))
                    padding = Mathf.Max(padding, line.padding.horizontal);
                if (row.Row.TryGetComponent(out LayoutElement size)) minWidth = Mathf.Max(minWidth, size.minWidth);
            }
            float width = Mathf.Clamp(widest + padding, minWidth, Mathf.Max(minWidth, config.OptionMaxWidth));
            optionList.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

            for (int i = 0; i < count; i++)
            {
                var row = optionRows[i];
                if (row.Label == null || row.Row == null || !row.Row.TryGetComponent(out LayoutElement size)) continue;
                float rowPadding = 0f, vertical = 0f;
                if (row.Row.TryGetComponent(out HorizontalOrVerticalLayoutGroup line))
                {
                    rowPadding = line.padding.horizontal;
                    vertical = line.padding.vertical;
                }
                float text = row.Label.GetPreferredValues(row.Label.text, Mathf.Max(1f, width - rowPadding),
                                                          float.PositiveInfinity).y;
                size.preferredHeight = Mathf.Max(size.minHeight, text + vertical);
            }
        }

        void OnChoiceHighlighted(int index) => _highlight = index;

        /// <summary>True when the NPC's head is on the right half of the screen: a
        /// mirrored, over-the-right-shoulder shot.</summary>
        bool NpcOnRight(Talkable speaker)
        {
            if (worldCamera == null || speaker == null) return false;
            Vector3 viewport = worldCamera.WorldToViewportPoint(speaker.FocusPosition);
            return viewport.z > 0f && viewport.x > 0.5f;
        }

        /// <summary>
        /// The list stands on the box's top edge at its right end, as Setup
        /// Dialogue placed it; for a mirrored shot it is mirrored to the left
        /// end and raised clear of the name tab. The setup placement is read
        /// once and kept, so this never drifts.
        /// </summary>
        void PlaceOptionList(bool left)
        {
            if (!_hasListHome)
            {
                _listHome = (optionList.anchorMin, optionList.anchorMax, optionList.pivot, optionList.anchoredPosition);
                _hasListHome = true;
            }
            var (min, max, pivot, position) = _listHome;
            if (left)
            {
                (min.x, max.x) = (1f - max.x, 1f - min.x);
                pivot.x = 1f - pivot.x;
                position.x = -position.x;
                if (nameTab != null && nameTab.gameObject.activeSelf)
                {
                    // Clear of the name tab by a gap, so it doesn't read as part of the box.
                    var tab = nameTab.rectTransform;
                    float tabTop = tab.anchoredPosition.y + tab.rect.height * (1f - tab.pivot.y);
                    position.y = Mathf.Max(position.y, tabTop + MirroredTabGap);
                }
            }
            optionList.anchorMin = min;
            optionList.anchorMax = max;
            optionList.pivot = pivot;
            optionList.anchoredPosition = position;
        }

        void OnChoiceMade(Talkable _, string nodeId, string choiceId) => HideOptions();

        void HideOptions()
        {
            _optionsShown = false;
            _optionCount = 0;
            _highlight = -1;
            if (optionGroup != null) optionGroup.alpha = 0f;
            if (optionRows != null)
                for (int i = 0; i < optionRows.Length; i++)
                    if (optionRows[i].Row != null) optionRows[i].Row.gameObject.SetActive(false);
            RecaptureCursor();
        }

        /// <summary>Per-row easing state, sized once to the rows (rebuilt after a reload).</summary>
        void EnsureRowState()
        {
            int n = optionRows.Length;
            if (_rowBlend == null || _rowBlend.Length != n) _rowBlend = new float[n];
            if (_cursorHome != null && _cursorHome.Length == n) return;
            _cursorHome = new Vector2[n];
            for (int i = 0; i < n; i++)
                _cursorHome[i] = optionRows[i].Cursor != null ? optionRows[i].Cursor.rectTransform.anchoredPosition : Vector2.zero;
        }

        /// <summary>Eases the list in and each row toward plain or highlighted, and
        /// pulses the ► on the highlighted row.</summary>
        void ApplyOptions(float dt)
        {
            if (config == null) return;
            // Dimmed until Interact can pick, then up to full.
            float fade = config.OptionFadeInSeconds;
            bool ready = director == null || director.IsChoiceReady;
            float target = ready ? 1f : config.OptionNotReadyAlpha;
            _optionsVisibility = fade > 0f ? Mathf.MoveTowards(_optionsVisibility, target, dt / fade) : target;
            if (optionGroup != null)
            {
                optionGroup.alpha = _optionsVisibility;
                optionGroup.blocksRaycasts = false;
                optionGroup.interactable = false;
            }

            float follow = 1f - Mathf.Exp(-dt / Mathf.Max(config.OptionHighlightSeconds, 1e-3f));
            _cursorTime += dt;
            float wave = 0.5f + 0.5f * Mathf.Cos(_cursorTime * config.ContinuePulseHz * 2f * Mathf.PI);
            var choices = director != null ? director.Choices : null;
            for (int i = 0; i < _optionCount; i++)
            {
                var row = optionRows[i];
                bool highlighted = i == _highlight;
                bool enabled = choices == null || i >= choices.Count || choices[i].IsEnabled;
                _rowBlend[i] = dt > 0f ? Mathf.Lerp(_rowBlend[i], highlighted ? 1f : 0f, follow) : _rowBlend[i];
                float b = _rowBlend[i];
                if (row.Row != null) row.Row.localScale = Vector3.one * Mathf.Lerp(1f, config.OptionHighlightScale, b);
                if (row.Plate != null)
                    row.Plate.color = Color.Lerp(config.OptionPlateColour, config.OptionHighlightPlateColour, b);
                if (row.Label != null)
                    row.Label.color = enabled
                        ? Color.Lerp(config.OptionTextColour, config.OptionHighlightTextColour, b)
                        : config.OptionDisabledTextColour;
                if (row.Cursor != null)
                {
                    row.Cursor.enabled = highlighted;
                    row.Cursor.color = config.OptionCursorColour;
                    row.Cursor.rectTransform.anchoredPosition =
                        _cursorHome[i] + Vector2.left * (config.OptionCursorBob * (1f - wave));
                }
            }
        }

        /// <summary>Mouse hover and click on the rows, while the cursor is free.</summary>
        void PollPointer()
        {
            var mouse = Mouse.current;
            if (mouse == null || director == null) return;
            bool moved = mouse.delta.ReadValue().sqrMagnitude > 0f;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                // A pad player kept the cursor captured; reaching for the mouse frees it.
                if (moved) ReleaseCursor();
                return;
            }
            bool clicked = mouse.leftButton.wasPressedThisFrame;
            if (!moved && !clicked) return;
            int row = RowAt(mouse.position.ReadValue());
            if (row < 0) return;
            // Inside the director's lockout a click only moves the cursor there.
            if (clicked) director.ConfirmChoice(row);
            else director.HighlightChoice(row);
        }

        /// <summary>The option row under <paramref name="screenPoint"/> (pixels), or -1.</summary>
        public int RowAt(Vector2 screenPoint)
        {
            if (!_optionsShown) return -1;
            Camera eventCamera = EventCamera();
            for (int i = 0; i < _optionCount; i++)
            {
                var plate = optionRows[i].Plate;
                if (plate != null && RectTransformUtility.RectangleContainsScreenPoint(plate.rectTransform, screenPoint, eventCamera))
                    return i;
            }
            return -1;
        }

        Camera EventCamera()
        {
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null) return null;
            var root = _canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        /// <summary>Frees a captured cursor so the mouse can point at the options.</summary>
        void ReleaseCursor()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _releasedCursor = true;
        }

        /// <summary>Captures the cursor again, only if it was this view that freed it.</summary>
        void RecaptureCursor()
        {
            if (!_releasedCursor) return;
            _releasedCursor = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ---------------------------------------------------------------- frame

        void Update()
        {
            bool fading = !Mathf.Approximately(_visibility, _target);
            if (!fading && !_continueShown && !_optionsShown) return;

            float dt = Time.unscaledDeltaTime;
            if (fading)
            {
                // No config: no fade, the box just appears and disappears.
                float seconds = config == null ? 0f
                              : _target > _visibility ? config.BoxFadeInSeconds : config.BoxFadeOutSeconds;
                _visibility = seconds > 0f ? Mathf.MoveTowards(_visibility, _target, dt / seconds) : _target;
                Apply();
            }

            if (_continueShown && continueMark != null && config != null)
            {
                _continueTime += dt;
                float wave = 0.5f + 0.5f * Mathf.Cos(_continueTime * config.ContinuePulseHz * 2f * Mathf.PI);
                var c = continueMark.color;
                c.a = Mathf.Lerp(ContinueMinAlpha, 1f, wave);
                continueMark.color = c;
                continueMark.rectTransform.anchoredPosition =
                    _continueHome + Vector2.down * (config.ContinueBob * (1f - wave));
            }

            if (_optionsShown)
            {
                ApplyOptions(dt);
                PollPointer();
            }
        }

        /// <summary>Alpha and scale from the eased visibility.</summary>
        void Apply()
        {
            // Smoothstep both ways: no snap at either end, and no jump if a
            // close interrupts the fade-in.
            float eased = DialogueFraming.SmoothStep01(_visibility);
            if (group != null)
            {
                group.alpha = eased;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            if (panel != null)
            {
                float from = config != null ? config.BoxScaleFrom : 1f;
                panel.localScale = Vector3.one * Mathf.Lerp(from, 1f, eased);
            }
            SetHudAlpha(1f - eased);
        }

        void SetHudAlpha(float alpha)
        {
            if (hideWhileTalking == null) return;
            for (int i = 0; i < hideWhileTalking.Length; i++)
                if (hideWhileTalking[i] != null) hideWhileTalking[i].alpha = alpha;
        }

        void ShowContinue(bool show)
        {
            _continueShown = show;
            _continueTime = 0f;
            if (continueMark == null) return;
            if (!_hasContinueHome)
            {
                _continueHome = continueMark.rectTransform.anchoredPosition;
                _hasContinueHome = true;
            }
            continueMark.rectTransform.anchoredPosition = _continueHome;
            continueMark.enabled = show;
        }

        void ApplyStyle()
        {
            if (config == null) return;
            if (box != null) box.color = config.BoxColour;
            if (nameTab != null) nameTab.color = config.NameTabColour;
            if (nameText != null) nameText.color = config.NameColour;
            if (bodyText != null) bodyText.color = config.TextColour;
            if (continueMark != null) continueMark.color = config.ContinueColour;
        }
    }
}
