using Pickleball.Sandbox.Inventory;
using TMPro;
using UnityEngine;

namespace Pickleball.UI
{
    /// <summary>
    /// The equipped-weapon readout in the HUD corner: the weapon's name on a
    /// dark plate. When a new weapon reaches the hand it pops and shows at
    /// full strength for a moment, then settles to a quieter idle opacity, so
    /// a quick-cycle press is confirmed without a banner in the way of play.
    /// Hidden while nothing has been equipped yet.
    ///
    /// Presentation only: listens to <see cref="WeaponEquipper.WeaponEquipped"/>
    /// (the swap that actually happened, not the request), writes the name
    /// only then, and animates on unscaled time.
    /// </summary>
    public class InventoryEquippedView : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The equipper whose swaps are shown. Hidden while missing.")]
        [SerializeField] private WeaponEquipper equipper;

        [Header("Parts")]
        [Tooltip("Fades the readout.")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("Scaled for the pop.")]
        [SerializeField] private RectTransform body;
        [Tooltip("The weapon's name.")]
        [SerializeField] private TMP_Text nameText;

        [Header("Motion")]
        [Tooltip("Opacity while nothing has changed for a while (0-1).")]
        [Range(0f, 1f)] [SerializeField] private float idleAlpha = 0.65f;
        [Tooltip("Seconds at full opacity after a swap.")]
        [Min(0f)] [SerializeField] private float holdSeconds = 1.6f;
        [Tooltip("Extra scale at the peak of the pop (x).")]
        [Min(0f)] [SerializeField] private float popAmount = 0.2f;
        [Tooltip("Seconds the pop takes to settle.")]
        [Min(0.01f)] [SerializeField] private float popSeconds = 0.3f;
        [Tooltip("Seconds to fade between full and idle opacity.")]
        [Min(0.01f)] [SerializeField] private float fadeSeconds = 0.4f;

        WeaponEquipper _subscribed;
        float _hold, _pop;
        bool _shown;

        void OnEnable()
        {
            Subscribe(equipper);
            if (equipper != null && equipper.Current != null) Show(equipper.Current, false);
            else if (group != null) group.alpha = 0f;
        }

        void OnDisable() => Subscribe(null);

        /// <summary>Points the readout at an equipper at runtime.</summary>
        public void Bind(WeaponEquipper weaponEquipper)
        {
            equipper = weaponEquipper;
            if (isActiveAndEnabled) Subscribe(equipper);
        }

        void Subscribe(WeaponEquipper next)
        {
            if (_subscribed != null) _subscribed.WeaponEquipped -= OnEquipped;
            _subscribed = next;
            if (_subscribed != null) _subscribed.WeaponEquipped += OnEquipped;
        }

        void OnEquipped(WeaponDefinition weapon) => Show(weapon, true);

        void Show(WeaponDefinition weapon, bool pop)
        {
            if (weapon == null) return;
            _shown = true;
            if (nameText != null) nameText.text = weapon.DisplayName.ToUpperInvariant();
            if (pop)
            {
                _hold = holdSeconds;
                _pop = 1f;
            }
        }

        void Update()
        {
            if (group == null) return;
            float dt = Time.unscaledDeltaTime;
            _hold = Mathf.Max(0f, _hold - dt);
            _pop = Mathf.MoveTowards(_pop, 0f, dt / popSeconds);
            float goal = !_shown ? 0f : _hold > 0f ? 1f : idleAlpha;
            group.alpha = Mathf.MoveTowards(group.alpha, goal, dt / fadeSeconds);
            if (body != null)
            {
                float s = 1f + popAmount * _pop * _pop;
                body.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
