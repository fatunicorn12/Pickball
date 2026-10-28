using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Looking around during a conversation: the camera orbits the NPC's focus
    /// point (its head), starting from the scripted shot.
    ///
    /// <b>Why the NPC's focus.</b> The camera keeps the same distance from the
    /// NPC's head as it swings, and <see cref="DialogueFraming.Compose"/> re-aims
    /// it each frame so the head stays exactly where the shot put it on screen
    /// (the upper third, toward the open side). The NPC never leaves the frame,
    /// whichever way the player turns.
    ///
    /// <b>State.</b> Yaw is an offset from the shot's own bearing round the
    /// pivot, free and wrapped to (−180, 180]. Pitch is the camera's elevation
    /// above the pivot, clamped to the range passed with each input (so it can be
    /// tuned live), widened to include the shot's own elevation so the first
    /// touch never snaps. Input moves a
    /// target; the shown angles follow it with exponential damping over
    /// <b>unscaled</b> time, because the world is stopped
    /// (<c>Time.timeScale</c> 0) for the whole talk. Yaw keeps the remaining
    /// turn unwrapped, so a fast spin never takes the short way back the wrong
    /// direction.
    ///
    /// <b>Collision</b> is split: the owner sweeps for walls and terrain from
    /// <see cref="Pivot"/> along <see cref="Direction"/>, and
    /// <see cref="FollowDistance"/> comes in at once to the clear distance (so the
    /// camera never sits inside a wall) and eases back out when the wall is
    /// gone. <see cref="PushOut"/> keeps the camera out of the player's own body.
    /// Pure math, so EditMode tests drive it without a scene.
    /// </summary>
    public sealed class DialogueOrbit
    {
        const float SettleAngle = 0.01f;       // °
        const float SettleDistance = 1e-4f;    // m
        // Elevation limit, short of straight up or down, where bearing is undefined.
        const float PoleLimit = 85f;

        float _baseBearing;          // ° of the shot's camera round the pivot
        float _yawRemaining;         // ° still to turn toward the target, unwrapped
        float _startPitch;           // ° the shot's own elevation
        bool _distanceSettled = true;

        /// <summary>The point the camera orbits: the NPC's focus, world space.</summary>
        public Vector3 Pivot { get; private set; }
        /// <summary>The shot's distance from the pivot, m: how far out the camera
        /// sits whenever nothing is in the way.</summary>
        public float Radius { get; private set; }
        /// <summary>Shown yaw offset from the shot's bearing, °, in (−180, 180].</summary>
        public float Yaw { get; private set; }
        /// <summary>Yaw the input asks for, °, in (−180, 180].</summary>
        public float TargetYaw => WrapAngle(Yaw + _yawRemaining);
        /// <summary>Shown elevation of the camera above the pivot, °.</summary>
        public float Pitch { get; private set; }
        /// <summary>Elevation the input asks for, °, within the clamp.</summary>
        public float TargetPitch { get; private set; }
        /// <summary>Shown distance from the pivot, m: <see cref="Radius"/> unless
        /// something pulled the camera in.</summary>
        public float Distance { get; private set; }
        /// <summary>True once any look input has arrived since <see cref="Begin"/>.
        /// Until then the camera stays exactly on the scripted shot.</summary>
        public bool Moved { get; private set; }
        /// <summary>True when the shown angles and distance have reached what was
        /// asked: with the world stopped, nothing changes until new input.</summary>
        public bool IsSettled => _yawRemaining == 0f && Pitch == TargetPitch && _distanceSettled;

        /// <summary>World-space direction from the pivot to the camera for the
        /// shown yaw and pitch.</summary>
        public Vector3 Direction
        {
            get
            {
                float bearing = (_baseBearing + Yaw) * Mathf.Deg2Rad;
                float elevation = Pitch * Mathf.Deg2Rad;
                float flat = Mathf.Cos(elevation);
                return new Vector3(Mathf.Sin(bearing) * flat, Mathf.Sin(elevation), Mathf.Cos(bearing) * flat);
            }
        }

        /// <summary>Where the camera goes: the pivot plus <see cref="Direction"/>
        /// times <see cref="Distance"/>.</summary>
        public Vector3 Position => Pivot + Direction * Distance;

        /// <summary>
        /// Starts orbiting <paramref name="pivot"/> from a camera at
        /// <paramref name="cameraPosition"/> (the scripted shot): yaw 0, the shot's
        /// own elevation, its distance.
        /// </summary>
        public void Begin(Vector3 pivot, Vector3 cameraPosition)
        {
            Pivot = pivot;
            Vector3 offset = cameraPosition - pivot;
            Radius = offset.magnitude;
            if (Radius < 1e-4f)
            {
                offset = Vector3.back;
                Radius = 0f;
            }
            float elevation = Mathf.Asin(Mathf.Clamp(offset.y / offset.magnitude, -1f, 1f)) * Mathf.Rad2Deg;
            _baseBearing = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            _startPitch = elevation;
            Pitch = TargetPitch = elevation;
            Yaw = 0f;
            _yawRemaining = 0f;
            Distance = Radius;
            _distanceSettled = true;
            Moved = false;
        }

        /// <summary>Adds look input in degrees: yaw turns the view (right
        /// positive, which swings the camera the other way round the pivot), pitch
        /// raises the camera (positive looks down). Yaw is free. Pitch is clamped
        /// to [<paramref name="minPitch"/>, <paramref name="maxPitch"/>] (elevation
        /// above the pivot, °), widened to include the shot's own elevation.</summary>
        public void AddLook(float yawDegrees, float pitchDegrees, float minPitch, float maxPitch)
        {
            if (yawDegrees == 0f && pitchDegrees == 0f) return;
            float low = Mathf.Max(Mathf.Min(minPitch, _startPitch), -PoleLimit);
            float high = Mathf.Min(Mathf.Max(maxPitch, _startPitch), PoleLimit);
            _yawRemaining += yawDegrees;
            TargetPitch = Mathf.Clamp(TargetPitch + pitchDegrees, low, Mathf.Max(low, high));
            Moved = true;
        }

        /// <summary>
        /// Moves the shown yaw and pitch toward the target over
        /// <paramref name="unscaledDeltaTime"/> seconds, with time constant
        /// <paramref name="smoothTime"/> (0: at once). Frame-rate independent.
        /// A step of 0 changes nothing; scaled time is never involved.
        /// </summary>
        public void Step(float unscaledDeltaTime, float smoothTime)
        {
            if (!(unscaledDeltaTime > 0f)) return;
            float k = smoothTime > 0f ? 1f - Mathf.Exp(-unscaledDeltaTime / smoothTime) : 1f;

            float turn = _yawRemaining * k;
            if (Mathf.Abs(_yawRemaining - turn) < SettleAngle) turn = _yawRemaining;
            Yaw = WrapAngle(Yaw + turn);
            _yawRemaining -= turn;
            if (Mathf.Abs(_yawRemaining) < 1e-6f) _yawRemaining = 0f;

            float rise = (TargetPitch - Pitch) * k;
            Pitch = Mathf.Abs(TargetPitch - Pitch - rise) < SettleAngle ? TargetPitch : Pitch + rise;
        }

        /// <summary>
        /// The distance to show this frame, given the clear space a sweep from the
        /// pivot along <see cref="Direction"/> found. Comes in at once (the camera
        /// is never left inside a wall), eases back out with time constant
        /// <paramref name="pullOutSeconds"/>, never beyond <see cref="Radius"/>
        /// and never closer than <paramref name="minDistance"/>.
        /// </summary>
        public float FollowDistance(float clearDistance, float minDistance, float unscaledDeltaTime,
                                    float pullOutSeconds)
        {
            float wanted = Mathf.Clamp(clearDistance, Mathf.Min(Mathf.Max(minDistance, 0f), Radius), Radius);
            if (wanted <= Distance)
                Distance = wanted;
            else if (unscaledDeltaTime > 0f)
            {
                float k = pullOutSeconds > 0f ? 1f - Mathf.Exp(-unscaledDeltaTime / pullOutSeconds) : 1f;
                Distance += (wanted - Distance) * k;
                if (wanted - Distance < SettleDistance) Distance = wanted;
            }
            _distanceSettled = Distance == wanted;
            return Distance;
        }

        /// <summary>
        /// <paramref name="point"/> moved out of a capsule (the segment
        /// <paramref name="bottom"/>–<paramref name="top"/>, radius
        /// <paramref name="radius"/>) to its surface, along the shortest way out;
        /// unchanged when already outside. Keeps an orbiting camera from passing
        /// through the player's body: it slides round him instead. A point exactly
        /// on the axis goes out along <paramref name="fallbackOut"/>.
        /// </summary>
        public static Vector3 PushOut(Vector3 point, Vector3 bottom, Vector3 top, float radius, Vector3 fallbackOut)
        {
            Vector3 axis = top - bottom;
            float lengthSq = axis.sqrMagnitude;
            float t = lengthSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - bottom, axis) / lengthSq) : 0f;
            Vector3 closest = bottom + axis * t;
            Vector3 away = point - closest;
            float distance = away.magnitude;
            if (distance >= radius) return point;
            Vector3 direction = distance > 1e-4f ? away / distance
                : fallbackOut.sqrMagnitude > 1e-8f ? fallbackOut.normalized : Vector3.up;
            return closest + direction * radius;
        }

        /// <summary><paramref name="degrees"/> wrapped to (−180, 180].</summary>
        public static float WrapAngle(float degrees)
        {
            float wrapped = Mathf.Repeat(degrees + 180f, 360f) - 180f;
            return wrapped <= -180f ? 180f : wrapped;
        }
    }
}
