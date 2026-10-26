using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;

namespace GravityGame.Tests.PlayMode
{
    public sealed class PhysicalWheelInputPlayModeTests
    {
        private const string TestLayout = "PhysicalWheelInputTestGamepad";
        private readonly List<Gamepad> _pads = new List<Gamepad>();
        private Type _inputType;
        private object _input, _settings;
        private Gamepad _previousGamepad;

        [SetUp]
        public void SetUp()
        {
            _previousGamepad = Gamepad.current;
            _inputType = FindType("Resource.Scripts.Gyro.PhysicalWheelInput");
            _input = Activator.CreateInstance(_inputType);
            _settings = Activator.CreateInstance(FindType("Resource.Scripts.Gyro.GyroSettings"));
            InputSystem.RegisterLayout("{\"name\":\"" + TestLayout + "\",\"extend\":\"Gamepad\"}",
                name: TestLayout,
                matches: new InputDeviceMatcher().WithInterface("XInput")
                    .WithCapability("physicalWheelTest", true));
        }

        [TearDown]
        public void TearDown()
        {
            (_input as IDisposable)?.Dispose();
            foreach (var pad in _pads)
                if (pad.added) InputSystem.RemoveDevice(pad);
            _pads.Clear();
            InputSystem.RemoveLayout(TestLayout);
            if (_previousGamepad != null && _previousGamepad.added) _previousGamepad.MakeCurrent();
        }

        [Test]
        public void CalibrationSelectsMovedAxisAndMapsFullTravelTo360Degrees()
        {
            Gamepad wheel = AddPad();
            SetState(wheel, 0f);
            BeginCalibration();
            SetState(wheel, -1f);
            SetState(wheel, 1f);
            SetState(wheel, 0.2f);
            Assert.That(FinishCalibration(), Is.True);
            Assert.That(Field<string>("wheelAxisPath"), Is.EqualTo("leftStick/x"));
            Assert.That(Field<float>("wheelLeft"), Is.EqualTo(-1f));
            Assert.That(Field<float>("wheelCenter"), Is.EqualTo(0.2f));
            Assert.That(Field<float>("wheelRight"), Is.EqualTo(1f));
            Assert.That(Convert.ToInt32(_settings.GetType().GetField("mode").GetValue(_settings)), Is.EqualTo(2));

            SetState(wheel, -1f);
            Assert.That(Property<bool>("HasInput"), Is.True);
            Assert.That(Property<float>("Angle"), Is.EqualTo(-180f));
            SetState(wheel, 0.2f);
            Assert.That(Property<float>("Angle"), Is.Zero);
            SetState(wheel, 1f);
            Assert.That(Property<float>("Angle"), Is.EqualTo(180f));
        }

        [Test]
        public void DifferentXInputUserIndexesKeepTheSelectedWheelBinding()
        {
            Gamepad other = AddXInputPad(0);
            Gamepad wheel = AddXInputPad(1);
            Assert.That(DeviceMatch(wheel), Is.Not.EqualTo(DeviceMatch(other)));
            Assert.That(DeviceMatch(wheel), Does.Contain("userIndex=1"));
            Calibrate(wheel);

            SetState(other, -1f);
            SetState(wheel, 0.5f);
            Assert.That(Property<bool>("HasInput"), Is.True);
            Assert.That(Property<float>("Angle"), Is.EqualTo(90f));
            Assert.That(Field<string>("wheelDeviceMatch"), Is.EqualTo(DeviceMatch(wheel)));
        }

        [Test]
        public void RemovingBoundWheelDoesNotUseAnotherXInputController()
        {
            Gamepad other = AddXInputPad(0);
            Gamepad wheel = AddXInputPad(1);
            Calibrate(wheel);
            Assert.That(Property<bool>("HasInput"), Is.True);

            InputSystem.RemoveDevice(wheel);
            SetState(other, 1f);
            Assert.That(Property<bool>("HasInput"), Is.False);
            Assert.That(Property<float>("Angle"), Is.Zero);
            Assert.That(other.added, Is.True);
        }

        [Test]
        public void CalibrationWithoutMovementPreservesPreviousCalibration()
        {
            Gamepad wheel = AddPad();
            Calibrate(wheel);
            string original = JsonUtility.ToJson(_settings);
            BeginCalibration();
            SetState(wheel, 0f);
            Assert.That(FinishCalibration(), Is.False);
            Assert.That(JsonUtility.ToJson(_settings), Is.EqualTo(original));
            Assert.That(Property<string>("Status"), Does.Contain("校准失败"));
        }

        private Gamepad AddPad() => AddXInputPad(7);

        private Gamepad AddXInputPad(int userIndex)
        {
            var pad = (Gamepad)InputSystem.AddDevice(new InputDeviceDescription
            {
                interfaceName = "XInput",
                capabilities = "{\"userIndex\":" + userIndex + ",\"physicalWheelTest\":true}"
            });
            _pads.Add(pad);
            Assert.That(pad.layout, Is.EqualTo(TestLayout));
            return pad;
        }

        private void Calibrate(Gamepad wheel)
        {
            SetState(wheel, 0f);
            BeginCalibration();
            SetState(wheel, -1f);
            SetState(wheel, 1f);
            SetState(wheel, 0f);
            Assert.That(FinishCalibration(), Is.True);
        }

        private void SetState(Gamepad pad, float leftX)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState
            {
                leftStick = new Vector2(leftX, 0.3f),
                rightStick = new Vector2(0.7f, -0.4f)
            });
            InputSystem.Update();
            UpdateInput();
        }

        private void BeginCalibration() => _inputType.GetMethod("BeginCalibration").Invoke(_input, null);
        private bool FinishCalibration() => (bool)_inputType.GetMethod("FinishCalibration").Invoke(_input, new[] { _settings });
        private void UpdateInput() => _inputType.GetMethod("Update").Invoke(_input, new[] { _settings });
        private T Property<T>(string name) => (T)_inputType.GetProperty(name).GetValue(_input);
        private T Field<T>(string name) => (T)_settings.GetType().GetField(name).GetValue(_settings);
        private string DeviceMatch(InputDevice device) => (string)_inputType.GetMethod("DeviceMatch").Invoke(null, new object[] { device });
        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
    }
}
