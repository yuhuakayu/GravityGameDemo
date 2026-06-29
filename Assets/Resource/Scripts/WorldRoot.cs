using UnityEngine;
using UnityEngine.InputSystem;

namespace Resource.Scripts
{
    public class WorldRotator : MonoBehaviour
    {
        [Header("调试")]
        public bool isDebugLog    = false;   // 控制台 Log
        public bool isDebugGizmos = false;   // Scene 画图（暂无，占位）

        [Header("旋转设置")]
        public float rotateSpeed = 90f;
        public float smoothing = 8f;

        [Header("旋转中心")]
        public Transform pivot;

        private float currentAngle = 0f;
        private float targetAngle = 0f;

        void Update()
        {
            float input = 0f;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.qKey.isPressed) input = 1f;
                if (Keyboard.current.eKey.isPressed) input = -1f;
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                float stickX = gamepad.rightStick.x.ReadValue();
                if (Mathf.Abs(stickX) > 0.1f)
                    input = stickX;
            }

            targetAngle += input * rotateSpeed * Time.deltaTime;
            currentAngle = Mathf.LerpAngle(
                currentAngle, targetAngle, Time.deltaTime * smoothing);

            if (pivot != null)
                transform.RotateAround(pivot.position, Vector3.forward,
                    input * rotateSpeed * Time.deltaTime);
            else
                transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);

            if (isDebugLog)
                Debug.Log($"[WorldRotator] angle={currentAngle:F1}  eulerZ={transform.eulerAngles.z:F1}");
        }
    }
}
