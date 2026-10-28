using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// A solved conversation shot: where the camera goes, how it is turned and
    /// how wide it is, plus the player-head pivot it was built from (clearance
    /// sweeps start there). When the camera has to move (pulled in by a wall),
    /// <see cref="DialogueFraming.Compose"/> rebuilds the rotation and field of
    /// view for the new position, so the NPC stays where the framing put it.
    /// </summary>
    public readonly struct DialogueShot
    {
        /// <summary>Over-the-shoulder pivot on the player's head, world space.</summary>
        public readonly Vector3 Pivot;
        /// <summary>Camera position, world space.</summary>
        public readonly Vector3 Position;
        /// <summary>Camera rotation, without roll.</summary>
        public readonly Quaternion Rotation;
        /// <summary>Vertical field of view, °.</summary>
        public readonly float FieldOfView;

        /// <summary>Creates a shot.</summary>
        public DialogueShot(Vector3 pivot, Vector3 position, Quaternion rotation, float fieldOfView)
        {
            Pivot = pivot;
            Position = position;
            Rotation = rotation;
            FieldOfView = fieldOfView;
        }
    }
}
