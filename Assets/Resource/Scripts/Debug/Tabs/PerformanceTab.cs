using System;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Debugging
{
    /// <summary>Frame sampling is driven once per Update, never by IMGUI's multiple layout/repaint passes.</summary>
    public sealed class PerformanceTab
    {
        private const int WindowSize = 120;
        private readonly float[] _frameTimes = new float[WindowSize];
        private int _frameCount;
        private int _nextFrame;
        private double _frameTimeSum;
        private float _lastFrameTime;
        private float _memoryTimer;
        private double _secondsSinceReset;
        private long _gcMemory, _allocatedMemory, _reservedMemory, _monoMemory;
        private bool _memorySampled;

        public void Tick(float unscaledDt)
        {
            if (float.IsNaN(unscaledDt) || float.IsInfinity(unscaledDt) || unscaledDt <= 0) return;
            _lastFrameTime = unscaledDt;
            _secondsSinceReset += unscaledDt;
            if (_frameCount == WindowSize) _frameTimeSum -= _frameTimes[_nextFrame];
            else _frameCount++;
            _frameTimes[_nextFrame] = unscaledDt;
            _frameTimeSum += unscaledDt;
            _nextFrame = (_nextFrame + 1) % WindowSize;

            _memoryTimer += unscaledDt;
            if (!_memorySampled || _memoryTimer >= 0.25f)
            {
                SampleMemory();
                _memoryTimer = 0;
            }
        }

        public void Draw(DebugConsole console)
        {
            GUILayout.Label("—— 帧率 ——");
            float instantFps = _lastFrameTime > 0 ? 1f / _lastFrameTime : 0;
            double averageFps = _frameTimeSum > 0 ? _frameCount / _frameTimeSum : 0;
            double averageMs = _frameCount > 0 ? _frameTimeSum * 1000 / _frameCount : 0;
            GUILayout.Label($"瞬时 FPS：{instantFps:F1}    帧时间：{_lastFrameTime * 1000:F2} 毫秒");
            GUILayout.Label($"平均 FPS（近 {_frameCount} / {WindowSize} 帧）：{averageFps:F1}    帧时间：{averageMs:F2} 毫秒");
            GUILayout.Space(12);

            GUILayout.Label("—— 内存 ——");
            GUILayout.Label($"托管堆（GC.GetTotalMemory）：{Megabytes(_gcMemory):F1} MB");
            GUILayout.Label($"Profiler 总分配：{Megabytes(_allocatedMemory):F1} MB    总保留：{Megabytes(_reservedMemory):F1} MB");
            GUILayout.Label($"Mono 已用：{Megabytes(_monoMemory):F1} MB");
            GUILayout.Space(12);

            GUILayout.Label("—— 运行环境 ——");
            GUILayout.Label("当前 Unity 场景：" + SceneManager.GetActiveScene().name);
            GUILayout.Label("游戏运行时长：" + FormatDuration(Time.unscaledTimeAsDouble)
                            + "    本次采样：" + FormatDuration(_secondsSinceReset));
            string target = Application.targetFrameRate <= 0 ? "不锁帧（-1）" : Application.targetFrameRate.ToString();
            GUILayout.Label("目标帧率：" + target + "    垂直同步：" + QualitySettings.vSyncCount);
            Resolution display = Screen.currentResolution;
            GUILayout.Label($"游戏画面：{Screen.width} × {Screen.height}    显示器：{display.width} × {display.height} @ {display.refreshRateRatio.value:F1} Hz");
            GUILayout.Space(12);

            GUILayout.Label("—— 陀螺仪线程 ——");
            var device = console.Runtime.Device;
            if (device == null)
            {
                GUILayout.Label("设备服务尚未运行。");
            }
            else
            {
                GUILayout.Label("传感器回调：" + (device.CallbackRunning ? "运行中" : "未收到新样本")
                                + "    连接线程：" + (device.WorkerRunning ? "运行中" : "已停止"));
                GUILayout.Label($"实际采样率：{device.SamplingRate:F1} Hz    队列积压：{device.QueueCount}    队列溢出丢弃：{device.DroppedSamples}");
                string age = double.IsInfinity(device.SampleAgeSeconds) ? "尚无样本" : (device.SampleAgeSeconds * 1000).ToString("F1") + " 毫秒";
                GUILayout.Label("最近有效样本：" + age);
                GUILayout.Label("设备状态：" + device.Status);
            }
            GUILayout.Space(12);

            GUILayout.Label("—— 操作 ——");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("GC.Collect()  立即回收", GUILayout.Height(32)))
            {
                GC.Collect();
                SampleMemory();
            }
            if (GUILayout.Button("重置采样", GUILayout.Height(32))) Reset();
            GUILayout.EndHorizontal();
        }

        public void Reset()
        {
            Array.Clear(_frameTimes, 0, _frameTimes.Length);
            _frameCount = _nextFrame = 0;
            _frameTimeSum = _secondsSinceReset = 0;
            _lastFrameTime = _memoryTimer = 0;
            SampleMemory();
        }

        private void SampleMemory()
        {
            _gcMemory = GC.GetTotalMemory(false);
            _allocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
            _reservedMemory = Profiler.GetTotalReservedMemoryLong();
            _monoMemory = Profiler.GetMonoUsedSizeLong();
            _memorySampled = true;
        }

        private static double Megabytes(long bytes) => bytes / (1024.0 * 1024.0);
        private static string FormatDuration(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");
    }
}
