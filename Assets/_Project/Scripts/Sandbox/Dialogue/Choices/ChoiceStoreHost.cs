using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Owns the game's <see cref="ChoiceStore"/> in a scene: builds it, loads
    /// the save into it, and writes it back. Dialogue (and anything else) holds
    /// this as an <see cref="IChoiceStoreProvider"/> Inspector reference wired
    /// by Setup Dialogue; there is no singleton and no runtime Find.
    ///
    /// <b>Store.</b> Built lazily on first use (or in <c>Awake</c>/<c>OnEnable</c>),
    /// so <see cref="Store"/> is never null, even for a caller whose own
    /// <c>Awake</c> runs first, and the store comes back after a script reload
    /// in Play mode (which drops it with every other non-serialized object):
    /// <c>OnDisable</c> saved it just before the reload and <c>OnEnable</c>
    /// loads it again. With <see cref="Persist"/> off a reload starts it empty.
    ///
    /// <b>Clock.</b> Each pick is stamped with cumulative play time: the
    /// seconds banked in the save plus <c>Time.unscaledTimeAsDouble</c> since
    /// the load (<see cref="PlayTimeClock"/>). Time in the background
    /// (<c>OnApplicationPause</c>) is left out.
    ///
    /// <b>Saving.</b> Any change (a pick, a fact, a reset) marks the store dirty;
    /// <c>LateUpdate</c> writes it at most once per frame, however many effects
    /// a choice applied. It also saves on <c>OnApplicationPause(true)</c>,
    /// <c>OnApplicationQuit</c> and <c>OnDisable</c>, dirty or not, so play time
    /// is banked. <c>LateUpdate</c> runs while a talk has
    /// <c>Time.timeScale</c> at 0. Writes go through
    /// <see cref="ChoiceStoreSave"/>: atomic, and never inside Assets/. A failed
    /// write is logged once and retried on the next change, not every frame.
    ///
    /// <b>The save path</b> is <c>&lt;persistentDataPath&gt;/Saves/&lt;fileName&gt;</c>,
    /// read when the store loads. A file name with folders in it, or any path
    /// that resolves inside the project's Assets folder, is rejected with an
    /// error and nothing is read or written that session.
    ///
    /// <b>Refused loads.</b> If the save is from a newer build, or cannot be
    /// read, the store starts empty, an error is logged, and saving is off for
    /// the session so the file survives. <see cref="NewGame"/> is the explicit
    /// way past that: it deletes the file.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChoiceStoreHost : MonoBehaviour, IChoiceStoreProvider
    {
        [Header("Saving")]
        [Tooltip("On: choices and facts load from and save to disk, so they carry across sessions. " +
                 "Off: the store lives in memory only and starts empty every Play (useful for tests and quick editor runs); " +
                 "nothing is read or written.")]
        [SerializeField] private bool persist = true;

        [Tooltip("Save file name under <persistentDataPath>/Saves/ (a plain name, no folders). " +
                 "Use another name to keep a separate save, e.g. for a test level. Read when the store loads.")]
        [SerializeField] private string fileName = ChoiceStoreSave.DefaultFileName;

        ChoiceStore _store;
        PlayTimeClock _clock;
        string _directoryOverride;
        string _savePath;
        bool _dirty;
        bool _saveBlocked;

        /// <inheritdoc/>
        public IChoiceStore Store => EnsureStore();

        /// <summary>Whether the store is loaded from and saved to disk.</summary>
        public bool Persist => persist;

        /// <summary>The full path the store loads from and saves to; null when
        /// <see cref="Persist"/> is off, the store isn't built yet, or the path was rejected.</summary>
        public string SavePath => persist ? _savePath : null;

        /// <summary>A change is waiting for the next <c>LateUpdate</c> save.</summary>
        public bool IsDirty => _dirty;

        /// <summary>Saving is off for this session: the path was rejected, or the
        /// save on disk is newer than this build or unreadable.</summary>
        public bool SavingBlocked => _saveBlocked;

        /// <summary>How many saves this host has written. Diagnostics, and the
        /// tests' check that saves are coalesced.</summary>
        public int SavesWritten { get; private set; }

        /// <summary>What the last load found (status, file version, message).</summary>
        public ChoiceLoadResult LastLoad { get; private set; }

        /// <summary>Cumulative play time in seconds: what the next pick is stamped with.</summary>
        public double PlayTime
        {
            get
            {
                EnsureStore();
                return _clock.Now;
            }
        }

        void Awake() => EnsureStore();

        // After a script reload in Play mode Awake does not run again; OnEnable does.
        void OnEnable() => EnsureStore();

        void LateUpdate() => FlushIfDirty();

        void OnApplicationPause(bool paused)
        {
            if (_clock == null) return;
            if (paused)
            {
                SaveNow();
                _clock.Freeze();
            }
            else
            {
                _clock.Resume();
            }
        }

        void OnApplicationQuit() => SaveNow();

        void OnDisable() => SaveNow();

        /// <summary>Saves if anything changed since the last save. <c>LateUpdate</c>
        /// calls it; public so tests can step the coalescing frame by frame.</summary>
        public void FlushIfDirty()
        {
            if (_dirty) SaveNow();
        }

        /// <summary>
        /// Writes the store and play time now. Returns false (and writes nothing)
        /// when <see cref="Persist"/> is off, the store was never built, saving is
        /// blocked, or the write failed (logged as a warning).
        /// </summary>
        public bool SaveNow()
        {
            bool wasDirty = _dirty;
            _dirty = false;
            if (!persist || _store == null || _saveBlocked || _savePath == null) return false;

            if (ChoiceStoreSave.Save(_savePath, _store, _clock.Now, out string error))
            {
                SavesWritten++;
                return true;
            }

            Debug.LogWarning($"ChoiceStoreHost: {error}" + (wasDirty ? " The latest choices are only in memory." : string.Empty), this);
            return false;
        }

        /// <summary>
        /// New game: empties the store (its <see cref="IChoiceStore.ResetAll"/>
        /// events fire), restarts play time at 0 and deletes the save file. It is
        /// the explicit way to discard a save this build refused to load, so it
        /// also lifts a block from one.
        /// </summary>
        [ContextMenu("New Game (clear choices)")]
        public void NewGame()
        {
            ChoiceStore store = EnsureStore();
            store.ResetAll();
            _clock.Restart(0);
            _dirty = false;
            if (!persist) return;

            if (_savePath == null && !TryResolveSavePath(out _savePath)) return;
            if (!ChoiceStoreSave.Delete(_savePath, out string error))
            {
                Debug.LogWarning($"ChoiceStoreHost: {error}", this);
                return;
            }
            _saveBlocked = false;
        }

        /// <summary>
        /// Test and tool seam: load from and save to <paramref name="directory"/>
        /// instead of <c>&lt;persistentDataPath&gt;/Saves</c> (null or empty
        /// restores the default). Takes effect at once: a live store is reloaded
        /// from the new place (one <see cref="IChoiceStore.Reset"/>), so it always
        /// mirrors the file it would save to. A directory inside Assets/ is
        /// rejected like any other path there.
        /// </summary>
        public void UseSaveDirectory(string directory)
        {
            _directoryOverride = directory;
            if (_store == null) return;
            LoadFromDisk();
            _dirty = false;
        }

        ChoiceStore EnsureStore()
        {
            if (_store != null) return _store;

            _clock = new PlayTimeClock(() => Time.unscaledTimeAsDouble);
            _store = new ChoiceStore(
                () => _clock.Now,
                message => Debug.LogWarning(message, this),
                exception => Debug.LogException(exception, this));
            LoadFromDisk();

            // Subscribed after the load so the load's own Reset doesn't mark a
            // save that would only write back what was just read.
            _store.ChoiceRecorded += _ => _dirty = true;
            _store.FactChanged += _ => _dirty = true;
            _store.Reset += () => _dirty = true;
            _dirty = false;
            return _store;
        }

        void LoadFromDisk()
        {
            _saveBlocked = false;
            _savePath = null;

            if (!persist)
            {
                _store.Restore(null, null);
                _clock.Restart(0);
                LastLoad = new ChoiceLoadResult(ChoiceLoadStatus.Missing, message: "persistence is off");
                return;
            }

            if (!TryResolveSavePath(out _savePath))
            {
                _saveBlocked = true;
                _store.Restore(null, null);
                _clock.Restart(0);
                LastLoad = new ChoiceLoadResult(ChoiceLoadStatus.Failed, message: "the save path was rejected");
                return;
            }

            ChoiceLoadResult result = ChoiceStoreSave.Load(_savePath, _store);
            LastLoad = result;
            _clock.Restart(result.PlayTime);
            _saveBlocked = !result.SafeToSave;

            switch (result.Status)
            {
                case ChoiceLoadStatus.Corrupt:
                    Debug.LogWarning($"ChoiceStoreHost: {result.Message}", this);
                    break;
                case ChoiceLoadStatus.Migrated:
                    Debug.Log($"ChoiceStoreHost: {result.Message}", this);
                    break;
                case ChoiceLoadStatus.TooNew:
                case ChoiceLoadStatus.Failed:
                    Debug.LogError($"ChoiceStoreHost: {result.Message}", this);
                    break;
            }
        }

        bool TryResolveSavePath(out string path)
        {
            string directory = string.IsNullOrEmpty(_directoryOverride) ? ChoiceStoreSave.DefaultDirectory : _directoryOverride;
            if (ChoiceStoreSave.TryResolvePath(directory, fileName, Application.dataPath, out path, out string error))
                return true;

            Debug.LogError($"ChoiceStoreHost: {error}. Choices will not be loaded or saved this session.", this);
            return false;
        }
    }
}
