using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Picks who the talk prompt is for. Pure, so the rules are pinned by
    /// EditMode tests rather than discovered by walking around.
    ///
    /// <b>Lock-on wins.</b> If the player is locked on to a character that can
    /// talk, that character is the target whenever it is within
    /// <see cref="TalkSelectionRules.LockedTalkRange"/>, whatever the player
    /// faces. Locking on is the "targeted" half of the feature: lock-on already
    /// gathers anything with a Health, so an NPC with one is targetable for free.
    ///
    /// <b>Locked on to anything else, no talk.</b> While the player is locked on
    /// to something that cannot talk (Newton), nobody gets the prompt, so E
    /// stays Dash for the whole fight even with an NPC standing nearby.
    ///
    /// <b>Otherwise the nearest one in front.</b> The candidate with the
    /// smallest flat distance that is within range and inside the facing cone.
    /// Facing is the player's body forward, not the camera, so turning toward
    /// someone is how you pick them out of a crowd.
    ///
    /// <b>Sticky.</b> The current target keeps the prompt a little past the
    /// range and cone, and a rival must be closer by the sticky margin to take
    /// it. Without this the prompt flickers between two NPCs standing side by
    /// side, and at the range edge.
    ///
    /// Distances are flat (XZ); height is a separate cut, so a tall and a short
    /// NPC at the same spot are equally close.
    /// </summary>
    public static class TalkTargetSelector
    {
        /// <summary>
        /// Index of the best candidate, or -1 for none.
        /// </summary>
        /// <param name="origin">The player's focus point: feet plus
        /// <see cref="DialogueConfig.PlayerFocusHeight"/>.</param>
        /// <param name="forward">The player's facing. Its vertical part is
        /// ignored; a zero vector disables the cone.</param>
        /// <param name="candidates">Everyone who might be talked to.</param>
        /// <param name="lockedIndex">Index of the lock-on target's candidate, or -1.</param>
        /// <param name="lockedOnNonTalker">True while the player is locked on to
        /// something with no Talkable. Nobody is selected then.</param>
        /// <param name="currentIndex">Index of the current prompt target, or -1.</param>
        /// <param name="rules">Ranges, cone and hysteresis.</param>
        public static int Select(Vector3 origin, Vector3 forward, IReadOnlyList<TalkCandidate> candidates,
                                 int lockedIndex, bool lockedOnNonTalker, int currentIndex,
                                 in TalkSelectionRules rules)
        {
            if (candidates == null || lockedOnNonTalker) return -1;
            int count = candidates.Count;

            if (lockedIndex >= 0 && lockedIndex < count)
            {
                var locked = candidates[lockedIndex];
                float reach = rules.LockedTalkRange + (lockedIndex == currentIndex ? rules.StickyMargin : 0f);
                if (locked.CanTalk && HeightOk(origin, locked.Position, rules)
                    && FlatDistance(origin, locked.Position) <= reach)
                    return lockedIndex;
            }

            Vector3 flatForward = new(forward.x, 0f, forward.z);
            bool useCone = rules.FacingConeDegrees < 180f && flatForward.sqrMagnitude > 1e-6f;

            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var c = candidates[i];
                if (!c.CanTalk || !HeightOk(origin, c.Position, rules)) continue;

                bool current = i == currentIndex;
                float distance = FlatDistance(origin, c.Position);
                float range = rules.TalkRange + (current ? rules.StickyMargin : 0f);
                if (distance > range) continue;

                if (useCone && distance > 1e-3f)
                {
                    Vector3 to = new(c.Position.x - origin.x, 0f, c.Position.z - origin.z);
                    float cone = rules.FacingConeDegrees + (current ? rules.StickyConeDegrees : 0f);
                    if (Vector3.Angle(flatForward, to) > cone) continue;
                }

                float score = distance - (current ? rules.StickyMargin : 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Distance in the ground plane, m.</summary>
        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static bool HeightOk(Vector3 origin, Vector3 point, in TalkSelectionRules rules)
            => Mathf.Abs(point.y - origin.y) <= rules.MaxHeightDifference;
    }
}
