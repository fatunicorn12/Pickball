using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.LockOn
{
    /// <summary>
    /// Builds lock-on rigs: targets shaped like the game's and a player with a real camera. Hosts
    /// aren't DontSave, because the candidate search (FindObjectsByType) skips DontSave objects; each
    /// is destroyed in TearDown. EditMode sends no lifecycle messages, so a Health is filled by
    /// hand and Update is called by reflection.
    /// </summary>
    public abstract class LockOnTestFixture
    {
        protected const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        readonly List<GameObject> _hosts = new();

        [TearDown]
        public void DestroyHosts()
        {
            foreach (var go in _hosts)
                if (go != null) Object.DestroyImmediate(go);
            _hosts.Clear();
            foreach (var reticle in Object.FindObjectsByType<LockOnReticle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(reticle.gameObject);
        }

        protected GameObject Host(string name, Vector3 position = default)
        {
            var go = new GameObject("Test_" + name);
            go.transform.position = position;
            _hosts.Add(go);
            return go;
        }

        protected GameObject Primitive(PrimitiveType type, string name, Vector3 position = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "Test_" + name;
            go.transform.position = position;
            _hosts.Add(go);
            return go;
        }

        /// <summary>A live Health on its own object.</summary>
        protected Health NewTarget(string name, Vector3 root)
        {
            var health = Host(name, root).AddComponent<Health>();
            health.ResetToFull();
            return health;
        }

        /// <summary>A Health planted at <paramref name="root"/> with a capsule body, 2 m tall and
        /// 1 m wide, floating <paramref name="height"/> above it on a child. As Hypatia is built.</summary>
        protected Health NewFloater(string name, Vector3 root, float height, out CapsuleCollider body)
        {
            var health = NewTarget(name, root);
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Body";
            go.transform.SetParent(health.transform, false);
            go.transform.localPosition = Vector3.up * height;
            body = go.GetComponent<CapsuleCollider>();
            return health;
        }

        protected static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            Assert.IsNotNull(property, $"{target.GetType().Name} has no serialized field '{field}'");
            property.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            Assert.IsNotNull(property, $"{target.GetType().Name} has no serialized field '{field}'");
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected static void SetMask(Object target, string field, int mask)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            Assert.IsNotNull(property, $"{target.GetType().Name} has no serialized field '{field}'");
            property.intValue = mask;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        protected static void SetPrivate(object target, string field, object value)
        {
            var info = target.GetType().GetField(field, Private);
            Assert.IsNotNull(info, $"{target.GetType().Name} has no field '{field}'");
            info.SetValue(target, value);
        }

        protected static T GetPrivate<T>(object target, string field)
        {
            var info = target.GetType().GetField(field, Private);
            Assert.IsNotNull(info, $"{target.GetType().Name} has no field '{field}'");
            return (T)info.GetValue(target);
        }

        protected static void Call(object target, string method)
        {
            var info = target.GetType().GetMethod(method, Private);
            Assert.IsNotNull(info, $"{target.GetType().Name} has no method '{method}'");
            info.Invoke(target, null);
        }

        /// <summary>Sets the locked target directly, skipping selection.</summary>
        protected static void ForceLock(LockOnTargeting lockOn, Health target)
            => typeof(LockOnTargeting).GetProperty(nameof(LockOnTargeting.Target)).SetValue(lockOn, target);
    }
}
