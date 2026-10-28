using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Freezes the game world by setting <c>Time.timeScale</c> to 0, and puts
    /// back exactly what was there before rather than assuming 1. Optionally
    /// pauses the <c>AudioListener</c> too, restoring its previous state.
    ///
    /// <see cref="Hold"/> twice keeps the value from the first call, so a
    /// second hold can never record "0" as the value to restore.
    /// <see cref="Release"/> without a hold does nothing.
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

        /// <summary>True between <see cref="Hold"/> and <see cref="Release"/>.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>The time scale that <see cref="Release"/> will restore.</summary>
        public float PreviousTimeScale => _previousTimeScale;

        /// <summary>Stops time, and the audio listener when <paramref name="pauseAudio"/> is set.</summary>
        public void Hold(bool pauseAudio)
        {
            if (IsHeld) return;
            IsHeld = true;
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            _pausedAudio = pauseAudio;
            if (pauseAudio)
            {
                _previousAudioPaused = AudioListener.pause;
                AudioListener.pause = true;
            }
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
