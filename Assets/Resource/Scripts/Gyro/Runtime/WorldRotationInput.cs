using UnityEngine;
using UnityEngine.InputSystem;

namespace Resource.Scripts.Gyro
{
    public readonly struct WorldRotationCommand
    {
        public bool Blocked { get; }
        public bool HasTarget { get; }
        public float TargetAngle { get; }
        public float ClockwiseDelta { get; }
        public float Output { get; }
        public WorldRotationCommand(bool blocked, bool hasTarget, float target, float delta, float output)
        { Blocked = blocked; HasTarget = hasTarget; TargetAngle = target; ClockwiseDelta = delta; Output = output; }
    }

    /// <summary>Feeds the existing rotating world with physical input and callback-time gyro movement.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldRotationInput : MonoBehaviour
    {
        private PlayerController _player;
        private WorldRotator _world;
        private bool _initialized;
        private bool _wasFresh;
        private bool _wasBlocked = true;
        private GyroControlMode _lastMode;
        private GyroInputSource _lastSource;
        private float _lastMultiplier;
        private bool _lastInvert;
        private int _recenterRevision;
        private float _manualAngleOffset;
        private float _smoothedStick;
        private float _sustainTimer;
        private float _lastStickSign;
        private int _evaluatedFrame = -1;
        private WorldRotationCommand _lastCommand;
        private bool _keepReplayAnchor;

        public bool IsSolarPulseActive { get; private set; }
        public int ContinuityRevision { get; private set; }
        public bool IsBlocked { get; private set; } = true;
        public bool IsFallback { get; private set; }
        public bool ModeBActive { get; private set; }
        public float Output { get; private set; }
        public string SourceLabel { get; private set; } = "等待输入初始化";

        private void Awake()
        {
            _world = GetComponent<WorldRotator>();
            _player = FindFirstObjectByType<PlayerController>();
        }

        public void ResetContinuity()
        {
            _initialized = false;
            _evaluatedFrame = -1;
            _keepReplayAnchor = false;
            if (_world != null) _world.DiscardPendingRotation();
        }
        private void OnEnable() { ResetContinuity(); }

        /// <summary>Anchor replay B to its first sensor sample, not the last sample of its first render batch.</summary>
        public void PreparePlaybackAngle(float firstTheta)
        {
            var runtime = GyroRuntime.Current;
            if (runtime == null || _world == null) return;
            _world.DiscardPendingRotation();
            var mapper = runtime.Processor.AngleMapper;
            mapper.SetBlocked(false, firstTheta, _world.ClockwiseAngleReadout);
            mapper.EnterAngleMode(firstTheta, _world.ClockwiseAngleReadout);
            _keepReplayAnchor = true;
            _evaluatedFrame = -1;
        }

        public WorldRotationCommand Evaluate(WorldRotator world)
        {
            if (_evaluatedFrame == Time.frameCount) return _lastCommand;
            _evaluatedFrame = Time.frameCount;
            _world = world;
            var runtime = GyroRuntime.Instance;
            var settings = runtime.Settings;
            GyroInputSource source = runtime.EffectiveInputSource;
            var processor = runtime.Processor;
            float current = world.ClockwiseAngleReadout;
            float theta = processor.LastSteering.Angle;
            bool fresh = runtime.HasFreshGyro;
            IsFallback = source == GyroInputSource.Gyro && !fresh;
            ModeBActive = settings.mode == GyroControlMode.Angle && source != GyroInputSource.RightStick && fresh;
            SourceLabel = runtime.IsPlaybackActive ? "CSV 回放" : IsFallback ? "已回退到右摇杆" : source == GyroInputSource.Gyro ? "陀螺仪" :
                source == GyroInputSource.RightStick ? "右摇杆" : fresh ? "陀螺仪 + 右摇杆" : "两者叠加（当前仅右摇杆）";
            IsBlocked = IsGameplayBlocked(world);

            bool changed = !_initialized || _lastMode != settings.mode || _lastSource != source ||
                _wasFresh != fresh || _wasBlocked != IsBlocked || _lastMultiplier != settings.angleMultiplier ||
                _lastInvert != settings.invertDirection || _recenterRevision != runtime.RecenterRevision;
            if (changed)
            {
                ++ContinuityRevision;
                if (!_keepReplayAnchor || !ModeBActive || IsBlocked)
                    processor.AngleMapper.Recenter(theta, current);
                _manualAngleOffset = 0f;
                _smoothedStick = 0f;
                _sustainTimer = 0f;
                _lastStickSign = 0f;
            }
            processor.AngleMapper.SetBlocked(IsBlocked || !ModeBActive, theta, current);
            _keepReplayAnchor = false;
            _initialized = true;
            _lastMode = settings.mode;
            _lastSource = source;
            _wasFresh = fresh;
            _wasBlocked = IsBlocked;
            _lastMultiplier = settings.angleMultiplier;
            _lastInvert = settings.invertDirection;
            _recenterRevision = runtime.RecenterRevision;
            if (IsBlocked)
            {
                Output = 0f;
                return _lastCommand = new WorldRotationCommand(true, false, current, 0f, 0f);
            }

            float keyboard = ReadKeyboardClockwise();
            bool useStick = source != GyroInputSource.Gyro || !fresh;
            float filteredStick = useStick ? FilterStick(runtime.PhysicalRightStickX, world) : 0f;
            float manual = Mathf.Abs(filteredStick) > 0f ? filteredStick : keyboard;
            if (ModeBActive)
            {
                // Manual movement shifts the world's neutral anchor instead of snapping back next frame.
                float manualForAngle = source == GyroInputSource.Combined ? manual : keyboard;
                _manualAngleOffset += manualForAngle * world.rotateSpeed * Time.deltaTime;
                Output = Mathf.Clamp(runtime.StickOutput + manualForAngle, -1f, 1f);
                return _lastCommand = new WorldRotationCommand(false, true,
                    processor.AngleMapper.OutputAngle + _manualAngleOffset, 0f, Output);
            }

            float delta;
            if (fresh && source != GyroInputSource.RightStick)
            {
                double sensorArea = source == GyroInputSource.Combined
                    ? runtime.FrameCombinedIntegral : runtime.FrameStickIntegral;
                // This area already contains callback deltaTime; never integrate it with render deltaTime.
                delta = (float)(sensorArea * world.rotateSpeed * Time.timeScale) + keyboard * world.rotateSpeed * Time.deltaTime;
                Output = Mathf.Clamp(runtime.StickOutput + keyboard +
                    (source == GyroInputSource.Combined ? runtime.PhysicalRightStickX : 0f), -1f, 1f);
            }
            else
            {
                delta = manual * world.rotateSpeed * Time.deltaTime;
                Output = manual;
            }
            return _lastCommand = new WorldRotationCommand(false, false, current, delta, Output);
        }

        public void SetSolarPulseActive(bool active)
        {
            if (IsSolarPulseActive == active) return;
            IsSolarPulseActive = active;
            _evaluatedFrame = -1;
            if (active) { Output = 0f; IsBlocked = true; }
            Recenter();
        }

        public void Recenter()
        {
            var runtime = GyroRuntime.Current;
            if (runtime == null || _world == null) return;
            _world.DiscardPendingRotation();
            runtime.Processor.AngleMapper.Recenter(runtime.Processor.LastSteering.Angle, _world.ClockwiseAngleReadout);
            _manualAngleOffset = 0f;
        }

        /// <summary>Shared live gate for render input sampling and every real physics step.</summary>
        public bool IsGameplayBlocked(WorldRotator world)
        {
            if (_player == null) _player = FindFirstObjectByType<PlayerController>();
            return IsSolarPulseActive || !world.isActiveAndEnabled || Time.timeScale <= 0f ||
                _player == null || !_player.IsGameplayActive || SceneTransition.Instance.IsTransitioning;
        }

        private float FilterStick(float raw, WorldRotator world)
        {
            _smoothedStick = Mathf.Lerp(_smoothedStick, raw, Time.deltaTime * world.stickSmoothing);
            if (Mathf.Abs(_smoothedStick) < world.stickDeadzone) _sustainTimer = 0f;
            else
            {
                float sign = Mathf.Sign(_smoothedStick);
                if (!Mathf.Approximately(sign, _lastStickSign))
                { _sustainTimer = 0f; _lastStickSign = sign; }
                else _sustainTimer += Time.deltaTime;
            }
            return _sustainTimer >= world.stickSustainTime && Mathf.Abs(_smoothedStick) > world.stickDeadzone
                ? _smoothedStick : 0f;
        }

        private static float ReadKeyboardClockwise()
        {
            if (Keyboard.current == null || GyroRuntime.ConsoleCapturesInput) return 0f;
            float result = Keyboard.current.qKey.isPressed ? -1f : 0f;
            if (Keyboard.current.eKey.isPressed) result = 1f;
            return result;
        }
    }
}
