using System;

namespace Pickleball.UI
{
    /// <summary>
    /// Anything that speaks lines a <see cref="BarkView"/> can show: a boss's
    /// barks (Newton's, Hypatia's). Exists so one HUD view serves every boss
    /// without knowing which one it is wired to.
    /// </summary>
    public interface IBarkSource
    {
        /// <summary>Raised with the line and how long to show it, in seconds.</summary>
        event Action<string, float> Spoke;
    }
}
