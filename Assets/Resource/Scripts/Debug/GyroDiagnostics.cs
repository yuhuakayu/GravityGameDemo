using System;
using System.Collections.Generic;
using Resource.Scripts.Gyro;
using UnityEngine;

namespace Resource.Scripts.Debugging
{
    /// <summary>
    /// Main-thread diagnostic service. Tick once per rendered frame using GyroDevice.NowSeconds,
    /// including while Time.timeScale is zero. Sensor integration always uses callback deltaTime.
    /// </summary>
    public sealed class GyroDiagnostics : IDisposable
    {
        public const double DriftDurationSeconds = 300.0;
        private const double MaximumDriftGapSeconds = 0.1;
        private const double MinimumDriftCoverage = 0.99;
        private const double FrameWarmupSeconds = 0.5;
        private static readonly int[] RequestedRates = { 60, 144, -1 };
        private readonly GyroRuntime _runtime;
        private readonly List<GyroFrameRateResult> _frameResults = new List<GyroFrameRateResult>(3);
        private readonly IReadOnlyList<GyroFrameRateResult> _readOnlyResults;
        private bool _disposed;

        private GyroProcessor _driftProcessor;
        private double _driftStart, _driftLastSampleTime;
        private double _nextDriftStatusTime;
        public bool DriftRunning { get; private set; }
        public bool DriftCompleted { get; private set; }
        public bool DriftPassed { get; private set; }
        public float DriftProgress { get; private set; }
        public double DriftDegrees { get; private set; }
        public double DriftElapsedSeconds { get; private set; }
        public double DriftSampleSeconds { get; private set; }
        public long DriftSampleCount { get; private set; }
        public string DriftStatus { get; private set; } = "尚未测试。请先完成校准，再把手柄放在桌上静止 5 分钟。";

        private GyroSample[] _recordedSamples;
        private Vector3? _recordedInitialBias;
        private Vector3? _recordedInitialAxis;
        private bool _recordedAxisCrossedDegeneracy;
        private GyroSettings _frozenSettings;
        private GyroProcessor _frameProcessor;
        private float _worldRotateSpeed;
        private double _recordingDuration;
        private int _runIndex, _sampleIndex;
        private double _warmupStarted, _runStarted, _nextSampleTime, _lastTickTime;
        private double _runSampleSeconds, _runSpeedDegrees, _nextFrameStatusTime;
        private long _runFrames;
        private bool _warmingUp, _runHadLongFrame;
        private int _savedTargetFrameRate, _savedVSync;
        private bool _ownsFrameRate;
        public bool FrameTestRunning { get; private set; }
        public bool FrameTestCompleted { get; private set; }
        public bool FrameTestPassed { get; private set; }
        public bool FrameTimingVerified { get; private set; }
        public float FrameTestProgress { get; private set; }
        public string FrameTestStatus { get; private set; } = "尚未测试。每档在真实渲染帧率下回放；参数在开始时冻结。";
        public double SpeedSpreadPercent { get; private set; }
        public double AngleSpreadPercent { get; private set; }
        public IReadOnlyList<GyroFrameRateResult> FrameResults => _readOnlyResults;

        public GyroDiagnostics(GyroRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            _runtime = runtime;
            _readOnlyResults = _frameResults.AsReadOnly();
            _runtime.SampleProcessed += OnLiveSample;
        }

        public void Tick(double realtimeNow)
        {
            if (_disposed) return;
            if (double.IsNaN(realtimeNow) || double.IsInfinity(realtimeNow)) return;
            if (DriftRunning) TickDrift(realtimeNow);
            if (!FrameTestRunning) return;
            try { TickFrameRate(realtimeNow); }
            catch (Exception exception)
            {
                AbortFrameRate("不完整：测试异常（" + exception.GetType().Name + "）：" + exception.Message);
            }
        }

        public void StartDriftTest()
        {
            ThrowIfDisposed();
            if (FrameTestRunning) throw new InvalidOperationException("请先停止帧率一致性测试。");
            if (_runtime.IsPlaybackActive) throw new InvalidOperationException("漂移测试只接受真机实时样本，请先停止回放。");
            if (_runtime.Device == null || !_runtime.Device.IsConnected || !_runtime.HasFreshGyro)
                throw new InvalidOperationException("未收到真机实时陀螺仪样本。");
            if (!_runtime.Processor.Calibration.IsCalibrated || _runtime.Processor.Calibration.IsManualCalibration)
                throw new InvalidOperationException("请先把手柄静置，等待校准完成。");

            _driftProcessor = _runtime.Processor;
            _driftStart = _driftLastSampleTime = GyroDevice.NowSeconds;
            _nextDriftStatusTime = _driftStart;
            DriftDegrees = DriftElapsedSeconds = DriftSampleSeconds = 0;
            DriftSampleCount = 0;
            DriftProgress = 0;
            DriftCompleted = DriftPassed = false;
            DriftRunning = true;
            DriftStatus = "请把手柄放桌上静止 5 分钟；累计校准后的方向盘角速度，速度死区不会隐藏漂移。";
        }

        public void StopDriftTest()
        {
            if (DriftRunning) AbortDrift("不完整：漂移测试已取消。");
        }

        private void OnLiveSample(GyroSample sample)
        {
            if (!DriftRunning || _disposed) return;
            if (_runtime.IsPlaybackActive || !ReferenceEquals(_runtime.Processor, _driftProcessor))
            {
                AbortDrift("不完整：输入已切换到回放或处理器已重置。");
                return;
            }
            if (!_driftProcessor.Calibration.IsCalibrated || _driftProcessor.Calibration.IsManualCalibration)
            {
                AbortDrift("不完整：校准状态发生变化，请校准后重新测试。");
                return;
            }
            double timestamp = sample.TimestampSeconds;
            if (timestamp <= _driftStart) return; // Reports already queued before the button was clicked.
            if (timestamp - _driftLastSampleTime > MaximumDriftGapSeconds)
            {
                AbortDrift("不完整：实时样本间隔超过 100 毫秒。");
                return;
            }
            if (timestamp < _driftLastSampleTime)
            {
                AbortDrift("不完整：样本时间戳倒退。");
                return;
            }
            _driftLastSampleTime = timestamp;
            double endTime = _driftStart + DriftDurationSeconds;
            // Clip the first/last sensor interval to the actual five-minute wall-clock window.
            double intervalStart = Math.Max(_driftStart, timestamp - sample.DeltaTime);
            double intervalEnd = Math.Min(endTime, timestamp);
            double includedSeconds = Math.Max(0, intervalEnd - intervalStart);
            if (includedSeconds <= 0) return;
            DriftDegrees += _driftProcessor.LastSteering.AngularSpeed * includedSeconds;
            DriftSampleSeconds += includedSeconds;
            DriftSampleCount++;
        }

        private void TickDrift(double now)
        {
            DriftElapsedSeconds = Math.Max(0, Math.Min(DriftDurationSeconds, now - _driftStart));
            DriftProgress = (float)(DriftElapsedSeconds / DriftDurationSeconds);
            if (_runtime == null || _runtime.IsPlaybackActive || _runtime.Device == null || !_runtime.Device.IsConnected)
            {
                AbortDrift("不完整：设备已断开或输入已切换到回放。");
                return;
            }
            if (!_driftProcessor.Calibration.IsCalibrated || _driftProcessor.Calibration.IsManualCalibration)
            {
                AbortDrift("不完整：校准状态发生变化。");
                return;
            }
            if (now - _driftLastSampleTime > MaximumDriftGapSeconds)
            {
                AbortDrift("不完整：超过 100 毫秒没有收到有效样本。");
                return;
            }
            if (now - _driftStart >= DriftDurationSeconds)
            {
                DriftRunning = false;
                DriftCompleted = DriftSampleSeconds >= DriftDurationSeconds * MinimumDriftCoverage;
                DriftPassed = DriftCompleted && Math.Abs(DriftDegrees) < 2.0;
                DriftStatus = !DriftCompleted
                    ? $"不完整：已计时 300 秒，但有效样本仅覆盖 {DriftSampleSeconds:F2} 秒（至少需要 297 秒）。"
                    : $"{(DriftPassed ? "通过" : "失败")}：300 秒累计 {DriftDegrees:F4}°，要求绝对值小于 2°；样本覆盖 {DriftSampleSeconds:F2} 秒。";
                return;
            }
            if (now >= _nextDriftStatusTime)
            {
                _nextDriftStatusTime = now + 0.25;
                DriftStatus = $"请保持手柄静止：{DriftElapsedSeconds:F1} / 300 秒，累计 {DriftDegrees:F4}°，样本覆盖 {DriftSampleSeconds:F2} 秒。";
            }
        }

        private void AbortDrift(string status)
        {
            DriftRunning = false;
            DriftCompleted = DriftPassed = false;
            DriftStatus = status;
        }

        public void StartFrameRateTest(GyroRecording recording, float worldRotateSpeed)
        {
            ThrowIfDisposed();
            if (DriftRunning) throw new InvalidOperationException("请先停止漂移测试。");
            if (FrameTestRunning) throw new InvalidOperationException("帧率一致性测试已经在运行。");
            if (_runtime.IsPlaybackActive) throw new InvalidOperationException("请先停止实时回放，再运行独立帧率测试。");
            if (recording == null || recording.Samples == null || recording.Samples.Count == 0)
                throw new ArgumentException("录制文件没有可回放的样本。", nameof(recording));
            if (float.IsNaN(worldRotateSpeed) || float.IsInfinity(worldRotateSpeed) || worldRotateSpeed <= 0)
                throw new ArgumentOutOfRangeException(nameof(worldRotateSpeed), "世界旋转速度必须大于零。");

            // Freeze both data and parameters, so changing live sliders cannot contaminate comparisons.
            _recordedSamples = new GyroSample[recording.Samples.Count];
            _recordingDuration = 0;
            for (int i = 0; i < _recordedSamples.Length; i++)
            {
                GyroSample sample = recording.Samples[i];
                if (!sample.IsValid) throw new ArgumentException("录制包含无效样本，序号 " + i, nameof(recording));
                _recordedSamples[i] = sample;
                _recordingDuration += sample.DeltaTime;
            }
            _frozenSettings = _runtime.Settings.Clone();
            _frozenSettings.Validate();
            _recordedInitialBias = recording.InitialBias;
            _recordedInitialAxis = recording.InitialAxis;
            _recordedAxisCrossedDegeneracy = recording.InitialAxisCrossedDegeneracy;
            _worldRotateSpeed = worldRotateSpeed;
            _frameResults.Clear();
            FrameTestCompleted = FrameTestPassed = FrameTimingVerified = false;
            FrameTestProgress = 0;
            SpeedSpreadPercent = AngleSpreadPercent = 0;
            _runIndex = 0;
            _savedTargetFrameRate = Application.targetFrameRate;
            _savedVSync = QualitySettings.vSyncCount;
            _ownsFrameRate = true;
            FrameTestRunning = true;
            try { BeginFrameRun(GyroDevice.NowSeconds); }
            catch
            {
                AbortFrameRate("不完整：无法应用测试帧率设置。");
                throw;
            }
        }

        public void StopFrameRateTest()
        {
            if (FrameTestRunning || _ownsFrameRate) AbortFrameRate("不完整：帧率一致性测试已取消，已恢复原帧率与垂直同步。");
        }

        private void BeginFrameRun(double now)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = RequestedRates[_runIndex];
            _frameProcessor = new GyroProcessor(_frozenSettings.Clone());
            if (_recordedInitialBias.HasValue) _frameProcessor.Calibration.SetInitialBias(_recordedInitialBias.Value);
            if (_recordedInitialAxis.HasValue)
                _frameProcessor.SetInitialSteeringAxis(_recordedInitialAxis.Value, _recordedAxisCrossedDegeneracy);
            _sampleIndex = 0;
            _runFrames = 0;
            _runSampleSeconds = _runSpeedDegrees = 0;
            _runHadLongFrame = false;
            _nextSampleTime = _recordedSamples[0].DeltaTime;
            _warmupStarted = _lastTickTime = now;
            _nextFrameStatusTime = now;
            _warmingUp = true;
            FrameTestStatus = $"第 {_runIndex + 1} / 3 档：{RateLabel(RequestedRates[_runIndex])}，稳定帧率中；使用开始时冻结的参数。";
        }

        private void TickFrameRate(double now)
        {
            if (Application.targetFrameRate != RequestedRates[_runIndex] || QualitySettings.vSyncCount != 0)
            {
                AbortFrameRate("不完整：测试期间帧率或垂直同步被外部修改。");
                return;
            }
            if (now < _lastTickTime)
            {
                AbortFrameRate("不完整：计时器倒退。");
                return;
            }
            if (_warmingUp)
            {
                _lastTickTime = now;
                if (now - _warmupStarted < FrameWarmupSeconds) return;
                _warmingUp = false;
                _runStarted = now;
                return;
            }
            double frameTime = now - _lastTickTime;
            _lastTickTime = now;
            if (frameTime <= 0) return;
            if (frameTime > 0.5) _runHadLongFrame = true;
            _runFrames++;
            double elapsed = now - _runStarted;
            while (_sampleIndex < _recordedSamples.Length && _nextSampleTime <= elapsed + 0.0000001)
            {
                GyroSample source = _recordedSamples[_sampleIndex];
                var timedSample = new GyroSample(source.DeltaTime, source.Gyro, source.Acceleration,
                    source.Gravity, _runStarted + _nextSampleTime);
                if (!_frameProcessor.ProcessSample(timedSample))
                    throw new InvalidOperationException("同一处理流程拒绝了已验证样本。");
                if (_sampleIndex == 0)
                    _frameProcessor.AngleMapper.EnterAngleMode(_frameProcessor.LastSteering.Angle, 0f);
                _runSampleSeconds += source.DeltaTime;
                _sampleIndex++;
                if (_sampleIndex < _recordedSamples.Length) _nextSampleTime += _recordedSamples[_sampleIndex].DeltaTime;
            }
            _frameProcessor.EndFrame(now);
            _runSpeedDegrees += _frameProcessor.ConsumeStickIntegral() * _worldRotateSpeed;
            FrameTestProgress = (float)((_runIndex + Math.Min(1, elapsed / _recordingDuration)) / RequestedRates.Length);
            if (_sampleIndex == _recordedSamples.Length)
            {
                CompleteFrameRun(now);
                return;
            }
            if (now >= _nextFrameStatusTime)
            {
                _nextFrameStatusTime = now + 0.25;
                FrameTestStatus = $"第 {_runIndex + 1} / 3 档：{RateLabel(RequestedRates[_runIndex])}，实测 {_runFrames / elapsed:F1} FPS；{elapsed:F1} / {_recordingDuration:F1} 秒。独立回放，不驱动世界。";
            }
        }

        private void CompleteFrameRun(double now)
        {
            double seconds = Math.Max(0.000001, now - _runStarted);
            double actualFps = _runFrames / seconds;
            int requested = RequestedRates[_runIndex];
            bool timingReached = !_runHadLongFrame && (requested < 0 || Math.Abs(actualFps - requested) / requested <= 0.15);
            _frameResults.Add(new GyroFrameRateResult(requested, actualFps, _runSpeedDegrees,
                _frameProcessor.AngleMapper.OutputAngle, seconds, _runSampleSeconds,
                _frameProcessor.ProcessedSamples, timingReached));
            _runIndex++;
            if (_runIndex < RequestedRates.Length)
            {
                BeginFrameRun(now);
                return;
            }

            SpeedSpreadPercent = SpreadPercent(false);
            AngleSpreadPercent = SpreadPercent(true);
            FrameTimingVerified = true;
            foreach (var result in _frameResults) FrameTimingVerified &= result.TargetTimingReached;
            bool consistent = SpeedSpreadPercent < 1.0 && AngleSpreadPercent < 1.0;
            FrameTestCompleted = true;
            FrameTestPassed = consistent && FrameTimingVerified;
            FrameTestProgress = 1;
            FrameTestRunning = false;
            RestoreFrameRate();
            ReleaseFrameTestBuffers();
            string verdict = !consistent ? "失败" : FrameTimingVerified ? "通过" : "数值一致，但实际帧率覆盖不足，不能判定实测通过";
            FrameTestStatus = $"{verdict}。A 角度差异 {SpeedSpreadPercent:F6}%，B 角度差异 {AngleSpreadPercent:F6}%；误差分母至少为 1°。原帧率与垂直同步已恢复。";
        }

        private double SpreadPercent(bool angleMode)
        {
            double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity, scale = 1;
            foreach (var result in _frameResults)
            {
                double value = angleMode ? result.AngleModeDegrees : result.SpeedModeDegrees;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                scale = Math.Max(scale, Math.Abs(value));
            }
            return (maximum - minimum) / scale * 100.0;
        }

        private void AbortFrameRate(string status)
        {
            FrameTestRunning = false;
            FrameTestCompleted = FrameTestPassed = FrameTimingVerified = false;
            try { RestoreFrameRate(); }
            finally
            {
                ReleaseFrameTestBuffers();
                FrameTestStatus = status;
            }
        }

        private void ReleaseFrameTestBuffers()
        {
            _recordedSamples = null;
            _frameProcessor = null;
            _frozenSettings = null;
            _recordedInitialBias = null;
        }

        private void RestoreFrameRate()
        {
            if (!_ownsFrameRate) return;
            try { Application.targetFrameRate = _savedTargetFrameRate; }
            finally
            {
                try { QualitySettings.vSyncCount = _savedVSync; }
                finally { _ownsFrameRate = false; }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GyroDiagnostics));
        }

        internal static string RateLabel(int requested) => requested < 0 ? "不锁帧（-1）" : requested + " FPS";

        public void Dispose()
        {
            if (_disposed) return;
            _runtime.SampleProcessed -= OnLiveSample;
            StopDriftTest();
            try { StopFrameRateTest(); }
            finally { _disposed = true; }
        }
    }

    public sealed class GyroFrameRateResult
    {
        public int RequestedFrameRate { get; }
        public string Label => GyroDiagnostics.RateLabel(RequestedFrameRate);
        public double MeasuredFps { get; }
        public double SpeedModeDegrees { get; }
        public double AngleModeDegrees { get; }
        public double WallSeconds { get; }
        public double SampleSeconds { get; }
        public long SampleCount { get; }
        public bool TargetTimingReached { get; }

        internal GyroFrameRateResult(int requestedFrameRate, double measuredFps, double speedModeDegrees,
            double angleModeDegrees, double wallSeconds, double sampleSeconds, long sampleCount, bool timingReached)
        {
            RequestedFrameRate = requestedFrameRate;
            MeasuredFps = measuredFps;
            SpeedModeDegrees = speedModeDegrees;
            AngleModeDegrees = angleModeDegrees;
            WallSeconds = wallSeconds;
            SampleSeconds = sampleSeconds;
            SampleCount = sampleCount;
            TargetTimingReached = timingReached;
        }
    }
}
