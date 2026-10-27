using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// Base for the sword's one-shot effects. An instance is active while it
    /// plays and deactivates itself when done, which is what returns it to the
    /// <see cref="EffectPool"/> — no timers held anywhere else, no Destroy.
    ///
    /// Subclasses animate in their own Update, which only runs while active.
    /// </summary>
    public abstract class PooledEffect : MonoBehaviour
    {
        /// <summary>True while playing; false means free for reuse.</summary>
        public bool IsPlaying => gameObject.activeSelf;

        /// <summary>Start (or restart) the effect for <paramref name="cue"/>.
        /// Called on an already-activated object.</summary>
        public abstract void Play(in EffectCue cue);

        /// <summary>Hand the instance back to the pool.</summary>
        protected void Finish() => gameObject.SetActive(false);
    }
}
