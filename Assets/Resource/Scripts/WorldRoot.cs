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

        [Header("旋转中心")]
        public Transform pivot;

        [Header("方向盘范围")]
        [Tooltip("方向盘总转角（720 = 左右各 360°）")]
        public float steeringRange = 720f;

        [Header("── 实时监控 ──")]
        [Range(-360f, 360f)]
        [SerializeField] private float _steeringAngle = 0f;

        void Start()
        {
            float z = transform.eulerAngles.z;
            _steeringAngle = z > 180f ? z - 360f : z;
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

            // 所有手柄右摇杆取绝对值最大（物理摇杆从 DualSense 读，陀螺仪从 Steam 虚拟手柄读）
            float bestStickX = 0f;
            foreach (var gp in Gamepad.all)
            {
                float sx = gp.rightStick.x.ReadValue();
                if (Mathf.Abs(sx) > Mathf.Abs(bestStickX))
                    bestStickX = sx;
            }
            if (Mathf.Abs(bestStickX) > 0.05f)
                input = -bestStickX;

            input *= rotateSpeed;

            // 方向盘角度累加 + 限幅
            float halfRange = steeringRange * 0.5f;
            _steeringAngle += input * Time.deltaTime;
            _steeringAngle  = Mathf.Clamp(_steeringAngle, -halfRange, halfRange);

            // 直接旋转，无平滑
            float delta = input * Time.deltaTime;

            if (pivot != null)
                transform.RotateAround(pivot.position, Vector3.forward, delta);
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
    }
}
