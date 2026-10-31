namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// Decides when the timeline a <see cref="CutscenePlayer"/> is playing has finished. Plain C#,
    /// so it can be tested without a director.
    /// </summary>
    /// <remarks>
    /// A timeline is finished only when either:
    /// <list type="bullet">
    /// <item>the director's time reaches the asset's duration; or</item>
    /// <item>the director's <c>stopped</c> event fired after it was seen playing at least once.</item>
    /// </list>
    /// "Not playing" alone never counts. A director that hasn't started yet (the first tick of an
    /// app that starts paused or unfocused) or that was paused mid-timeline (app pause, focus loss)
    /// is still playing, and the player resumes it. The old test, "the director's state isn't
    /// Playing", skipped scene 1 entirely when Play mode started with the Editor unfocused and
    /// run-in-background off.
    /// </remarks>
    public sealed class TimelineCompletion
    {
        /// <summary>Seconds of slack when comparing the director's time with the duration.</summary>
        public const double EndTolerance = 1e-4;

        bool _seenPlaying;
        bool _stoppedAfterPlaying;

        /// <summary>True once the director has been seen playing this timeline.</summary>
        public bool SeenPlaying => _seenPlaying;

        /// <summary>True once the director stopped after being seen playing (the timeline ended).</summary>
        public bool StoppedAfterPlaying => _stoppedAfterPlaying;

        /// <summary>
        /// Whether the timeline is over, without counting a tick: the time reached the end, or the
        /// director stopped after playing.
        /// </summary>
        public bool IsOver(double time, double duration) =>
            _stoppedAfterPlaying || (duration > 0.0 && time >= duration - EndTolerance);

        /// <summary>Starts tracking a newly started timeline.</summary>
        public void Begin()
        {
            _seenPlaying = false;
            _stoppedAfterPlaying = false;
        }

        /// <summary>The director's <c>stopped</c> event. It counts only after the timeline was seen playing.</summary>
        public void OnStopped()
        {
            if (_seenPlaying) _stoppedAfterPlaying = true;
        }

        /// <summary>
        /// One tick. Returns true when the timeline has finished; false while it is still playing,
        /// including when the director isn't advancing right now (not started yet, or paused).
        /// </summary>
        public bool Tick(bool directorPlaying, double time, double duration)
        {
            if (directorPlaying) _seenPlaying = true;
            if (duration > 0.0 && time >= duration - EndTolerance) return true;
            return _stoppedAfterPlaying;
        }

        /// <summary>
        /// True when the director should be nudged to play: the timeline isn't finished, but the
        /// director isn't advancing (it never started, or it was paused).
        /// </summary>
        public bool NeedsResume(bool directorPlaying, bool finished) => !finished && !directorPlaying;
    }
}
