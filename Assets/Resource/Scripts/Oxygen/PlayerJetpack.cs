using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using Resource.Scripts.Gyro;

namespace Resource.Scripts
{
    /// <summary>输入在 Update 读取；位移由 PlayerController 的 FixedUpdate 调度，避免两个控制器争抢速度。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(PlayerOxygen), typeof(PlayerController))]
    public sealed class PlayerJetpack : MonoBehaviour
    {
        [Header("引用（留空自动获取同一玩家上的组件）")]
        [SerializeField] private PlayerController controller;
        [SerializeField] private PlayerOxygen oxygen;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private JetpackEffects effects;

        [Header("喷射")]
        [SerializeField, Min(0.01f)] private float dashSpeed = 18f;
        [SerializeField, Min(0.01f)] private float dashDuration = 0.18f;
        [Tooltip("从上一次喷射结束起计算；暂停期间不计时。")]
        [SerializeField, Min(0f)] private float dashCooldown = 0.5f;
        [SerializeField, Range(0f, 1f)] private float gravityMultiplier = 0.1f;
        [Tooltip("在固体表面前保留的安全距离；沿用玩家原来的 Collider2D 和碰撞层。")]
        [SerializeField, Min(0f)] private float collisionSkin = 0.01f;

        [Header("输入")]
        [SerializeField] private Key keyboardDashKey = Key.LeftShift;
        [SerializeField, Range(0f, 0.95f)] private float stickDeadzone = 0.2f;
        [SerializeField] private bool allowKeyboardDirection = true;

        private readonly List<RaycastHit2D> _castResults = new List<RaycastHit2D>(16);
        private Collider2D[] _bodyColliders;
        private Vector2 _dashDirection;
        private Vector2 _gravityVelocity;
        private float _remainingDuration;
        private float _cooldownRemaining;
        private float _savedGravityScale;
        private CollisionDetectionMode2D _savedCollisionMode;
        private bool _stopAfterPhysicsStep;

        public bool IsDashing { get; private set; }
        public float CooldownRemaining => _cooldownRemaining;
        public Vector2 DashDirection => _dashDirection;
        public float DashSpeed { get => dashSpeed; set { if (Finite(value)) dashSpeed = Mathf.Max(0.01f, value); } }
        public float DashDuration { get => dashDuration; set { if (Finite(value)) dashDuration = Mathf.Max(0.01f, value); } }
        public float DashCooldown { get => dashCooldown; set { if (Finite(value)) dashCooldown = Mathf.Max(0f, value); } }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (oxygen == null) oxygen = GetComponent<PlayerOxygen>();
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (effects == null) effects = GetComponent<JetpackEffects>();
            _bodyColliders = GetComponentsInChildren<Collider2D>();
        }

        private void Update()
        {
            if (controller == null || !controller.IsGameplayActive)
            {
                if (IsDashing) CancelDash();
                return;
            }

            _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - Time.deltaTime);
            if (GyroRuntime.ConsoleCapturesInput) return;

            bool pressed = Keyboard.current != null && keyboardDashKey != Key.None &&
                           Keyboard.current[keyboardDashKey].wasPressedThisFrame;
            Gamepad pressedGamepad = null;
            // Steam 可能把 Gamepad.current 设成虚拟 Xbox 手柄；仍扫描所有真实 DS4 / DualSense。
            foreach (var gamepad in Gamepad.all)
            {
                if (gamepad is DualShockGamepad dualShock && dualShock.touchpadButton != null &&
                    dualShock.touchpadButton.wasPressedThisFrame)
                {
                    pressed = true;
                    pressedGamepad = gamepad;
                    break;
                }
            }

            var gyro = GyroRuntime.Current;
            bool nativeTouchpad = gyro != null && (gyro.PressedButtons & GyroButtons.Touchpad) != 0;
            if (pressed || nativeTouchpad)
            {
                Vector2 direction = ReadLocalDirection(pressedGamepad);
                if (nativeTouchpad && gyro.Controls.LeftStick.sqrMagnitude > stickDeadzone * stickDeadzone)
                    direction = gyro.Controls.LeftStick;
                TryDash(direction);
            }
        }

        private Vector2 ReadLocalDirection(Gamepad pressedGamepad)
        {
            float deadzoneSquared = stickDeadzone * stickDeadzone;
            if (pressedGamepad != null)
            {
                Vector2 pressedStick = pressedGamepad.leftStick.ReadValue();
                if (pressedStick.sqrMagnitude > deadzoneSquared) return pressedStick;
            }

            Vector2 bestStick = Vector2.zero;
            foreach (var gamepad in Gamepad.all)
            {
                Vector2 stick = gamepad.leftStick.ReadValue();
                if (stick.sqrMagnitude > bestStick.sqrMagnitude) bestStick = stick;
            }
            if (bestStick.sqrMagnitude > deadzoneSquared) return bestStick;

            Vector2 keyboardDirection = Vector2.zero;
            var keyboard = Keyboard.current;
            if (allowKeyboardDirection && keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) keyboardDirection.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) keyboardDirection.x += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) keyboardDirection.y -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) keyboardDirection.y += 1f;
            }
            return keyboardDirection;
        }

        /// <summary>方向使用玩家本地坐标；传入零向量时使用玩家朝向。氧气扣费也会调用现有死亡入口。</summary>
        public bool TryDash(Vector2 localDirection)
        {
            if (!isActiveAndEnabled || controller == null || !controller.IsGameplayActive ||
                oxygen == null || body == null || !body.simulated ||
                body.bodyType != RigidbodyType2D.Dynamic || IsDashing || _cooldownRemaining > 0f)
                return false;
            if (float.IsNaN(localDirection.x) || float.IsNaN(localDirection.y) ||
                float.IsInfinity(localDirection.x) || float.IsInfinity(localDirection.y))
                return false;

            if (localDirection.sqrMagnitude < 0.0001f)
                localDirection = new Vector2(controller.FacingSign, 0f);

            Vector2 worldDirection = transform.TransformDirection(localDirection.normalized);
            if (worldDirection.sqrMagnitude < 0.0001f) return false;
            if (!oxygen.TryConsume(oxygen.DashCost)) return false;
            // 恰好用尽氧气时 TryConsume 会同步触发死亡，不能把冻结的玩家再次启动。
            if (!controller.IsGameplayActive || oxygen.CurrentOxygen <= 0f) return false;

            _dashDirection = worldDirection.normalized;
            _gravityVelocity = Vector2.zero;
            _remainingDuration = dashDuration;
            _stopAfterPhysicsStep = false;
            _savedGravityScale = controller.SimulatedGravityScale;
            _savedCollisionMode = body.collisionDetectionMode;
            controller.SimulatedGravityScale = _savedGravityScale * gravityMultiplier;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            IsDashing = true;
            effects?.BeginDash(_dashDirection);
            return true;
        }

        /// <summary>返回 true 时本物理步由冲刺处理，普通行走应跳过；不得另设 FixedUpdate 抢写速度。</summary>
        public bool TickDash(float fixedDeltaTime)
        {
            if (!IsDashing) return false;
            if (!isActiveAndEnabled || controller == null || !controller.IsGameplayActive ||
                body == null || !body.simulated || body.bodyType != RigidbodyType2D.Dynamic)
            {
                CancelDash();
                return true;
            }
            if (_stopAfterPhysicsStep || _remainingDuration <= 0f)
            {
                CancelDash();
                return false;
            }
            if (fixedDeltaTime <= 0f) return true;

            float stepDuration = Mathf.Min(fixedDeltaTime, _remainingDuration);
            Vector2 velocity = _dashDirection * (dashSpeed * stepDuration / fixedDeltaTime) + _gravityVelocity;
            // 自管理模式由控制器施加重力；关闭时仍由 Unity 施加。预测包含本步重力。
            Vector2 stepGravity = Physics2D.gravity * controller.SimulatedGravityScale * fixedDeltaTime;
            Vector2 predictedVelocity = velocity + stepGravity;
            Vector2 displacement = predictedVelocity * fixedDeltaTime;
            float distance = displacement.magnitude;
            if (distance > 0.00001f)
            {
                Vector2 travelDirection = displacement / distance;
                float allowedDistance = FindClearDistance(travelDirection, distance);
                if (allowedDistance < distance)
                {
                    // 走到安全距离后结束，而不是直接改 Transform 越过墙壁。
                    velocity = travelDirection * (allowedDistance / fixedDeltaTime) - stepGravity;
                    _stopAfterPhysicsStep = true;
                }
            }

            if (controller.antiPushEnabled)
                controller.SetIntendedVelocity(velocity + stepGravity);
            else
                body.linearVelocity = velocity;
            _gravityVelocity += stepGravity;
            _remainingDuration -= stepDuration;
            effects?.UpdateDirection(_dashDirection);
            return true;
        }

        private float FindClearDistance(Vector2 direction, float distance)
        {
            float clearDistance = distance;
            foreach (var source in _bodyColliders)
            {
                if (source == null || !source.enabled || !source.gameObject.activeInHierarchy ||
                    source.isTrigger || source.attachedRigidbody != body)
                    continue;

                var filter = new ContactFilter2D();
                filter.SetLayerMask(Physics2D.GetLayerCollisionMask(source.gameObject.layer));
                filter.useTriggers = false;
                _castResults.Clear();
                source.Cast(direction, filter, _castResults, distance + collisionSkin, true);
                foreach (var hit in _castResults)
                {
                    Collider2D target = hit.collider;
                    if (target == null || target.attachedRigidbody == body || target.isTrigger ||
                        Physics2D.GetIgnoreCollision(source, target))
                        continue;
                    Vector2 surfaceNormal = hit.normal;
                    if (hit.distance <= 0.00001f)
                    {
                        // Cast 从贴面/重叠处出发时可能返回 -direction，而不是真实表面法线。
                        // 用两个碰撞体的最近表面恢复法线，避免把脚下地面误当成正前方墙壁。
                        ColliderDistance2D separation = Physics2D.Distance(source, target);
                        if (separation.isValid)
                        {
                            Vector2 betweenSurfaces = separation.pointA - separation.pointB;
                            if (betweenSurfaces.sqrMagnitude > 0.00000001f)
                                surfaceNormal = betweenSurfaces.normalized * (separation.isOverlapped ? -1f : 1f);
                            else
                                surfaceNormal = separation.normal;
                        }
                    }
                    // 阻挡意图以喷射方向判断：预测位移中的少量重力不能取消沿地面的水平喷射。
                    // 重力接触仍交给原来的物理解算和连续碰撞处理。
                    if (Vector2.Dot(_dashDirection, surfaceNormal) >= -0.001f) continue;
                    clearDistance = Mathf.Min(clearDistance, Mathf.Max(0f, hit.distance - collisionSkin));
                }
            }
            return clearDistance;
        }

        private void OnCollisionEnter2D(Collision2D collision) => StopForCollision(collision);
        private void OnCollisionStay2D(Collision2D collision) => StopForCollision(collision);

        private void StopForCollision(Collision2D collision)
        {
            if (!IsDashing) return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (Vector2.Dot(_dashDirection, collision.GetContact(i).normal) < -0.001f)
                {
                    CancelDash();
                    return;
                }
            }
        }

        /// <summary>正常结束、死亡、暂停/预览和禁用时共用；不修改 bodyType，不会解除死亡冻结。</summary>
        public void CancelDash()
        {
            if (IsDashing)
            {
                IsDashing = false;
                _remainingDuration = 0f;
                _cooldownRemaining = dashCooldown;
                _stopAfterPhysicsStep = false;
                if (body != null)
                {
                    if (controller != null) controller.SimulatedGravityScale = _savedGravityScale;
                    else body.gravityScale = _savedGravityScale;
                    body.collisionDetectionMode = _savedCollisionMode;
                    if (controller != null && controller.antiPushEnabled)
                        controller.SetIntendedVelocity(Vector2.zero);
                    else
                        body.linearVelocity = Vector2.zero;
                }
            }
            effects?.EndDash();
        }

        private void OnDisable() => CancelDash();

        private void OnValidate()
        {
            dashSpeed = Mathf.Max(0.01f, dashSpeed);
            dashDuration = Mathf.Max(0.01f, dashDuration);
            dashCooldown = Mathf.Max(0f, dashCooldown);
            collisionSkin = Mathf.Max(0f, collisionSkin);
        }
    }
}
