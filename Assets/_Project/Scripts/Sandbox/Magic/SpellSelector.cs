using System;
using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The player's spell slots: which spells they carry and which one is
    /// selected. Selecting tells the <see cref="SpellCaster"/> through
    /// <see cref="SpellCaster.SetSpell"/>; the caster still holds exactly one
    /// spell, so everything that reads the caster (aim, cost, HUD) follows the
    /// selection with no knowledge of slots.
    ///
    /// Gameplay only. Buttons come from <see cref="SpellSelectInput"/> (number
    /// keys pick a slot, the pad and mouse wheel cycle while aiming); the HUD's
    /// <c>SpellSelectorView</c> listens to <see cref="SelectionChanged"/> and
    /// reads the slots. Empty slots are skipped by cycling and refused by
    /// <see cref="Select"/>. The index arithmetic is <see cref="SpellSelection"/>.
    ///
    /// Switching spell mid-channel cancels the channel (the caster does that).
    /// The slot list is data on this component, filled by Setup Magic only when
    /// empty, so a hand-arranged loadout survives a re-run.
    /// </summary>
    public class SpellSelector : MonoBehaviour
    {
        [Tooltip("The caster whose spell this selects. Found on this object when empty.")]
        [SerializeField] private SpellCaster caster;

        [Tooltip("Selection buttons. Found on this object when empty; without it, only " +
                 "code (Select / Cycle) changes the spell.")]
        [SerializeField] private SpellSelectInput input;

        [Tooltip("The spells carried, in slot order: slot 1 is number key 1. Empty entries " +
                 "are skipped.")]
        [SerializeField] private SpellDefinition[] spells = Array.Empty<SpellDefinition>();

        /// <summary>Raised after the selected slot changes, by input, by code, or
        /// by something else setting the caster's spell.</summary>
        public event Action<SpellSelector> SelectionChanged;

        int _selected = -1;
        bool _selecting;

        /// <summary>Number of slots (filled or not).</summary>
        public int Count => spells != null ? spells.Length : 0;

        /// <summary>The selected slot, 0-based; -1 when the caster's spell is in no slot.</summary>
        public int SelectedIndex => _selected;

        /// <summary>The selected spell; null when none.</summary>
        public SpellDefinition Selected => SpellSelection.IsUsable(spells, _selected) ? spells[_selected] : null;

        /// <summary>The caster this selects for; may be null.</summary>
        public SpellCaster Caster => caster;

        /// <summary>The spell in slot <paramref name="index"/>; null when empty or out of range.</summary>
        public SpellDefinition SpellAt(int index) => SpellSelection.IsUsable(spells, index) ? spells[index] : null;

        /// <summary>Selects slot <paramref name="index"/>. False (and nothing happens)
        /// when it is empty, out of range, or already selected.</summary>
        public bool Select(int index)
        {
            if (!SpellSelection.IsUsable(spells, index) || index == _selected) return false;
            _selected = index;
            _selecting = true;
            try
            {
                if (caster != null) caster.SetSpell(spells[index]);
            }
            finally
            {
                _selecting = false;
            }
            SelectionChanged?.Invoke(this);
            return true;
        }

        /// <summary>Selects the next filled slot in <paramref name="direction"/>'s
        /// direction (+1 next, -1 previous), wrapping round. False when that is
        /// the slot already selected or there is none.</summary>
        public bool Cycle(int direction) => Select(SpellSelection.Cycle(spells, _selected, direction));

        void Awake()
        {
            if (caster == null) caster = GetComponent<SpellCaster>();
            if (input == null) input = GetComponent<SpellSelectInput>();
        }

        void OnEnable()
        {
            if (caster != null) caster.SpellChanged += OnCasterSpellChanged;
            Sync();
        }

        void OnDisable()
        {
            if (caster != null) caster.SpellChanged -= OnCasterSpellChanged;
        }

        void Update()
        {
            if (input == null) return;
            int slot = input.SlotPressedThisFrame;
            if (slot >= 0) Select(slot);
            int step = input.CycleThisFrame;
            if (step != 0) Cycle(step);
        }

        /// <summary>Finds the caster's spell among the slots; if it is in none,
        /// selects the first filled slot (which hands that spell to the caster).</summary>
        void Sync()
        {
            int index = IndexOf(caster != null ? caster.Spell : null);
            if (index >= 0)
            {
                _selected = index;
                SelectionChanged?.Invoke(this);
                return;
            }
            _selected = -1;
            Select(SpellSelection.FirstUsable(spells));
        }

        void OnCasterSpellChanged(SpellDefinition spell)
        {
            if (_selecting) return;
            int index = IndexOf(spell);
            if (index == _selected) return;
            _selected = index;
            SelectionChanged?.Invoke(this);
        }

        int IndexOf(SpellDefinition spell)
        {
            if (spell == null || spells == null) return -1;
            for (int i = 0; i < spells.Length; i++)
                if (spells[i] == spell) return i;
            return -1;
        }
    }
}
