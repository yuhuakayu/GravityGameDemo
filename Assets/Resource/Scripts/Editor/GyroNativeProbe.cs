#if UNITY_EDITOR
using System;
using Resource.Scripts.Gyro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.LowLevel;

namespace Resource.Scripts.Editor
{
    /// <summary>Bounded native smoke test, with cleanup before domain reload. Does not start gameplay.</summary>
    public static class GyroNativeProbe
    {
        private static GyroDevice _device;
        private static GyroProcessor _processor;
        private static double _started;
        private static int _unityReports;
        public static string LastResult { get; private set; } = "尚未运行";
        public static bool IsRunning => _device != null;

        public static void Begin()
        {
            if (EditorApplication.isPlaying) return;
            Stop();
            _processor = new GyroProcessor(new GyroSettings());
            _device = new GyroDevice();
            _started = GyroDevice.NowSeconds;
            _unityReports = 0;
            LastResult = "正在采样，10 秒后自动停止";
            InputSystem.onEvent += OnInputEvent;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _device.Start();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Stop();
        }

        private static void OnInputEvent(InputEventPtr inputEvent, InputDevice device)
        {
            if (device is DualShockGamepad && (inputEvent.type == StateEvent.Type || inputEvent.type == DeltaStateEvent.Type))
                _unityReports++;
        }

        private static void Tick()
        {
            if (_device == null) return;
            while (_device.TryDequeue(out GyroSample sample)) _processor.ProcessSample(sample);
            _processor.EndFrame(GyroDevice.NowSeconds);
            if (GyroDevice.NowSeconds - _started >= 10) Stop();
        }

        public static void Stop()
        {
            if (_device == null) return;
            var snapshot = new ProbeResult
            {
                seconds = (float)(GyroDevice.NowSeconds - _started),
                connected = _device.IsConnected,
                model = _device.Model,
                transport = _device.Transport,
                samplesPerSecond = _device.SamplingRate,
                processedSamples = _processor.ProcessedSamples,
                nativeStatus = _device.Status,
                unityInputReports = _unityReports,
                gyro = _processor.LastSample.Gyro,
                gravity = _processor.LastSample.Gravity,
                calibrationBias = _processor.Calibration.Bias,
                calibrated = _processor.Calibration.IsCalibrated,
                output = _processor.StickOutput,
                dropped = _device.DroppedSamples
            };
            InputSystem.onEvent -= OnInputEvent;
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.quitting -= Stop;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _device.Dispose();
            _device = null;
            LastResult = JsonUtility.ToJson(snapshot, true);
        }

        [Serializable]
        private sealed class ProbeResult
        {
            public float seconds, samplesPerSecond, output;
            public bool connected, calibrated;
            public string model, transport, nativeStatus;
            public long processedSamples;
            public int unityInputReports, dropped;
            public Vector3 gyro, gravity, calibrationBias;
        }
    }
}
#endif
