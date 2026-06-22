using UnityEngine;
using UnityEngine.InputSystem;

namespace Resource.Scripts
{
    public class DS5Test : MonoBehaviour
    {
        private float timer = 0f;

        void Update()
        {
            timer += Time.deltaTime;
            if (timer < 0.5f) return;
            timer = 0f;

            var gamepad = Gamepad.current;
            if (gamepad == null)
            {
                Debug.LogWarning("没有找到手柄！");
                return;
            }

            // 左右摇杆
            Vector2 leftStick  = gamepad.leftStick.ReadValue();
            Vector2 rightStick = gamepad.rightStick.ReadValue();

            // 扳机
            float l2 = gamepad.leftTrigger.ReadValue();
            float r2 = gamepad.rightTrigger.ReadValue();

            // Steam Input 把陀螺仪映射成右摇杆
            // 所以右摇杆就是陀螺仪数据
            Debug.Log($"右摇杆(陀螺仪) X:{rightStick.x:F3}  Y:{rightStick.y:F3}");
            Debug.Log($"左摇杆 X:{leftStick.x:F3}  Y:{leftStick.y:F3}");
            Debug.Log($"L2:{l2:F3}  R2:{r2:F3}");

            // 提示手柄名称
            Debug.Log($"手柄: {gamepad.name}");
        }
    }
}