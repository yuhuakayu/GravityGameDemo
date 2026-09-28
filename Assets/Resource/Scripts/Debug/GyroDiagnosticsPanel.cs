using System;
using System.IO;
using System.Linq;
using Resource.Scripts.Gyro;
using UnityEngine;

namespace Resource.Scripts.Debugging
{
    /// <summary>Owns diagnostic sessions and cached file browsing; never polls the scene in OnGUI.</summary>
    public sealed class GyroDiagnosticsPanel : IDisposable
    {
        private readonly GyroRuntime _runtime;
        private readonly GyroRecorder _recorder;
        private string[] _files = Array.Empty<string>();
        private int _selected;
        private string _status = "录制前先静置 1 秒完成校准；建议录制中也包含一段静止样本。";
        private double _lastRecordedTimestamp;
        public GyroDiagnostics Diagnostics { get; }
        public GyroRecorder Recorder => _recorder;
        public string DirectoryPath { get; }

        public GyroDiagnosticsPanel(GyroRuntime runtime)
        {
            _runtime = runtime;
            DirectoryPath = Path.Combine(Application.persistentDataPath, "GyroRecordings");
            _recorder = new GyroRecorder(DirectoryPath);
            Diagnostics = new GyroDiagnostics(runtime);
            _runtime.SampleProcessed += OnSample;
            RefreshFiles();
        }

        public void Tick()
        {
            Diagnostics.Tick(GyroDevice.NowSeconds);
            if (_recorder.IsRecording && (_runtime.IsPlaybackActive || _runtime.Device == null || !_runtime.Device.IsConnected))
                Run(() => { StopRecording(); _status = "录制已结束：实时设备断线或输入切换；已有样本已保存。"; });
        }

        private void OnSample(GyroSample sample)
        {
            if (!_recorder.IsRecording || _runtime.IsPlaybackActive) return;
            Run(() =>
            {
                if (_lastRecordedTimestamp > 0 && sample.TimestampSeconds - _lastRecordedTimestamp > 0.05)
                {
                    StopRecording();
                    _status = "录制已结束：传感器间隔超过 50 ms；已有样本已保存。";
                    return;
                }
                _lastRecordedTimestamp = sample.TimestampSeconds;
                _recorder.Append(sample);
            });
        }

        public void StartRecording()
        {
            if (_runtime.IsPlaybackActive || Diagnostics.FrameTestRunning || Diagnostics.DriftRunning)
                throw new InvalidOperationException("请先结束当前验证或回放。");
            if (_runtime.Device == null || !_runtime.Device.IsConnected)
                throw new InvalidOperationException("请先连接有实时样本的手柄。");
            _lastRecordedTimestamp = 0;
            Vector3? bias = _runtime.Processor.Calibration.IsCalibrated
                ? _runtime.Processor.Calibration.Bias : (Vector3?)null;
            _status = "正在录制：" + _recorder.Start(bias, _runtime.Processor.SteeringAxis,
                _runtime.Processor.SteeringCrossedDegeneracy);
        }

        public void StopRecording()
        {
            _recorder.Stop();
            _status = $"已保存 {_recorder.SampleCount} 个样本，{_recorder.Duration:F2} 秒：{_recorder.CurrentPath}";
            RefreshFiles();
        }

        public void RefreshFiles()
        {
            Run(() =>
            {
                _files = Directory.Exists(DirectoryPath)
                    ? Directory.GetFiles(DirectoryPath, "*.csv").OrderByDescending(Path.GetFileName).ToArray()
                    : Array.Empty<string>();
                _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _files.Length - 1));
            });
        }

        private GyroRecording LoadSelected()
        {
            if (_files.Length == 0) throw new InvalidOperationException("请先录制一段数据。");
            return GyroRecording.Load(_files[_selected]);
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            { _status = "操作未完成：" + ex.Message; }
        }

        public void Draw(DebugConsole console)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("录制与回放 · 原始传感器 CSV");
            GUILayout.Label(DirectoryPath);
            bool busy = _recorder.IsRecording || _runtime.IsPlaybackActive || Diagnostics.FrameTestRunning || Diagnostics.DriftRunning;
            bool oldEnabled = GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled = oldEnabled && !busy;
            if (GUILayout.Button("开始录制")) Run(StartRecording);
            GUI.enabled = oldEnabled && _recorder.IsRecording;
            if (GUILayout.Button("停止并保存")) Run(StopRecording);
            GUI.enabled = oldEnabled;
            if (GUILayout.Button("刷新文件列表")) RefreshFiles();
            GUILayout.EndHorizontal();
            if (_recorder.IsRecording) GUILayout.Label($"录制中：{_recorder.Duration:F2} s · {_recorder.SampleCount} 个样本");
            GUILayout.Label(_status);
            if (_files.Length == 0) GUILayout.Label("还没有录制文件。");
            else
            {
                // A bounded selector keeps a long recording history from expanding the whole tab.
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("上一份", GUILayout.Width(90))) _selected = Mathf.Max(0, _selected - 1);
                GUILayout.Label($"{_selected + 1}/{_files.Length} · {Path.GetFileName(_files[_selected])}");
                if (GUILayout.Button("下一份", GUILayout.Width(90))) _selected = Mathf.Min(_files.Length - 1, _selected + 1);
                GUILayout.EndHorizontal();
                GUI.enabled = oldEnabled && !busy;
                if (GUILayout.Button("回放所选文件（替代实时陀螺仪）")) Run(() => _runtime.StartPlayback(LoadSelected()));
                GUI.enabled = oldEnabled;
            }
            GUILayout.Label(_runtime.PlaybackStatus + (_runtime.IsPlaybackActive ? $" · {_runtime.PlaybackProgress:P0}" : ""));
            if (_runtime.IsPlaybackActive && GUILayout.Button("停止回放")) _runtime.StopPlayback();
            GUILayout.Label("回放按真实时间推进，暂停时仍可看曲线；要观察世界旋转，请开始关卡并解除暂停。回放期间物理右摇杆不参与，结束后恢复原输入来源。");
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("可靠性验证");
            GUILayout.Label("漂移：把手柄放在桌上，完成校准后静置 5 分钟。累计校准后的方向盘角速度，不使用死区隐藏漂移。");
            GUI.enabled = oldEnabled && !busy;
            if (GUILayout.Button("开始 5 分钟漂移测试")) Run(Diagnostics.StartDriftTest);
            GUI.enabled = oldEnabled;
            GUILayout.Label(Diagnostics.DriftStatus);
            GUILayout.Label($"计时 {Diagnostics.DriftElapsedSeconds:F1} / 300 s · 有效样本 {Diagnostics.DriftSampleSeconds:F1} s · 累计 {Diagnostics.DriftDegrees:F4}° · {Diagnostics.DriftProgress:P0}");
            if (Diagnostics.DriftRunning && GUILayout.Button("中止漂移测试")) Diagnostics.StopDriftTest();
            GUI.enabled = oldEnabled && !busy && _files.Length > 0;
            if (GUILayout.Button("所选录制：60 / 144 / 不锁帧一致性测试"))
                Run(() => Diagnostics.StartFrameRateTest(LoadSelected(), console.World != null ? console.World.rotateSpeed : 90f));
            GUI.enabled = oldEnabled;
            GUILayout.Label("使用启动时的参数快照，独立回放三次；不驱动场景。临时关闭垂直同步并切换帧率上限，完成 / 中止后恢复。");
            GUILayout.Label(Diagnostics.FrameTestStatus);
            if (Diagnostics.FrameTestRunning) GUILayout.Label($"三档测试进度 {Diagnostics.FrameTestProgress:P0}");
            if (Diagnostics.FrameTestRunning && GUILayout.Button("中止帧率测试并恢复设置")) Diagnostics.StopFrameRateTest();
            DrawResults();
            GUILayout.EndVertical();
        }

        private void DrawResults()
        {
            foreach (GyroFrameRateResult result in Diagnostics.FrameResults)
                GUILayout.Label($"{result.Label} · 实测 {result.MeasuredFps:F1} FPS · A {result.SpeedModeDegrees:F6}° · B {result.AngleModeDegrees:F6}° · {result.SampleCount} 样本 · {(result.TargetTimingReached ? "帧率覆盖有效" : "帧率未达目标")}");
            if (Diagnostics.FrameTestCompleted)
                GUILayout.Label($"三档差异：A {Diagnostics.SpeedSpreadPercent:F6}% · B {Diagnostics.AngleSpreadPercent:F6}% · {(Diagnostics.FrameTestPassed ? "通过" : "未通过 / 覆盖不足")}");
        }

        public void Dispose()
        {
            _runtime.SampleProcessed -= OnSample;
            Run(_recorder.Dispose);
            Diagnostics.Dispose();
            _runtime.StopPlayback();
        }
    }
}
