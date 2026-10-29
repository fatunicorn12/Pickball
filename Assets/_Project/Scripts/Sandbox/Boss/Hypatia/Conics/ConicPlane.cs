using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// Places a perifocal-frame conic (focus at the origin, periapsis on +X) flat
    /// in the world: the focus becomes <see cref="Focus"/> (its Y is the flight
    /// height), the periapsis direction is turned <see cref="Angle"/> radians from
    /// world +X toward +Z, and <see cref="Sense"/> = −1 mirrors it so the body
    /// travels clockwise seen from above. Every conic is horizontal because the
    /// plate is, and the player reads them as shapes on the floor.
    /// </summary>
    public readonly struct ConicPlane
    {
        readonly float _cos, _sin;

        /// <param name="sense">+1 counter-clockwise from above, −1 clockwise. Any
        /// value below zero counts as −1.</param>
        public ConicPlane(Vector3 focus, float angleRadians, int sense)
        {
            Focus = focus;
            Angle = angleRadians;
            Sense = sense < 0 ? -1 : 1;
            _cos = Mathf.Cos(angleRadians);
            _sin = Mathf.Sin(angleRadians);
        }

        /// <summary>World position of the focus (her, projected to the flight height).</summary>
        public Vector3 Focus { get; }

        /// <summary>World bearing of periapsis, rad, from +X toward +Z.</summary>
        public float Angle { get; }

        /// <summary>+1 or −1.</summary>
        public int Sense { get; }

        /// <summary>A perifocal point in world space, at the focus's height.</summary>
        public Vector3 ToWorld(Vector2 perifocal)
        {
            Vector3 d = DirectionToWorld(perifocal);
            return new Vector3(Focus.x + d.x, Focus.y, Focus.z + d.z);
        }

        /// <summary>A perifocal direction (or offset) as a horizontal world vector.</summary>
        public Vector3 DirectionToWorld(Vector2 perifocal)
        {
            float y = perifocal.y * Sense;
            return new Vector3(perifocal.x * _cos - y * _sin, 0f, perifocal.x * _sin + y * _cos);
        }

        /// <summary>The inverse of <see cref="ToWorld"/>, ignoring height.</summary>
        public Vector2 ToPerifocal(Vector3 world)
        {
            float dx = world.x - Focus.x, dz = world.z - Focus.z;
            float x = dx * _cos + dz * _sin;
            float y = -dx * _sin + dz * _cos;
            return new Vector2(x, y * Sense);
        }

        /// <summary>Bearing of a horizontal world offset, rad, from +X toward +Z.</summary>
        public static float BearingOf(float worldX, float worldZ) => Mathf.Atan2(worldZ, worldX);
    }
}
