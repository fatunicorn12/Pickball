using System;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Pickleball.UI
{
    /// <summary>
    /// The "[E] Talk" pill that floats over whoever the player can talk to.
    ///
    /// Presentation only: it follows <see cref="PlayerInteractor.TargetChanged"/>
    /// and projects the target's <see cref="Talkable.FocusPosition"/> (raised by
    /// <see cref="DialogueConfig.PromptHeightOffset"/>) to the screen each
    /// frame. The interactor drops its target during a conversation, so the
    /// prompt hides itself then without knowing dialogue exists.
    ///
    /// The key cap shows E or RB depending on which device last pressed a
    /// button (mouse clicks count as keyboard), so a pad player is not told to
    /// press a key they cannot see.
    ///
    /// One marker at a time, as in BotW: an NPC that lock-on can target (it has
    /// a <see cref="Health"/>) gets the lock-on hint ring above its head, so
    /// over such an NPC the pill steps aside by
    /// <see cref="DialogueConfig.PromptHintClearance"/> instead of sitting on the
    /// ring (to the right, or the left near the right edge of the screen).
    ///
    /// Runs late (<c>DefaultExecutionOrder(1000)</c>, like the lock-on reticle)
    /// so it projects through the camera after the orbit camera has moved this
    /// frame, and the pill does not trail a frame behind the NPC.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class InteractPromptView : MonoBehaviour
    {
        [Header("Sources")]
        [Tooltip("Whose target the prompt shows.")]
        [SerializeField] private PlayerInteractor interactor;
        [Tooltip("Prompt height, fade time and labels.")]
        [SerializeField] private DialogueConfig config;
        [Tooltip("Camera to project the target through. Set by Setup Dialogue.")]
        [SerializeField] private Camera worldCamera;

        [Header("Parts")]
        [Tooltip("Fades the pill.")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("Moved to the target's screen position. Its parent must be a full-screen rect.")]
        [SerializeField] private RectTransform pill;
        [Tooltip("Text inside the key cap: E or RB.")]
        [SerializeField] private TMP_Text keyText;
        [Tooltip("The verb: Talk.")]
        [SerializeField] private TMP_Text labelText;

        // The pill grows from this scale as it fades in, a small pop.
        const float PopFrom = 0.8f;

        Talkable _target;
        bool _targetHasHint;
        float _visibility;
        float _wanted;
        bool _gamepadLast;
        IDisposable _anyButton;
        Canvas _canvas;

        void OnEnable()
        {
            if (interactor != null) interactor.TargetChanged += OnTargetChanged;
            _anyButton = InputSystem.onAnyButtonPress.Call(OnAnyButton);
            RefreshLabels();
            _target = interactor != null ? interactor.CurrentTarget : null;
            _targetHasHint = _target != null && _target.TryGetComponent(out Health _);
            _wanted = _target != null ? 1f : 0f;
            _visibility = 0f;
            Apply();
        }

        void OnDisable()
        {
            if (interactor != null) interactor.TargetChanged -= OnTargetChanged;
            _anyButton?.Dispose();
            _anyButton = null;
        }

        void OnTargetChanged(Talkable target)
        {
            // Keep the last target while fading out, so the pill fades where it was.
            if (target != null)
            {
                _target = target;
                _targetHasHint = target.TryGetComponent(out Health _);
                RefreshLabels();
            }
            _wanted = target != null ? 1f : 0f;
        }

        void OnAnyButton(InputControl control)
        {
            bool gamepad = control != null && control.device is Gamepad;
            if (gamepad == _gamepadLast) return;
            _gamepadLast = gamepad;
            RefreshLabels();
        }

        void LateUpdate()
        {
            if (_visibility <= 0f && _wanted <= 0f) return;
            float seconds = config != null ? config.PromptFadeSeconds : 0f;
            _visibility = seconds > 0f
                ? Mathf.MoveTowards(_visibility, _wanted, Time.unscaledDeltaTime / seconds)
                : _wanted;
            Apply();
        }

        void Apply()
        {
            bool onScreen = Place();
            float eased = onScreen ? DialogueFraming.SmoothStep01(_visibility) : 0f;
            if (group != null)
            {
                group.alpha = eased;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            if (pill != null) pill.localScale = Vector3.one * Mathf.Lerp(PopFrom, 1f, eased);
        }

        /// <summary>Moves the pill over the target. False when there is nothing to
        /// show or the anchor is off screen, by the same test the interactor uses
        /// to decide whether E talks (<see cref="TalkTargetSelector.IsInView"/>).</summary>
        bool Place()
        {
            if (_target == null || pill == null || worldCamera == null || config == null) return false;
            Vector3 world = _target.FocusPosition + Vector3.up * config.PromptHeightOffset;
            if (!TalkTargetSelector.IsInView(worldCamera.WorldToViewportPoint(world))) return false;
            Vector3 screen = worldCamera.WorldToScreenPoint(world);

            var parent = pill.parent as RectTransform;
            if (parent == null) return false;
            if (_canvas == null) _canvas = pill.GetComponentInParent<Canvas>();
            Canvas root = _canvas != null ? _canvas.rootCanvas : null;
            if (_targetHasHint)
            {
                float halfWidth = pill.rect.width * 0.5f * (root != null ? root.scaleFactor : 1f);
                screen.x += HintClearanceShift(screen.x, worldCamera.pixelWidth, worldCamera.pixelHeight,
                                               config.PromptHintClearance, halfWidth);
            }
            Camera uiCamera = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out Vector2 local))
                return false;
            pill.localPosition = new Vector3(local.x, local.y, 0f);
            return true;
        }

        /// <summary>
        /// Sideways shift, in pixels, that puts a pill <paramref name="pillHalfWidth"/>
        /// pixels wide either side of its centre clear of a marker centred on
        /// <paramref name="anchorX"/>: <paramref name="clearance"/> screen heights
        /// plus half the pill, to the right, or to the left when that would run
        /// off the right edge. 0 when <paramref name="clearance"/> is 0.
        /// </summary>
        public static float HintClearanceShift(float anchorX, float pixelWidth, float pixelHeight,
                                               float clearance, float pillHalfWidth)
        {
            if (clearance <= 0f) return 0f;
            float shift = clearance * pixelHeight + pillHalfWidth;
            return anchorX + shift + pillHalfWidth > pixelWidth ? -shift : shift;
        }

        void RefreshLabels()
        {
            if (config == null) return;
            if (keyText != null) keyText.text = _gamepadLast ? config.GamepadKeyLabel : config.KeyboardKeyLabel;
            if (labelText != null) labelText.text = config.PromptLabel;
        }
    }
}
