using System;

namespace Pickleball.Cutscene
{
    /// <summary>
    /// What presentation needs to know about a playing cutscene: when one starts and ends, and how
    /// far the hold-to-skip has got. Implemented by the pipeline's player; the subtitle view and
    /// the skip indicator listen. Gameplay hooks listen to <see cref="Finished"/>.
    /// </summary>
    public interface ICutscenePlayback
    {
        /// <summary>Raised with the cutscene id when one starts.</summary>
        event Action<string> Started;

        /// <summary>Raised with the cutscene id and whether it was skipped, after control is handed back.</summary>
        event Action<string, bool> Finished;

        /// <summary>True from Started until Finished.</summary>
        bool IsPlaying { get; }

        /// <summary>Hold-to-skip progress, 0 (not held) to 1 (skips). Polled by the indicator.</summary>
        float SkipProgress { get; }
    }
}
