using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using AOT;
using UnityEngine;

namespace Resource.Scripts.Gyro
{
    /// <summary>
    /// Single owner of the process-wide JSL callback. Start/Dispose are main-thread lifecycle calls;
    /// device discovery and every HID operation execute on worker/native callback threads.
    /// No Time.deltaTime, Unity scene API, logging, or input-system API runs on those threads.
    /// </summary>
    public sealed class GyroDevice : IDisposable
    {
        public const double StaleAfterSeconds = 0.05;
        private const int MaximumQueuedSamples = 8192;
        private static readonly object OwnerLock = new object();
        private static GyroDevice _owner;
        private static readonly JslNative.StateCallback StateCallbackRoot = OnNativeState;
        private static readonly JslNative.DisconnectCallback DisconnectCallbackRoot = OnNativeDisconnect;

        private readonly object _stateLock = new object();
        private readonly ConcurrentQueue<GyroSample> _samples = new ConcurrentQueue<GyroSample>();
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim();
        private Thread _worker;
        private int _selectedHandle = -1;
        private int _queuedCount;
        private int _droppedSamples;
        private int _pressedButtons;
        private int _connectionGeneration;
        private long _lastSampleTicks;
        private long _lastArrivalTicks;
        private double _rateWindowStart;
        private int _rateWindowCount;
        private float _samplingRate;
        private string _model = "未连接";
        private string _transport = "未知";
        private string _status = "尚未启动";
        private GyroControllerState _controls;
        private volatile bool _disposed;
        private bool _started;

        public static double NowSeconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        public bool IsConnected => Volatile.Read(ref _selectedHandle) >= 0 && SampleAgeSeconds <= StaleAfterSeconds;
        public bool CallbackRunning => IsConnected;
        public bool WorkerRunning => _worker != null && _worker.IsAlive;
        public int QueueCount => Math.Max(0, Volatile.Read(ref _queuedCount));
        public int DroppedSamples => Volatile.Read(ref _droppedSamples);
        public double SampleAgeSeconds
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastSampleTicks);
                return ticks == 0 ? double.PositiveInfinity : (double)(Stopwatch.GetTimestamp() - ticks) / Stopwatch.Frequency;
            }
        }
        public float SamplingRate { get { lock (_stateLock) return IsConnected ? _samplingRate : 0f; } }
        public string Model { get { lock (_stateLock) return _model; } }
        public string Transport { get { lock (_stateLock) return _transport; } }
        public string Status { get { lock (_stateLock) return _status; } }
        public GyroControllerState Controls { get { lock (_stateLock) return IsConnected ? _controls : default; } }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GyroDevice));
            if (_started) return;
            _started = true;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (IntPtr.Size != 8)
            {
                SetStatus("JoyShockLibrary 需要 Windows x64");
                return;
            }
            lock (OwnerLock)
            {
                if (_owner != null)
                {
                    SetStatus("已有陀螺仪设备服务在运行");
                    return;
                }
                _owner = this;
            }
            _worker = new Thread(Run) { IsBackground = true, Name = "JoyShockLibrary discovery" };
            _worker.Start();
#else
            SetStatus("当前平台未安装原生陀螺仪插件，使用右摇杆");
#endif
        }

        public bool TryDequeue(out GyroSample sample)
        {
            while (_samples.TryDequeue(out sample))
            {
                Interlocked.Decrement(ref _queuedCount);
                // Preserve every report during a slow render frame while the stream is live.
                // Original timestamps reach the mapper unchanged; a stopped stream cannot replay
                // its backlog as fresh input. The runtime drains/ignores samples while paused.
                if (IsConnected) return true;
            }
            sample = default;
            return false;
        }

        public void ClearSamples()
        {
            while (_samples.TryDequeue(out _)) Interlocked.Decrement(ref _queuedCount);
        }

        /// <summary>Edges accumulate between frames, including quick touchpad clicks. Consume once per frame.</summary>
        public GyroButtons ConsumePressedButtons()
        {
            int pressed = Interlocked.Exchange(ref _pressedButtons, 0);
            return IsConnected ? (GyroButtons)pressed : GyroButtons.None;
        }

        private void Run()
        {
            bool nativeLoaded = false;
            try
            {
                SetStatus("正在查找手柄…");
                JslNative.JslSetCallback(StateCallbackRoot);
                nativeLoaded = true;
                JslNative.JslSetDisconnectCallback(DisconnectCallbackRoot);
                double nextDiscovery = 0;
                while (!_stop.IsSet)
                {
                    double now = NowSeconds;
                    if (now >= nextDiscovery)
                    {
                        nextDiscovery = now + 2.0;
                        int handle = Volatile.Read(ref _selectedHandle);
                        if (handle < 0 || !JslNative.JslStillConnected(handle) || SampleAgeSeconds > StaleAfterSeconds)
                            DiscoverDevice();
                    }
                    _stop.Wait(10);
                }
            }
            catch (DllNotFoundException) { SetStatus("缺少 JoyShockLibrary.dll，使用右摇杆"); }
            catch (BadImageFormatException) { SetStatus("JoyShockLibrary.dll 架构不匹配，需要 Windows x64"); }
            catch (EntryPointNotFoundException) { SetStatus("JoyShockLibrary.dll 版本不匹配，需要官方 v3.0"); }
            catch (Exception ex) { SetStatus("陀螺仪连接失败：" + ex.GetType().Name); }
            finally
            {
                if (nativeLoaded)
                {
                    try
                    {
                        // These calls wait for active callbacks before the managed owner is released.
                        JslNative.JslSetCallback(null);
                        JslNative.JslSetDisconnectCallback(null);
                        JslNative.JslDisconnectAndDisposeAll();
                    }
                    catch (Exception ex) { SetStatus("陀螺仪关闭：" + ex.GetType().Name); }
                }
                ResetConnection();
                lock (OwnerLock) { if (ReferenceEquals(_owner, this)) _owner = null; }
            }
        }

        private void DiscoverDevice()
        {
            int count = JslNative.JslConnectDevices();
            if (_stop.IsSet) return;
            var handles = new int[Math.Max(count, 1)];
            int found = JslNative.JslGetConnectedDeviceHandles(handles, handles.Length);
            int selected = -1;
            int selectedType = 0;
            for (int i = 0; i < found; i++)
            {
                int type = JslNative.JslGetControllerType(handles[i]);
                if (type != JslNative.DualSense && type != JslNative.DualShock4) continue;
                if (selected < 0 || type == JslNative.DualSense)
                {
                    selected = handles[i];
                    selectedType = type;
                    if (type == JslNative.DualSense) break;
                }
            }
            if (selected < 0)
            {
                ResetConnection();
                SetStatus("未检测到 DualSense / DualShock 4，每 2 秒重试");
                return;
            }
            if (selected == Volatile.Read(ref _selectedHandle)) return;

            // The pure managed calibration owns all zero-offset changes. Keep input in local space.
            JslNative.JslSetGyroSpace(selected, 0);
            JslNative.JslSetAutomaticCalibration(selected, false);
            JslNative.JslPauseContinuousCalibration(selected);
            JslNative.JslSetCalibrationOffset(selected, 0, 0, 0);
            string transport = WindowsControllerTransport.Find(selectedType);
            ResetConnection();
            lock (_stateLock)
            {
                _model = selectedType == JslNative.DualSense ? "DualSense" : "DualShock 4";
                _transport = transport;
                _status = "已连接，等待传感器样本";
            }
            Volatile.Write(ref _selectedHandle, selected);
        }

        [MonoPInvokeCallback(typeof(JslNative.StateCallback))]
        private static void OnNativeState(int deviceId, JslNative.SimpleState current, JslNative.SimpleState previous,
            JslNative.ImuState imu, JslNative.ImuState previousImu, float deltaTime)
        {
            GyroDevice device = _owner;
            if (device == null || device._disposed || deviceId != Volatile.Read(ref device._selectedHandle)) return;
            try { device.Receive(deviceId, current, previous, imu, deltaTime); }
            catch (Exception ex) { device.SetStatus("传感器样本已忽略：" + ex.GetType().Name); }
        }

        [MonoPInvokeCallback(typeof(JslNative.DisconnectCallback))]
        private static void OnNativeDisconnect(int deviceId, bool timedOut)
        {
            GyroDevice device = _owner;
            if (device == null || deviceId != Volatile.Read(ref device._selectedHandle)) return;
            device.ResetConnection();
            device.SetStatus(timedOut ? "手柄超时，等待自动重连" : "手柄已断开，等待自动重连");
        }

        private void Receive(int deviceId, JslNative.SimpleState current, JslNative.SimpleState previous,
            JslNative.ImuState imu, float deltaTime)
        {
            int generation = Volatile.Read(ref _connectionGeneration);
            long nowTicks = Stopwatch.GetTimestamp();
            double now = (double)nowTicks / Stopwatch.Frequency;
            long previousTicks = Interlocked.Exchange(ref _lastArrivalTicks, nowTicks);
            if (!Finite(deltaTime) || deltaTime <= 0)
            {
                if (previousTicks == 0) return;
                deltaTime = (float)((double)(nowTicks - previousTicks) / Stopwatch.Frequency);
            }
            // A first packet after a device stall cannot integrate the disconnected interval.
            if (!Finite(deltaTime) || deltaTime <= 0 || deltaTime > StaleAfterSeconds)
                return;
            JslNative.MotionState motion = JslNative.JslGetMotionState(deviceId);
            var gyro = new Vector3(imu.GyroX, imu.GyroY, imu.GyroZ);
            var acceleration = new Vector3(imu.AccelX, imu.AccelY, imu.AccelZ);
            var gravity = new Vector3(motion.GravityX, motion.GravityY, motion.GravityZ);
            if (!Finite(gyro.x) || !Finite(gyro.y) || !Finite(gyro.z)
                || !Finite(acceleration.x) || !Finite(acceleration.y) || !Finite(acceleration.z)) return;
            if (!Finite(gravity.x) || !Finite(gravity.y) || !Finite(gravity.z) || gravity.sqrMagnitude < 0.01f)
                gravity = -acceleration.normalized;
            if (gravity.sqrMagnitude < 0.01f) return;
            lock (_stateLock)
            {
                // Discovery/disconnect can race an already-entered native callback.
                if (generation != _connectionGeneration || deviceId != _selectedHandle || _disposed) return;
                _controls = new GyroControllerState((GyroButtons)current.Buttons,
                    new Vector2(current.LeftX, current.LeftY), new Vector2(current.RightX, current.RightY),
                    current.LeftTrigger, current.RightTrigger);
                _status = "传感器运行中";
                if (_rateWindowStart == 0) _rateWindowStart = now;
                _rateWindowCount++;
                if (now - _rateWindowStart >= 1.0)
                {
                    _samplingRate = (float)(_rateWindowCount / (now - _rateWindowStart));
                    _rateWindowStart = now;
                    _rateWindowCount = 0;
                }
                int edges = current.Buttons & ~previous.Buttons;
                int oldEdges;
                do { oldEdges = Volatile.Read(ref _pressedButtons); }
                while (Interlocked.CompareExchange(ref _pressedButtons, oldEdges | edges, oldEdges) != oldEdges);
                Interlocked.Exchange(ref _lastSampleTicks, nowTicks);
                _samples.Enqueue(new GyroSample(deltaTime, gyro, acceleration, gravity, now));
                int queueCount = Interlocked.Increment(ref _queuedCount);
                if (queueCount > MaximumQueuedSamples && _samples.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref _queuedCount);
                    Interlocked.Increment(ref _droppedSamples);
                }
            }
        }

        private void ResetConnection()
        {
            lock (_stateLock)
            {
                Interlocked.Increment(ref _connectionGeneration);
                Volatile.Write(ref _selectedHandle, -1);
                Interlocked.Exchange(ref _lastSampleTicks, 0);
                Interlocked.Exchange(ref _lastArrivalTicks, 0);
                Interlocked.Exchange(ref _pressedButtons, 0);
                ClearSamples();
                _controls = default;
                _samplingRate = 0;
                _rateWindowCount = 0;
                _rateWindowStart = 0;
                _model = "未连接";
                _transport = "未知";
            }
        }

        private void SetStatus(string status) { lock (_stateLock) _status = status; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Set();
            // Only shutdown waits for discovery. Never leave a native callback pointing into an
            // unloaded Unity domain; ordinary reconnection never waits on the main thread.
            if (_worker != null && Thread.CurrentThread != _worker) _worker.Join();
            _stop.Dispose();
            ClearSamples();
        }
    }

    public readonly struct GyroControllerState
    {
        public GyroButtons Buttons { get; }
        public Vector2 LeftStick { get; }
        public Vector2 RightStick { get; }
        public float LeftTrigger { get; }
        public float RightTrigger { get; }
        public GyroControllerState(GyroButtons buttons, Vector2 leftStick, Vector2 rightStick, float leftTrigger, float rightTrigger)
        {
            Buttons = buttons; LeftStick = leftStick; RightStick = rightStick;
            LeftTrigger = leftTrigger; RightTrigger = rightTrigger;
        }
        public bool IsPressed(GyroButtons button) => (Buttons & button) == button;
    }
}
