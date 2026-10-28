using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Freezes the game world by setting <c>Time.timeScale</c> to 0, and puts
    /// back exactly what was there before rather than assuming 1. Optionally
    /// pauses the <c>AudioListener</c> too, restoring its previous state.
    ///
    /// <see cref="Hold"/> refuses (returns false, records nothing) when time is
    /// already stopped: something else paused the game, perhaps a second
    /// director, and recording its 0 as "the value to restore" would leave the
    /// game frozen when both let go. A second <see cref="Hold"/> while held
    /// keeps the value from the first. <see cref="Release"/> without a hold
    /// does nothing.
    ///
    /// The dialogue director is the only code in the project that writes
    /// <c>Time.timeScale</c>. If something else ever starts to, it should
    /// share this class (or a stack of holders) rather than write the value
    /// itself, or the two will restore over each other.
    /// </summary>
    public sealed class TimeScalePause
    {
        float _previousTimeScale = 1f;
        bool _previousAudioPaused;
        bool _pausedAudio;

        /// <summary>True between a successful <see cref="Hold"/> and <see cref="Release"/>.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>
        /// Stops time, and the audio listener when <paramref name="pauseAudio"/> is
        /// set. Returns false, changing nothing, when time is already stopped by
        /// someone else; true when held (including already held by this pause).
        /// </summary>
        public bool Hold(bool pauseAudio)
        {
            if (IsHeld) return true;
            float current = Time.timeScale;
            if (!(current > 0f)) return false;

            IsHeld = true;
            _previousTimeScale = current;
            Time.timeScale = 0f;

            _pausedAudio = pauseAudio;
            if (pauseAudio)
            {
                _previousAudioPaused = AudioListener.pause;
                AudioListener.pause = true;
            }
            return true;
        }

        /// <summary>Restores the time scale (and audio pause) recorded by <see cref="Hold"/>.</summary>
        public void Release()
        {
            if (!IsHeld) return;
            IsHeld = false;
            Time.timeScale = _previousTimeScale;
            if (_pausedAudio) AudioListener.pause = _previousAudioPaused;
            _pausedAudio = false;
        }
    }
}
