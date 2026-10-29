using System;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Cumulative play time: the seconds played in earlier sessions (from the
    /// save) plus the seconds of a real-time source since this session started.
    /// It is the clock <see cref="ChoiceStoreHost"/> gives its
    /// <see cref="ChoiceStore"/>, so a <see cref="ChoiceRecord.Time"/> made in
    /// the third session still sorts after one made in the first.
    ///
    /// Plain C# with the real-time source injected (the host passes
    /// <c>Time.unscaledTimeAsDouble</c>), so tests drive it with a fake.
    /// <see cref="Freeze"/> and <see cref="Resume"/> let the host leave time
    /// spent in the background out of play time on every platform, whatever
    /// the real-time source does while the app is suspended.
    ///
    /// <b>Monotonic.</b> Between restarts, <see cref="Now"/> never goes back,
    /// even if the source does: Unity zeroes <c>Time.unscaledTimeAsDouble</c>
    /// before <c>OnDisable</c> at the end of Play, and a clock that went back
    /// to the loaded value there lost the whole session's play time (QA bug
    /// DLG-C-3). When a reading is lower than the one before, the time counted
    /// up to that earlier reading is banked and counting continues from the
    /// new value. Only the unread stretch between the last reading and the
    /// step back is lost, which is why the host reads the clock when it saves
    /// at quit.
    /// </summary>
    public sealed class PlayTimeClock
    {
        readonly Func<double> _realtime;
        double _carried;
        double _sessionStart;
        double _lastReading;
        bool _frozen;

        /// <param name="realtime">Seconds from a real-time source; only
        /// differences are used, and a step back is survived (see the summary).</param>
        /// <param name="carried">Play time already banked, e.g. from the save.</param>
        public PlayTimeClock(Func<double> realtime, double carried = 0)
        {
            _realtime = realtime ?? throw new ArgumentNullException(nameof(realtime));
            Restart(carried);
        }

        /// <summary>Seconds of play time: never negative, and never lower than an
        /// earlier reading since the last <see cref="Restart"/>, whatever the
        /// source does. Reading it is how a step back in the source is noticed.</summary>
        public double Now
        {
            get
            {
                if (_frozen) return _carried;
                double reading = _realtime();
                if (reading < _lastReading)
                {
                    _carried += Math.Max(0, _lastReading - _sessionStart);
                    _sessionStart = reading;
                }
                _lastReading = reading;
                return _carried + Math.Max(0, reading - _sessionStart);
            }
        }

        /// <summary>True between <see cref="Freeze"/> and the next <see cref="Restart"/>.</summary>
        public bool IsFrozen => _frozen;

        /// <summary>Banks <paramref name="carried"/> seconds and counts on from now.
        /// A negative, NaN or infinite value counts as 0. The one way to set the
        /// clock lower (a load, or a new game).</summary>
        public void Restart(double carried)
        {
            _carried = double.IsNaN(carried) || double.IsInfinity(carried) || carried < 0 ? 0 : carried;
            _sessionStart = _realtime();
            _lastReading = _sessionStart;
            _frozen = false;
        }

        /// <summary>Stops the clock at its current reading (app sent to the
        /// background); <see cref="Resume"/> counts on from there.</summary>
        public void Freeze()
        {
            if (_frozen) return;
            _carried = Now;
            _frozen = true;
        }

        /// <summary>Resumes a frozen clock from where it stopped; does nothing if running.</summary>
        public void Resume()
        {
            if (_frozen) Restart(_carried);
        }
    }
}
