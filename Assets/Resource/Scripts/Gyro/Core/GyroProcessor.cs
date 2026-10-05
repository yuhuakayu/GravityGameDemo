using UnityEngine;

namespace Resource.Scripts.Gyro
{
    /// <summary>Shared live/replay pipeline. Process every callback; EndFrame only groups virtual-stick samples.</summary>
    public sealed class GyroProcessor
    {
        public const double HoldSeconds = 0.020;
        private readonly SteeringExtractor _extractor = new SteeringExtractor();
        private readonly GyroStickMapper _stickMapper = new GyroStickMapper();
        private double _frameWeightedOutput;
        private double _frameSampleSeconds;
        private double _unconsumedStickIntegral;
        private double _lastSampleTime = double.NegativeInfinity;
        private double _sampleClock;
        private float _shakeHoldRemaining;

        public GyroSettings Settings { get; }
        public GyroCalibration Calibration { get; } = new GyroCalibration();
        public WheelAngleMapper AngleMapper { get; }
        public SteeringReading LastSteering { get; private set; }
        public GyroSample LastSample { get; private set; }
        public float StickOutput { get; private set; }
        public float GatedSpeed { get; private set; }
        public float Purity { get; private set; } = 1f;
        public float ShakeWeight { get; private set; } = 1f;
        public bool IsShakeHolding => _shakeHoldRemaining > 0f;
        public double IntegratedStickSeconds { get; private set; }
        public double IntegratedWheelDegrees { get; private set; }
        public long ProcessedSamples { get; private set; }
        public bool HasSample => ProcessedSamples > 0;
        public Vector3? SteeringAxis => _extractor.HasAxis ? _extractor.LastAxis : (Vector3?)null;
        public bool SteeringCrossedDegeneracy => _extractor.CrossedDegeneracy;

        public GyroProcessor(GyroSettings settings)
        {
            Settings = settings;
            AngleMapper = new WheelAngleMapper(settings);
        }

        public bool ProcessSample(GyroSample sample)
        {
            if (!sample.IsValid) return false;
            LastSample = sample;
            Vector3 corrected = Calibration.Process(sample);
            LastSteering = _extractor.Extract(corrected, sample.Gravity);
            float speed = LastSteering.AngularSpeed;
            float magnitude = corrected.magnitude;
            Purity = magnitude < 0.001f ? 1f : Mathf.Abs(speed) / magnitude;
            _shakeHoldRemaining = Mathf.Max(0f, _shakeHoldRemaining - sample.DeltaTime);
            if (Settings.shakeReject)
            {
                float offAxis = Mathf.Sqrt(Mathf.Max(0f, corrected.sqrMagnitude - speed * speed));
                if (offAxis > Settings.offAxisTrigger && Purity < Settings.purityLow)
                    _shakeHoldRemaining = Settings.shakeHoldTime;
                ShakeWeight = IsShakeHolding ? 0f : Mathf.Clamp01(
                    (Purity - Settings.purityLow) / (Settings.purityHigh - Settings.purityLow));
            }
            else
            {
                _shakeHoldRemaining = 0f;
                ShakeWeight = 1f;
            }
            GatedSpeed = speed * ShakeWeight;
            float value = _stickMapper.Map(GatedSpeed, Settings);
            _frameWeightedOutput += value * sample.DeltaTime;
            _frameSampleSeconds += sample.DeltaTime;
            IntegratedStickSeconds += value * sample.DeltaTime;
            _unconsumedStickIntegral += value * sample.DeltaTime;
            IntegratedWheelDegrees += LastSteering.AngularSpeed * sample.DeltaTime;
            _sampleClock += sample.DeltaTime;
            _lastSampleTime = sample.TimestampSeconds > 0 ? sample.TimestampSeconds : _sampleClock;
            float acceleration = sample.Acceleration.magnitude;
            if (!Settings.shakeReject || (ShakeWeight >= 0.5f &&
                (acceleration == 0f || Mathf.Abs(acceleration - 1f) <= 0.15f)))
                AngleMapper.Evaluate(LastSteering.Angle, sample.DeltaTime);
            ++ProcessedSamples;
            return true;
        }

        public float EndFrame(double nowSeconds)
        {
            if (_frameSampleSeconds > 0)
                StickOutput = (float)(_frameWeightedOutput / _frameSampleSeconds);
            if (nowSeconds - _lastSampleTime > HoldSeconds || !HasSample)
                StickOutput = 0f;
            _frameWeightedOutput = 0;
            _frameSampleSeconds = 0;
            return StickOutput;
        }

        public bool IsFresh(double nowSeconds) => HasSample && nowSeconds - _lastSampleTime <= HoldSeconds;

        public void SetInitialSteeringAxis(Vector3 axis, bool crossedDegeneracy) =>
            _extractor.SetInitialAxis(axis, crossedDegeneracy);

        /// <summary>
        /// Consume virtual-stick seconds exactly once for world movement. The UI/frame stick average is
        /// not an integration clock: multiplying its sample-and-hold value by render dt aliases input
        /// whenever render FPS exceeds sensor Hz. Paused callers must consume and discard this area.
        /// </summary>
        public double ConsumeStickIntegral()
        {
            double result = _unconsumedStickIntegral;
            _unconsumedStickIntegral = 0;
            return result;
        }

        public void Disconnect()
        {
            StickOutput = 0f;
            GatedSpeed = 0f;
            Purity = ShakeWeight = 1f;
            _shakeHoldRemaining = 0f;
            _frameWeightedOutput = 0;
            _frameSampleSeconds = 0;
            _unconsumedStickIntegral = 0;
            _lastSampleTime = double.NegativeInfinity;
        }

        public void Reset()
        {
            Disconnect();
            _extractor.Reset();
            Calibration.Reset();
            _sampleClock = 0;
            ProcessedSamples = 0;
            IntegratedStickSeconds = 0;
            IntegratedWheelDegrees = 0;
            LastSample = default;
            LastSteering = default;
            AngleMapper.SetBlocked(false, 0f, 0f);
            AngleMapper.Recenter(0f, 0f);
        }
    }
}
