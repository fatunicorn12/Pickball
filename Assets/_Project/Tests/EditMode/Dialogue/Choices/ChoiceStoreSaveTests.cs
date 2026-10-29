using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// <see cref="ChoiceStoreSave"/> in a per-test temp folder: the round trip,
    /// atomic writes with no temp file left behind, a missing file as a new
    /// game, a corrupt file moved aside, a newer version refused and left
    /// untouched, new game clearing disk and memory, a load raising one Reset,
    /// and the guard that keeps every write out of the project's Assets folder.
    /// </summary>
    public class ChoiceStoreSaveTests : ChoiceTestFixture
    {
        const string NewerVersionJson = "{\"version\": 2, \"playTime\": 5.0, \"choices\": [], \"facts\": [], \"future\": true}";

        ChoiceStore FilledStore()
        {
            var store = NewStore();
            Now = 1.25;
            store.RecordChoice("birdy", "ask", "polite");
            Now = 9.5;
            store.RecordChoice("birdy", "ask", "rude");
            store.RecordChoice("newton", "apple", "catch");
            store.SetFact("met_birdy", FactValue.Of(true));
            store.SetFact("rep", FactValue.Of(-3));
            store.SetFact("nickname", FactValue.Of("Bïrdy \"the\" bird\n"));
            return store;
        }

        static void AssertSameState(IChoiceStore expected, IChoiceStore actual)
        {
            Assert.That(actual.Choices.Count, Is.EqualTo(expected.Choices.Count));
            for (int i = 0; i < expected.Choices.Count; i++)
            {
                Assert.That(actual.Choices[i].DialogueId, Is.EqualTo(expected.Choices[i].DialogueId));
                Assert.That(actual.Choices[i].NodeId, Is.EqualTo(expected.Choices[i].NodeId));
                Assert.That(actual.Choices[i].ChoiceId, Is.EqualTo(expected.Choices[i].ChoiceId));
                Assert.That(actual.Choices[i].Time, Is.EqualTo(expected.Choices[i].Time).Within(1e-9));
            }

            Assert.That(actual.Facts.Count, Is.EqualTo(expected.Facts.Count));
            foreach (KeyValuePair<string, FactValue> fact in expected.Facts)
            {
                Assert.That(actual.TryGetFact(fact.Key, out FactValue value), Is.True, fact.Key);
                Assert.That(value, Is.EqualTo(fact.Value), fact.Key);
            }
        }

        // ---- round trip ----

        [Test]
        public void SaveThenLoad_RoundTripsTheLogFactsAndPlayTime()
        {
            ChoiceStore original = FilledStore();
            Assert.That(ChoiceStoreSave.Save(SavePath, original, 42.75, out string error), Is.True, error);

            ChoiceStore loaded = NewStore();
            ChoiceLoadResult result = ChoiceStoreSave.Load(SavePath, loaded);

            Assert.That(result.Status, Is.EqualTo(ChoiceLoadStatus.Loaded), result.ToString());
            Assert.That(result.FileVersion, Is.EqualTo(ChoiceStoreSave.CurrentVersion));
            Assert.That(result.PlayTime, Is.EqualTo(42.75).Within(1e-9));
            Assert.That(result.SafeToSave, Is.True);
            AssertSameState(original, loaded);
            Assert.That(loaded.LastChoice("birdy", "ask"), Is.EqualTo("rude"));
        }

        [Test]
        public void TheFile_IsVersionedJson_WithFactsInKeyOrder()
        {
            Assert.That(ChoiceStoreSave.Save(SavePath, FilledStore(), 1, out string error), Is.True, error);
            string json = File.ReadAllText(SavePath);

            var data = JsonUtility.FromJson<ChoiceSaveData>(json);
            Assert.That(data.version, Is.EqualTo(1));
            Assert.That(json, Does.Contain("\"version\""));
            Assert.That(data.choices.Length, Is.EqualTo(3));
            Assert.That(data.facts.Length, Is.EqualTo(3));
            Assert.That(data.facts[0].key, Is.EqualTo("met_birdy"));
            Assert.That(data.facts[1].key, Is.EqualTo("nickname"));
            Assert.That(data.facts[2].key, Is.EqualTo("rep"));
        }

        [Test]
        public void Load_OverALiveStore_RaisesResetOnce_AndNoFactChanged()
        {
            Assert.That(ChoiceStoreSave.Save(SavePath, FilledStore(), 0, out string error), Is.True, error);

            ChoiceStore live = NewStore();
            live.SetFact("stale", FactValue.Of(1));
            live.RecordChoice("stale", "n", "c");
            int resets = 0, facts = 0, picks = 0;
            live.Reset += () => resets++;
            live.FactChanged += _ => facts++;
            live.ChoiceRecorded += _ => picks++;

            ChoiceStoreSave.Load(SavePath, live);

            Assert.That(resets, Is.EqualTo(1));
            Assert.That(facts, Is.Zero);
            Assert.That(picks, Is.Zero);
            Assert.That(live.TryGetFact("stale", out _), Is.False, "A load replaces, it does not merge.");
            Assert.That(live.HasChosen("stale", "n"), Is.False);
            Assert.That(live.GetInt("rep"), Is.EqualTo(-3));
        }

        [Test]
        public void AMinimalFile_LoadsAsEmpty()
        {
            File.WriteAllText(SavePath, "{\"version\": 1}");
            ChoiceStore store = NewStore();
            ChoiceLoadResult result = ChoiceStoreSave.Load(SavePath, store);
            Assert.That(result.Status, Is.EqualTo(ChoiceLoadStatus.Loaded));
            Assert.That(result.PlayTime, Is.Zero);
            Assert.That(store.Choices, Is.Empty);
            Assert.That(store.Facts, Is.Empty);
        }

        [Test]
        public void HandEditedEntriesWithEmptyIds_AreSkipped()
        {
            File.WriteAllText(SavePath,
                "{\"version\":1,\"choices\":[" +
                "{\"dialogueId\":\"d\",\"nodeId\":\"n\",\"choiceId\":\"c\",\"time\":1}," +
                "{\"dialogueId\":\"d\",\"nodeId\":\"n\",\"choiceId\":\"\",\"time\":2}]," +
                "\"facts\":[{\"key\":\"\",\"value\":{\"type\":1,\"intValue\":3}}," +
                "{\"key\":\"rep\",\"value\":{\"type\":1,\"intValue\":3}}]}");
            var warnings = new List<string>();
            ChoiceStore store = NewStore(warnings);

            Assert.That(ChoiceStoreSave.Load(SavePath, store).Status, Is.EqualTo(ChoiceLoadStatus.Loaded));

            Assert.That(store.Choices.Count, Is.EqualTo(1));
            Assert.That(store.GetInt("rep"), Is.EqualTo(3));
            Assert.That(store.Facts.Count, Is.EqualTo(1));
            Assert.That(warnings.Count, Is.EqualTo(1));
        }

        [TestCase(double.NaN)]
        [TestCase(-5.0)]
        [TestCase(double.PositiveInfinity)]
        public void ABadPlayTime_IsSavedAsZero(double playTime)
        {
            Assert.That(ChoiceStoreSave.Save(SavePath, NewStore(), playTime, out string error), Is.True, error);
            Assert.That(ChoiceStoreSave.Load(SavePath, NewStore()).PlayTime, Is.Zero);
        }

        // ---- atomic writes ----

        [Test]
        public void Save_LeavesNoTempFileBehind_OnCreateOrReplace()
        {
            ChoiceStore store = FilledStore();
            Assert.That(ChoiceStoreSave.Save(SavePath, store, 1, out string error), Is.True, error);
            Assert.That(TempFiles(), Is.EqualTo(new[] { "choices.json" }));

            store.SetFact("rep", FactValue.Of(10));
            Assert.That(ChoiceStoreSave.Save(SavePath, store, 2, out error), Is.True, error);
            Assert.That(TempFiles(), Is.EqualTo(new[] { "choices.json" }), "The replace leaves no .tmp either.");

            ChoiceStore loaded = NewStore();
            ChoiceLoadResult result = ChoiceStoreSave.Load(SavePath, loaded);
            Assert.That(loaded.GetInt("rep"), Is.EqualTo(10), "The second save replaced the first.");
            Assert.That(result.PlayTime, Is.EqualTo(2).Within(1e-9));
        }

        [Test]
        public void Load_DeletesAStrayTempFile_AndKeepsTheLastCompleteSave()
        {
            Assert.That(ChoiceStoreSave.Save(SavePath, FilledStore(), 7, out string error), Is.True, error);
            File.WriteAllText(ChoiceStoreSave.TempPathFor(SavePath), "{\"version\": 1, \"choi");

            ChoiceStore loaded = NewStore();
            ChoiceLoadResult result = ChoiceStoreSave.Load(SavePath, loaded);

            Assert.That(result.Status, Is.EqualTo(ChoiceLoadStatus.Loaded));
            Assert.That(loaded.GetInt("rep"), Is.EqualTo(-3));
            Assert.That(TempFiles(), Is.EqualTo(new[] { "choices.json" }));
        }

        [Test]
        public void Save_CreatesMissingFolders()
        {
            string path = Path.Combine(TempDir, "a", "b", "choices.json");
            Assert.That(ChoiceStoreSave.Save(path, FilledStore(), 0, out string error), Is.True, error);
            Assert.That(File.Exists(path), Is.True);
        }

        // ---- missing, corrupt, newer ----

        [Test]
        public void AMissingFile_IsAnEmptyStore_AndEmptiesALiveOne()
        {
            ChoiceStore store = NewStore();
            store.SetFact("stale", FactValue.Of(1));
            int resets = 0;
            store.Reset += () => resets++;

            ChoiceLoadResult result = ChoiceStoreSave.Load(SavePath, store);

            Assert.That(result.Status, Is.EqualTo(ChoiceLoadStatus.Missing));
            Assert.That(result.SafeToSave, Is.True);
            Assert.That(result.PlayTime, Is.Zero);
            Assert.That(store.Facts, Is.Empty);
            Assert.That(resets, Is.EqualTo(1));
            Assert.That(TempFiles(), Is.Empty, "Loading a missing file creates nothing.");
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json at all")]
        [TestCase("null")]
        [TestCase("[1, 2]")]
        [TestCase("{\"foo\": 1}")]
        [TestCase("{\"version\": 0}")]
        [TestCase("{\"version\": 1, \"choices\": [")]
        public void ACorruptFile_IsRenamedAside_AndTheStoreStartsEmpty(string text)
        {
            File.WriteAllText(SavePath, text);
            ChoiceStore store = NewStore();
            store.SetFact("stale", FactValue.Of(1));

            ChoiceLoadResult result = ChoiceStoreSave.Load(SavePath, store);

            Assert.That(result.Status, Is.EqualTo(ChoiceLoadStatus.Corrupt), result.ToString());
            Assert.That(result.SafeToSave, Is.True, "The bad file is out of the way, so saving fresh is safe.");
            Assert.That(store.Facts, Is.Empty);
            Assert.That(store.Choices, Is.Empty);
            Assert.That(File.Exists(SavePath), Is.False);

            string[] corrupt = Directory.GetFiles(TempDir, "choices.corrupt-*.json");
            Assert.That(corrupt.Length, Is.EqualTo(1));
            Assert.That(result.QuarantinedPath, Is.EqualTo(corrupt[0]));
            Assert.That(File.ReadAllText(corrupt[0]), Is.EqualTo(text), "The bad file is kept, byte for byte.");
            Assert.That(result.Message, Does.Contain("corrupt"));
        }

        [Test]
        public void AFileFromANewerBuild_IsRefused_AndLeftUntouched()
        {
            File.WriteAllText(SavePath, NewerVersionJson);
            ChoiceStore store = NewStore();
            store.SetFact("stale", FactValue.Of(1));

            ChoiceLoadResult result = ChoiceStoreSave.Load(SavePath, store);

            Assert.That(result.Status, Is.EqualTo(ChoiceLoadStatus.TooNew));
            Assert.That(result.FileVersion, Is.EqualTo(2));
            Assert.That(result.SafeToSave, Is.False);
            Assert.That(store.Facts, Is.Empty);
            Assert.That(File.ReadAllText(SavePath), Is.EqualTo(NewerVersionJson));
            Assert.That(TempFiles(), Is.EqualTo(new[] { "choices.json" }), "Not renamed, not quarantined.");
        }

        [Test]
        public void CorruptPathFor_StampsTheTime_AndAvoidsCollisions()
        {
            var when = new DateTime(2026, 10, 28, 13, 5, 9);
            string first = ChoiceStoreSave.CorruptPathFor(SavePath, when);
            Assert.That(first, Is.EqualTo(Path.Combine(TempDir, "choices.corrupt-20261028-130509.json")));

            File.WriteAllText(first, "x");
            Assert.That(ChoiceStoreSave.CorruptPathFor(SavePath, when),
                Is.EqualTo(Path.Combine(TempDir, "choices.corrupt-20261028-130509-2.json")));
        }

        // ---- new game and delete ----

        [Test]
        public void NewGame_ClearsTheStoreAndDeletesTheSave()
        {
            ChoiceStore store = FilledStore();
            Assert.That(ChoiceStoreSave.Save(SavePath, store, 3, out string error), Is.True, error);
            File.WriteAllText(ChoiceStoreSave.TempPathFor(SavePath), "stray");
            int resets = 0;
            store.Reset += () => resets++;

            Assert.That(ChoiceStoreSave.NewGame(SavePath, store, out error), Is.True, error);

            Assert.That(store.Choices, Is.Empty);
            Assert.That(store.Facts, Is.Empty);
            Assert.That(resets, Is.EqualTo(1));
            Assert.That(TempFiles(), Is.Empty);

            ChoiceStore reloaded = NewStore();
            Assert.That(ChoiceStoreSave.Load(SavePath, reloaded).Status, Is.EqualTo(ChoiceLoadStatus.Missing));
            Assert.That(reloaded.Facts, Is.Empty);
        }

        [Test]
        public void Delete_AMissingFile_Succeeds()
        {
            Assert.That(ChoiceStoreSave.Delete(SavePath, out string error), Is.True, error);
        }

        // ---- paths and the Assets guard ----

        [Test]
        public void TryResolvePath_CombinesAPlainFileName()
        {
            Assert.That(ChoiceStoreSave.TryResolvePath(TempDir, "slot2.json", Application.dataPath, out string path, out string error),
                Is.True, error);
            Assert.That(path, Is.EqualTo(Path.GetFullPath(Path.Combine(TempDir, "slot2.json"))));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("../choices.json")]
        [TestCase("sub/choices.json")]
        [TestCase("sub\\choices.json")]
        [TestCase("C:\\choices.json")]
        [TestCase("/choices.json")]
        public void TryResolvePath_RejectsAnythingButAPlainFileName(string fileName)
        {
            Assert.That(ChoiceStoreSave.TryResolvePath(TempDir, fileName, Application.dataPath, out string path, out string error),
                Is.False);
            Assert.That(path, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void TryResolvePath_RejectsAFolderInsideAssets()
        {
            Assert.That(ChoiceStoreSave.TryResolvePath(Application.dataPath, "choices.json", Application.dataPath, out _, out string error),
                Is.False);
            Assert.That(error, Does.Contain("Assets"));
            Assert.That(ChoiceStoreSave.TryResolvePath(Path.Combine(Application.dataPath, "Saves"), "choices.json",
                Application.dataPath, out _, out _), Is.False);
            Assert.That(ChoiceStoreSave.TryResolvePath("", "choices.json", Application.dataPath, out _, out _), Is.False);
        }

        [Test]
        public void IsInside_NormalizesAndIgnoresCase_ButNotSiblingPrefixes()
        {
            string assets = Path.Combine(TempDir, "Proj", "Assets");

            Assert.That(ChoiceStoreSave.IsInside(Path.Combine(assets, "x.json"), assets), Is.True);
            Assert.That(ChoiceStoreSave.IsInside(assets, assets + Path.DirectorySeparatorChar), Is.True);
            Assert.That(ChoiceStoreSave.IsInside(Path.Combine(assets, "Sub", "x.json").ToUpperInvariant(), assets), Is.True);
            Assert.That(ChoiceStoreSave.IsInside(Path.Combine(assets, "..", "Assets", "x.json"), assets), Is.True);
            Assert.That(ChoiceStoreSave.IsInside(Path.Combine(TempDir, "Proj", "AssetsBackup", "x.json"), assets), Is.False);
            Assert.That(ChoiceStoreSave.IsInside(Path.Combine(assets, "..", "x.json"), assets), Is.False);
            Assert.That(ChoiceStoreSave.IsInside(null, assets), Is.False);
            Assert.That(ChoiceStoreSave.IsInside(assets, ""), Is.False);
        }

        [Test]
        public void EveryWrite_RefusesAPathInsideAssets()
        {
            string folder = Path.Combine(Application.dataPath, "__ChoiceStoreGuardTest__");
            string path = Path.Combine(folder, "choices.json");
            try
            {
                ChoiceStore store = FilledStore();

                Assert.That(ChoiceStoreSave.Save(path, store, 0, out string error), Is.False);
                Assert.That(error, Does.Contain("Assets"));
                Assert.That(Directory.Exists(folder), Is.False, "Not even the folder was created.");

                ChoiceLoadResult result = ChoiceStoreSave.Load(path, store);
                Assert.That(result.Status, Is.EqualTo(ChoiceLoadStatus.Failed));
                Assert.That(result.SafeToSave, Is.False);
                Assert.That(store.Facts, Is.Empty, "A refused load still leaves the store consistent (empty).");

                Assert.That(ChoiceStoreSave.Delete(path, out _), Is.False);
                Assert.That(ChoiceStoreSave.NewGame(path, store, out _), Is.False);
                Assert.That(Directory.Exists(folder), Is.False);
            }
            finally
            {
                // Only reached if the guard failed; keep the project clean regardless.
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                if (File.Exists(folder + ".meta")) File.Delete(folder + ".meta");
            }
        }
    }
}
