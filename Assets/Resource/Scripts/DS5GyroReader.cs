using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// ── 使用前请勾选：Edit → Project Settings → Player → Other Settings → Allow Unsafe Code ──

namespace Resource.Scripts
{
    /// <summary>
    /// DS5 陀螺仪读取器（HID 原始事件版）
    ///
    /// 原理：订阅 InputSystem.onEvent，在每个 HID 状态帧里直接读取指定字节偏移的 int16 值。
    /// 不修改设备布局，不移除/重连手柄，手柄其他功能（按键/摇杆/扳机）完全不受影响。
    ///
    /// 找对字节偏移：
    ///   勾选 Is Debug Scan，进 Play Mode，Console 会每 0.5 秒打出 offset 10~28 所有值。
    ///   手柄静止时记录各列数值；然后左右旋转手柄，看哪一列变化最大 → 那就是正确偏移。
    /// </summary>
    public class DS5GyroReader : MonoBehaviour
    {
        [Header("调试")]
        public bool isDebugLog  = false;
        [Tooltip("扫描模式：每 0.5 秒打出 offset 12~31 所有值（步长1，奇偶都覆盖），帮助找正确陀螺仪偏移")]
        public bool isDebugScan = false;

        [Header("陀螺仪参数")]
        [Tooltip("陀螺仪 Z 轴在 HID 状态缓冲中的字节偏移（从 0 起算）\n" +
                 "USB 模式（含 Report ID byte0）正确值：20\n" +
                 "如果 HIDrogen 插件剥离了 Report ID，则正确值变为 19\n" +
                 "如果还是不对，勾选 Debug Scan → 旋转手柄 → 看哪列数值变化大")]
        public int gyroZOffset = 20;

        [Tooltip("死区（归一化 0~1），低于此值视为静止，建议 0.05~0.10")]
        [Range(0f, 0.3f)]
        public float gyroDeadzone = 0.06f;

        [Tooltip("平滑强度（越大越跟手但抖，越小越丝滑；建议 6~12）")]
        [Range(1f, 30f)]
        public float gyroSmoothing = 8f;

        [Tooltip("翻转方向（如果世界转反了请勾选）")]
        public bool invertGyro = false;

        // ── 对外只读 ─────────────────────────────────────────────
        /// <summary>陀螺仪 Z 轴速度，归一化 -1~1，死区 + 低通后的值</summary>
        public float GyroVelocity { get; private set; }
        public bool  IsAvailable  { get; private set; }

        // ── 私有 ─────────────────────────────────────────────────
        private InputDevice _ds5;
        private float       _rawGyroZ;
        private float       _smoothed;

        // 扫描用（step-1：offset 12~31，共 20 个位置）
        private const int ScanStart = 12;
        private const int ScanCount = 20;
        private bool    _scanDirty;
        private float   _scanTimer;
        private float[] _scanValues = new float[ScanCount];

        // ─────────────────────────────────────────────────────────
        void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            InputSystem.onEvent        += OnInputEvent;
            FindDS5();
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onEvent        -= OnInputEvent;
        }

        // ── 找设备 ───────────────────────────────────────────────
        void FindDS5()
        {
            foreach (var dev in InputSystem.devices)
            {
                if (IsDS5(dev))
                {
                    _ds5        = dev;
                    IsAvailable = true;
                    if (isDebugLog)
                        Debug.Log($"[DS5Gyro] ✓ 找到 DualSense: {dev.name} | layout={dev.layout}");
                    return;
                }
            }
            if (isDebugLog) Debug.Log("[DS5Gyro] 未找到 DualSense，请确认 USB 已连接");
        }

        static bool IsDS5(InputDevice dev) =>
            dev.description.product != null &&
            dev.description.product.Contains("DualSense");

        void OnDeviceChange(InputDevice dev, InputDeviceChange change)
        {
            if (IsDS5(dev) &&
                (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected))
            {
                _ds5        = dev;
                IsAvailable = true;
                if (isDebugLog) Debug.Log("[DS5Gyro] DualSense 已连接");
            }
            else if (dev == _ds5 && change == InputDeviceChange.Removed)
            {
                _ds5        = null;
                IsAvailable = false;
                _rawGyroZ   = 0f;
                if (isDebugLog) Debug.LogWarning("[DS5Gyro] DualSense 已断开");
            }
        }

        // ── HID 事件拦截（unsafe 读原始字节）────────────────────
        private unsafe void OnInputEvent(InputEventPtr eventPtr, InputDevice dev)
        {
            if (dev != _ds5) return;
            if (eventPtr.type != StateEvent.Type) return;

            var   se   = StateEvent.From(eventPtr);
            byte* data = (byte*)se->state;
            int   size = (int)se->stateSizeInBytes;

            // 陀螺仪 Z
            if (gyroZOffset + 1 < size)
            {
                short raw = *(short*)(data + gyroZOffset);
                _rawGyroZ = raw / 32768f;
            }

            // 扫描：offset 12~31，步长 1（覆盖奇偶全部对齐方式）
            if (isDebugScan)
            {
                for (int i = 0; i < ScanCount; i++)
                {
                    int off = ScanStart + i;
                    if (off + 1 < size)
                    {
                        short v = *(short*)(data + off);
                        _scanValues[i] = v / 32768f;
                    }
                }
                _scanDirty = true;
            }
        }

        // ── 每帧处理 ─────────────────────────────────────────────
        void Update()
        {
            if (!IsAvailable) return;

            float raw = _rawGyroZ * (invertGyro ? -1f : 1f);
            _smoothed = Mathf.Lerp(_smoothed, raw, Time.deltaTime * gyroSmoothing);
            GyroVelocity = Mathf.Abs(_smoothed) < gyroDeadzone ? 0f : _smoothed;

            if (isDebugLog)
                Debug.Log($"[DS5Gyro] raw={raw:F3}  smoothed={_smoothed:F3}  out={GyroVelocity:F3}");

            // 扫描输出（0.5 秒一次）
            if (isDebugScan && _scanDirty)
            {
                _scanTimer += Time.deltaTime;
                if (_scanTimer >= 0.5f)
                {
                    _scanTimer = 0f;
                    _scanDirty = false;
                    PrintScan();
                }
            }
        }

        void PrintScan()
        {
            // 每行 10 个，便于在 Console 里对比静止 vs 旋转时哪列变化大
            var sb = new System.Text.StringBuilder("[DS5Gyro 扫描] offset→值（旋转手柄时观察变化大的列）\n  ");
            for (int i = 0; i < ScanCount; i++)
            {
                sb.Append($"[{ScanStart + i:D2}]={_scanValues[i]:+0.000;-0.000} ");
                if (i == 9) sb.Append("\n  ");
            }
            Debug.Log(sb.ToString());
        }
    }
}
