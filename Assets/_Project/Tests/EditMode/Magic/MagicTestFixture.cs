using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// Shared plumbing for the spell tests: DontSave hosts (from
    /// <see cref="BossTestFixture"/>), ScriptableObjects that are destroyed in
    /// TearDown, a full <see cref="Mana"/> bar, and serialized-field writers that
    /// fail loudly on a renamed field. Every tunable a test depends on is set
    /// by the test, never inherited from code defaults.
    /// </summary>
    public abstract class MagicTestFixture : BossTestFixture
    {
        readonly List<Object> _assets = new();

        [TearDown]
        public void DestroyAssets()
        {
            foreach (var asset in _assets)
                if (asset != null) Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        /// <summary>A ScriptableObject destroyed after the test.</summary>
        protected T NewAsset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.hideFlags = HideFlags.DontSave;
            _assets.Add(asset);
            return asset;
        }

        /// <summary>A full 100-point bar (the shipped default) that never regenerates
        /// unless the test ticks it.</summary>
        protected Mana NewMana()
        {
            var mana = NewComponent<Mana>();
            SetReference(mana, "config", NewAsset<ManaConfig>());
            Assert.That(mana.Current, Is.EqualTo(mana.Max), "a new bar starts full");
            return mana;
        }

        protected static void SetReference(Object target, string field, Object value)
        {
            var so = Serialized(target, field, out var p);
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected static void SetFloat(Object target, string field, float value)
        {
            var so = Serialized(target, field, out var p);
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected static void SetInt(Object target, string field, int value)
        {
            var so = Serialized(target, field, out var p);
            p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected static void SetBool(Object target, string field, bool value)
        {
            var so = Serialized(target, field, out var p);
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected static void SetArray<T>(Object target, string field, T[] values) where T : Object
        {
            var so = Serialized(target, field, out var p);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static SerializedObject Serialized(Object target, string field, out SerializedProperty property)
        {
            var so = new SerializedObject(target);
            property = so.FindProperty(field);
            Assert.That(property, Is.Not.Null,
                        $"{target.GetType().Name} has no serialized field '{field}' — it was probably " +
                        "renamed. Update the test (and Setup Magic, which writes it).");
            return so;
        }
    }
}
