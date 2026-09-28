using System;
using Resource.Scripts.Gyro;
using UnityEngine;

namespace Resource.Scripts.Debugging
{
    /// <summary>Live gyro tuning and five-second callback history. All samples arrive on the Unity thread.</summary>
    public sealed class GyroTab : IDisposable
    {
        private const int HistoryCapacity = 8192;
        private const double HistorySeconds = 5.0;
        private const int GraphPointLimit = 300;
        private static readonly string[] ModeLabels = { "模式 A · 速度", "模式 B · 角度" };
        private static readonly string[] SourceLabels = { "陀螺仪", "右摇杆", "两者叠加" };
        private static readonly string[] RecenterLabels = { "L3", "R3", "触控板", "× / A", "△ / Y", "L1", "R1" };
        private static readonly string[] RawLegendLabels = { "X", "Y", "Z" };
        private static readonly string[] FilterLegendLabels = { "方向盘 ωs", "被过滤分量" };
        private static readonly Color XColor = new Color(1f, 0.38f, 0.32f);
        private static readonly Color YColor = new Color(0.37f, 0.92f, 0.53f);
        private static readonly Color ZColor = new Color(0.35f, 0.68f, 1f);
        private static readonly Color SteeringColor = new Color(0.3f, 0.93f, 0.97f);
        private static readonly Color FilteredColor = new Color(1f, 0.74f, 0.27f);
        private static readonly Color[] RawLegendColors = { XColor, YColor, ZColor };
        private static readonly Color[] FilterLegendColors = { SteeringColor, FilteredColor };
        private readonly GyroRuntime _runtime;
        private readonly GraphSample[] _history = new GraphSample[HistoryCapacity];
        private int _next;
        private int _count;
        private bool _disposed;

        private struct GraphSample
        {
            public double Time;
            public Vector3 Raw;
            public float Wheel;
            public float Filtered;
        }

        public GyroTab(GyroRuntime runtime)
        {
            _runtime = runtime;
            _runtime.SampleProcessed += OnSample;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_runtime != null) _runtime.SampleProcessed -= OnSample;
        }

        private void OnSample(GyroSample sample)
        {
            double now = GyroDevice.NowSeconds;
            // Live samples keep their callback timestamps; replay can supply a relative recording clock.
            double time = Math.Abs(sample.TimestampSeconds - now) < 10 ? sample.TimestampSeconds : now;
            SteeringReading reading = _runtime.Processor.LastSteering;
            _history[_next] = new GraphSample
            {
                Time = time, Raw = sample.Gyro, Wheel = reading.AngularSpeed, Filtered = reading.FilteredSpeed
            };
            _next = (_next + 1) % HistoryCapacity;
            _count = Mathf.Min(_count + 1, HistoryCapacity);
        }

        public void Draw(DebugConsole console)
        {
            if (_runtime == null || _runtime.Processor == null)
            {
                GUILayout.Label("陀螺仪服务尚未就绪。");
                return;
            }
            GyroSettings settings = _runtime.Settings;
            DrawStatus(console.World);
            GUILayout.Space(6f);
            GUILayout.Label("控制模式");
            settings.mode = (GyroControlMode)GUILayout.Toolbar((int)settings.mode, ModeLabels, GUILayout.Height(28f));
            GUILayout.Label("输入来源");
            bool inputEnabled = GUI.enabled;
            GUI.enabled = inputEnabled && !_runtime.IsPlaybackActive;
            GyroInputSource selectedSource = (GyroInputSource)GUILayout.Toolbar((int)_runtime.EffectiveInputSource, SourceLabels, GUILayout.Height(28f));
            if (!_runtime.IsPlaybackActive) settings.inputSource = selectedSource;
            GUI.enabled = inputEnabled;
            settings.invertDirection = GUILayout.Toggle(settings.invertDirection, "反转方向（默认：方向盘顺时针 → 世界顺时针）");
            if (settings.mode == GyroControlMode.Angle && settings.inputSource == GyroInputSource.Combined)
                GUILayout.Label("角度模式叠加：右摇杆调整世界基准，方向盘在此基准上转动。");
            if (settings.mode == GyroControlMode.Angle && settings.inputSource == GyroInputSource.RightStick)
                GUILayout.Label("右摇杆来源使用速度控制；切回陀螺仪后恢复角度控制。");

            GUILayout.Space(5f);
            bool wide = console.ContentWidth >= 1060f;
            if (wide) GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("模式 A · 速度参数");
            settings.sensitivity = ConsoleUi.Slider("灵敏度", settings.sensitivity, 0f, 20f, "F2", "x");
            settings.minInputSpeed = ConsoleUi.Slider("最低输入速度", settings.minInputSpeed, 0f, 1000f, "F1", "°/s");
            settings.maxInputSpeed = ConsoleUi.Slider("最高输入速度", settings.maxInputSpeed,
                Mathf.Max(1f, settings.minInputSpeed + 0.01f), 2000f, "F1", "°/s");
            settings.minOutput = ConsoleUi.Slider("输出最小值", settings.minOutput, 0f, 1f, "P1");
            settings.maxOutput = ConsoleUi.Slider("输出最大值", settings.maxOutput, settings.minOutput, 1f, "P1");
            settings.outputCurve = ConsoleUi.Slider("输出曲线", settings.outputCurve, 0.1f, 4f, "F2");
            settings.speedDeadzone = ConsoleUi.Slider("速度死区", settings.speedDeadzone, 0f, 20f, "F2", "°/s");
            settings.precisionSpeed = ConsoleUi.Slider("精确速度", settings.precisionSpeed, 0f, 2000f, "F1", "°/s");
            GUILayout.Label("曲线 1 = 线性；精确速度 0 = 关闭。");
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("模式 B · 角度参数");
            settings.angleMultiplier = ConsoleUi.Slider("角度倍率", settings.angleMultiplier, 0.5f, 4f, "F2", "x");
            settings.angleSmoothTime = ConsoleUi.Slider("平滑时间", settings.angleSmoothTime, 0f, 1f, "F3", "s");
            settings.angleDeadzone = ConsoleUi.Slider("角度死区", settings.angleDeadzone, 0f, 20f, "F2", "°");
            GUILayout.Label("回正按键");
            settings.recenterButton = (GyroRecenterButton)GUILayout.SelectionGrid((int)settings.recenterButton,
                RecenterLabels, 4, GUILayout.Height(54f));
            GUILayout.Label("回正保留世界当前朝向。角度测量范围：−90° ～ +90°。");
            GUILayout.Space(8f);
            DrawCalibration();
            GUILayout.EndVertical();
            if (wide) GUILayout.EndHorizontal();
            settings.Validate();

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("校准（静止 3 秒）", GUILayout.Height(30f))) _runtime.Calibrate();
            if (GUILayout.Button("回正", GUILayout.Height(30f))) _runtime.Recenter();
            if (GUILayout.Button("恢复默认", GUILayout.Height(30f)))
            {
                _runtime.RestoreDefaults();
                console.SetPauseOnOpen(_runtime.Settings.pauseOnOpen);
                console.SetTimeScale(_runtime.Settings.timeScale);
            }
            if (GUILayout.Button("保存参数", GUILayout.Height(30f))) _runtime.SaveSettings();
            GUILayout.EndHorizontal();
            GUILayout.Label(_runtime.SettingsStatus + "  ·  修改立即生效，点击保存才写入文件。");

            GUILayout.Space(7f);
            if (wide) GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUI.skin.box);
            DrawLegend("原始陀螺仪 · 最近 5 秒", RawLegendLabels, RawLegendColors);
            DrawHistoryGraph(false);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUI.skin.box);
            DrawLegend("方向盘过滤 · 最近 5 秒", FilterLegendLabels, FilterLegendColors);
            DrawHistoryGraph(true);
            GUILayout.EndVertical();
            if (wide) GUILayout.EndHorizontal();

            DrawReadouts(console.World);
            GUILayout.Space(7f);
            console.DrawDiagnostics();
        }

        private void DrawStatus(WorldRotator world)
        {
            GyroDevice device = _runtime.Device;
            bool connected = device != null && device.IsConnected;
            string model = device != null ? device.Model : "未连接";
            string transport = device != null ? device.Transport : "未知";
            // The world component is disabled throughout preview, so its last command is not a live status.
            string source = _runtime.Settings.inputSource == GyroInputSource.RightStick ? "右摇杆" :
                _runtime.Settings.inputSource == GyroInputSource.Combined ?
                    (_runtime.HasFreshGyro ? "陀螺仪 + 右摇杆" : "两者叠加（当前仅右摇杆）") :
                    (_runtime.HasFreshGyro ? "陀螺仪" : "已回退到右摇杆");
            if (_runtime.IsPlaybackActive) source = "CSV 回放（替代实时陀螺仪）";
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"设备：{model}    连接：{transport}    状态：{(connected ? "在线" : "未连接 / 正在重试")}");
            GUILayout.Label($"实际采样率：{(device != null ? device.SamplingRate : 0):F1} Hz    当前来源：{source}");
            if (device != null)
            {
                string age = connected ? (device.SampleAgeSeconds * 1000).ToString("F1") + " ms" : "无新样本";
                GUILayout.Label($"最新样本：{age}    {device.Status}");
            }
            if (world != null && world.RotationInput != null && world.RotationInput.IsBlocked)
                GUILayout.Label(world.RotationInput.IsSolarPulseActive ? "太阳脉冲：世界旋转已锁定" : "预览 / 暂停 / 转场：世界旋转已锁定");
            else if (world == null)
                GUILayout.Label("当前场景没有世界旋转器；可以查看和调试传感器。");
            GUILayout.EndVertical();
        }

        private void DrawCalibration()
        {
            GyroCalibration calibration = _runtime.Processor.Calibration;
            Vector3 bias = calibration.Bias;
            GUILayout.Label("校准：" + calibration.Status);
            GUILayout.Label($"零点偏移：X {bias.x:+0.000;-0.000;0.000}  Y {bias.y:+0.000;-0.000;0.000}  Z {bias.z:+0.000;-0.000;0.000} °/s");
            if (calibration.IsManualCalibration || !calibration.IsCalibrated)
            {
                Rect rect = GUILayoutUtility.GetRect(60f, 22f, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                {
                    Fill(rect, new Color(0.05f, 0.08f, 0.11f, 1f));
                    Fill(new Rect(rect.x, rect.y, rect.width * calibration.Progress, rect.height),
                        new Color(0.16f, 0.47f, 0.61f, 1f));
                }
                GUI.Label(rect, $"  静止进度 {calibration.Progress:P0}");
                if (_runtime.Device == null || !_runtime.Device.IsConnected) GUILayout.Label("等待原生手柄样本后开始计时。");
            }
        }

        private void DrawReadouts(WorldRotator world)
        {
            GyroProcessor processor = _runtime.Processor;
            SteeringReading reading = processor.LastSteering;
            Vector3 raw = processor.LastSample.Gyro;
            Vector3 gravity = processor.LastSample.Gravity.normalized;
            string grip = !processor.HasSample ? "等待样本" : Mathf.Abs(gravity.x) > 0.95f ? "侧立（保持最后有效轴）" :
                Mathf.Abs(gravity.y) > 0.85f ? "平握" : Mathf.Abs(gravity.z) > 0.85f ? "竖握" : "斜握";
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.BeginVertical(GUILayout.Width(215f));
            GUILayout.Label("方向盘表盘");
            Rect dial = GUILayoutUtility.GetRect(205f, 160f, GUILayout.ExpandWidth(false));
            DrawDial(dial, reading.Angle, processor.AngleMapper.CenterAngle);
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            GUILayout.Label($"握持姿势：{grip}");
            GUILayout.Label($"原始：X {raw.x:F2}    Y {raw.y:F2}    Z {raw.z:F2} °/s");
            GUILayout.Label($"方向盘 ωs：{reading.AngularSpeed:F2} °/s    被过滤：{reading.FilteredSpeed:F2} °/s");
            GUILayout.Label($"θ：{reading.Angle:F2}°    回正基准：{processor.AngleMapper.CenterAngle:F2}°");
            GUILayout.Label(world != null ? $"世界当前角度（顺时针）：{world.ClockwiseAngleReadout:F2}°" : "世界当前角度：当前场景不可用");
            float output = world != null && world.RotationInput != null ?
                (world.isActiveAndEnabled && !world.RotationInput.IsBlocked ? world.RotationInput.Output : 0f) : _runtime.StickOutput;
            GUILayout.Label($"最终输出：{output:+0.000;-0.000;0.000}    物理右摇杆：{_runtime.PhysicalRightStickX:+0.000;-0.000;0.000}");
            DrawOutputBar(output);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static void DrawLegend(string title, string[] labels, Color[] colors)
        {
            GUILayout.Label(title);
            GUILayout.BeginHorizontal();
            Color previous = GUI.contentColor;
            for (int i = 0; i < labels.Length; ++i)
            {
                GUI.contentColor = colors[i];
                GUILayout.Label("━ " + labels[i]);
            }
            GUI.contentColor = previous;
            GUILayout.EndHorizontal();
        }

        private void DrawHistoryGraph(bool steering)
        {
            Rect rect = GUILayoutUtility.GetRect(260f, 145f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            Fill(rect, new Color(0.025f, 0.04f, 0.06f, 0.93f));
            Rect plot = new Rect(rect.x + 35f, rect.y + 10f, Mathf.Max(1f, rect.width - 43f), rect.height - 29f);
            double now = GyroDevice.NowSeconds;
            double oldest = now - HistorySeconds;
            int first = 0;
            while (first < _count && At(first).Time < oldest) first++;
            float scale = 10f;
            for (int i = first; i < _count; ++i)
            {
                GraphSample value = At(i);
                float peak = steering ? Mathf.Max(Mathf.Abs(value.Wheel), value.Filtered) :
                    Mathf.Max(Mathf.Abs(value.Raw.x), Mathf.Max(Mathf.Abs(value.Raw.y), Mathf.Abs(value.Raw.z)));
                if (!float.IsNaN(peak) && !float.IsInfinity(peak)) scale = Mathf.Max(scale, peak);
            }
            scale = Mathf.Ceil(scale / 10f) * 10f;
            Color grid = new Color(0.35f, 0.43f, 0.5f, 0.33f);
            for (int i = 0; i <= 4; ++i)
            {
                float y = plot.y + plot.height * i / 4f;
                Line(new Vector2(plot.x, y), new Vector2(plot.xMax, y), grid, 1f);
            }
            GUI.Label(new Rect(rect.x + 2f, rect.y, 45f, 20f), scale.ToString("F0"));
            GUI.Label(new Rect(rect.x + 2f, plot.center.y - 9f, 30f, 20f), "0");
            GUI.Label(new Rect(rect.x + 2f, plot.yMax - 15f, 45f, 20f), (-scale).ToString("F0"));
            GUI.Label(new Rect(plot.x, rect.yMax - 20f, 90f, 20f), "−5 s");
            GUI.Label(new Rect(plot.xMax - 85f, rect.yMax - 20f, 85f, 20f), "现在 · °/s");
            int visible = _count - first;
            int stride = Mathf.Max(1, Mathf.CeilToInt(visible / (float)GraphPointLimit));
            int channels = steering ? 2 : 3;
            for (int channel = 0; channel < channels; ++channel)
            {
                Color color = steering ? (channel == 0 ? SteeringColor : FilteredColor) :
                    channel == 0 ? XColor : channel == 1 ? YColor : ZColor;
                bool hasPrevious = false;
                Vector2 previous = default;
                for (int i = first; i < _count; i += stride)
                {
                    GraphSample value = At(i);
                    float magnitude = steering ? (channel == 0 ? value.Wheel : value.Filtered) : value.Raw[channel];
                    if (float.IsNaN(magnitude) || float.IsInfinity(magnitude)) { hasPrevious = false; continue; }
                    Vector2 point = new Vector2(plot.x + Mathf.Clamp01((float)((value.Time - oldest) / HistorySeconds)) * plot.width,
                        plot.center.y - Mathf.Clamp(magnitude / scale, -1f, 1f) * plot.height * 0.5f);
                    if (hasPrevious) Line(previous, point, color, 1.3f);
                    previous = point;
                    hasPrevious = true;
                }
            }
            if (visible == 0) GUI.Label(new Rect(plot.x + 10f, plot.center.y - 12f, plot.width - 20f, 26f), "等待传感器样本…");
        }

        private GraphSample At(int chronologicalIndex) =>
            _history[(_next - _count + chronologicalIndex + HistoryCapacity) % HistoryCapacity];

        private static void DrawDial(Rect rect, float theta, float centerAngle)
        {
            if (Event.current.type != EventType.Repaint) return;
            Vector2 center = new Vector2(rect.center.x, rect.y + 69f);
            const float radius = 54f;
            Color ring = new Color(0.58f, 0.65f, 0.7f, 1f);
            for (int i = 0; i < 64; ++i)
            {
                float a = i * Mathf.PI * 2f / 64f;
                float b = (i + 1) * Mathf.PI * 2f / 64f;
                Line(center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius,
                    center + new Vector2(Mathf.Sin(b), -Mathf.Cos(b)) * radius, ring, 1.5f);
            }
            DrawNeedle(center, radius - 7f, centerAngle, FilteredColor, 2f);
            DrawNeedle(center, radius - 2f, theta, SteeringColor, 3f);
            Fill(new Rect(center.x - 3f, center.y - 3f, 6f, 6f), Color.white);
            GUI.Label(new Rect(center.x - 11f, rect.y, 35f, 21f), "0°");
            GUI.Label(new Rect(rect.x, center.y - 12f, 49f, 24f), "−90°");
            GUI.Label(new Rect(rect.xMax - 45f, center.y - 12f, 45f, 24f), "+90°");
            Color previous = GUI.contentColor;
            GUI.contentColor = SteeringColor;
            GUI.Label(new Rect(rect.x + 15f, rect.yMax - 28f, 78f, 25f), "━ 当前 θ");
            GUI.contentColor = FilteredColor;
            GUI.Label(new Rect(rect.x + 99f, rect.yMax - 28f, 100f, 25f), "━ 回正基准");
            GUI.contentColor = previous;
        }

        private static void DrawNeedle(Vector2 center, float radius, float degrees, Color color, float width)
        {
            float radians = degrees * Mathf.Deg2Rad;
            Line(center, center + new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians)) * radius, color, width);
        }

        private static void DrawOutputBar(float output)
        {
            Rect rect = GUILayoutUtility.GetRect(160f, 22f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                Fill(rect, new Color(0.035f, 0.055f, 0.075f, 1f));
                float amount = Mathf.Clamp(output, -1f, 1f) * rect.width * 0.5f;
                Fill(new Rect(rect.center.x + Mathf.Min(0f, amount), rect.y + 2f, Mathf.Abs(amount), rect.height - 4f), SteeringColor);
                Fill(new Rect(rect.center.x - 0.5f, rect.y, 1f, rect.height), Color.white);
            }
            GUI.Label(new Rect(rect.x + 3f, rect.y, 30f, rect.height), "−1");
            GUI.Label(new Rect(rect.xMax - 25f, rect.y, 25f, rect.height), "+1");
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void Line(Vector2 from, Vector2 to, Color color, float width)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < 0.0001f) return;
            // Draw in the existing GUI clip/scale coordinates. Changing GUI.matrix for each segment
            // interacts with a scrolled area's clipping and can displace or entirely clip the line.
            if (Mathf.Abs(delta.y) < 0.1f)
            {
                Fill(new Rect(Mathf.Min(from.x, to.x), from.y - width * 0.5f, Mathf.Abs(delta.x), width), color);
                return;
            }
            if (Mathf.Abs(delta.x) < 0.1f)
            {
                Fill(new Rect(from.x - width * 0.5f, Mathf.Min(from.y, to.y), width, Mathf.Abs(delta.y)), color);
                return;
            }
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) / 1.25f), 1, 2048);
            Color previous = GUI.color;
            GUI.color = color;
            for (int i = 0; i <= steps; ++i)
            {
                Vector2 point = Vector2.Lerp(from, to, i / (float)steps);
                GUI.DrawTexture(new Rect(point.x - width * 0.5f, point.y - width * 0.5f, width, width), Texture2D.whiteTexture);
            }
            GUI.color = previous;
        }
    }
}
