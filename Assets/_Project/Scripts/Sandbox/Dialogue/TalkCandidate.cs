using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// One possible talk target as plain data: where it is and whether it can
    /// talk now. The interactor copies each <see cref="Talkable"/> into one of
    /// these so <see cref="TalkTargetSelector"/> stays a pure function that
    /// tests can feed without building a scene.
    /// </summary>
    public readonly struct TalkCandidate
    {
        /// <summary>The NPC's focus point (<see cref="Talkable.FocusPosition"/>), world space.</summary>
        public readonly Vector3 Position;

        /// <summary><see cref="Talkable.CanTalk"/> at the moment of the copy.</summary>
        public readonly bool CanTalk;

        /// <summary>Creates a candidate at <paramref name="position"/>.</summary>
        public TalkCandidate(Vector3 position, bool canTalk)
        {
            Position = position;
            CanTalk = canTalk;
        }
    }
}
