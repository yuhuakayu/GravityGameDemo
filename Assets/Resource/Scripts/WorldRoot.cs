using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Resource.Scripts
{
    public class WorldRotator : MonoBehaviour
    {
        [Header("调试")]
        public bool isDebugLog = false;
        [Tooltip("每 0.5 秒打出所有手柄名称和右摇杆 X 值，用于排查陀螺仪")]
        public bool isDebugScanGamepads = false;
        private float _scanTimer;

        [Header("旋转设置")]
        [Tooltip("旋转速度（度/秒），键盘 Q/E 和手柄右摇杆都用这个值")]
        public float rotateSpeed = 90f;

        [Header("陀螺仪")]
        [Tooltip("优先用 DS5GyroReader 读到的原始陀螺仪角速度（只响应真正的旋转，不会被手柄平移/晃动误触发）。" +
                 "找不到 DS5GyroReader 或它没连上手柄时，自动退回普通摇杆 X 轴。陀螺仪手感跟摇杆不一样，试过觉得诡异可以关掉，默认关")]
        public bool useGyroIfAvailable = false;
        private DS5GyroReader _gyroReader;

        [Header("摇杆平滑（缓解 Steam Input 手柄晃动误触发）")]
        [Tooltip("摇杆信号死区，低于这个值视为 0")]
        public float stickDeadzone = 0.05f;
        [Tooltip("摇杆信号平滑强度：越大跟手但越容易被晃动带出小尖峰，越小越稳但转动手感会有延迟")]
        [Range(1f, 30f)]
        public float stickSmoothing = 12f;
        [Tooltip("摇杆信号要朝同一个方向持续这么多秒，才会被当作真实转动输入；晃动通常是短促的、方向来回跳的，达不到这个时长会被过滤掉")]
        public float stickSustainTime = 0.08f;
        private float _smoothedStickX;
        private float _sustainTimer;
        private float _lastSign;

        [Header("旋转中心")]
        [Tooltip("留空则自动找场景里的 PlayerController 当旋转中心；手动拖一个 Transform 可以覆盖")]
        public Transform pivot;
        [Tooltip("自动模式下，玩家在空中时旋转中心用几帧之前的玩家坐标（而不是当前帧），落地后改用当前坐标")]
        public int airborneDelayFrames = 10;

        private PlayerController _autoPlayer;
        private readonly Queue<Vector3> _positionHistory = new Queue<Vector3>();

        [Header("方向盘范围")]
        [Tooltip("不勾选 = 无限旋转，不做范围限制")]
        public bool limitSteeringRange = true;
        [Tooltip("方向盘总转角（720 = 左右各 360°），仅在 limitSteeringRange 打开时生效")]
        public float steeringRange = 720f;

        [Header("── 实时监控 ──")]
        [Range(-360f, 360f)]
        [SerializeField] private float _steeringAngle = 0f;
        /// <summary>当前方向盘角度（只读，调试面板用）</summary>
        public float SteeringAngleReadout => _steeringAngle;
        private float _lastGearTickAngle;

        void Start()
        {
            float z = transform.eulerAngles.z;
            _steeringAngle = z > 180f ? z - 360f : z;
            _lastGearTickAngle = _steeringAngle;
        }

        void Update()
        {
            float input = 0f;

            // 键盘 Q/E
            if (Keyboard.current != null)
            {
                if (Keyboard.current.qKey.isPressed) input = +1f;
                if (Keyboard.current.eKey.isPressed) input = -1f;
            }

            // 优先用 DS5 原始陀螺仪角速度：真正测的是旋转，手柄平移/晃动不会触发
            bool usedGyro = false;
            if (useGyroIfAvailable)
            {
                if (_gyroReader == null)
                    _gyroReader = FindObjectOfType<DS5GyroReader>();

                if (_gyroReader != null && _gyroReader.IsAvailable && Mathf.Abs(_gyroReader.GyroVelocity) > 0.001f)
                {
                    input = _gyroReader.GyroVelocity;
                    usedGyro = true;
                }
            }

            // 没有陀螺仪数据（没找到 DS5GyroReader / 手柄没连上，或者关掉了 useGyroIfAvailable）时，
            // 走普通摇杆 X 轴，叠两层过滤：
            //   1) 低通平滑：滤掉快速的小尖峰
            //   2) 方向持续时间：必须朝同一个方向持续 stickSustainTime 秒才生效——
            //      手柄晃动一般是短促、方向来回跳的，故意转手柄才会是持续同一个方向
            if (!usedGyro)
            {
                float bestStickX = 0f;
                foreach (var gp in Gamepad.all)
                {
                    float sx = gp.rightStick.x.ReadValue();
                    if (Mathf.Abs(sx) > Mathf.Abs(bestStickX))
                        bestStickX = sx;
                }

                _smoothedStickX = Mathf.Lerp(_smoothedStickX, bestStickX, Time.deltaTime * stickSmoothing);

                if (Mathf.Abs(_smoothedStickX) < stickDeadzone)
                {
                    _sustainTimer = 0f;
                }
                else
                {
                    float sign = Mathf.Sign(_smoothedStickX);
                    if (!Mathf.Approximately(sign, _lastSign))
                    {
                        _sustainTimer = 0f;
                        _lastSign = sign;
                    }
                    else
                    {
                        _sustainTimer += Time.deltaTime;
                    }
                }

                if (_sustainTimer >= stickSustainTime && Mathf.Abs(_smoothedStickX) > stickDeadzone)
                    input = -_smoothedStickX;
            }

            input *= rotateSpeed;

            // 方向盘角度累加 + 限幅（可关闭）
            _steeringAngle += input * Time.deltaTime;
            if (limitSteeringRange)
            {
                float halfRange = steeringRange * 0.5f;
                _steeringAngle = Mathf.Clamp(_steeringAngle, -halfRange, halfRange);
            }

            // 旋转"吱呀"摩擦音：音量/音调跟旋转速度联动
            float rotateSpeed01 = rotateSpeed > 0f ? Mathf.Abs(input) / rotateSpeed : 0f;
            SfxManager.Instance.UpdateRotateCreak(rotateSpeed01);

            // 每转过 90° 播放一次齿轮"咔嚓"声
            if (Mathf.Abs(_steeringAngle - _lastGearTickAngle) >= 90f)
            {
                _lastGearTickAngle = _steeringAngle;
                SfxManager.Instance.PlayGearClick(rotateSpeed01);
            }

            // 直接旋转，无平滑
            float delta = input * Time.deltaTime;

            // 旋转中心：优先用手动指定的 pivot；没指定就走自动逻辑
            Vector3? pivotPos = pivot != null ? pivot.position : ResolveAutoPivotPosition();
            if (pivotPos.HasValue)
                transform.RotateAround(pivotPos.Value, Vector3.forward, delta);
            else
                transform.Rotate(0f, 0f, delta);

            if (isDebugLog)
                Debug.Log($"[WorldRotator] steering={_steeringAngle:F1}° input={input:F1}");

            // 手柄扫描
            if (isDebugScanGamepads)
            {
                _scanTimer += Time.deltaTime;
                if (_scanTimer >= 0.5f)
                {
                    _scanTimer = 0f;
                    var sb = new System.Text.StringBuilder("[GamepadScan] ");
                    if (Gamepad.all.Count == 0) sb.Append("无手柄");
                    foreach (var gp in Gamepad.all)
                        sb.Append($"[{gp.name}] R.x={gp.rightStick.x.ReadValue():+0.000;-0.000}  ");
                    Debug.Log(sb.ToString());
                }
            }
        }

        /// <summary>
        /// 自动旋转中心：玩家落地时用"当前帧"坐标；在空中时改用"N 帧之前"的坐标
        /// （历史队列，每帧入队一次，超过 airborneDelayFrames 就把最老的一个丢出去当结果）。
        /// </summary>
        private Vector3? ResolveAutoPivotPosition()
        {
            if (_autoPlayer == null)
                _autoPlayer = FindObjectOfType<PlayerController>();
            if (_autoPlayer == null)
                return null;

            Vector3 currentPos = _autoPlayer.transform.position;

            _positionHistory.Enqueue(currentPos);
            while (_positionHistory.Count > Mathf.Max(1, airborneDelayFrames))
                _positionHistory.Dequeue();

            if (_autoPlayer.IsGrounded)
                return currentPos;

            return _positionHistory.Peek(); // 队列里最老的一个，也就是"N 帧之前"的坐标
        }
    }
}
