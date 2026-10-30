using System;
using System.Collections.Generic;
using Pickleball.Sandbox.Hands;
using Pickleball.Sandbox.Weapon;
using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// Puts the inventory's equipped weapon in the hand and behind the sword.
    /// Gameplay: it changes what the blade is and how it swings. Presentation
    /// listens to <see cref="WeaponEquipped"/>.
    ///
    /// <b>One swap does all of it, in this order.</b>
    /// <list type="number">
    /// <item>The weapon's held prefab is instantiated once (the first time it is
    /// equipped) and kept. Stowed weapons wait, active themselves, under an
    /// inactive "StowedWeapons" child, so nothing is created or destroyed on a
    /// later swap and a stowed weapon draws nothing.</item>
    /// <item><see cref="HandGrip.Release"/> stows the old item and
    /// <see cref="HandGrip.Hold"/> seats the new one by its grip anchor.</item>
    /// <item>The hand's own <c>BladeBase</c> / <c>BladeTip</c> transforms move
    /// onto the held item's blade markers. They are moved, never replaced:
    /// MeleeCombat, BladeTrail, WeaponClearance and SwordEffects all hold
    /// references to them. They stay children of the grab point, so once
    /// placed they follow the hand like the item does.</item>
    /// <item><see cref="WeaponClearance.SetProfile"/> takes the weapon's
    /// profile (an empty one keeps the current profile).</item>
    /// <item><see cref="MeleeCombat.Equip"/> takes its moveset and blade radius,
    /// which also starts combo progress afresh.</item>
    /// </list>
    ///
    /// <b>Never mid-swing.</b> A switch asked for while a swing or its hitstop
    /// is running waits until both are over (<see cref="PendingEquip"/>), so
    /// the blade never jumps between two weapons inside one sweep. With the
    /// game paused (the menu open) nothing advances, so a switch picked from
    /// the menu mid-swing lands when the swing ends after the menu closes.
    ///
    /// <b>The compass already in the hand.</b> Chibi.prefab holds the compass
    /// as its "WeaponVisual" child. It is the <see cref="fallbackItem"/>: a
    /// weapon whose held prefab is not set (Held_Compass or Held_Ruler before
    /// they exist) is shown with it, and the blade markers go back to where
    /// the weapon setup fitted them to it. Once a weapon has its own prefab,
    /// the fallback is stowed like any other item. A weapon with no moveset
    /// keeps swinging the current one.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponEquipper : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Whose equipped weapon this applies. Found on this object when empty.")]
        [SerializeField] private Inventory inventory;

        [Tooltip("Takes the weapon's moveset and blade radius. Found on this object when empty.")]
        [SerializeField] private MeleeCombat melee;

        [Tooltip("The grab point (WeaponSocket) the weapon is seated on.")]
        [SerializeField] private HandGrip handGrip;

        [Tooltip("Takes the weapon's clearance profile. Optional.")]
        [SerializeField] private WeaponClearance clearance;

        [Tooltip("The hand's BladeBase marker (child of the grab point). Moved onto each " +
                 "weapon's own marker; every blade system keeps its reference to it.")]
        [SerializeField] private Transform bladeBase;

        [Tooltip("The hand's BladeTip marker (child of the grab point).")]
        [SerializeField] private Transform bladeTip;

        [Tooltip("Inactive parent for stowed weapons. Created under this object when empty.")]
        [SerializeField] private Transform stowRoot;

        [Header("Fallback")]
        [Tooltip("The item already in the hand (Chibi.prefab's WeaponVisual compass). Shown for " +
                 "a weapon with no held prefab; the blade markers then return to where they " +
                 "were fitted to it.")]
        [SerializeField] private HeldItem fallbackItem;

        /// <summary>Raised after a weapon is in the hand and behind the sword. For presentation (the equipped readout).</summary>
        public event Action<WeaponDefinition> WeaponEquipped;

        // Plain serializable fields, so a script reload in Play mode keeps them.
        WeaponDefinition _current;
        List<WeaponDefinition> _instancedWeapons = new();
        List<HeldItem> _instances = new();
        Vector3 _fallbackBasePosition, _fallbackTipPosition;
        Quaternion _fallbackBaseRotation = Quaternion.identity, _fallbackTipRotation = Quaternion.identity;
        bool _fallbackPoseCaptured;

        PendingEquip _pending;
        Inventory _subscribed;

        /// <summary>The weapon in the hand now, or null before the first swap.</summary>
        public WeaponDefinition Current => _current;

        /// <summary>True while a switch waits for the swing to end.</summary>
        public bool IsSwitchPending => _pending != null && _pending.HasPending;

        /// <summary>The weapon waiting to go in, or null.</summary>
        public WeaponDefinition PendingWeapon => _pending != null ? _pending.Pending : null;

        /// <summary>The item the hand holds, or null.</summary>
        public HeldItem Held => handGrip != null ? handGrip.Held : null;

        bool Busy => melee != null && (melee.IsAttacking || melee.IsInHitstop);

        void Awake()
        {
            if (inventory == null) inventory = GetComponent<Inventory>();
            if (melee == null) melee = GetComponent<MeleeCombat>();
            CaptureFallbackPose();
        }

        void OnEnable()
        {
            _pending = new PendingEquip();
            Subscribe(inventory);
            if (inventory != null) _pending.Request(inventory.Equipped, _current);
        }

        void OnDisable() => Subscribe(null);

        void Update()
        {
            if (_pending.TryTake(Busy, out var weapon)) Apply(weapon);
        }

        /// <summary>
        /// Asks for <paramref name="weapon"/> in the hand: now if the sword is
        /// idle, else when the swing ends. Normally driven by the inventory's
        /// EquippedChanged; public for tests and debug tools.
        /// </summary>
        public void Request(WeaponDefinition weapon)
        {
            _pending ??= new PendingEquip();
            if (_pending.Request(weapon, _current) && isActiveAndEnabled && !Busy && _pending.TryTake(false, out var now))
                Apply(now);
        }

        void Subscribe(Inventory next)
        {
            if (_subscribed != null) _subscribed.EquippedChanged -= Request;
            _subscribed = next;
            if (_subscribed != null) _subscribed.EquippedChanged += Request;
        }

        /// <summary>Does the swap at once. Callers make sure no swing is running.</summary>
        void Apply(WeaponDefinition weapon)
        {
            if (weapon == null) return;   // nothing equipped: leave the hand as it is
            CaptureFallbackPose();

            var item = weapon.HeldPrefab != null ? InstanceFor(weapon) : fallbackItem;
            if (weapon.HeldPrefab == null && fallbackItem == null)
                Debug.LogWarning($"[WeaponEquipper] {weapon.DisplayName} has no held prefab and there is " +
                                 "no fallback item; the hand keeps what it holds.", this);

            if (item != null && handGrip != null)
            {
                if (handGrip.Held != null && handGrip.Held != item) handGrip.Release(StowRoot);
                handGrip.Hold(item);
                if (!item.gameObject.activeSelf) item.gameObject.SetActive(true);
            }
            PlaceBladeMarkers(item);

            if (clearance != null && weapon.ClearanceProfile != null) clearance.SetProfile(weapon.ClearanceProfile);
            if (melee != null)
            {
                if (weapon.Moveset == null)
                    Debug.LogWarning($"[WeaponEquipper] {weapon.DisplayName} has no moveset yet; it swings " +
                                     "the current one.", this);
                melee.Equip(weapon.Moveset, weapon.BladeRadius);
            }

            _current = weapon;
            WeaponEquipped?.Invoke(weapon);
        }

        HeldItem InstanceFor(WeaponDefinition weapon)
        {
            for (int i = 0; i < _instancedWeapons.Count; i++)
                if (_instancedWeapons[i] == weapon && i < _instances.Count && _instances[i] != null)
                    return _instances[i];

            var item = Instantiate(weapon.HeldPrefab, StowRoot, false);
            item.name = weapon.HeldPrefab.name;
            // A held prop must not push the character or be struck by its own blade.
            foreach (var col in item.GetComponentsInChildren<Collider>(true)) Destroy(col);
            foreach (var body in item.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            if (!item.HasBlade)
                Debug.LogWarning($"[WeaponEquipper] {item.name} has no blade markers; the hit sweep " +
                                 "uses the fallback blade.", this);

            int slot = _instancedWeapons.IndexOf(weapon);
            if (slot >= 0 && slot < _instances.Count) _instances[slot] = item;
            else
            {
                _instancedWeapons.Add(weapon);
                _instances.Add(item);
            }
            return item;
        }

        void PlaceBladeMarkers(HeldItem item)
        {
            if (bladeBase == null || bladeTip == null) return;
            if (item != null && item.HasBlade && item != fallbackItem)
            {
                bladeBase.SetPositionAndRotation(item.BladeBase.position, item.BladeBase.rotation);
                bladeTip.SetPositionAndRotation(item.BladeTip.position, item.BladeTip.rotation);
                return;
            }
            if (!_fallbackPoseCaptured) return;
            bladeBase.SetLocalPositionAndRotation(_fallbackBasePosition, _fallbackBaseRotation);
            bladeTip.SetLocalPositionAndRotation(_fallbackTipPosition, _fallbackTipRotation);
        }

        /// <summary>Remembers where the weapon setup fitted the markers to the
        /// fallback compass, once, before anything moves them.</summary>
        void CaptureFallbackPose()
        {
            if (_fallbackPoseCaptured || bladeBase == null || bladeTip == null) return;
            _fallbackBasePosition = bladeBase.localPosition;
            _fallbackBaseRotation = bladeBase.localRotation;
            _fallbackTipPosition = bladeTip.localPosition;
            _fallbackTipRotation = bladeTip.localRotation;
            _fallbackPoseCaptured = true;
        }

        Transform StowRoot
        {
            get
            {
                if (stowRoot != null) return stowRoot;
                var go = new GameObject("StowedWeapons");
                go.SetActive(false);
                go.transform.SetParent(transform, false);
                stowRoot = go.transform;
                return stowRoot;
            }
        }

        /// <summary>Wires the hand. For setup scripts and tests.</summary>
        public void Bind(Inventory owner, MeleeCombat combat, HandGrip grip, WeaponClearance weaponClearance,
                         Transform baseMarker, Transform tipMarker, Transform stow, HeldItem fallback)
        {
            inventory = owner;
            melee = combat;
            handGrip = grip;
            clearance = weaponClearance;
            bladeBase = baseMarker;
            bladeTip = tipMarker;
            stowRoot = stow;
            fallbackItem = fallback;
            _fallbackPoseCaptured = false;
            CaptureFallbackPose();
            if (isActiveAndEnabled) Subscribe(inventory);
        }
    }
}
