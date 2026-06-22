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
            float moveInput = 0f;

            // 键盘 A/D 移动（测试用）
            if (Keyboard.current.aKey.isPressed) moveInput = -1f;
            if (Keyboard.current.dKey.isPressed) moveInput =  1f;

            // 扳机移动（手柄用）
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                float l2 = gamepad.leftTrigger.ReadValue();
                float r2 = gamepad.rightTrigger.ReadValue();
                if (l2 < 0.05f) l2 = 0f;
                if (r2 < 0.05f) r2 = 0f;
                if (l2 > 0 || r2 > 0)
                    moveInput = r2 - l2;
            }

            rb.linearVelocity = new Vector2(
                moveInput * maxMoveSpeed,
                rb.linearVelocity.y
            );
        }

        void HandleJump()
        {
            bool jumpPressed = false;

            // 空格跳跃
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                jumpPressed = true;

            // 手柄 × 键跳跃
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
                if (contact.normal.y > 0.5f)
                    isGrounded = true;
        }

        void OnCollisionExit2D(Collision2D col)
        {
            isGrounded = false;
        }
    }
}