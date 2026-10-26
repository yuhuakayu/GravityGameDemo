using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

namespace GravityGame.Tests.PlayMode
{
    public sealed class JoyConInputPlayModeTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private object _device, _bridge;
        private Gamepad _previousGamepad, _sony;

        [SetUp]
        public void SetUp()
        {
            _previousGamepad = Gamepad.current;
            _device = Activator.CreateInstance(FindType("Resource.Scripts.Gyro.GyroDevice"));
            _bridge = Activator.CreateInstance(FindType("Resource.Scripts.Gyro.JoyConGamepadBridge"), true);
        }

        [TearDown]
        public void TearDown()
        {
            (_bridge as IDisposable)?.Dispose();
            (_device as IDisposable)?.Dispose();
            if (_sony != null && _sony.added) InputSystem.RemoveDevice(_sony);
            if (_previousGamepad != null && _previousGamepad.added) _previousGamepad.MakeCurrent();
        }

        [Test]
        public void RightJoyCon_AConfirms_BCancels_AndItsStickNavigates()
        {
            SetControls(2, 0x02000 | 0x04000 | 0x08000, new Vector2(.75f, -.5f));
            UpdateBridge();
            Gamepad pad = BridgedPad;
            Assert.That(pad.buttonSouth.isPressed, Is.True, "Nintendo A confirms.");
            Assert.That(pad.buttonEast.isPressed, Is.False);
            Assert.That(pad.buttonWest.isPressed && pad.buttonNorth.isPressed, Is.True);
            Assert.That(pad.leftStick.ReadUnprocessedValue(), Is.EqualTo(new Vector2(.75f, -.5f)));
            Assert.That(pad.rightStick.ReadUnprocessedValue(), Is.EqualTo(new Vector2(.75f, -.5f)));
            Assert.That(pad.dpad.ReadValue(), Is.EqualTo(Vector2.zero));

            SetControls(2, 0x01000, Vector2.zero);
            UpdateBridge();
            Assert.That(pad.buttonEast.isPressed, Is.True, "Nintendo B cancels.");
            Assert.That(pad.buttonSouth.isPressed, Is.False);
        }

        [Test]
        public void LeftJoyCon_ConfirmAndCancelDoNotAlsoMoveTheDpad()
        {
            SetControls(1, 0x00002 | 0x00040, new Vector2(-.5f, .75f));
            UpdateBridge();
            Gamepad pad = BridgedPad;
            Assert.That(pad.buttonSouth.isPressed, Is.True);
            Assert.That(pad.leftStickButton.isPressed && pad.rightStickButton.isPressed, Is.True);
            Assert.That(pad.leftStick.ReadUnprocessedValue(), Is.EqualTo(new Vector2(-.5f, .75f)));
            Assert.That(pad.dpad.ReadValue(), Is.EqualTo(Vector2.zero), "Confirm must not also navigate down.");

            SetControls(1, 0x00008, Vector2.zero);
            UpdateBridge();
            Assert.That(pad.buttonEast.isPressed, Is.True);
            Assert.That(pad.buttonSouth.isPressed, Is.False);
            Assert.That(pad.dpad.ReadValue(), Is.EqualTo(Vector2.zero));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void EitherJoyCon_ShoulderChordAndMenuButtonReachExistingBindings(int type)
        {
            SetControls(type, 0x40000 | 0x80000, Vector2.zero);
            UpdateBridge();
            Gamepad pad = BridgedPad;
            Assert.That(pad.startButton.isPressed && pad.selectButton.isPressed, Is.True);
            Assert.That(pad.leftShoulder.isPressed && pad.rightShoulder.isPressed, Is.True);

            SetControls(type, type == 1 ? 0x00020 : 0x00010, Vector2.zero);
            UpdateBridge();
            Assert.That(pad.startButton.isPressed, Is.True);
            Assert.That(pad.selectButton.isPressed, Is.False);
            Assert.That(pad.leftShoulder.isPressed || pad.rightShoulder.isPressed, Is.False);
        }

        [Test]
        public void StaleNativeSample_ReleasesAllInputsWithoutReplacingTheDevice()
        {
            SetControls(2, 0x02000 | 0x40000 | 0x80000 | 0x00080, Vector2.one);
            UpdateBridge();
            Gamepad pad = BridgedPad;
            Assert.That(pad.buttonSouth.isPressed, Is.True);
            SetField(_device, "_lastSampleTicks", 0L);
            UpdateBridge();
            Assert.That(BridgedPad, Is.SameAs(pad));
            Assert.That(pad.allControls.OfType<UnityEngine.InputSystem.Controls.ButtonControl>().Any(button => button.isPressed), Is.False);
            Assert.That(pad.leftStick.ReadValue(), Is.EqualTo(Vector2.zero));
            Assert.That(pad.rightStick.ReadValue(), Is.EqualTo(Vector2.zero));
        }

        [TestCase(0)]
        [TestCase(5)]
        public void NonJoyConSelection_LeavesSonyUntouched_AndRemovesTheJoyConBridge(int type)
        {
            _sony = InputSystem.AddDevice<DualSenseGamepadHID>("JoyConBridgeSonyRegression");
            SetControls(type, 0, Vector2.zero);
            UpdateBridge();
            Assert.That(BridgedPad, Is.Null, "PS5 input must keep using its existing Input System device.");
            Assert.That(_sony.added, Is.True);

            SetControls(1, 0, Vector2.zero);
            UpdateBridge();
            Gamepad pad = BridgedPad;
            Assert.That(pad.added, Is.True);
            SetControls(type, 0, Vector2.zero);
            UpdateBridge();
            Assert.That(BridgedPad, Is.Null);
            Assert.That(pad.added, Is.False);
            Assert.That(_sony.added, Is.True);
        }

        [Test]
        public void RightJoyCon_SameGenerationSampleStallPreservesSteeringHemisphereAndBias()
        {
            VerifyRuntimeSampleRecovery(2, true);
        }

        [Test]
        public void DualSense_SameGenerationSampleStallKeepsExistingProcessorReset()
        {
            VerifyRuntimeSampleRecovery(5, false);
        }

        [Test]
        public void RightJoyCon_SameHandleReconnectPreservesStateAcrossConnectionGenerations()
        {
            VerifyRuntimeSampleRecovery(2, true, 8, 42);
        }

        [Test]
        public void RightJoyCon_DifferentHandleReconnectResetsPreviousControllerState()
        {
            VerifyRuntimeSampleRecovery(2, false, 8, 43);
        }

        [Test]
        public void RightJoyCon_DelayedValidSampleContributesOnceWithoutContinuingOnAnEmptyFrame()
        {
            var runtimeObject = new GameObject("Gyro delayed sample regression");
            runtimeObject.SetActive(false);
            try
            {
                // Invoke the real Update with queued reports, without native discovery or hardware.
                Type runtimeType = FindType("Resource.Scripts.Gyro.GyroRuntime");
                Component runtime = runtimeObject.AddComponent(runtimeType);
                object settings = Activator.CreateInstance(FindType("Resource.Scripts.Gyro.GyroSettings"));
                Type processorType = FindType("Resource.Scripts.Gyro.GyroProcessor");
                object processor = Activator.CreateInstance(processorType, new[] { settings });
                SetField(runtime, "<Settings>k__BackingField", settings);
                SetField(runtime, "<Processor>k__BackingField", processor);
                SetField(runtime, "<Device>k__BackingField", _device);
                MethodInfo update = runtimeType.GetMethod("Update", Fields);
                SetControls(2, 0, Vector2.zero);
                update.Invoke(runtime, null);

                Type sampleType = FindType("Resource.Scripts.Gyro.GyroSample");
                object sample = Activator.CreateInstance(sampleType, new object[]
                {
                    0.015f, Vector3.up * 30f, Vector3.back, Vector3.forward,
                    (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency - 0.030
                });
                object queue = _device.GetType().GetField("_samples", Fields).GetValue(_device);
                queue.GetType().GetMethod("Enqueue").Invoke(queue, new[] { sample });
                SetField(_device, "_queuedCount", 1);
                SetField(_device, "_lastSampleTicks", Stopwatch.GetTimestamp());
                update.Invoke(runtime, null);

                Assert.That(runtimeType.GetProperty("FrameStickIntegral").GetValue(runtime), Is.GreaterThan(0d),
                    "A valid report delayed past the display hold window must retain its measured area.");
                Assert.That(runtimeType.GetProperty("HasFreshGyro").GetValue(runtime), Is.EqualTo(true));
                update.Invoke(runtime, null);
                Assert.That(runtimeType.GetProperty("FrameStickIntegral").GetValue(runtime), Is.EqualTo(0d),
                    "An empty frame must not integrate the delayed report for a second time.");
                SetField(_device, "_lastSampleTicks", Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 0.075));
                update.Invoke(runtime, null);
                Assert.That(runtimeType.GetProperty("HasFreshGyro").GetValue(runtime), Is.EqualTo(false));
                Assert.That(runtimeType.GetProperty("FrameStickIntegral").GetValue(runtime), Is.EqualTo(0d));
            }
            finally { UnityEngine.Object.DestroyImmediate(runtimeObject); }
        }

        private void VerifyRuntimeSampleRecovery(int controllerType, bool preserveState, int generationAfter = 7, int handleAfter = 42)
        {
            var runtimeObject = new GameObject("Gyro sample recovery regression");
            runtimeObject.SetActive(false);
            try
            {
                // Keep OnEnable inactive: invoke the real Update without starting native discovery.
                Type runtimeType = FindType("Resource.Scripts.Gyro.GyroRuntime");
                Component runtime = runtimeObject.AddComponent(runtimeType);
                object settings = Activator.CreateInstance(FindType("Resource.Scripts.Gyro.GyroSettings"));
                Type processorType = FindType("Resource.Scripts.Gyro.GyroProcessor");
                object processor = Activator.CreateInstance(processorType, new[] { settings });
                SetField(runtime, "<Settings>k__BackingField", settings);
                SetField(runtime, "<Processor>k__BackingField", processor);
                SetField(runtime, "<Device>k__BackingField", _device);
                MethodInfo update = runtimeType.GetMethod("Update", Fields);
                MethodInfo processSample = processorType.GetMethod("ProcessSample");
                Type sampleType = FindType("Resource.Scripts.Gyro.GyroSample");
                object calibration = processorType.GetProperty("Calibration").GetValue(processor);
                var bias = new Vector3(0.7f, -0.4f, 0.2f);
                Vector3 steeringAxis = controllerType == 1 || controllerType == 2 ? Vector3.up : Vector3.forward;
                object Sample() => Activator.CreateInstance(sampleType, new object[]
                {
                    0.015f, bias + steeringAxis * 30f, Vector3.down, Vector3.up,
                    (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency
                });

                SetControls(controllerType, 0, Vector2.zero);
                SetField(_device, "_connectionGeneration", 7);
                update.Invoke(runtime, null);
                calibration.GetType().GetMethod("SetInitialBias").Invoke(calibration, new object[] { bias });
                processorType.GetMethod("SetInitialSteeringAxis").Invoke(processor, new object[] { steeringAxis, true });
                Assert.That(processSample.Invoke(processor, new[] { Sample() }), Is.EqualTo(true));

                SetField(_device, "_lastSampleTicks", Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 0.075));
                update.Invoke(runtime, null);
                Assert.That(runtimeType.GetProperty("FrameStickIntegral").GetValue(runtime), Is.EqualTo(0d));
                Assert.That(processorType.GetProperty("SteeringAxis").GetValue(processor), Is.EqualTo(steeringAxis));
                Assert.That(calibration.GetType().GetProperty("Bias").GetValue(calibration), Is.EqualTo(bias));

                // A fresh report arrives with the requested connection identity.
                SetField(_device, "_connectionGeneration", generationAfter);
                SetField(_device, "_selectedHandle", handleAfter);
                object queue = _device.GetType().GetField("_samples", Fields).GetValue(_device);
                queue.GetType().GetMethod("Enqueue").Invoke(queue, new[] { Sample() });
                SetField(_device, "_queuedCount", 1);
                SetField(_device, "_lastSampleTicks", Stopwatch.GetTimestamp());
                update.Invoke(runtime, null);

                Assert.That(calibration.GetType().GetProperty("Bias").GetValue(calibration),
                    Is.EqualTo(preserveState ? bias : Vector3.zero));
                Assert.That(calibration.GetType().GetProperty("IsCalibrated").GetValue(calibration), Is.EqualTo(preserveState));
                Assert.That(processorType.GetProperty("ProcessedSamples").GetValue(processor), Is.EqualTo(preserveState ? 2L : 1L));
                if (preserveState)
                {
                    Assert.That(processorType.GetProperty("SteeringAxis").GetValue(processor), Is.EqualTo(steeringAxis));
                    object steering = processorType.GetProperty("LastSteering").GetValue(processor);
                    Assert.That((float)steering.GetType().GetProperty("AngularSpeed").GetValue(steering),
                        Is.EqualTo(30f).Within(0.001f), "A short sensor stall must not reverse the held wheel hemisphere.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(runtimeObject); }
        }

        private Gamepad BridgedPad => (Gamepad)_bridge.GetType().GetField("_gamepad", Fields).GetValue(_bridge);

        private void SetControls(int type, int buttons, Vector2 stick)
        {
            SetField(_device, "_selectedType", type);
            SetField(_device, "_selectedHandle", 42);
            Type controlsType = _device.GetType().GetProperty("Controls").PropertyType;
            ConstructorInfo constructor = controlsType.GetConstructors().Single();
            Type buttonsType = constructor.GetParameters()[0].ParameterType;
            SetField(_device, "_controls", constructor.Invoke(new object[]
            {
                Enum.ToObject(buttonsType, buttons), type == 1 ? stick : Vector2.zero,
                type == 2 ? stick : Vector2.zero, 0f, 0f
            }));
            SetField(_device, "_lastSampleTicks", Stopwatch.GetTimestamp());
        }

        private void UpdateBridge()
        {
            _bridge.GetType().GetMethod("Update", Fields).Invoke(_bridge, new[] { _device });
            InputSystem.Update();
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, Fields).SetValue(target, value);

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);
    }
}
