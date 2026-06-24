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
        public float groundCheckRadius = 0.2f;
        public LayerMask groundLayer;

        [Header("墙壁检测")]
        public Transform wallCheckLeft;
        public Transform wallCheckRight;
        public float wallCheckRadius = 0.2f;
        public LayerMask wallLayer;

        private Rigidbody2D rb;
        private bool isGrounded = false;
        private bool isTouchingWallLeft = false;
        private bool isTouchingWallRight = false;
        private float debugTimer = 0f;

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();
        }

        void FixedUpdate()
        {
            CheckGround();
            CheckWalls();
            HandleMovement();
        }

        void Update()
        {
            HandleJump();

            debugTimer += Time.deltaTime;
            if (debugTimer >= 1f)
            {
                debugTimer = 0f;
                Debug.Log($"isGrounded:{isGrounded} | " +
                          $"WallLeft:{isTouchingWallLeft} | " +
                          $"WallRight:{isTouchingWallRight} | " +
                          $"velocity:{rb.linearVelocity}");
            }
        }

        void CheckGround()
        {
            if (groundCheck != null && groundLayer != 0)
            {
                isGrounded = Physics2D.OverlapCircle(
                    groundCheck.position,
                    groundCheckRadius,
                    groundLayer
                );
            }
        }

        void CheckWalls()
        {
            if (wallCheckLeft != null && wallLayer != 0)
            {
                isTouchingWallLeft = Physics2D.OverlapCircle(
                    wallCheckLeft.position,
                    wallCheckRadius,
                    wallLayer
                );
            }

            if (wallCheckRight != null && wallLayer != 0)
            {
                isTouchingWallRight = Physics2D.OverlapCircle(
                    wallCheckRight.position,
                    wallCheckRadius,
                    wallLayer
                );
            }
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

            // 左边碰墙 → 禁止向左移动
            if (isTouchingWallLeft && moveInput < 0)
            {
                Debug.Log("左边碰墙！禁止向左移动");
                moveInput = 0f;
            }

            // 右边碰墙 → 禁止向右移动
            if (isTouchingWallRight && moveInput > 0)
            {
                Debug.Log("右边碰墙！禁止向右移动");
                moveInput = 0f;
            }

            rb.linearVelocity = new Vector2(
                moveInput * maxMoveSpeed,
                rb.linearVelocity.y
            );
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
                    rb.linearVelocity = new Vector2(
                        rb.linearVelocity.x, jumpForce);
                    Debug.Log("跳跃成功！");
                }
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

        void OnDrawGizmos()
        {
            // 地面检测圆（绿/红）
            if (groundCheck != null)
            {
                Gizmos.color = isGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
                // 从玩家画线到 GroundCheck
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, groundCheck.position);
            }

            // 左墙检测圆（蓝/青）
            if (wallCheckLeft != null)
            {
                Gizmos.color = isTouchingWallLeft ? Color.blue : Color.cyan;
                Gizmos.DrawWireSphere(wallCheckLeft.position, wallCheckRadius);
                // 从玩家画线到 WallCheckLeft
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, wallCheckLeft.position);
            }

            // 右墙检测圆（黄/白）
            if (wallCheckRight != null)
            {
                Gizmos.color = isTouchingWallRight ? Color.yellow : Color.white;
                Gizmos.DrawWireSphere(wallCheckRight.position, wallCheckRadius);
                // 从玩家画线到 WallCheckRight
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, wallCheckRight.position);
            }
        }
    }
}