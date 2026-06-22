using UnityEngine;
using UnityEngine.InputSystem;

namespace Resource.Scripts
{
    public class WorldRotator : MonoBehaviour
    {
        [Header("旋转设置")]
        public float rotateSpeed = 90f;
        public float smoothing = 8f;

        [Header("旋转中心")]
        public Transform pivot; // 拖入任意物体作为旋转中心，不填则以自身为中心

        private float currentAngle = 0f;
        private float targetAngle = 0f;

        void Update()
        {
            float input = 0f;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.qKey.isPressed) input = -1f;
                if (Keyboard.current.eKey.isPressed) input =  1f;
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
            {
                // 以 pivot 为中心旋转
                transform.RotateAround(
                    pivot.position, 
                    Vector3.forward, 
                    input * rotateSpeed * Time.deltaTime
                );
            }
            else
            {
                // 以自身原点为中心
                transform.rotation = Quaternion.Euler(0f, 0f, currentAngle);
            }
        }
    }
}