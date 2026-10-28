using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The maths of the conversation camera, kept pure so it is tested rather
    /// than eyeballed.
    ///
    /// The shot is over the player's shoulder, built on the flat line from the
    /// player to the NPC: the camera sits behind the player on that line,
    /// raised and shifted to one side, and looks at the NPC's focus point
    /// nudged toward the player's side. Over the right shoulder the player
    /// fills the left of the frame and the NPC sits a little right of centre,
    /// and a tighter field of view does the rest of the "pulled in" feel.
    ///
    /// Built on the player → NPC line rather than the player's facing, so the
    /// shot is right even when the player was locked on and facing elsewhere.
    /// </summary>
    public static class DialogueFraming
    {
        /// <summary>
        /// The conversation shot for a player standing at
        /// <paramref name="playerFeet"/> talking to an NPC whose focus point is
        /// <paramref name="npcFocus"/>. When the NPC is straight above or below
        /// the pivot, <paramref name="playerForward"/> stands in for the line
        /// between them, and world forward if that is vertical too.
        /// </summary>
        public static DialogueShot Solve(Vector3 playerFeet, Vector3 playerForward, Vector3 npcFocus,
                                         in DialogueFramingSettings settings)
        {
            Vector3 pivot = playerFeet + Vector3.up * settings.PivotHeight;

            Vector3 line = npcFocus - pivot;
            line.y = 0f;
            if (line.sqrMagnitude < 1e-6f)
            {
                line = new Vector3(playerForward.x, 0f, playerForward.z);
                if (line.sqrMagnitude < 1e-6f) line = Vector3.forward;
            }
            Vector3 toNpc = line.normalized;
            // Unity is left-handed: up × forward is right.
            Vector3 right = Vector3.Cross(Vector3.up, toNpc);

            Vector3 position = pivot
                               - toNpc * settings.PushInDistance
                               + right * settings.SideOffset
                               + Vector3.up * settings.CameraHeight;

            // Over the right shoulder the player fills the LEFT of the frame.
            // Aiming a little to the player's side of the NPC moves the NPC the
            // other way, to the open right side, so shoulder and NPC never
            // overlap. The shift follows whichever side the camera is on.
            float side = settings.SideOffset < 0f ? -1f : 1f;
            Vector3 aim = npcFocus
                          + Vector3.up * settings.AimHeightOffset
                          - right * (settings.AimSideOffset * side);

            return new DialogueShot(pivot, position, aim, settings.FieldOfView);
        }

        /// <summary>
        /// Where the camera goes once something blocks it. A sweep from
        /// <paramref name="pivot"/> toward <paramref name="desired"/> found clear
        /// space for <paramref name="clearDistance"/> metres; the camera stops
        /// there, but never closer to the pivot than <paramref name="minDistance"/>.
        /// A clear distance at or beyond the desired one leaves it untouched.
        /// </summary>
        public static Vector3 PullIn(Vector3 pivot, Vector3 desired, float clearDistance, float minDistance)
        {
            Vector3 offset = desired - pivot;
            float distance = offset.magnitude;
            if (distance < 1e-5f || clearDistance >= distance) return desired;
            float kept = Mathf.Clamp(clearDistance, Mathf.Min(minDistance, distance), distance);
            return pivot + offset * (kept / distance);
        }

        /// <summary>Smoothstep easing of <paramref name="t"/>, clamped to 0–1:
        /// zero slope at both ends, so a blend neither snaps in nor out.</summary>
        public static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
