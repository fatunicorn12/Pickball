using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Reads and writes a <see cref="ChoiceStore"/> as versioned JSON
    /// (<see cref="ChoiceSaveData"/>). Static and stateless: the caller passes
    /// the path, so tests use a temp folder and never the real save.
    ///
    /// <b>Where.</b> <see cref="DefaultDirectory"/> is
    /// <c>&lt;Application.persistentDataPath&gt;/Saves/</c>; the file is
    /// <see cref="DefaultFileName"/> unless the host names another. Every write
    /// (save, delete, quarantine) refuses a path inside <c>Application.dataPath</c>
    /// (the project's Assets/ folder in the Editor), so a save can never land in
    /// the repository.
    ///
    /// <b>Atomic writes.</b> <see cref="Save"/> writes <c>&lt;file&gt;.tmp</c>,
    /// flushes it to disk, then swaps it over the old file with
    /// <c>File.Replace</c>. A crash mid-save leaves the previous file whole; the
    /// stray temp file is deleted by the next load.
    ///
    /// <b>Loading</b> (<see cref="Load"/>) always leaves the store consistent
    /// and raises its <see cref="IChoiceStore.Reset"/> exactly once, with no
    /// per-fact <see cref="IChoiceStore.FactChanged"/>:
    /// <list type="bullet">
    /// <item>missing file: empty store (a new game);</item>
    /// <item>unparsable file, or one with no version: renamed to
    /// <c>&lt;name&gt;.corrupt-&lt;timestamp&gt;.json</c>, empty store;</item>
    /// <item>older version: migrated in memory through <see cref="TryMigrate"/>;</item>
    /// <item>newer version than <see cref="CurrentVersion"/>: refused, file left
    /// untouched, empty store, and <see cref="ChoiceLoadResult.SafeToSave"/> false
    /// so the caller does not overwrite it.</item>
    /// </list>
    /// Nothing here logs; results carry messages and the host logs them.
    /// </summary>
    public static class ChoiceStoreSave
    {
        /// <summary>The format this build writes and the newest it reads.</summary>
        public const int CurrentVersion = 1;

        /// <summary>The save's file name unless the host is given another.</summary>
        public const string DefaultFileName = "choices.json";

        /// <summary>The folder under <c>Application.persistentDataPath</c> that holds saves.</summary>
        public const string SaveFolderName = "Saves";

        const string TempSuffix = ".tmp";
        const string CorruptInfix = ".corrupt-";

        /// <summary><c>&lt;Application.persistentDataPath&gt;/Saves</c>: outside the
        /// project in the Editor, the per-user app data folder on device.</summary>
        public static string DefaultDirectory => Path.Combine(Application.persistentDataPath, SaveFolderName);

        /// <summary>The temp file a save writes before swapping it in.</summary>
        public static string TempPathFor(string path) => path + TempSuffix;

        /// <summary>
        /// Combines <paramref name="directory"/> and <paramref name="fileName"/>
        /// into a full path, or explains why not: the name must be a plain file
        /// name (no folders, no "..", no rooted path, no invalid characters) and
        /// the result must not be inside <paramref name="forbiddenDirectory"/>
        /// (the host passes <c>Application.dataPath</c>, the Assets folder).
        /// </summary>
        public static bool TryResolvePath(string directory, string fileName, string forbiddenDirectory,
            out string path, out string error)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "the save directory is empty";
                return false;
            }
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "the save file name is empty";
                return false;
            }
            if (fileName == "." || fileName == ".." || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0 || Path.IsPathRooted(fileName))
            {
                error = $"the save file name '{fileName}' must be a plain file name, with no folders";
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(directory, fileName));
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                error = $"the save path '{directory}' + '{fileName}' is invalid ({e.Message})";
                return false;
            }

            if (!string.IsNullOrEmpty(forbiddenDirectory) && IsInside(full, forbiddenDirectory))
            {
                error = $"the save path '{full}' is inside '{forbiddenDirectory}'; saves must never be written into the project's Assets folder";
                return false;
            }

            path = full;
            error = null;
            return true;
        }

        /// <summary>Whether <paramref name="path"/> is <paramref name="directory"/>
        /// or anywhere below it. Case-insensitive on every platform, so it errs
        /// toward "inside".</summary>
        public static bool IsInside(string path, string directory)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory)) return false;
            string p, d;
            try
            {
                p = TrimSeparators(Path.GetFullPath(path));
                d = TrimSeparators(Path.GetFullPath(directory));
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return false;
            }
            return p.Equals(d, StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith(d + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        static string TrimSeparators(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>The store as save-file JSON at <see cref="CurrentVersion"/>,
        /// facts in ordinal key order.</summary>
        public static string ToJson(IChoiceStore store, double playTime)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            var choices = new ChoiceRecord[store.Choices.Count];
            for (int i = 0; i < choices.Length; i++) choices[i] = store.Choices[i];

            var sorted = new List<KeyValuePair<string, FactValue>>(store.Facts);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            var facts = new ChoiceSaveData.FactEntry[sorted.Count];
            for (int i = 0; i < facts.Length; i++) facts[i] = new ChoiceSaveData.FactEntry(sorted[i].Key, sorted[i].Value);

            var data = new ChoiceSaveData
            {
                version = CurrentVersion,
                playTime = Sanitize(playTime),
                choices = choices,
                facts = facts,
            };
            return JsonUtility.ToJson(data, true);
        }

        /// <summary>
        /// Writes <paramref name="store"/> and <paramref name="playTime"/> to
        /// <paramref name="path"/> atomically, creating the folder if needed.
        /// Returns false with <paramref name="error"/> set on any I/O failure (the
        /// old file, if any, is left whole) or a path inside Assets/.
        /// </summary>
        public static bool Save(string path, IChoiceStore store, double playTime, out string error)
        {
            if (store == null)
            {
                error = "no store to save";
                return false;
            }
            if (!CheckWritable(path, out error)) return false;

            string json = ToJson(store, playTime);
            string temp = TempPathFor(path);
            try
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }

                SwapIn(temp, path);
                error = null;
                return true;
            }
            catch (Exception e) when (IsFileError(e))
            {
                TryDelete(temp);
                error = $"could not write the choice save '{path}': {e.Message}";
                return false;
            }
        }

        /// <summary>
        /// Replaces <paramref name="store"/>'s state with the save at
        /// <paramref name="path"/> (see the class summary for each outcome). The
        /// store's <see cref="IChoiceStore.Reset"/> fires once whatever happens;
        /// it never sees a <see cref="IChoiceStore.FactChanged"/> from a load.
        /// </summary>
        public static ChoiceLoadResult Load(string path, ChoiceStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            if (!CheckWritable(path, out string pathError))
            {
                store.Restore(null, null);
                return new ChoiceLoadResult(ChoiceLoadStatus.Failed, message: pathError);
            }

            // A temp file only survives a save that died before the swap; the
            // real file is still the last complete save.
            TryDelete(TempPathFor(path));

            if (!File.Exists(path))
            {
                store.Restore(null, null);
                return new ChoiceLoadResult(ChoiceLoadStatus.Missing);
            }

            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e) when (IsFileError(e))
            {
                store.Restore(null, null);
                return new ChoiceLoadResult(ChoiceLoadStatus.Failed,
                    message: $"could not read the choice save '{path}': {e.Message}. It was left as is; choices will not be saved this session.");
            }

            if (!TryParse(text, out ChoiceSaveData data, out string parseError))
                return Quarantine(path, store, parseError);

            int fileVersion = data.version;
            if (fileVersion > CurrentVersion)
            {
                store.Restore(null, null);
                return new ChoiceLoadResult(ChoiceLoadStatus.TooNew, fileVersion: fileVersion,
                    message: $"the choice save '{path}' is version {fileVersion}, newer than this build's {CurrentVersion}. " +
                             "It was not loaded and will not be overwritten; choices will not be saved this session.");
            }

            bool migrated = fileVersion < CurrentVersion;
            if (migrated && !TryMigrate(data, out string migrateError))
            {
                store.Restore(null, null);
                return new ChoiceLoadResult(ChoiceLoadStatus.Failed, fileVersion: fileVersion,
                    message: $"the choice save '{path}' (version {fileVersion}) could not be migrated: {migrateError}. " +
                             "It was left as is; choices will not be saved this session.");
            }

            store.Restore(data.choices, FactPairs(data.facts));
            return new ChoiceLoadResult(migrated ? ChoiceLoadStatus.Migrated : ChoiceLoadStatus.Loaded,
                Sanitize(data.playTime), fileVersion,
                migrated ? $"migrated the choice save from version {fileVersion} to {CurrentVersion}" : null);
        }

        /// <summary>Deletes the save and any stray temp file. True when neither
        /// remains (a missing file counts as deleted).</summary>
        public static bool Delete(string path, out string error)
        {
            if (!CheckWritable(path, out error)) return false;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                TryDelete(TempPathFor(path));
                return true;
            }
            catch (Exception e) when (IsFileError(e))
            {
                error = $"could not delete the choice save '{path}': {e.Message}";
                return false;
            }
        }

        /// <summary>New game: empties <paramref name="store"/> (its
        /// <see cref="IChoiceStore.ResetAll"/> events fire) and deletes the save.
        /// The store is cleared even if the delete fails.</summary>
        public static bool NewGame(string path, IChoiceStore store, out string error)
        {
            store?.ResetAll();
            return Delete(path, out error);
        }

        /// <summary>Where a corrupt save at <paramref name="path"/> is moved:
        /// <c>&lt;name&gt;.corrupt-&lt;yyyyMMdd-HHmmss&gt;&lt;ext&gt;</c> beside it, with
        /// a numeric suffix if that name is taken.</summary>
        public static string CorruptPathFor(string path, DateTime when)
        {
            string folder = Path.GetDirectoryName(path) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension)) extension = ".json";
            string stamp = when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

            string candidate = Path.Combine(folder, stem + CorruptInfix + stamp + extension);
            for (int n = 2; File.Exists(candidate); n++)
                candidate = Path.Combine(folder, $"{stem}{CorruptInfix}{stamp}-{n}{extension}");
            return candidate;
        }

        /// <summary>
        /// The migration hook: upgrades <paramref name="data"/> from an older
        /// version to <see cref="CurrentVersion"/> in place, one
        /// <see cref="TryMigrateStep"/> at a time. A step that does not advance
        /// the version fails rather than looping.
        /// </summary>
        static bool TryMigrate(ChoiceSaveData data, out string error)
        {
            while (data.version < CurrentVersion)
            {
                int from = data.version;
                if (!TryMigrateStep(data))
                {
                    error = $"no migration step from version {from}";
                    return false;
                }
                if (data.version <= from)
                {
                    error = $"the migration step from version {from} did not advance the version";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// One format upgrade. Version 1 is the first format, so there are no
        /// steps yet. To change the format: bump <see cref="CurrentVersion"/>,
        /// add a field to <see cref="ChoiceSaveData"/> if needed, and add a
        /// <c>case</c> for the old version that rewrites <paramref name="data"/>
        /// and sets <c>data.version</c> to the next version.
        /// </summary>
        static bool TryMigrateStep(ChoiceSaveData data)
        {
            switch (data.version)
            {
                // case 1: MigrateV1ToV2(data); return true;   // must set data.version = 2
                default:
                    return false;
            }
        }

        static bool TryParse(string text, out ChoiceSaveData data, out string error)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "the file is empty";
                return false;
            }

            try
            {
                data = JsonUtility.FromJson<ChoiceSaveData>(text);
            }
            catch (Exception e)
            {
                // JsonUtility reports bad JSON as ArgumentException; anything
                // else it throws on this text means the same thing here.
                error = $"it is not valid JSON ({e.Message})";
                return false;
            }

            if (data == null)
            {
                error = "it is not a JSON object";
                return false;
            }
            if (data.version <= 0)
            {
                error = "it has no version number";
                return false;
            }

            error = null;
            return true;
        }

        static ChoiceLoadResult Quarantine(string path, ChoiceStore store, string reason)
        {
            store.Restore(null, null);
            string target = CorruptPathFor(path, DateTime.Now);
            try
            {
                File.Move(path, target);
            }
            catch (Exception e) when (IsFileError(e))
            {
                return new ChoiceLoadResult(ChoiceLoadStatus.Failed,
                    message: $"the choice save '{path}' is corrupt ({reason}) and could not be moved aside ({e.Message}). " +
                             "It was left as is; choices will not be saved this session.");
            }

            return new ChoiceLoadResult(ChoiceLoadStatus.Corrupt,
                message: $"the choice save '{path}' was corrupt ({reason}); it was moved to '{target}' and choices start empty.",
                quarantinedPath: target);
        }

        static IEnumerable<KeyValuePair<string, FactValue>> FactPairs(ChoiceSaveData.FactEntry[] entries)
        {
            if (entries == null) yield break;
            for (int i = 0; i < entries.Length; i++)
                yield return new KeyValuePair<string, FactValue>(entries[i].key, entries[i].value);
        }

        // Swaps a finished temp file over the save. File.Replace is a single
        // rename at the OS level; the copy is a fallback for a platform without it.
        static void SwapIn(string temp, string path)
        {
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }

            try
            {
                File.Replace(temp, path, null, true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
        }

        static bool CheckWritable(string path, out string error)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "no save path";
                return false;
            }
            if (IsInside(path, Application.dataPath))
            {
                error = $"the save path '{path}' is inside the project's Assets folder ('{Application.dataPath}'); refusing to touch it";
                return false;
            }
            error = null;
            return true;
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (IsFileError(e))
            {
                // Best effort: a stray temp file is harmless and retried next time.
            }
        }

        static bool IsFileError(Exception e)
            => e is IOException || e is UnauthorizedAccessException || e is NotSupportedException
               || e is ArgumentException || e is System.Security.SecurityException;

        static double Sanitize(double seconds)
            => double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 ? 0 : seconds;
    }
}
