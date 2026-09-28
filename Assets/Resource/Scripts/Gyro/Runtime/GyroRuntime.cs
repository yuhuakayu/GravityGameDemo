using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Processors;

namespace Resource.Scripts.Gyro
{
    /// <summary>Release-safe, persistent owner of sensor processing. All Unity access stays on this thread.</summary>
    [DefaultExecutionOrder(-300)]
    public sealed class GyroRuntime : MonoBehaviour
    {
        private static GyroRuntime _instance;
        private bool _wasConnected;
        private readonly GyroStickMapper _mapper = new GyroStickMapper();
        private readonly StickDeadzoneProcessor _stickDeadzone = new StickDeadzoneProcessor();
        private static bool _consoleCapturesInput;
        private GyroRecording _playback;
        private int _playbackIndex;
        private double _playbackStarted;
        private double _playbackSampleTime;
        private bool _playbackFinishedFrame;
        private WorldRotator _playbackWorld;

        public static GyroRuntime Current => _instance;
        public static GyroRuntime Instance
        {
            get
            {
                if (_instance == null) new GameObject("Gyro Runtime").AddComponent<GyroRuntime>();
                return _instance;
            }
        }
        public GyroSettings Settings { get; private set; }
        public GyroProcessor Processor { get; private set; }
        public GyroDevice Device { get; private set; }
        public string SettingsStatus { get; private set; }
        public double FrameStickIntegral { get; private set; }
        public double FrameCombinedIntegral { get; private set; }
        public float PhysicalRightStickX { get; private set; }
        public float StickOutput => HasFreshGyro ? Processor.StickOutput : 0f;
        public bool HasFreshGyro => Processor != null && Processor.IsFresh(GyroDevice.NowSeconds) &&
            (IsPlaybackActive || (Device != null && Device.IsConnected));
        public bool IsPlaybackActive => _playback != null;
        public GyroInputSource EffectiveInputSource => IsPlaybackActive ? GyroInputSource.Gyro : Settings.inputSource;
        public float PlaybackProgress => _playback == null ? 0f :
            Mathf.Clamp01((float)((GyroDevice.NowSeconds - _playbackStarted) / _playback.Duration));
        public string PlaybackStatus { get; private set; } = "尚未回放";
        public GyroButtons PressedButtons { get; private set; }
        public GyroControllerState Controls => Device != null ? Device.Controls : default;
        public int RecenterRevision { get; private set; }
        public event Action<GyroSample> SampleProcessed;

        public static bool ConsoleCapturesInput
        {
            get
            {
                return _consoleCapturesInput || IsConsoleChordHeld;
            }
            set => _consoleCapturesInput = value;
        }

        public static bool IsConsoleChordHeld
        {
            get
            {
                foreach (var pad in Gamepad.all)
                    if (pad.startButton.isPressed && pad.selectButton.isPressed) return true;
                return _instance != null && _instance.Controls.IsPressed(GyroButtons.Options | GyroButtons.Create);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; _consoleCapturesInput = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() { _ = Instance; }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Settings = GyroSettingsStore.Load(out string status);
            SettingsStatus = status;
            Processor = new GyroProcessor(Settings);
        }

        private void OnEnable()
        {
            // Script reload invokes OnEnable on surviving components without a fresh Awake.
            if (_instance == null) _instance = this;
            if (_instance != this) return;
            if (Settings == null)
            {
                Settings = GyroSettingsStore.Load(out string status);
                SettingsStatus = status;
            }
            if (Processor == null) Processor = new GyroProcessor(Settings);
            Device = new GyroDevice();
            Device.Start();
        }

        private void Update()
        {
            if (Device == null) return;
            if (_playbackFinishedFrame) FinishPlayback("回放完成");
            bool connected = Device.IsConnected;
            if (!IsPlaybackActive)
            {
                if (connected && !_wasConnected) Processor.Reset();
                if (!connected && _wasConnected) Processor.Disconnect();
            }
            _wasConnected = connected;
            PressedButtons = Device.ConsumePressedButtons();
            PhysicalRightStickX = IsPlaybackActive ? 0f : ReadPhysicalRightStick();
            FrameCombinedIntegral = 0;
            if (IsPlaybackActive)
            {
                // Native callbacks continue for buttons/reconnection, but cannot enter a replay stream.
                Device.ClearSamples();
                double now = GyroDevice.NowSeconds;
                double elapsed = now - _playbackStarted;
                while (_playbackIndex < _playback.Samples.Count)
                {
                    GyroSample recorded = _playback.Samples[_playbackIndex];
                    double due = _playbackSampleTime + recorded.DeltaTime;
                    if (due > elapsed) break;
                    _playbackSampleTime = due;
                    ++_playbackIndex;
                    // Delivery freshness uses this frame's clock; integration retains the recorded dt.
                    ProcessSensorSample(new GyroSample(recorded.DeltaTime, recorded.Gyro,
                        recorded.Acceleration, recorded.Gravity, now));
                    if (_playbackIndex == 1 && _playbackWorld != null && _playbackWorld.RotationInput != null)
                        _playbackWorld.RotationInput.PreparePlaybackAngle(Processor.LastSteering.Angle);
                }
                // Let WorldRotator consume the final frame before switching back to live input.
                _playbackFinishedFrame = _playbackIndex == _playback.Samples.Count;
            }
            else while (Device.TryDequeue(out GyroSample sample)) ProcessSensorSample(sample);
            Processor.EndFrame(GyroDevice.NowSeconds);
            FrameStickIntegral = Processor.ConsumeStickIntegral();
            if (!HasFreshGyro) FrameStickIntegral = FrameCombinedIntegral = 0;
            if (!ConsoleCapturesInput && RecenterButtonPressed()) Recenter();
        }

        private void ProcessSensorSample(GyroSample sample)
        {
            if (!Processor.ProcessSample(sample)) return;
            float mapped = _mapper.Map(Processor.LastSteering.AngularSpeed, Settings);
            FrameCombinedIntegral += Mathf.Clamp(mapped + PhysicalRightStickX, -1f, 1f) * sample.DeltaTime;
            SampleProcessed?.Invoke(sample);
        }

        private float ReadPhysicalRightStick()
        {
            // Prefer the physical report, which cannot contain Steam's gyro-to-virtual-stick mapping.
            if (Device.IsConnected) return _stickDeadzone.Process(Device.Controls.RightStick, null).x;
            float best = 0f;
            foreach (var pad in Gamepad.all)
            {
                float value = pad.rightStick.x.ReadValue();
                if (Mathf.Abs(value) > Mathf.Abs(best)) best = value;
            }
            return best;
        }

        private bool RecenterButtonPressed()
        {
            GyroButtons nativeButton;
            switch (Settings.recenterButton)
            {
                case GyroRecenterButton.RightStick: nativeButton = GyroButtons.RightStick; break;
                case GyroRecenterButton.Touchpad: nativeButton = GyroButtons.Touchpad; break;
                case GyroRecenterButton.South: nativeButton = GyroButtons.South; break;
                case GyroRecenterButton.North: nativeButton = GyroButtons.North; break;
                case GyroRecenterButton.LeftShoulder: nativeButton = GyroButtons.LeftShoulder; break;
                case GyroRecenterButton.RightShoulder: nativeButton = GyroButtons.RightShoulder; break;
                default: nativeButton = GyroButtons.LeftStick; break;
            }
            if ((PressedButtons & nativeButton) != 0) return true;
            foreach (var pad in Gamepad.all)
            {
                switch (Settings.recenterButton)
                {
                    case GyroRecenterButton.LeftStick: if (pad.leftStickButton.wasPressedThisFrame) return true; break;
                    case GyroRecenterButton.RightStick: if (pad.rightStickButton.wasPressedThisFrame) return true; break;
                    case GyroRecenterButton.Touchpad:
                        if (pad is DualShockGamepad ds && ds.touchpadButton.wasPressedThisFrame) return true;
                        break;
                    case GyroRecenterButton.South: if (pad.buttonSouth.wasPressedThisFrame) return true; break;
                    case GyroRecenterButton.North: if (pad.buttonNorth.wasPressedThisFrame) return true; break;
                    case GyroRecenterButton.LeftShoulder: if (pad.leftShoulder.wasPressedThisFrame) return true; break;
                    case GyroRecenterButton.RightShoulder: if (pad.rightShoulder.wasPressedThisFrame) return true; break;
                }
            }
            return false;
        }

        public void Recenter() { RecenterRevision++; }
        public void StartPlayback(GyroRecording recording)
        {
            if (recording == null || recording.Samples.Count == 0)
                throw new ArgumentException("录制文件没有有效样本", nameof(recording));
            if (IsPlaybackActive) FinishPlayback("已切换回放");
            _playback = recording;
            _playbackIndex = 0;
            _playbackSampleTime = 0;
            _playbackStarted = GyroDevice.NowSeconds;
            _playbackWorld = FindFirstObjectByType<WorldRotator>();
            _playbackFinishedFrame = false;
            Device?.ClearSamples();
            Processor.Reset();
            if (recording.InitialBias.HasValue) Processor.Calibration.SetInitialBias(recording.InitialBias.Value);
            if (recording.InitialAxis.HasValue)
                Processor.SetInitialSteeringAxis(recording.InitialAxis.Value, recording.InitialAxisCrossedDegeneracy);
            FrameStickIntegral = FrameCombinedIntegral = 0;
            PhysicalRightStickX = 0;
            PlaybackStatus = "回放中：" + System.IO.Path.GetFileName(recording.FilePath);
            Recenter();
        }

        public void StopPlayback() { FinishPlayback("回放已停止"); }

        private void FinishPlayback(string status)
        {
            if (!IsPlaybackActive) return;
            _playback = null;
            _playbackWorld = null;
            _playbackFinishedFrame = false;
            PlaybackStatus = status;
            Processor.Reset();
            Device?.ClearSamples();
            FrameStickIntegral = FrameCombinedIntegral = 0;
            PhysicalRightStickX = 0;
            _wasConnected = false;
            Recenter();
        }

        public void Calibrate() { Processor.Calibration.BeginManualCalibration(); }
        public void SaveSettings()
        {
            GyroSettingsStore.Save(Settings, out string status);
            SettingsStatus = status;
        }
        public void RestoreDefaults()
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new GyroSettings()), Settings);
            Settings.Validate();
            Recenter();
            SettingsStatus = "已恢复默认值；点击保存后写入文件";
        }

        private void OnDisable()
        {
            StopPlayback();
            Device?.Dispose();
            Device = null;
            FrameStickIntegral = FrameCombinedIntegral = 0;
            _wasConnected = false;
        }
        private void OnDestroy() { if (_instance == this) _instance = null; }
    }
}
