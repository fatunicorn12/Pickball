using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Validation
{
    /// <summary>
    /// Read-only checks on the assets the game ships with, as opposed to the code.
    ///
    /// Setup menus, YAML merges and hand edits can all leave an asset broken in a
    /// way no logic test notices: a script reference that no longer resolves, a
    /// value outside the range its Inspector would allow. These tests only load
    /// and read; they never open a scene or write an asset, so they are safe to
    /// run in the shared Editor.
    /// </summary>
    [Category("Assets")]
    public class ShippedAssetTests
    {
        const string ProjectRoot = "Assets/_Project";
        const string DataRoot = ProjectRoot + "/ScriptableObjects";
        const string EventsDir = DataRoot + "/Events";

        // ------------------------------------------------------------ channels

        [Ignore("Known bug PB1: the four channel classes share EventChannels.cs, so Unity " +
                "cannot bind these assets to a script. See Docs/Testing/QA-Assessment.md.")]
        [TestCase("BounceEvent", typeof(BallContactEventChannelSO))]
        [TestCase("BallDeadEvent", typeof(BallContactEventChannelSO))]
        [TestCase("BallHitEvent", typeof(CourtSideEventChannelSO))]
        [TestCase("GameStateEvent", typeof(GameStateEventChannelSO))]
        [TestCase("ScoreEvent", typeof(ScoreEventChannelSO))]
        public void EventChannelAsset_LoadsAsItsChannelType(string assetName, Type channelType)
        {
            string path = $"{EventsDir}/{assetName}.asset";
            var asset = AssetDatabase.LoadMainAssetAtPath(path);

            Assert.That(asset, Is.Not.Null,
                        $"{path} does not load. Its script reference is broken, so every " +
                        "component wired to it gets null.");
            Assert.That(asset, Is.InstanceOf(channelType));
        }

        // ------------------------------------------------------- script links

        /// <summary>
        /// Every MonoBehaviour in every scene and prefab points at a script that
        /// exists. Read from the YAML rather than by opening the scenes, so it
        /// never touches the scene the shared Editor has open.
        /// </summary>
        [Test]
        public void ScenesAndPrefabs_HaveNoMissingScripts()
        {
            var broken = new List<string>();
            int scanned = 0;
            foreach (string path in Files(ProjectRoot, "*.unity", "*.prefab"))
            {
                scanned++;
                broken.AddRange(MissingScripts(path));
            }

            Assert.That(scanned, Is.GreaterThan(0), $"No scenes or prefabs found under {ProjectRoot}.");

            Assert.That(broken, Is.Empty,
                        "Components whose script no longer resolves (path: reason):\n  " +
                        string.Join("\n  ", broken));
        }

        /// <summary>
        /// Every data asset resolves its script. The pickleball event channels are
        /// left to <see cref="EventChannelAsset_LoadsAsItsChannelType"/>, which
        /// tracks them as a known bug.
        /// </summary>
        [Test]
        public void DataAssets_ResolveTheirScripts()
        {
            // Enumerated from disk, not FindAssets: an asset whose script is gone
            // no longer reports a ScriptableObject type, so a type filter would
            // skip exactly the assets this test is looking for.
            var broken = new List<string>();
            foreach (string path in Files(DataRoot, "*.asset"))
            {
                if (path.StartsWith(EventsDir + "/", StringComparison.Ordinal)) continue;
                broken.AddRange(MissingScripts(path));
            }

            Assert.That(broken, Is.Empty,
                        "Data assets whose script no longer resolves:\n  " + string.Join("\n  ", broken));
        }

        // -------------------------------------------------------------- ranges

        /// <summary>
        /// Every <c>[Range]</c> and <c>[Min]</c> field on every data asset holds a
        /// value its Inspector would accept, and no float is NaN or infinite. The
        /// attributes only clamp in the Inspector; values written by a setup
        /// script, a YAML merge or code bypass them.
        /// </summary>
        [Test]
        public void DataAssets_RespectTheirRangeAndMinAttributes()
        {
            var problems = new List<string>();
            int checkedAssets = 0;
            foreach (string path in Files(DataRoot, "*.asset"))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                if (asset == null) continue;    // unloadable assets are DataAssets_ResolveTheirScripts' job
                checkedAssets++;
                CheckFields(asset, asset.GetType(), path, problems, depth: 0);
            }

            Assert.That(checkedAssets, Is.GreaterThan(0), $"No data assets found under {DataRoot}.");

            Assert.That(problems, Is.Empty,
                        "Serialized values outside their declared bounds:\n  " + string.Join("\n  ", problems));
        }

        // ------------------------------------------------------------- helpers

        /// <summary>Project-relative paths (forward slashes) of every file under
        /// <paramref name="folder"/> matching any of the patterns. The Editor's
        /// working directory is the project root.</summary>
        static IEnumerable<string> Files(string folder, params string[] patterns)
        {
            foreach (string pattern in patterns)
                foreach (string file in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
                    yield return file.Replace('\\', '/');
        }

        static readonly Regex ScriptRef = new(
            @"m_Script: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d)?\}", RegexOptions.Compiled);

        /// <summary>Script references in a YAML asset that point nowhere: an empty
        /// reference, or a GUID the AssetDatabase does not know.</summary>
        static IEnumerable<string> MissingScripts(string assetPath)
        {
            string text = File.ReadAllText(assetPath);
            if (!text.StartsWith("%YAML", StringComparison.Ordinal)) yield break;   // binary-serialized

            foreach (Match m in ScriptRef.Matches(text))
            {
                if (m.Groups[1].Value == "0")
                {
                    yield return $"{assetPath}: empty script reference";
                    continue;
                }
                string guid = m.Groups[2].Value;
                if (guid.Length > 0 && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                    yield return $"{assetPath}: unknown script GUID {guid}";
            }
        }

        const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static void CheckFields(object target, Type type, string where, List<string> problems, int depth)
        {
            if (target == null || depth > 6) return;

            for (var t = type; t != null && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(FieldFlags | BindingFlags.DeclaredOnly))
                {
                    if (!IsSerialized(field)) continue;
                    object value = field.GetValue(target);
                    string label = $"{where} → {field.Name}";

                    if (value is float f) CheckNumber(f, field, label, problems);
                    else if (value is int i) CheckNumber(i, field, label, problems);
                    else if (value is Vector3 v)
                    {
                        if (!IsFinite(v.x) || !IsFinite(v.y) || !IsFinite(v.z))
                            problems.Add($"{label} = {v} is not finite");
                    }
                    else if (value is IList list && !(value is UnityEngine.Object))
                    {
                        for (int k = 0; k < list.Count; k++)
                            if (IsNestedSerializable(list[k]))
                                CheckFields(list[k], list[k].GetType(), $"{label}[{k}]", problems, depth + 1);
                    }
                    else if (IsNestedSerializable(value))
                    {
                        CheckFields(value, value.GetType(), label, problems, depth + 1);
                    }
                }
            }
        }

        static void CheckNumber(float value, FieldInfo field, string label, List<string> problems)
        {
            if (!IsFinite(value))
            {
                problems.Add($"{label} = {value} is not finite");
                return;
            }
            var range = field.GetCustomAttribute<UnityEngine.RangeAttribute>();
            if (range != null && (value < range.min || value > range.max))
                problems.Add($"{label} = {value} is outside [Range({range.min}, {range.max})]");

            var min = field.GetCustomAttribute<MinAttribute>();
            if (min != null && value < min.min)
                problems.Add($"{label} = {value} is below [Min({min.min})]");
        }

        static bool IsSerialized(FieldInfo field)
        {
            if (field.IsNotSerialized || field.IsInitOnly) return false;
            return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
        }

        static bool IsNestedSerializable(object value)
        {
            if (value == null || value is UnityEngine.Object || value is string) return false;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum) return false;
            return type.GetCustomAttribute<SerializableAttribute>() != null
                   && (type.Namespace ?? "").StartsWith("Pickleball", StringComparison.Ordinal);
        }

        static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
