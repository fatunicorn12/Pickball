using System;
using System.Collections.Generic;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// The player's inventory: an <see cref="InventoryModel"/> on the player,
    /// seeded with starting items and kept in step with the systems that
    /// already own part of it. Gameplay only; the menu (<see cref="InventoryMenu"/>)
    /// and its view read it, the <see cref="WeaponEquipper"/> applies its
    /// equipped weapon.
    ///
    /// <b>One authority per tab, never two.</b>
    /// <list type="bullet">
    /// <item><b>Weapons</b> are owned here: starting weapons, pickups
    /// (<see cref="ItemPickup"/>), <see cref="Equip"/> and the quick-cycle button.</item>
    /// <item><b>Key items</b> mirror <see cref="PlayerAbilities"/>, which stays the
    /// source of truth for what the player can do. Each unlocked ability shows
    /// its <see cref="KeyItemDefinition"/> (from the catalog, else a runtime one
    /// named by <see cref="KeyItemNames"/>). Adding a key item grants its
    /// ability; removing one revokes it. <c>PlayerAbilities.Revoke</c> raises no
    /// event, so the tab is re-checked whenever the menu opens.</item>
    /// <item><b>Magic</b> mirrors the <see cref="SpellSelector"/>'s slots, in slot
    /// order. <see cref="SelectSpell"/> selects the spell there, so the
    /// selector keeps sole authority over what is cast.</item>
    /// </list>
    ///
    /// <b>Reload safety.</b> The model is rebuilt on demand after a script
    /// reload from the starting items plus whatever was added at runtime (kept
    /// in a plain serializable list, which a reload in Play mode preserves),
    /// then re-synced; the equipped weapon is restored the same way.
    /// </summary>
    [DisallowMultipleComponent]
    public class Inventory : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Slots per tab and menu layout. Created by Tools ▸ Pickleball ▸ Setup Inventory. " +
                 "Empty: the shipped default capacity.")]
        [SerializeField] private InventoryConfig config;

        [Tooltip("Items owned from the start (weapons, and any key or magic item that should " +
                 "show even before its system grants it). Order is slot order.")]
        [SerializeField] private InventoryItemDefinition[] startingItems = Array.Empty<InventoryItemDefinition>();

        [Tooltip("The weapon in hand at the start. Must also be in Starting Items. Empty: the " +
                 "first starting weapon.")]
        [SerializeField] private WeaponDefinition startingWeapon;

        [Header("Catalogs")]
        [Tooltip("The key item shown for each ability. An ability with no entry here still " +
                 "shows, under a runtime item named from KeyItemNames.")]
        [SerializeField] private KeyItemDefinition[] keyItems = Array.Empty<KeyItemDefinition>();

        [Tooltip("The menu entry for each spell. A spell with no entry here still shows, under " +
                 "a runtime item named after the spell.")]
        [SerializeField] private MagicItemDefinition[] magicItems = Array.Empty<MagicItemDefinition>();

        [Header("Wiring")]
        [Tooltip("The unlocked abilities the Key Items tab mirrors. Found on this object when empty.")]
        [SerializeField] private PlayerAbilities abilities;

        [Tooltip("The spell slots the Magic tab mirrors. Found on this object when empty; " +
                 "without one, the Magic tab is empty.")]
        [SerializeField] private SpellSelector spells;

        [Tooltip("Reads the next-weapon button. Found on this object when empty.")]
        [SerializeField] private InventoryInput input;

        [Tooltip("While Sword is blocked (aiming a spell, a conversation, the open menu) the " +
                 "next-weapon button does nothing. Empty: never blocked.")]
        [SerializeField] private PlayerInputGate inputGate;

        /// <summary>Raised after an item is added to or removed from a tab.</summary>
        public event Action<InventoryCategory> Changed;

        /// <summary>Raised after the equipped weapon changes (the request; the hand follows
        /// through <see cref="WeaponEquipper"/>). Carries the new weapon, or null.</summary>
        public event Action<WeaponDefinition> EquippedChanged;

        static readonly AbilityKind[] AllAbilities = (AbilityKind[])Enum.GetValues(typeof(AbilityKind));

        InventoryModel _model;
        // Runtime additions and the equipped weapon, as plain serializable fields so a
        // script reload in Play mode keeps them (the model itself is not serializable).
        List<InventoryItemDefinition> _addedAtRuntime = new();
        WeaponDefinition _equippedAtRuntime;
        Dictionary<AbilityKind, KeyItemDefinition> _runtimeKeyItems;
        Dictionary<SpellDefinition, MagicItemDefinition> _runtimeMagicItems;
        PlayerAbilities _subscribedAbilities;
        SpellSelector _subscribedSpells;
        bool _syncing;

        /// <summary>The state behind the menu. Built on first use.</summary>
        public InventoryModel Model => EnsureModel();

        /// <summary>The config in use; may be null (default capacity).</summary>
        public InventoryConfig Config => config;

        /// <summary>The equipped weapon (requested), or null.</summary>
        public WeaponDefinition Equipped => Model.Equipped;

        /// <summary>The spell slots mirrored by the Magic tab; may be null.</summary>
        public SpellSelector Spells => spells;

        /// <summary>The magic item for the spell the selector has selected, or null.</summary>
        public MagicItemDefinition SelectedSpellItem
            => spells != null && spells.Selected != null ? MagicItemFor(spells.Selected) : null;

        /// <summary>True when <paramref name="item"/> is owned.</summary>
        public bool Has(InventoryItemDefinition item) => Model.Has(item);

        void Awake()
        {
            if (abilities == null) abilities = GetComponent<PlayerAbilities>();
            if (spells == null) spells = GetComponent<SpellSelector>();
            if (input == null) input = GetComponent<InventoryInput>();
        }

        void OnEnable()
        {
            EnsureModel();
            Subscribe(abilities, spells);
            Resync();
        }

        void Start() => Resync();   // abilities granted in other components' Start

        void OnDisable() => Subscribe(null, null);

        void OnDestroy()
        {
            if (_runtimeKeyItems != null)
                foreach (var item in _runtimeKeyItems.Values) if (item != null) Destroy(item);
            if (_runtimeMagicItems != null)
                foreach (var item in _runtimeMagicItems.Values) if (item != null) Destroy(item);
        }

        void Update()
        {
            if (input == null || !input.CyclePressed) return;
            if (inputGate != null && inputGate.IsBlocked(InputBlock.Sword)) return;
            CycleWeapon(+1);
        }

        // ------------------------------------------------------------ queries

        /// <summary>The key item that stands for <paramref name="kind"/>: the catalog's, else a runtime one.</summary>
        public KeyItemDefinition KeyItemFor(AbilityKind kind)
        {
            if (keyItems != null)
                foreach (var item in keyItems)
                    if (item != null && item.Ability == kind) return item;
            _runtimeKeyItems ??= new Dictionary<AbilityKind, KeyItemDefinition>();
            if (_runtimeKeyItems.TryGetValue(kind, out var made) && made != null) return made;
            made = ScriptableObject.CreateInstance<KeyItemDefinition>();
            made.hideFlags = HideFlags.DontSave;
            var names = KeyItemNames.For(kind);
            made.name = $"Runtime_{names.Id}";
            made.SetIdentity(names.Id, names.DisplayName, names.Description);
            made.Configure(kind);
            _runtimeKeyItems[kind] = made;
            return made;
        }

        /// <summary>The magic item for <paramref name="spell"/>: the catalog's, else a runtime one. Null for a null spell.</summary>
        public MagicItemDefinition MagicItemFor(SpellDefinition spell)
        {
            if (spell == null) return null;
            if (magicItems != null)
                foreach (var item in magicItems)
                    if (item != null && item.Spell == spell) return item;
            _runtimeMagicItems ??= new Dictionary<SpellDefinition, MagicItemDefinition>();
            if (_runtimeMagicItems.TryGetValue(spell, out var made) && made != null) return made;
            made = MagicItemDefinition.CreateRuntime(spell);
            _runtimeMagicItems[spell] = made;
            return made;
        }

        // ------------------------------------------------------------ changes

        /// <summary>
        /// Adds <paramref name="item"/>. A key item also grants its ability
        /// (PlayerAbilities stays the authority); a weapon added while
        /// empty-handed is equipped.
        /// </summary>
        public InventoryAddResult Add(InventoryItemDefinition item)
        {
            if (item == null) return InventoryAddResult.Invalid;
            var model = Model;
            if (item is KeyItemDefinition key && abilities != null)
            {
                if (abilities.Has(key.Ability))
                {
                    SyncKeyItems();
                    return InventoryAddResult.AlreadyOwned;
                }
                var keyResult = model.Add(item);
                if (keyResult == InventoryAddResult.Added) abilities.Grant(key.Ability);
                return keyResult;
            }

            var result = model.Add(item);
            if (result == InventoryAddResult.Added && item.Category == InventoryCategory.Weapon
                && !Contains(startingItems, item))
                _addedAtRuntime.Add(item);
            return result;
        }

        /// <summary>Removes <paramref name="item"/>; a key item also revokes its ability.</summary>
        public bool Remove(InventoryItemDefinition item)
        {
            if (item is KeyItemDefinition key && abilities != null) abilities.Revoke(key.Ability);
            _addedAtRuntime.Remove(item);
            return Model.Remove(item);
        }

        /// <summary>Equips an owned weapon. False when not owned or already equipped.</summary>
        public bool Equip(WeaponDefinition weapon) => Model.Equip(weapon);

        /// <summary>Equips the next (+1) or previous (-1) owned weapon. The new weapon, or null when nothing changed.</summary>
        public WeaponDefinition CycleWeapon(int direction) => Model.CycleWeapon(direction);

        /// <summary>
        /// Selects <paramref name="item"/>'s spell on the selector. False when
        /// there is no selector, the spell is in none of its slots, or it is
        /// already selected.
        /// </summary>
        public bool SelectSpell(MagicItemDefinition item)
        {
            if (spells == null || item == null || item.Spell == null) return false;
            for (int i = 0; i < spells.Count; i++)
                if (spells.SpellAt(i) == item.Spell) return spells.Select(i);
            return false;
        }

        /// <summary>
        /// Re-reads the systems the Key Items and Magic tabs mirror. Cheap and
        /// safe to call any time; the menu calls it on open.
        /// </summary>
        public void Resync()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                SyncKeyItems();
                SyncMagic();
            }
            finally
            {
                _syncing = false;
            }
        }

        // ------------------------------------------------------------ internals

        InventoryModel EnsureModel()
        {
            if (_model != null) return _model;
            // Chosen before seeding: adding the first weapon equips it, and that
            // must not be mistaken for the weapon to restore.
            var equip = _equippedAtRuntime != null ? _equippedAtRuntime : startingWeapon;
            _model = new InventoryModel(config != null ? config.Capacity : InventoryCapacity.Default);

            if (startingItems != null)
                foreach (var item in startingItems)
                    if (item != null && item.Category != InventoryCategory.KeyItem) _model.Add(item);
            _addedAtRuntime ??= new List<InventoryItemDefinition>();
            foreach (var item in _addedAtRuntime) if (item != null) _model.Add(item);
            if (equip != null) _model.Equip(equip);
            _equippedAtRuntime = _model.Equipped;

            // Listened to only once seeded: nothing outside has seen the empty model,
            // and the equipper asks for Equipped itself when it enables.
            _model.Changed += OnModelChanged;
            _model.EquippedChanged += OnModelEquippedChanged;
            return _model;
        }

        void OnModelChanged(InventoryCategory category) => Changed?.Invoke(category);

        void OnModelEquippedChanged(WeaponDefinition weapon)
        {
            _equippedAtRuntime = weapon;
            EquippedChanged?.Invoke(weapon);
        }

        void Subscribe(PlayerAbilities nextAbilities, SpellSelector nextSpells)
        {
            if (_subscribedAbilities != null) _subscribedAbilities.Unlocked -= OnUnlocked;
            _subscribedAbilities = nextAbilities;
            if (_subscribedAbilities != null) _subscribedAbilities.Unlocked += OnUnlocked;

            if (_subscribedSpells != null) _subscribedSpells.SelectionChanged -= OnSpellSelectionChanged;
            _subscribedSpells = nextSpells;
            if (_subscribedSpells != null) _subscribedSpells.SelectionChanged += OnSpellSelectionChanged;
        }

        void OnUnlocked(AbilityKind kind) => SyncKeyItems();

        void OnSpellSelectionChanged(SpellSelector _) => SyncMagic();

        /// <summary>
        /// Key Items = the unlocked abilities, plus any starting key item. With
        /// no PlayerAbilities, only the starting key items show. New ones append
        /// in unlock order; revoked ones are removed.
        /// </summary>
        void SyncKeyItems()
        {
            var model = Model;
            if (abilities == null)
            {
                if (startingItems != null)
                    foreach (var item in startingItems)
                        if (item is KeyItemDefinition) model.Add(item);
                return;
            }

            // Starting key items are granted, so PlayerAbilities stays the authority.
            if (startingItems != null)
                foreach (var item in startingItems)
                    if (item is KeyItemDefinition startKey) abilities.Grant(startKey.Ability);

            var owned = model.Items(InventoryCategory.KeyItem);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] is KeyItemDefinition k && !abilities.Has(k.Ability)) model.Remove(owned[i]);

            foreach (var kind in AllAbilities)
            {
                if (!abilities.Has(kind)) continue;
                if (HasKeyFor(model, kind)) continue;
                model.Add(KeyItemFor(kind));
            }
        }

        static bool HasKeyFor(InventoryModel model, AbilityKind kind)
        {
            var owned = model.Items(InventoryCategory.KeyItem);
            for (int i = 0; i < owned.Count; i++)
                if (owned[i] is KeyItemDefinition k && k.Ability == kind) return true;
            return false;
        }

        /// <summary>Magic = the selector's filled slots, in slot order. Rebuilt only when it differs.</summary>
        void SyncMagic()
        {
            var model = Model;
            var owned = model.Items(InventoryCategory.Magic);
            int count = spells != null ? spells.Count : 0;

            int filled = 0;
            bool same = true;
            for (int i = 0; i < count; i++)
            {
                var spell = spells.SpellAt(i);
                if (spell == null) continue;
                if (filled >= owned.Count || !(owned[filled] is MagicItemDefinition m) || m.Spell != spell)
                    same = false;
                filled++;
            }
            if (same && filled == owned.Count) return;

            model.Clear(InventoryCategory.Magic);
            for (int i = 0; i < count; i++)
            {
                var spell = spells.SpellAt(i);
                if (spell != null) model.Add(MagicItemFor(spell));
            }
        }

        static bool Contains(InventoryItemDefinition[] items, InventoryItemDefinition item)
        {
            if (items == null) return false;
            foreach (var i in items) if (i == item) return true;
            return false;
        }

        /// <summary>Wires the data. For setup scripts and tests.</summary>
        public void Configure(InventoryConfig inventoryConfig, InventoryItemDefinition[] starting,
                              WeaponDefinition equippedAtStart)
        {
            config = inventoryConfig;
            startingItems = starting ?? Array.Empty<InventoryItemDefinition>();
            startingWeapon = equippedAtStart;
            _model = null;
            _equippedAtRuntime = null;
            _addedAtRuntime?.Clear();
        }

        /// <summary>Wires the systems the tabs mirror. For setup scripts and tests.</summary>
        public void Bind(PlayerAbilities playerAbilities, SpellSelector spellSelector)
        {
            abilities = playerAbilities;
            spells = spellSelector;
            if (isActiveAndEnabled) Subscribe(abilities, spells);
        }
    }
}
