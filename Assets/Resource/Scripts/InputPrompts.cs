using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Resource.Scripts
{
    /// <summary>Prompt family follows deliberate input, rather than the last connected device.</summary>
    [DefaultExecutionOrder(-290)]
    public sealed class InputPrompts : MonoBehaviour
    {
        private static InputPrompts _instance;
        private readonly Dictionary<int, bool> _sticksHeld = new Dictionary<int, bool>();
        private Gamepad _lastGamepad;
        private bool _usesNintendo, _hasInput;
        private float _gyroSeconds;
        private bool _gyroHeld;

        public static event Action Changed;
        public static bool UsesNintendo => _instance != null && _instance._usesNintendo;
        public static Gamepad ActiveGamepad => _instance != null && _instance._lastGamepad != null && _instance._lastGamepad.added
            ? _instance._lastGamepad : Gamepad.current;
        public static bool UsesJoyCon => ActiveGamepad != null && ActiveGamepad.name.StartsWith("JoyConGamepad", StringComparison.Ordinal);
        public static string Confirm => UsesNintendo ? "A" : "Enter";
        public static string Back => UsesNintendo ? "B" : "Esc";
        public static string Pause => UsesNintendo ? "+" : "Esc";
        public static string WorldLeft => UsesNintendo ? UsesJoyCon ? "SL" : "L" : "Q";
        public static string WorldRight => UsesNintendo ? UsesJoyCon ? "SR" : "R" : "E";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; Changed = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance == null) new GameObject("Input Prompts").AddComponent<InputPrompts>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            Gamepad defaultPad = null, usedPad = null;
            double lastTime = double.NegativeInfinity;
            bool keyboardUsed = false;
            foreach (var pad in Gamepad.all)
            {
                if (defaultPad == null || pad.name.StartsWith("JoyConGamepad", StringComparison.Ordinal)) defaultPad = pad;
                bool moved = pad.leftStick.ReadValue().sqrMagnitude > .25f || pad.rightStick.ReadValue().sqrMagnitude > .25f;
                bool known = _sticksHeld.TryGetValue(pad.deviceId, out bool held);
                _sticksHeld[pad.deviceId] = moved;
                bool pressed = false;
                foreach (var control in pad.allControls)
                    if (control is ButtonControl button && button.wasPressedThisFrame) { pressed = true; break; }
                // A new device's initial stick state and small drift are not fresh user actions.
                if ((pressed || (known && moved && !held)) && pad.lastUpdateTime >= lastTime)
                { lastTime = pad.lastUpdateTime; usedPad = pad; }
            }
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            double keyboardTime = double.NegativeInfinity;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) keyboardTime = keyboard.lastUpdateTime;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                keyboardTime = Math.Max(keyboardTime, mouse.lastUpdateTime);
            if (keyboardTime > lastTime) keyboardUsed = true;

            var runtime = Gyro.GyroRuntime.Current;
            bool turning = runtime != null && !runtime.IsPlaybackActive && runtime.HasFreshGyro &&
                Mathf.Abs(runtime.Processor.LastSteering.AngularSpeed) > 20f && Mathf.Abs(runtime.StickOutput) > .1f;
            _gyroSeconds = turning ? _gyroSeconds + Time.unscaledDeltaTime : 0f;
            bool gyroHeld = _gyroSeconds >= .12f;
            bool gyroStarted = gyroHeld && !_gyroHeld;
            _gyroHeld = gyroHeld;

            if (keyboardUsed) SetFamily(false, _lastGamepad, true);
            else if (usedPad != null) SetFamily(true, usedPad, true);
            else if (gyroStarted && defaultPad != null) SetFamily(true, defaultPad, true);
            else if (!_hasInput && defaultPad != null) SetFamily(true, defaultPad, false);
            else if (_usesNintendo && defaultPad == null) SetFamily(false, null, false);
            else if (_lastGamepad != null && !_lastGamepad.added) SetFamily(_usesNintendo, defaultPad, false);
        }

        private void SetFamily(bool nintendo, Gamepad pad, bool used)
        {
            bool changed = _usesNintendo != nintendo || (nintendo && _lastGamepad != pad);
            _usesNintendo = nintendo;
            _lastGamepad = pad;
            _hasInput |= used;
            if (changed) Changed?.Invoke();
        }

        public static string Resolve(string text) => text.Replace("{confirm}", Confirm).Replace("{back}", Back)
            .Replace("{pause}", Pause).Replace("{worldLeft}", WorldLeft).Replace("{worldRight}", WorldRight);
    }
}
