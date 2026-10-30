using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Inventory;
using UnityEngine;

namespace Pickleball.Tests.Inventory
{
    /// <summary>
    /// Throwaway item definitions and host objects for the inventory suites,
    /// all <see cref="HideFlags.DontSave"/> and destroyed after each test, so a
    /// run leaves nothing in the open scene or the project.
    /// </summary>
    public abstract class InventoryTestFixture
    {
        readonly List<Object> _made = new();

        [TearDown]
        public void DestroyMade()
        {
            for (int i = _made.Count - 1; i >= 0; i--)
                if (_made[i] != null) Object.DestroyImmediate(_made[i]);
            _made.Clear();
        }

        /// <summary>Destroys <paramref name="o"/> in TearDown.</summary>
        protected T Track<T>(T o) where T : Object
        {
            if (o != null) _made.Add(o);
            return o;
        }

        protected WeaponDefinition Weapon(string id)
        {
            var weapon = Track(ScriptableObject.CreateInstance<WeaponDefinition>());
            weapon.hideFlags = HideFlags.DontSave;
            weapon.name = id;
            weapon.SetIdentity(id, id, "");
            return weapon;
        }

        protected KeyItemDefinition Key(AbilityKind kind)
        {
            var key = Track(ScriptableObject.CreateInstance<KeyItemDefinition>());
            key.hideFlags = HideFlags.DontSave;
            var names = KeyItemNames.For(kind);
            key.SetIdentity(names.Id, names.DisplayName, names.Description);
            key.Configure(kind);
            return key;
        }

        protected MagicItemDefinition Magic(string id)
        {
            var magic = Track(ScriptableObject.CreateInstance<MagicItemDefinition>());
            magic.hideFlags = HideFlags.DontSave;
            magic.SetIdentity(id, id, "");
            return magic;
        }

        /// <summary>A host object with <typeparamref name="T"/>; Awake and OnEnable do not run in EditMode.</summary>
        protected T NewComponent<T>(GameObject host = null) where T : Component
        {
            if (host == null)
            {
                host = Track(new GameObject($"Test_{typeof(T).Name}") { hideFlags = HideFlags.DontSave });
            }
            return host.AddComponent<T>();
        }

        protected static InventoryModel NewModel(int weapons = 8, int magic = 10, int keys = 10)
            => new(new InventoryCapacity(weapons, magic, keys));
    }
}
