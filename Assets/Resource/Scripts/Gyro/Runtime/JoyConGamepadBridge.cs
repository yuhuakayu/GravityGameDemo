using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Resource.Scripts.Gyro
{
    /// <summary>Expose one selected Joy-Con to the existing menu and gamepad bindings.</summary>
    internal sealed class JoyConGamepadBridge : IDisposable
    {
        private Gamepad _gamepad;

        internal void Update(GyroDevice device)
        {
            if (device == null || !device.IsJoyCon)
            {
                Dispose();
                return;
            }

            GyroControllerState controls = device.Controls;
            if (_gamepad == null) _gamepad = InputSystem.AddDevice<Gamepad>("JoyConGamepad");
            bool right = device.IsRightJoyCon;
            var stick = right ? controls.RightStick : controls.LeftStick;
            bool chord = controls.IsPressed(GyroButtons.SL | GyroButtons.SR);
            bool stickClick = controls.IsPressed(GyroButtons.LeftStick) || controls.IsPressed(GyroButtons.RightStick);
            var state = new GamepadState
            {
                leftStick = stick,
                rightStick = stick,
                leftTrigger = controls.LeftTrigger,
                rightTrigger = controls.RightTrigger
            };
            state = state.WithButton(GamepadButton.South, controls.IsPressed(right ? GyroButtons.East : GyroButtons.Down));
            state = state.WithButton(GamepadButton.East, controls.IsPressed(right ? GyroButtons.South : GyroButtons.Right));
            state = state.WithButton(GamepadButton.West, controls.IsPressed(right ? GyroButtons.West : GyroButtons.Left));
            state = state.WithButton(GamepadButton.North, controls.IsPressed(right ? GyroButtons.North : GyroButtons.Up));
            state = state.WithButton(GamepadButton.Start, chord || controls.IsPressed(GyroButtons.Options) || controls.IsPressed(GyroButtons.Create));
            state = state.WithButton(GamepadButton.Select, chord);
            state = state.WithButton(GamepadButton.LeftShoulder, controls.IsPressed(GyroButtons.SL) || controls.IsPressed(GyroButtons.LeftShoulder));
            state = state.WithButton(GamepadButton.RightShoulder, controls.IsPressed(GyroButtons.SR) || controls.IsPressed(GyroButtons.RightShoulder));
            state = state.WithButton(GamepadButton.LeftStick, stickClick);
            state = state.WithButton(GamepadButton.RightStick, stickClick);
            InputSystem.QueueStateEvent(_gamepad, state);
        }

        public void Dispose()
        {
            if (_gamepad == null) return;
            InputSystem.RemoveDevice(_gamepad);
            _gamepad = null;
        }
    }
}
