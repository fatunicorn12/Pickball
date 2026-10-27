using UnityEngine;

namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// Detects "rotate the stick" for the spin jump: how far the stick has turned,
    /// in one direction, over the last fraction of a second.
    ///
    /// Rotation is binned into fixed 1/30 s slots (Sunshine's own frame) in a
    /// ring, so the window means the same thing at any frame rate and nothing is
    /// allocated per frame. Rotation only counts while the stick is pushed past
    /// a minimum, so wiggling around the dead zone never accumulates; reversing
    /// direction cancels what was built the other way, so a back-and-forth
    /// waggle sums to nothing.
    /// </summary>
    public sealed class SpinGesture
    {
        /// <summary>Slot length, s.</summary>
        public const float SlotSeconds = 1f / 30f;
        /// <summary>Longest window that can be asked about, s.</summary>
        public const float MaxWindow = SlotCount * SlotSeconds;
        const int SlotCount = 32;

        // Not readonly: owners rebuild this object after a script reload, and the
        // project rule is that nothing runtime-built hides in a readonly field.
        float[] _slots = new float[SlotCount];
        int _head;
        float _slotTime;
        float _lastAngle;
        bool _hasLast;

        /// <summary>Feeds one frame of raw stick input (screen space, −1..1).</summary>
        public void Tick(Vector2 stick, float minMagnitude, float dt)
        {
            Advance(dt);
            if (stick.sqrMagnitude < minMagnitude * minMagnitude)
            {
                _hasLast = false;
                return;
            }
            float angle = Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg;
            if (_hasLast) _slots[_head] += Mathf.DeltaAngle(_lastAngle, angle);
            _lastAngle = angle;
            _hasLast = true;
        }

        /// <summary>Net signed rotation, degrees, over the last <paramref name="window"/> s
        /// (counter-clockwise positive).</summary>
        public float Rotation(float window)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(window / SlotSeconds), 1, SlotCount);
            float sum = 0f;
            for (int i = 0; i < n; i++)
                sum += _slots[(_head - i + SlotCount) % SlotCount];
            return sum;
        }

        /// <summary>True when the stick has turned at least <paramref name="degrees"/>
        /// one way within <paramref name="window"/> s.</summary>
        public bool HasSpin(float degrees, float window) => Mathf.Abs(Rotation(window)) >= degrees;

        /// <summary>Spends the gesture, so one spin buys one spin jump.</summary>
        public void Consume()
        {
            System.Array.Clear(_slots, 0, SlotCount);
            _hasLast = false;
        }

        void Advance(float dt)
        {
            _slotTime += dt;
            // A long hitch clears the whole ring rather than looping through it.
            int steps = Mathf.Min((int)(_slotTime / SlotSeconds), SlotCount);
            if (steps <= 0) return;
            _slotTime -= steps * SlotSeconds;
            if (_slotTime >= SlotSeconds) _slotTime = 0f;
            for (int i = 0; i < steps; i++)
            {
                _head = (_head + 1) % SlotCount;
                _slots[_head] = 0f;
            }
        }
    }
}
