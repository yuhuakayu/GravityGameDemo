using UnityEngine;
using UnityEngine.InputSystem;

namespace Resource.Scripts
{
    public class PlayerController : MonoBehaviour
    {
        [Header("移动设置")]
        public float maxMoveSpeed = 6f;
        public float jumpForce = 12f;

        [Header("地面检测")]
        public Transform groundCheck;
        public float groundCheckRadius = 0.1f;
        public LayerMask groundLayer;

        private Rigidbody2D rb;
        private bool isGrounded = false;
        private float debugTimer = 0f;

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            Debug.Log("PlayerController 初始化完成");
        }

        void Update()
        {
            // 地面检测
            if (groundCheck != null)
            {
                isGrounded = Physics2D.OverlapCircle(
                    groundCheck.position,
                    groundCheckRadius,
                    groundLayer
                );
            }

            HandleJump();

            // 每秒打印一次状态
            debugTimer += Time.deltaTime;
            if (debugTimer >= 1f)
            {
                debugTimer = 0f;
                Debug.Log($"isGrounded:{isGrounded} | " +
                          $"velocity:{rb.linearVelocity} | " +
                          $"position:{transform.position} | " +
                          $"groundCheck:{(groundCheck != null ? groundCheck.position.ToString() : "未设置")} | " +
                          $"groundLayer:{groundLayer.value}");
            }
        }

        void FixedUpdate()
        {
            HandleMovement();
        }

        void HandleMovement()
        {
            float moveInput = 0f;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.aKey.isPressed ||
                    Keyboard.current.leftArrowKey.isPressed)
                    moveInput = -1f;

                if (Keyboard.current.dKey.isPressed ||
                    Keyboard.current.rightArrowKey.isPressed)
                    moveInput = 1f;
            }

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

            float yBefore = rb.linearVelocity.y;

            if (isGrounded)
            {
                rb.linearVelocity = new Vector2(
                    moveInput * maxMoveSpeed,
                    rb.linearVelocity.y
                );
            }
            else
            {
                // 空中锁死 Y 轴不能变正
                float yVel = Mathf.Min(rb.linearVelocity.y, 0f);
                rb.linearVelocity = new Vector2(
                    moveInput * maxMoveSpeed,
                    yVel
                );

                // Debug：检测 Y 轴是否被斜面推高
                if (rb.linearVelocity.y > 0.1f)
                {
                    Debug.LogWarning($"空中 Y 轴被推高！" +
                                    $"修正前 Y:{yBefore:F3} | " +
                                    $"修正后 Y:{rb.linearVelocity.y:F3}");
                }
            }

            // 每帧打印移动状态（只在有输入时）
            if (Mathf.Abs(moveInput) > 0)
            {
                Debug.Log($"移动输入:{moveInput:F2} | " +
                          $"isGrounded:{isGrounded} | " +
                          $"velocityX:{rb.linearVelocity.x:F3} | " +
                          $"velocityY:{rb.linearVelocity.y:F3}");
            }
        }

        void HandleJump()
        {
            bool jumpPressed = false;

            if (Keyboard.current != null &&
                Keyboard.current.spaceKey.wasPressedThisFrame)
                jumpPressed = true;

            var gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)
                jumpPressed = true;

            if (jumpPressed)
            {
                Debug.Log($"跳跃尝试 | isGrounded:{isGrounded}");
                if (isGrounded)
                {
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                    Debug.Log($"跳跃成功！jumpForce:{jumpForce}");
                }
                else
                {
                    Debug.LogWarning("跳跃失败：不在地面上！");
                }
            }
        }

        void OnCollisionEnter2D(Collision2D col)
        {
            if (groundCheck == null)
            {
                foreach (ContactPoint2D contact in col.contacts)
                {
                    Debug.Log($"碰撞点法线 Y:{contact.normal.y:F3} | " +
                              $"碰撞物体:{col.gameObject.name}");
                    if (contact.normal.y > 0.5f)
                        isGrounded = true;
                }
            }
        }

        void OnCollisionStay2D(Collision2D col)
        {
            // 关键！持续检测碰撞点，找出是否被斜面推高
            foreach (ContactPoint2D contact in col.contacts)
            {
                if (Mathf.Abs(contact.normal.x) > 0.3f)
                {
                    Debug.LogWarning($"碰到斜面！法线 X:{contact.normal.x:F3} | " +
                                    $"法线 Y:{contact.normal.y:F3} | " +
                                    $"当前 velocityY:{rb.linearVelocity.y:F3} | " +
                                    $"isGrounded:{isGrounded}");
                }
            }
        }

        void OnCollisionExit2D(Collision2D col)
        {
            if (groundCheck == null)
                isGrounded = false;
        }

        void OnDrawGizmosSelected()
        {
            if (groundCheck != null)
            {
                Gizmos.color = isGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
            }
        }
    }
}