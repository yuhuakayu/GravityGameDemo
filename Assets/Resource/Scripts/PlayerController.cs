using UnityEngine;
using UnityEngine.InputSystem;

namespace Resource.Scripts
{
    public class PlayerController : MonoBehaviour
    {
        [Header("移动设置")]
        public float maxMoveSpeed = 6f;
        public float jumpForce = 12f;

        private Rigidbody2D rb;
        private bool isGrounded = false;

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();
        }

        void Update()
        {
            HandleMovement();
            HandleJump();
        }

        void HandleMovement()
        {
            var gamepad = Gamepad.current;
            float moveInput = 0f;

            if (gamepad != null)
            {
                // 扳机控制移动（L2 向左，R2 向右）
                float l2 = gamepad.leftTrigger.ReadValue();
                float r2 = gamepad.rightTrigger.ReadValue();

                // 死区处理
                if (l2 < 0.05f) l2 = 0f;
                if (r2 < 0.05f) r2 = 0f;

                moveInput = r2 - l2;
            }

            // 键盘备用（A/D 或 左右方向键）
            if (Keyboard.current.aKey.isPressed ||
                Keyboard.current.leftArrowKey.isPressed)
                moveInput = -1f;

            if (Keyboard.current.dKey.isPressed ||
                Keyboard.current.rightArrowKey.isPressed)
                moveInput = 1f;

            rb.linearVelocity = new Vector2(
                moveInput * maxMoveSpeed,
                rb.linearVelocity.y
            );
        }

        void HandleJump()
        {
            bool jumpPressed = false;

            // 键盘空格跳跃
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                jumpPressed = true;

            // 手柄南键（×键）跳跃
            var gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)
                jumpPressed = true;

            if (jumpPressed && isGrounded)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                isGrounded = false;
            }
        }

        void OnCollisionEnter2D(Collision2D col)
        {
            foreach (ContactPoint2D contact in col.contacts)
            {
                if (contact.normal.y > 0.5f)
                    isGrounded = true;
            }
        }

        void OnCollisionExit2D(Collision2D col)
        {
            isGrounded = false;
        }
    }
}