using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GravityGame.Tests.PlayMode
{
    public sealed class PhysicalWheelCommandPlayModeTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void EnteringWheelAtNonzeroPositionKeepsItsAbsoluteOffsetFromPhysicalCenter()
        {
            using (var fixture = new Fixture())
            {
                fixture.SetWheel(0.5f);
                object command = fixture.Evaluate();
                Assert.That(Property<bool>(command, "Blocked"), Is.False);
                Assert.That(Property<bool>(command, "HasTarget"), Is.True);
                Assert.That(Property<float>(command, "TargetAngle"), Is.EqualTo(120f).Within(0.001f),
                    "Current world 30° plus physical wheel 90° must target 120°, including the first command.");
                Assert.That(Property<bool>(fixture.Input, "DirectAngleActive"), Is.True);
            }
        }

        [Test]
        public void PauseAndReconnectPreserveTheSameAbsoluteWheelMapping()
        {
            using (var fixture = new Fixture())
            {
                fixture.SetWheel(0.25f);
                Assert.That(Property<float>(fixture.Evaluate(), "TargetAngle"), Is.EqualTo(75f).Within(0.001f));
                Time.timeScale = 0f;
                fixture.SetWheel(0.5f);
                Assert.That(Property<bool>(fixture.Evaluate(), "Blocked"), Is.True);
                Assert.That(Property<bool>(fixture.Input, "DirectAngleActive"), Is.False);

                Time.timeScale = 1f;
                Assert.That(Property<float>(fixture.Evaluate(), "TargetAngle"), Is.EqualTo(120f).Within(0.001f),
                    "Moving the physical wheel while paused must not change the fixed center reference.");
                Call(fixture.Input, "ResetContinuity");
                Assert.That(Property<float>(fixture.Evaluate(), "TargetAngle"), Is.EqualTo(120f).Within(0.001f),
                    "Resetting pending input must preserve the physical wheel's fixed center reference.");
                fixture.DisconnectWheel();
                Assert.That(Property<bool>(fixture.Evaluate(), "Blocked"), Is.True);
                Assert.That(Property<bool>(fixture.Input, "DirectAngleActive"), Is.False);
                fixture.ReconnectWheel(0.5f);
                Assert.That(Property<float>(fixture.Evaluate(), "TargetAngle"), Is.EqualTo(120f).Within(0.001f));
                Assert.That(Property<bool>(fixture.Input, "DirectAngleActive"), Is.True);
            }
        }

        [Test]
        public void ExplicitRecenterPreservesCurrentOrientationAndSpeedModeDisablesDirectAngle()
        {
            using (var fixture = new Fixture())
            {
                fixture.SetWheel(0.5f);
                fixture.Evaluate();
                fixture.WorldBody.rotation = -75f;
                Call(fixture.Runtime, "Recenter");
                Assert.That(Property<float>(fixture.Evaluate(), "TargetAngle"), Is.EqualTo(75f).Within(0.001f));
                fixture.SetWheel(0.25f);
                Assert.That(Property<float>(fixture.Evaluate(), "TargetAngle"), Is.EqualTo(30f).Within(0.001f));

                fixture.SetMode(0);
                fixture.Evaluate();
                Assert.That(Property<bool>(fixture.Input, "DirectAngleActive"), Is.False);
            }
        }

        private sealed class Fixture : IDisposable
        {
            private const string Layout = "PhysicalWheelCommandTestGamepad";
            private readonly Scene _previousScene, _scene;
            private readonly float _previousTimeScale;
            private readonly Gamepad _previousGamepad;
            private readonly List<Gamepad> _devices = new List<Gamepad>();
            private readonly object _settings, _wheelInput;
            private readonly string _savedSettings;
            private readonly FieldInfo _recenterRevision, _transitionInstance;
            private readonly object _previousTransition, _savedRecenterRevision;
            private Gamepad _wheel;
            public readonly object Runtime;
            public Component World { get; private set; }
            public Component Input { get; private set; }
            public Rigidbody2D WorldBody { get; private set; }

            public Fixture()
            {
                _previousScene = SceneManager.GetActiveScene();
                _previousTimeScale = Time.timeScale;
                _previousGamepad = Gamepad.current;
                Runtime = FindType("Resource.Scripts.Gyro.GyroRuntime").GetProperty("Instance").GetValue(null);
                _settings = Runtime.GetType().GetProperty("Settings").GetValue(Runtime);
                _wheelInput = Runtime.GetType().GetProperty("Wheel").GetValue(Runtime);
                _savedSettings = JsonUtility.ToJson(_settings);
                _recenterRevision = Runtime.GetType().GetField("<RecenterRevision>k__BackingField", Fields);
                _savedRecenterRevision = _recenterRevision.GetValue(Runtime);
                _transitionInstance = FindType("Resource.Scripts.SceneTransition")
                    .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
                _previousTransition = _transitionInstance.GetValue(null);
                _scene = SceneManager.CreateScene("PhysicalWheelCommand_" + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                try
                {
                    SceneManager.SetActiveScene(_scene);
                    Time.timeScale = 1f;
                    var gateObject = new GameObject("Isolated inactive transition gate");
                    gateObject.SetActive(false);
                    _transitionInstance.SetValue(null, gateObject.AddComponent(FindType("Resource.Scripts.SceneTransition")));
                    var playerObject = new GameObject("Wheel command player");
                    playerObject.AddComponent<Rigidbody2D>().gravityScale = 0f;
                    playerObject.AddComponent<BoxCollider2D>();
                    var player = playerObject.AddComponent(FindType("Resource.Scripts.PlayerController"));
                    Call(player, "BeginGameplay");
                    var worldObject = new GameObject("Wheel command world");
                    WorldBody = worldObject.AddComponent<Rigidbody2D>();
                    WorldBody.rotation = -30f;
                    World = worldObject.AddComponent(FindType("Resource.Scripts.WorldRotator"));
                    Input = worldObject.GetComponent(FindType("Resource.Scripts.Gyro.WorldRotationInput"));
                    Set(Input, "_player", player);
                    InputSystem.RegisterLayout("{\"name\":\"" + Layout + "\",\"extend\":\"Gamepad\"}", name: Layout,
                        matches: new InputDeviceMatcher().WithInterface("PhysicalWheelCommandTest"));
                    _wheel = CreateWheel();
                    Set(_settings, "wheelDeviceMatch", Call(_wheelInput, "DeviceMatch", _wheel));
                    Set(_settings, "wheelAxisPath", "leftStick/x");
                    Set(_settings, "wheelLeft", -1f);
                    Set(_settings, "wheelCenter", 0f);
                    Set(_settings, "wheelRight", 1f);
                    Set(_settings, "wheelCalibrated", true);
                    Set(_settings, "invertDirection", false);
                    Set(_settings, "inputSource", Enum.ToObject(_settings.GetType().GetField("inputSource").FieldType, 1));
                    SetMode(2);
                    SetWheel(0f);
                }
                catch { Dispose(); throw; }
            }

            public void SetMode(int value) =>
                Set(_settings, "mode", Enum.ToObject(_settings.GetType().GetField("mode").FieldType, value));

            public void SetWheel(float raw)
            {
                InputSystem.QueueStateEvent(_wheel, new GamepadState { leftStick = new Vector2(raw, 0f) });
                InputSystem.Update();
                Call(_wheelInput, "Update", _settings);
            }

            public object Evaluate()
            {
                Set(Input, "_evaluatedFrame", -1);
                return Call(Input, "Evaluate", World);
            }

            public void DisconnectWheel()
            {
                InputSystem.RemoveDevice(_wheel);
                Call(_wheelInput, "Update", _settings);
            }

            public void ReconnectWheel(float raw) { _wheel = CreateWheel(); SetWheel(raw); }

            private Gamepad CreateWheel()
            {
                var pad = (Gamepad)InputSystem.AddDevice(new InputDeviceDescription
                    { interfaceName = "PhysicalWheelCommandTest", product = "Command test wheel" });
                _devices.Add(pad);
                return pad;
            }

            public void Dispose()
            {
                JsonUtility.FromJsonOverwrite(_savedSettings, _settings);
                _recenterRevision.SetValue(Runtime, _savedRecenterRevision);
                foreach (var device in _devices) if (device.added) InputSystem.RemoveDevice(device);
                InputSystem.RemoveLayout(Layout);
                Call(_wheelInput, "Update", _settings);
                if (_previousGamepad != null && _previousGamepad.added) _previousGamepad.MakeCurrent();
                _transitionInstance.SetValue(null, _previousTransition);
                Time.timeScale = _previousTimeScale;
                if (_previousScene.IsValid() && _previousScene.isLoaded) SceneManager.SetActiveScene(_previousScene);
                if (!_scene.IsValid() || !_scene.isLoaded) return;
                foreach (var root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(_scene);
            }
        }

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
        private static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
        private static object Call(object target, string method, params object[] arguments) => target.GetType()
            .GetMethod(method, Fields | BindingFlags.Static).Invoke(target, arguments);
    }
}
