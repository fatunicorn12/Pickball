using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Pickleball.Tests.PlayMode.Harness
{
    /// <summary>
    /// A keyboard, mouse and gamepad that exist only for the length of a scenario, driven by
    /// state events so the game's own <c>InputAction</c>s see real presses. Written by hand
    /// because the project has no <c>testables</c> entry for the Input System package, so
    /// <c>InputTestFixture</c> is not available.
    ///
    /// State is kept here and the whole device state is re-queued on every change; the Input
    /// System applies it on its next update (the start of the next frame). Presses held across
    /// frames therefore behave exactly like held keys.
    /// </summary>
    public sealed class VirtualInput : IDisposable
    {
        readonly HashSet<Key> _keys = new();
        GamepadState _pad;
        MouseState _mouse;
        Vector2 _pendingDelta;
        bool _disposed;

        /// <summary>The virtual keyboard.</summary>
        public Keyboard Keyboard { get; }
        /// <summary>The virtual mouse.</summary>
        public Mouse Mouse { get; }
        /// <summary>The virtual gamepad.</summary>
        public Gamepad Pad { get; }

        public VirtualInput()
        {
            Keyboard = InputSystem.AddDevice<Keyboard>("PlaytestKeyboard");
            Mouse = InputSystem.AddDevice<Mouse>("PlaytestMouse");
            Pad = InputSystem.AddDevice<Gamepad>("PlaytestGamepad");
            // The game reads Keyboard.current / Gamepad.current / Mouse.current directly in places.
            Keyboard.MakeCurrent();
            Mouse.MakeCurrent();
            Pad.MakeCurrent();
        }

        // ---------------------------------------------------------------- keyboard

        /// <summary>Holds <paramref name="key"/> down until released.</summary>
        public void Hold(Key key) { if (_keys.Add(key)) FlushKeys(); }

        /// <summary>Releases <paramref name="key"/>.</summary>
        public void Release(Key key) { if (_keys.Remove(key)) FlushKeys(); }

        /// <summary>True while the virtual key is down.</summary>
        public bool IsHeld(Key key) => _keys.Contains(key);

        void FlushKeys()
        {
            if (_disposed) return;
            var arr = new Key[_keys.Count];
            _keys.CopyTo(arr);
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState(arr));
        }

        // ------------------------------------------------------------------- mouse

        /// <summary>Holds or releases a mouse button.</summary>
        public void SetMouseButton(MouseButton button, bool down)
        {
            _mouse = _mouse.WithButton(button, down);
            FlushMouse();
        }

        /// <summary>Queues a one-frame mouse delta (pixels), as a flick or a look.</summary>
        public void MoveMouse(Vector2 delta)
        {
            _pendingDelta += delta;
            FlushMouse();
        }

        void FlushMouse()
        {
            if (_disposed) return;
            var s = _mouse;
            s.delta = _pendingDelta;
            _pendingDelta = Vector2.zero;
            InputSystem.QueueStateEvent(Mouse, s);
        }

        // ----------------------------------------------------------------- gamepad

        /// <summary>Sets the left stick (−1..1 per axis).</summary>
        public void SetLeftStick(Vector2 v) { _pad.leftStick = Vector2.ClampMagnitude(v, 1f); FlushPad(); }

        /// <summary>Sets the right stick (−1..1 per axis).</summary>
        public void SetRightStick(Vector2 v) { _pad.rightStick = Vector2.ClampMagnitude(v, 1f); FlushPad(); }

        /// <summary>Holds or releases a pad button.</summary>
        public void SetPadButton(GamepadButton button, bool down)
        {
            _pad = _pad.WithButton(button, down);
            if (button == GamepadButton.LeftTrigger) _pad.leftTrigger = down ? 1f : 0f;
            if (button == GamepadButton.RightTrigger) _pad.rightTrigger = down ? 1f : 0f;
            FlushPad();
        }

        void FlushPad()
        {
            if (_disposed) return;
            InputSystem.QueueStateEvent(Pad, _pad);
        }

        /// <summary>Releases everything on every device.</summary>
        public void ReleaseAll()
        {
            _keys.Clear();
            _pad = default;
            _mouse = default;
            _pendingDelta = Vector2.zero;
            if (_disposed) return;
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(Mouse, new MouseState());
            InputSystem.QueueStateEvent(Pad, new GamepadState());
        }

        /// <summary>Removes the devices. Safe to call twice.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            ReleaseAll();
            _disposed = true;
            foreach (var d in new InputDevice[] { Keyboard, Mouse, Pad })
                if (d != null && d.added) InputSystem.RemoveDevice(d);
        }
    }
}
