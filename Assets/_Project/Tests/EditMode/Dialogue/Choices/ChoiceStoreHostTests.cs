using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// <see cref="ChoiceStoreHost"/> in Edit mode, where no Awake runs: its
    /// store exists on first use, changes are saved once per flush however many
    /// there were, a load is not itself a change, New Game clears memory and
    /// disk, a save from a newer build is never overwritten, the store comes
    /// back after a script reload, and a save path inside Assets/ or a file
    /// name with folders is rejected with an error. Every host here saves to the
    /// fixture's temp folder or not at all.
    /// </summary>
    public class ChoiceStoreHostTests : ChoiceTestFixture
    {
        static void Invoke(ChoiceStoreHost host, string message, params object[] args)
        {
            MethodInfo method = typeof(ChoiceStoreHost).GetMethod(message, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"ChoiceStoreHost has no {message}.");
            method.Invoke(host, args.Length == 0 ? null : args);
        }

        static void SetPrivate(ChoiceStoreHost host, string field, object value)
        {
            FieldInfo info = typeof(ChoiceStoreHost).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, $"ChoiceStoreHost has no field {field}.");
            info.SetValue(host, value);
        }

        [Test]
        public void Store_ExistsOnFirstUse_WithoutAwake_AndStaysTheSame()
        {
            ChoiceStoreHost host = NewHost(persist: false);
            IChoiceStoreProvider provider = host;

            Assert.That(provider.Store, Is.Not.Null);
            Assert.That(provider.Store, Is.SameAs(host.Store));
        }

        [Test]
        public void PersistOff_NeverTouchesTheDisk()
        {
            ChoiceStoreHost host = NewHost(persist: false);
            host.Store.SetFact("a", FactValue.Of(1));
            host.FlushIfDirty();

            Assert.That(host.SaveNow(), Is.False);
            Assert.That(host.SavesWritten, Is.Zero);
            Assert.That(host.SavePath, Is.Null);
            Assert.That(TempFiles(), Is.Empty);
            Assert.That(host.Store.GetInt("a"), Is.EqualTo(1), "The store still works in memory.");
        }

        [Test]
        public void Changes_AreCoalesced_IntoOneSavePerFlush()
        {
            ChoiceStoreHost host = NewHost();
            IChoiceStore store = host.Store;
            Assert.That(host.LastLoad.Status, Is.EqualTo(ChoiceLoadStatus.Missing));
            Assert.That(host.SavePath, Is.EqualTo(Path.GetFullPath(SavePath)));
            Assert.That(host.IsDirty, Is.False);

            store.RecordChoice("birdy", "ask", "polite");
            store.SetFact("met_birdy", FactValue.Of(true));
            FactEffect.Add("rep", 2).Apply(store);

            Assert.That(host.IsDirty, Is.True);
            Assert.That(host.SavesWritten, Is.Zero, "Nothing is written until the frame's flush.");
            Assert.That(File.Exists(SavePath), Is.False);

            host.FlushIfDirty();
            Assert.That(host.SavesWritten, Is.EqualTo(1), "Three changes, one write.");
            Assert.That(host.IsDirty, Is.False);
            Assert.That(TempFiles(), Is.EqualTo(new[] { "choices.json" }));

            host.FlushIfDirty();
            Assert.That(host.SavesWritten, Is.EqualTo(1), "A clean flush writes nothing.");

            store.SetFact("met_birdy", FactValue.Of(true));
            Assert.That(host.IsDirty, Is.False, "Setting the same value is not a change.");

            store.ClearFact("met_birdy");
            host.FlushIfDirty();
            Assert.That(host.SavesWritten, Is.EqualTo(2));

            ChoiceStore check = NewStore();
            ChoiceStoreSave.Load(SavePath, check);
            Assert.That(check.HasChosen("birdy", "ask", "polite"), Is.True);
            Assert.That(check.GetInt("rep"), Is.EqualTo(2));
            Assert.That(check.TryGetFact("met_birdy", out _), Is.False);
        }

        [Test]
        public void TheSave_IsLoadedOnFirstUse_WithoutMarkingItDirty_AndPlayTimeCarriesOn()
        {
            ChoiceStore seed = NewStore();
            Now = 30;
            seed.RecordChoice("birdy", "ask", "polite");
            seed.SetFact("rep", FactValue.Of(4));
            Assert.That(ChoiceStoreSave.Save(SavePath, seed, 100, out string error), Is.True, error);

            ChoiceStoreHost host = NewHost();
            IChoiceStore store = host.Store;

            Assert.That(host.LastLoad.Status, Is.EqualTo(ChoiceLoadStatus.Loaded));
            Assert.That(host.IsDirty, Is.False);
            Assert.That(store.HasChosen("birdy", "ask", "polite"), Is.True);
            Assert.That(store.Choices[0].Time, Is.EqualTo(30).Within(1e-9));
            Assert.That(store.GetInt("rep"), Is.EqualTo(4));
            Assert.That(host.PlayTime, Is.GreaterThanOrEqualTo(100));

            store.RecordChoice("birdy", "ask", "rude");
            Assert.That(store.Choices[1].Time, Is.GreaterThanOrEqualTo(100), "New picks count on from the banked play time.");
        }

        [Test]
        public void LifecycleSaves_WriteEvenWhenClean_SoPlayTimeIsBanked_ButNotAgainOnDisableAfterQuit()
        {
            ChoiceStoreHost host = NewHost();
            host.Store.SetFact("rep", FactValue.Of(1));
            host.FlushIfDirty();
            Assert.That(host.SavesWritten, Is.EqualTo(1));

            Invoke(host, "OnApplicationPause", true);
            Assert.That(host.SavesWritten, Is.EqualTo(2));
            Invoke(host, "OnApplicationPause", false);
            Invoke(host, "OnDisable");
            Assert.That(host.SavesWritten, Is.EqualTo(3), "Disabled without quitting (a script reload): saves.");
            Invoke(host, "OnApplicationQuit");
            Assert.That(host.SavesWritten, Is.EqualTo(4));
            Invoke(host, "OnDisable");
            Assert.That(host.SavesWritten, Is.EqualTo(4), "The OnDisable after a quit does not save again (DLG-C-3).");
            Assert.That(TempFiles(), Is.EqualTo(new[] { "choices.json" }));
        }

        [Test]
        public void TheEndOfPlay_KeepsTheSessionsPlayTime_WhenUnityZeroesTheClockBeforeOnDisable()
        {
            ChoiceStore seed = NewStore();
            seed.SetFact("met_birdy", FactValue.Of(true));
            Assert.That(ChoiceStoreSave.Save(SavePath, seed, 100, out string error), Is.True, error);

            ChoiceStoreHost host = NewHost();
            _ = host.Store;
            double unscaled = 50;
            SetPrivate(host, "_clock", new PlayTimeClock(() => unscaled, 100));   // what the load banked
            unscaled = 53;   // three seconds of play

            Invoke(host, "OnApplicationQuit");
            unscaled = 0;    // Unity's reset before OnDisable at the end of Play (measured by QA)
            Invoke(host, "OnDisable");

            Assert.That(ChoiceStoreSave.Load(SavePath, NewStore()).PlayTime, Is.EqualTo(103).Within(1e-9));
            Assert.That(host.PlayTime, Is.EqualTo(103).Within(1e-9), "The clock is frozen at the quit.");
        }

        [Test]
        public void ASave_NeverBanksLessPlayTimeThanTheFileHolds()
        {
            ChoiceStore seed = NewStore();
            seed.SetFact("met_birdy", FactValue.Of(true));
            Assert.That(ChoiceStoreSave.Save(SavePath, seed, 100, out string error), Is.True, error);

            ChoiceStoreHost host = NewHost();
            _ = host.Store;
            SetPrivate(host, "_clock", new PlayTimeClock(() => 0, 10));   // a clock that somehow reads low

            Assert.That(host.SaveNow(), Is.True);
            Assert.That(ChoiceStoreSave.Load(SavePath, NewStore()).PlayTime, Is.EqualTo(100).Within(1e-9));
        }

        [Test]
        public void PlayingWithoutPickingAnything_NeverCreatesASave()
        {
            ChoiceStoreHost host = NewHost();
            _ = host.Store;

            Invoke(host, "OnApplicationPause", true);
            Invoke(host, "OnApplicationPause", false);
            host.FlushIfDirty();
            Assert.That(host.SaveNow(), Is.False);
            Invoke(host, "OnApplicationQuit");
            Invoke(host, "OnDisable");

            Assert.That(host.SavesWritten, Is.Zero);
            Assert.That(TempFiles(), Is.Empty, "Play time alone does not create a file (DLG-C-4).");
        }

        [Test]
        public void TheFirstPickOrFact_CreatesTheSave()
        {
            ChoiceStoreHost host = NewHost();
            host.Store.RecordChoice("birdy", "ask", "polite");
            host.FlushIfDirty();
            Assert.That(File.Exists(SavePath), Is.True);
        }

        [Test]
        public void AnEmptiedStore_StillOverwritesItsExistingSave_SoAClearedFactStaysCleared()
        {
            ChoiceStoreHost host = NewHost();
            host.Store.SetFact("quest_started", FactValue.Of(true));
            host.FlushIfDirty();
            Assert.That(host.SavesWritten, Is.EqualTo(1));

            host.Store.ClearFact("quest_started");
            host.FlushIfDirty();

            Assert.That(host.SavesWritten, Is.EqualTo(2));
            ChoiceStore check = NewStore();
            Assert.That(ChoiceStoreSave.Load(SavePath, check).Status, Is.EqualTo(ChoiceLoadStatus.Loaded));
            Assert.That(check.TryGetFact("quest_started", out _), Is.False);
        }

        [Test]
        public void OnDisable_BeforeTheStoreExists_WritesNothing()
        {
            ChoiceStoreHost host = NewHost();
            Invoke(host, "OnDisable");
            Assert.That(host.SavesWritten, Is.Zero);
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void AfterAScriptReload_TheStoreComesBackFromTheSave()
        {
            ChoiceStoreHost host = NewHost();
            IChoiceStore before = host.Store;
            before.SetFact("rep", FactValue.Of(9));
            before.RecordChoice("birdy", "ask", "polite");

            // What a domain reload in Play mode does: OnDisable, every
            // non-serialized object dropped, then OnEnable (no Awake).
            Invoke(host, "OnDisable");
            Assert.That(host.SavesWritten, Is.EqualTo(1), "OnDisable saved before the reload.");
            SetPrivate(host, "_store", null);
            SetPrivate(host, "_clock", null);
            Invoke(host, "OnEnable");

            Assert.That(host.Store, Is.Not.SameAs(before));
            Assert.That(host.Store.GetInt("rep"), Is.EqualTo(9));
            Assert.That(host.Store.HasChosen("birdy", "ask", "polite"), Is.True);
            Assert.That(host.IsDirty, Is.False);
        }

        [Test]
        public void NewGame_ClearsMemoryAndDisk()
        {
            ChoiceStoreHost host = NewHost();
            host.Store.SetFact("rep", FactValue.Of(3));
            host.Store.RecordChoice("birdy", "ask", "polite");
            host.FlushIfDirty();
            Assert.That(File.Exists(SavePath), Is.True);
            int resets = 0;
            host.Store.Reset += () => resets++;

            host.NewGame();

            Assert.That(host.Store.Facts, Is.Empty);
            Assert.That(host.Store.Choices, Is.Empty);
            Assert.That(resets, Is.EqualTo(1));
            Assert.That(File.Exists(SavePath), Is.False);
            Assert.That(host.IsDirty, Is.False, "The cleared state is not written back as a save.");
            Assert.That(host.PlayTime, Is.LessThan(5), "Play time restarts at 0.");

            host.FlushIfDirty();
            Assert.That(TempFiles(), Is.Empty);
        }

        [Test]
        public void UseSaveDirectory_OnALiveStore_ReloadsFromTheNewFolder()
        {
            string other = Path.Combine(TempDir, "other");
            ChoiceStore seed = NewStore();
            seed.SetFact("x", FactValue.Of(7));
            Assert.That(ChoiceStoreSave.Save(Path.Combine(other, "choices.json"), seed, 0, out string error), Is.True, error);

            ChoiceStoreHost host = NewHost();
            IChoiceStore store = host.Store;
            store.SetFact("a", FactValue.Of(1));
            host.FlushIfDirty();
            int resets = 0;
            store.Reset += () => resets++;

            host.UseSaveDirectory(other);

            Assert.That(host.Store, Is.SameAs(store));
            Assert.That(store.GetInt("x"), Is.EqualTo(7));
            Assert.That(store.TryGetFact("a", out _), Is.False);
            Assert.That(resets, Is.EqualTo(1));
            Assert.That(host.IsDirty, Is.False);
            Assert.That(host.SavePath, Is.EqualTo(Path.GetFullPath(Path.Combine(other, "choices.json"))));
        }

        [Test]
        public void ACorruptSave_IsMovedAside_AndTheHostStartsEmptyAndKeepsSaving()
        {
            File.WriteAllText(SavePath, "garbage");
            ChoiceStoreHost host = NewHost();
            IChoiceStore store = host.Store;

            Assert.That(host.LastLoad.Status, Is.EqualTo(ChoiceLoadStatus.Corrupt));
            Assert.That(host.SavingBlocked, Is.False);
            Assert.That(store.Facts, Is.Empty);
            Assert.That(Directory.GetFiles(TempDir, "choices.corrupt-*.json").Length, Is.EqualTo(1));

            store.SetFact("a", FactValue.Of(1));
            host.FlushIfDirty();
            Assert.That(host.SavesWritten, Is.EqualTo(1));
            Assert.That(File.Exists(SavePath), Is.True);
        }

        [Test]
        public void ASaveFromANewerBuild_IsNeverOverwritten_UntilNewGame()
        {
            const string newer = "{\"version\": 99, \"playTime\": 5}";
            File.WriteAllText(SavePath, newer);
            LogAssert.Expect(LogType.Error, new Regex("newer than this build"));

            ChoiceStoreHost host = NewHost();
            IChoiceStore store = host.Store;

            Assert.That(host.LastLoad.Status, Is.EqualTo(ChoiceLoadStatus.TooNew));
            Assert.That(host.SavingBlocked, Is.True);
            store.SetFact("a", FactValue.Of(1));
            host.FlushIfDirty();
            Assert.That(host.SaveNow(), Is.False);
            Invoke(host, "OnDisable");
            Assert.That(host.SavesWritten, Is.Zero);
            Assert.That(File.ReadAllText(SavePath), Is.EqualTo(newer));

            host.NewGame();
            Assert.That(host.SavingBlocked, Is.False, "New Game is the explicit way past a refused save.");
            Assert.That(File.Exists(SavePath), Is.False);

            store.SetFact("b", FactValue.Of(1));
            host.FlushIfDirty();
            Assert.That(host.SavesWritten, Is.EqualTo(1));
            Assert.That(JsonUtility.FromJson<ChoiceSaveData>(File.ReadAllText(SavePath)).version, Is.EqualTo(1));
        }

        [Test]
        public void ASaveFolderInsideAssets_IsRejectedWithAnError_AndNothingIsWritten()
        {
            string folder = Path.Combine(Application.dataPath, "__ChoiceStoreGuardTest__");
            try
            {
                ChoiceStoreHost host = NewHost();
                host.UseSaveDirectory(folder);
                LogAssert.Expect(LogType.Error, new Regex("Assets folder"));

                IChoiceStore store = host.Store;

                Assert.That(host.SavingBlocked, Is.True);
                Assert.That(host.SavePath, Is.Null);
                Assert.That(host.LastLoad.Status, Is.EqualTo(ChoiceLoadStatus.Failed));
                store.SetFact("a", FactValue.Of(1));
                host.FlushIfDirty();
                Assert.That(host.SaveNow(), Is.False);
                Invoke(host, "OnDisable");
                Assert.That(host.SavesWritten, Is.Zero);
                Assert.That(Directory.Exists(folder), Is.False);
                Assert.That(store.GetInt("a"), Is.EqualTo(1), "The store still works in memory.");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                if (File.Exists(folder + ".meta")) File.Delete(folder + ".meta");
            }
        }

        [TestCase("../choices.json")]
        [TestCase("sub/choices.json")]
        [TestCase("")]
        public void AFileNameWithFolders_IsRejectedWithAnError(string fileName)
        {
            LogAssert.Expect(LogType.Error, new Regex("save file name"));
            ChoiceStoreHost host = NewHost(fileName: fileName);

            IChoiceStore store = host.Store;
            store.SetFact("a", FactValue.Of(1));
            host.FlushIfDirty();

            Assert.That(host.SavingBlocked, Is.True);
            Assert.That(host.SavesWritten, Is.Zero);
            Assert.That(TempFiles(), Is.Empty);
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(TempDir), "choices.json")), Is.False);
        }
    }
}
