using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Shared scaffolding for the dialogue suites: throwaway DontSave hosts and
    /// assets destroyed in TearDown, serialized-field wiring the way the setup
    /// menu does it, and a guaranteed restore of <c>Time.timeScale</c> and
    /// <c>AudioListener.pause</c>, which the director changes.
    /// </summary>
    public abstract class DialogueTestFixture
    {
        readonly List<Object> _spawned = new();
        float _timeScale;
        bool _audioPaused;

        [SetUp]
        public void RememberGlobals()
        {
            _timeScale = Time.timeScale;
            _audioPaused = AudioListener.pause;
            Time.timeScale = 1f;
        }

        [TearDown]
        public void RestoreGlobalsAndDestroy()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
            Time.timeScale = _timeScale;
            AudioListener.pause = _audioPaused;
        }

        /// <summary>A DontSave host object, destroyed in TearDown.</summary>
        protected GameObject NewHost(string name, Vector3 position = default)
        {
            var go = new GameObject($"Test_{name}") { hideFlags = HideFlags.DontSave };
            go.transform.position = position;
            _spawned.Add(go);
            return go;
        }

        /// <summary>Tracks an asset or object for destruction in TearDown.</summary>
        protected T Track<T>(T obj) where T : Object
        {
            _spawned.Add(obj);
            return obj;
        }

        protected DialogueDefinition Definition(params string[] lines)
        {
            var array = new DialogueLine[lines.Length];
            for (int i = 0; i < lines.Length; i++) array[i] = new DialogueLine(lines[i]);
            return Track(DialogueDefinition.CreateRuntime("Birdy", array));
        }

        protected DialogueConfig NewConfig() => Track(ScriptableObject.CreateInstance<DialogueConfig>());

        protected TalkableSet NewSet() => Track(ScriptableObject.CreateInstance<TalkableSet>());

        /// <summary>A talkable NPC with <paramref name="definition"/>. Edit mode does
        /// not run its OnEnable; call <see cref="Lifecycle"/> where a test needs it.</summary>
        protected Talkable NewTalkable(DialogueDefinition definition, Vector3 position = default)
        {
            var host = NewHost("Npc", position);
            var talkable = host.AddComponent<Talkable>();
            SetRef(talkable, "dialogue", definition);
            return talkable;
        }

        /// <summary>Runs a private Unity message (OnEnable, OnDisable) that Edit mode
        /// does not send to ordinary scripts.</summary>
        protected static void Lifecycle(Object target, string message)
        {
            var method = target.GetType().GetMethod(message,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            Assert.That(method, Is.Not.Null, $"{target.GetType().Name} has no {message}.");
            method.Invoke(target, null);
        }

        /// <summary>A director wired to <paramref name="config"/> and <paramref name="gate"/>.</summary>
        protected DialogueDirector NewDirector(DialogueConfig config, PlayerInputGate gate)
        {
            var director = NewHost("Director").AddComponent<DialogueDirector>();
            SetRef(director, "config", config);
            SetRef(director, "inputGate", gate);
            return director;
        }

        /// <summary>Writes a private serialized reference, failing loudly on a rename.</summary>
        protected static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} has no serialized field '{field}'.");
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Writes a private serialized float, failing loudly on a rename.</summary>
        protected static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} has no serialized field '{field}'.");
            property.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Writes a private serialized bool, failing loudly on a rename.</summary>
        protected static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} has no serialized field '{field}'.");
            property.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
