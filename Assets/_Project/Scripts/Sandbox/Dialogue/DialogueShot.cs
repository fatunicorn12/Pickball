using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// A solved conversation shot: where the camera goes, what it looks at, and
    /// how wide. The rotation is derived from position and aim so that pulling
    /// the camera in for clearance (<see cref="WithPosition"/>) keeps the NPC
    /// where the framing put it.
    /// </summary>
    public readonly struct DialogueShot
    {
        /// <summary>Over-the-shoulder pivot on the player, world space. Clearance is measured from here.</summary>
        public readonly Vector3 Pivot;
        /// <summary>Camera position, world space.</summary>
        public readonly Vector3 Position;
        /// <summary>Point the camera looks at, world space.</summary>
        public readonly Vector3 Aim;
        /// <summary>Vertical field of view, °.</summary>
        public readonly float FieldOfView;

        /// <summary>Creates a shot.</summary>
        public DialogueShot(Vector3 pivot, Vector3 position, Vector3 aim, float fieldOfView)
        {
            Pivot = pivot;
            Position = position;
            Aim = aim;
            FieldOfView = fieldOfView;
        }

        /// <summary>Camera rotation: looking from <see cref="Position"/> at <see cref="Aim"/>, world up.</summary>
        public Quaternion Rotation
        {
            get
            {
                Vector3 look = Aim - Position;
                return look.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity;
            }
        }

        /// <summary>The same shot from another camera position.</summary>
        public DialogueShot WithPosition(Vector3 position) => new(Pivot, position, Aim, FieldOfView);
    }
}
