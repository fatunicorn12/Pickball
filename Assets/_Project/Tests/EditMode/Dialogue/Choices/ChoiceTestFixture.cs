using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Shared scaffolding for the choice-store suites: a fresh temp folder per
    /// test (deleted in TearDown), so no test ever reads or writes the real
    /// save under <c>Application.persistentDataPath</c> or anything in the
    /// project; a fake clock; DontSave hosts that are pointed at the temp folder
    /// before their store is first touched.
    /// </summary>
    public abstract class ChoiceTestFixture
    {
        readonly List<Object> _spawned = new();

        /// <summary>This test's own empty folder under the system temp path.</summary>
        protected string TempDir { get; private set; }

        /// <summary><see cref="TempDir"/>/choices.json.</summary>
        protected string SavePath => Path.Combine(TempDir, ChoiceStoreSave.DefaultFileName);

        /// <summary>The fake clock's reading; stores built with <see cref="NewStore"/> read it.</summary>
        protected double Now { get; set; }

        [SetUp]
        public void CreateTempDir()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "PickballChoiceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Now = 0;
        }

        [TearDown]
        public void DeleteTempDirAndHosts()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();

            if (!string.IsNullOrEmpty(TempDir) && Directory.Exists(TempDir))
                Directory.Delete(TempDir, true);
        }

        /// <summary>A store on the fake clock, collecting warnings into <paramref name="warnings"/> if given.</summary>
        protected ChoiceStore NewStore(List<string> warnings = null, Action<Exception> listenerError = null)
            => new(() => Now, warnings != null ? new Action<string>(warnings.Add) : null, listenerError);

        /// <summary>
        /// A DontSave host whose save folder is <see cref="TempDir"/> (or, with
        /// <paramref name="persist"/> false, no disk at all). Edit mode runs no
        /// Awake, so its store is built on first use, after the redirect.
        /// </summary>
        protected ChoiceStoreHost NewHost(bool persist = true, string fileName = null)
        {
            var go = new GameObject("Test_ChoiceStoreHost") { hideFlags = HideFlags.DontSave };
            _spawned.Add(go);
            var host = go.AddComponent<ChoiceStoreHost>();

            var so = new SerializedObject(host);
            SerializedProperty persistProperty = so.FindProperty("persist");
            Assert.That(persistProperty, Is.Not.Null, "ChoiceStoreHost has no serialized field 'persist'.");
            persistProperty.boolValue = persist;
            if (fileName != null)
            {
                SerializedProperty nameProperty = so.FindProperty("fileName");
                Assert.That(nameProperty, Is.Not.Null, "ChoiceStoreHost has no serialized field 'fileName'.");
                nameProperty.stringValue = fileName;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            host.UseSaveDirectory(TempDir);
            return host;
        }

        /// <summary>Every file in <see cref="TempDir"/>, by name, sorted.</summary>
        protected string[] TempFiles()
        {
            string[] files = Directory.GetFiles(TempDir);
            for (int i = 0; i < files.Length; i++) files[i] = Path.GetFileName(files[i]);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
    }
}
