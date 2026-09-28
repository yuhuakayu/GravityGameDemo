using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Resource.Scripts
{
    /// <summary>
    /// 摆锤脚本（纯数学模拟版）
    ///
    /// 工作原理：
    ///   - Rigidbody2D (Kinematic) 驱动几何碰撞，玩家防推动由 PlayerController / CrushGuard 负责
    ///   - 脚本自己维护 _angle / _angularVelocity，每帧用标准单摆公式更新
    ///   - 重力方向 = Physics2D.gravity（与玩家完全一致）
    ///   - 范围限制 = 直接 Clamp，超出乘以 bounciness
    ///   - MoveRotation + MovePosition 在 FixedUpdate 驱动刚体
    ///
    /// 角度约定（顺时针正方向）：
    ///   0° = 12点钟（重力反方向）
    ///  90° = 3点钟
    /// 180° = 6点钟（自然悬挂位置，重力方向）
    ///
    /// 场景结构：
    ///   Pivot 可以是任意外部物体（比如挂在 WorldRoot 下、纯视觉的支架），不需要是 Clock
    ///   的子物体；属于 WorldRoot 时使用它本步计划的物理姿态，避免连接点落后一帧。
    ///   Clock  （此脚本 + Rigidbody2D + Collider2D）
    ///     Circle  → HingePoint：板内真正铰链，位置固定在外部 Pivot 上。
    ///     Grivity → GravityPoint：只提供臂长，不参与板角度和位置的反推。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class PivotPendulum : MonoBehaviour
    {
        // ── 调试 ─────────────────────────────────────────────────
        [Header("调试")]
        public bool isDebugLog    = false;   // 控制台 Log
        public bool isDebugGizmos = false;   // Scene / Game 视图画图

        // ── 关键点 ───────────────────────────────────────────────
        [Header("关键点")]
        [Tooltip("外部圆心/锚点；WorldRoot 下的锚点使用本步计划姿态，保证与板内铰链同步")]
        public Transform pivot;
        [Tooltip("板内真正铰链（Circle）。必须是板的子物体，启动时将它与外部 Pivot 对齐")]
        public Transform hingePoint;
        [Tooltip("重力点（Grivity），必须是 Clock 自己的子物体，仅决定到 HingePoint 的摆臂长度")]
        public Transform gravityPoint;
        [Tooltip("板的本地长轴，默认沿 X；启动时自动选择从铰链朝板内的一侧，避免左右板朝向反转")]
        public Vector2 localLongAxis = Vector2.right;
        [Tooltip("玩家 Transform，调试重力线从玩家中心出发")]
        public Transform player;

        // ── 物理参数 ─────────────────────────────────────────────
        [Header("物理参数")]
        [Tooltip("重力强度，数值越大摆动越快（方向始终跟 Physics2D.gravity 一致）")]
        public float gravity = 20f;
        [Tooltip("阻尼，控制摆动衰减速度（0 = 不衰减）")]
        [Range(0f, 10f)]
        public float angularDamping = 1f;
        [Tooltip("边界弹性（0 = 碰边停止，1 = 完全弹回）")]
        [Range(0f, 1f)]
        public float bounciness = 0.3f;

        // ── 旋转范围 ─────────────────────────────────────────────
        [Header("旋转范围（0=12点, 90=3点, 180=6点, 顺时针正方向）")]
        public float minAngle = -90f;
        public float maxAngle =  90f;

        // ── 旋转参考系 ───────────────────────────────────────────
        [Header("旋转参考系")]
        [Tooltip("WorldRoot GameObject（可选）。用于摆角和范围限制的参考方向；世界旋转期间钟摆仍持续受重力影响。留空时使用重力反方向作为参考")]
        public Transform worldRoot;

        [Header("单步几何运动限制")]
        [Range(0.01f, 0.49f), Tooltip("板上最远点的单步运动距离，相对于玩家最小厚度的上限")]
        public float maxStepTravelFraction = 0.45f;
        [Range(0f, 1f), Tooltip("靠近玩家时允许本次摆动使用的穿透修复预算比例；先扣除本步世界运动")]
        public float repairBudgetFraction = 0.8f;

        [Header("撞击音效")]
        [Tooltip("角速度低于此值（度/秒）不触发撞击声，避免贴着边界静止时反复触发")]
        public float impactSfxMinSpeed = 8f;
        [Tooltip("角速度达到此值（度/秒）时撞击声为最大音量")]
        public float impactSfxMaxSpeed = 180f;

        [Header("玩家碰撞")]
        [Tooltip("关闭（默认）：撞到玩家表现得像普通墙壁，不会把玩家撞飞。打开：保留 Unity 物理的真实撞击力，可以当发射/助力机制用")]
        public bool dealsImpactForce = false;
        [Tooltip("dealsImpactForce 关闭时生效：玩家碰到摆锤后速度上限（超过会被压回这个值），只是防止被撞飞，不影响正常移动/跳跃手感")]
        public float maxPlayerSpeedOnContact = 14f;

        // ── 运行时私有变量 ───────────────────────────────────────
        private Rigidbody2D _rb;
        private float _angle;             // 当前角度（顺时针，0 = 12点钟）
        private float _angularVelocity;   // 度/秒，正值 = 顺时针

        private float   _armLength;
        // Retain physical scale and remove only body rotation. InverseTransformPoint would
        // divide out the x scale of the Stage1 planks and detach their hinges.
        private Vector2 _hingeOffsetUnrotated;
        private Vector2 _axisUnrotated;
        private float _localAxisAngle;
        private WorldRotator _pivotWorld;
        private WorldRotator _referenceWorld;
        private Rigidbody2D _pivotWorldBody;
        private Rigidbody2D _referenceWorldBody;
        private PlayerController _playerController;
        private Collider2D[] _geometry;
        private bool _initialized;
        public float AngleReadout => _angle;
        public float LastRequestedAngularStep { get; private set; }
        public float LastAppliedAngularStep { get; private set; }
        public Vector2 HingeWorldPosition => _rb != null && _initialized
            ? _rb.position + (Vector2)(Quaternion.Euler(0f, 0f, _rb.rotation) * (Vector3)_hingeOffsetUnrotated)
            : hingePoint != null ? (Vector2)hingePoint.position : (Vector2)transform.position;

        // ─────────────────────────────────────────────────────────
        void Start() { InitializePendulum(); }

        /// <summary>Initialize once before physics; also available to isolated physics fixtures.</summary>
        public bool InitializePendulum()
        {
            if (_initialized) return true;
            if (hingePoint == null) hingePoint = transform.Find("Circle");
            if (pivot == null || gravityPoint == null || hingePoint == null ||
                !hingePoint.IsChildOf(transform) || !gravityPoint.IsChildOf(transform))
            {
                Debug.LogWarning("[PivotPendulum] 需要外部 Pivot、板内 HingePoint (Circle) 和 GravityPoint (Grivity)。", this);
                enabled = false;
                return false;
            }

            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.gravityScale = 0f;
            _rb.useFullKinematicContacts = true;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _pivotWorld = pivot.GetComponentInParent<WorldRotator>();
            _referenceWorld = worldRoot != null ? worldRoot.GetComponentInParent<WorldRotator>() : null;
            _pivotWorldBody = _pivotWorld != null ? _pivotWorld.GetComponent<Rigidbody2D>() : null;
            _referenceWorldBody = _referenceWorld != null ? _referenceWorld.GetComponent<Rigidbody2D>() : null;
            _playerController = player != null ? player.GetComponentInParent<PlayerController>() : FindFirstObjectByType<PlayerController>();
            _geometry = GetComponentsInChildren<Collider2D>(true);

            // Initialization only: align the actual hinge before caching geometry. A body-position
            // write does not manufacture velocity, and no running-physics teleport is needed later.
            Vector2 hingeBeforeAlignment = hingePoint.position;
            Vector2 alignment = (Vector2)pivot.position - hingeBeforeAlignment;
            _rb.position += alignment;
            _hingeOffsetUnrotated = Quaternion.Euler(0f, 0f, -_rb.rotation)
                * (Vector3)(hingeBeforeAlignment + alignment - _rb.position);
            _armLength = Vector2.Distance(hingePoint.position, gravityPoint.position);
            if (_armLength < 0.001f)
            {
                Debug.LogError("[PivotPendulum] HingePoint 和 GravityPoint 重叠，臂长为 0。", this);
                enabled = false;
                return false;
            }

            Vector2 worldAxis = GetBoardAxisWorld();
            _axisUnrotated = Quaternion.Euler(0f, 0f, -_rb.rotation) * (Vector3)worldAxis.normalized;
            _localAxisAngle = Mathf.Atan2(_axisUnrotated.x, _axisUnrotated.y) * Mathf.Rad2Deg;
            Vector2 upDir = ResolveReferenceUp(false);
            _angle = Mathf.Clamp(-Vector2.SignedAngle(upDir, worldAxis), minAngle, maxAngle);
            _angularVelocity = 0f;
            CalculatePose(_angle, upDir, pivot.position, out Vector2 initialPosition, out float initialRotation);
            _rb.rotation = initialRotation;
            _rb.position = initialPosition;
            _initialized = true;
            if (isDebugLog)
                Debug.Log($"[PivotPendulum] 初始化完成 | armLength={_armLength:F2} | initAngle={_angle:F1}°");
            return true;
        }

        // ─────────────────────────────────────────────────────────
        void FixedUpdate() { Step(Time.fixedDeltaTime); }

        /// <summary>Schedule the plank for the same next physics pose as its world-root hinge.</summary>
        public void Step(float dt)
        {
            if (!_initialized && !InitializePendulum()) return;
            if (_rb == null || pivot == null || dt <= 0f) return;

            Vector2 gravDir = Physics2D.gravity.normalized;   // 重力方向（世界空间，用于计算力矩）
            Vector2 upDir = ResolveReferenceUp(true);
            Vector2 pivotPosition = ResolvePivotPosition();

            // ── 1. 当前 arm 方向（从 upDir 顺时针 _angle 度）─────
            //   AngleAxis(-_angle, forward) = Unity 约定 CCW 正，取负 = 顺时针
            Vector2 armDir = Quaternion.AngleAxis(-_angle, Vector3.forward) * upDir;

            // ── 2. 顺时针切线方向（_angle 增大的方向）────────
            Vector2 tangentCW = Quaternion.AngleAxis(-90f, Vector3.forward) * armDir;

            // ── 3. 角加速度（重力在切线方向的投影 / 臂长）────
            float tangGrav = Vector2.Dot(gravDir * gravity, tangentCW);
            float alpha    = (tangGrav / _armLength) * Mathf.Rad2Deg;

            // ── 4. 积分：世界旋转期间也持续摆动 ────────────────
            _angularVelocity += alpha * dt;
            _angularVelocity *= Mathf.Max(0f, 1f - angularDamping * dt); // 阻尼

            float previousAngle = _angle;
            float unconstrainedAngle = previousAngle + _angularVelocity * dt;
            float desiredAngle = Mathf.Clamp(unconstrainedAngle, minAngle, maxAngle);
            LastRequestedAngularStep = desiredAngle - previousAngle;
            // Keep the hinge at the world's NEXT physics pose while budgeting only the extra
            // independent swing. Interpolating the body's position/rotation would detach it.
            CalculatePose(previousAngle, upDir, pivotPosition, out Vector2 transportedPosition, out float transportedAngle);
            LastAppliedAngularStep = -KinematicMotionSafety.ClampRotationStep(_geometry, pivotPosition,
                -LastRequestedAngularStep, _playerController, maxStepTravelFraction, repairBudgetFraction,
                _rb, transportedPosition, transportedAngle);
            _angle = previousAngle + LastAppliedAngularStep;
            if (Mathf.Abs(LastAppliedAngularStep) + .000001f < Mathf.Abs(LastRequestedAngularStep))
                _angularVelocity = LastAppliedAngularStep / dt;

            // Play the stop and rebound only when the board ACTUALLY reaches the boundary;
            // a limited step that has not arrived must not bounce early or accumulate speed.
            if (unconstrainedAngle < minAngle && _angle <= minAngle + .0001f)
            {
                PlayImpactSfx(Mathf.Abs(_angularVelocity));
                _angle           = minAngle;
                _angularVelocity = Mathf.Abs(_angularVelocity) * bounciness;  // 正=顺时针弹回
            }
            else if (unconstrainedAngle > maxAngle && _angle >= maxAngle - .0001f)
            {
                PlayImpactSfx(Mathf.Abs(_angularVelocity));
                _angle           = maxAngle;
                _angularVelocity = -Mathf.Abs(_angularVelocity) * bounciness; // 负=逆时针弹回
            }

            // Geometry comes from the board's long axis and real hinge, never the Grivity
            // marker direction. Both mirrored planks are exactly parallel at +90 / -90.
            CalculatePose(_angle, upDir, pivotPosition, out Vector2 newPos, out float newZRot);

            // ── 9. 驱动 Kinematic Rigidbody2D ────────────────────
            // 防护：任一值为 NaN 或 Infinity 时重置，避免 Unity Assertion 报错
            bool badRot = float.IsNaN(newZRot)  || float.IsInfinity(newZRot);
            bool badPos = float.IsNaN(newPos.x)  || float.IsInfinity(newPos.x)
                       || float.IsNaN(newPos.y)  || float.IsInfinity(newPos.y);
            if (badRot || badPos)
            {
                Debug.LogWarning($"[PivotPendulum] 检测到非法值（NaN/Inf），已重置！rot={newZRot} pos={newPos}");
                _angularVelocity = 0f;
                _angle = Mathf.Clamp(0f, minAngle, maxAngle);
                return;
            }
            _rb.MoveRotation(newZRot);
            _rb.MovePosition(newPos);

            // ── 10. 调试 ──────────────────────────────────────────
            if (isDebugLog)
                Debug.Log($"[Pendulum] angle={_angle:F1}° | angVel={_angularVelocity:F1}°/s");

            if (isDebugGizmos)
            {
                Vector3 origin = player != null ? player.position : pivot.position;
                Debug.DrawRay(origin, gravDir * 3f, new Color(1f, 0.5f, 0f));
            }
        }

        private void CalculatePose(float angle, Vector2 referenceUp, Vector2 hingePosition,
            out Vector2 position, out float rotation)
        {
            Vector2 axis = Quaternion.Euler(0f, 0f, -angle) * (Vector3)referenceUp;
            float axisAngle = Mathf.Atan2(axis.x, axis.y) * Mathf.Rad2Deg;
            rotation = _localAxisAngle - axisAngle;
            position = hingePosition - (Vector2)(Quaternion.Euler(0f, 0f, rotation) * (Vector3)_hingeOffsetUnrotated);
        }

        private Vector2 ResolvePivotPosition()
        {
            if (_pivotWorld != null && pivot.IsChildOf(_pivotWorld.transform))
            {
                if (_pivotWorld.HasPendingPhysicsPose) return _pivotWorld.GetPointAtNextPose(pivot);
                if (_pivotWorldBody != null)
                {
                    // Interpolation is visual. Even a stopped root must use its actual physics
                    // pose instead of an interpolated Transform when driving another body.
                    Vector3 offset = Quaternion.Inverse(_pivotWorld.transform.rotation)
                        * (pivot.position - _pivotWorld.transform.position);
                    return _pivotWorldBody.position + (Vector2)(Quaternion.Euler(0f, 0f, _pivotWorldBody.rotation) * offset);
                }
            }
            return pivot.position;
        }

        private Vector2 ResolveReferenceUp(bool nextPhysicsPose)
        {
            if (worldRoot == null)
                return Physics2D.gravity.sqrMagnitude > .000001f ? -Physics2D.gravity.normalized : Vector2.up;
            if (nextPhysicsPose && _referenceWorld != null &&
                (worldRoot == _referenceWorld.transform || worldRoot.IsChildOf(_referenceWorld.transform)))
            {
                if (_referenceWorld.HasPendingPhysicsPose && worldRoot == _referenceWorld.transform) return _referenceWorld.NextUp;
                Vector3 localUp = Quaternion.Inverse(_referenceWorld.transform.rotation) * worldRoot.up;
                if (_referenceWorld.HasPendingPhysicsPose)
                    return ((Vector2)(Quaternion.Euler(0f, 0f, _referenceWorld.NextAngle) * localUp)).normalized;
                if (_referenceWorldBody != null)
                    return ((Vector2)(Quaternion.Euler(0f, 0f, _referenceWorldBody.rotation) * localUp)).normalized;
            }
            return worldRoot.up;
        }

        private Vector2 GetBoardAxisWorld()
        {
            Vector2 axis = localLongAxis.sqrMagnitude > .000001f ? localLongAxis.normalized : Vector2.right;
            Vector2 worldAxis = transform.TransformVector(axis);
            if (worldAxis.sqrMagnitude < .000001f) worldAxis = transform.right;
            if (hingePoint != null && Vector2.Dot(worldAxis, (Vector2)(transform.position - hingePoint.position)) < 0f)
                worldAxis = -worldAxis;
            return worldAxis.normalized;
        }

        /// <summary>给摆锤施加角冲量（度/秒），可用于碰撞触发摆动</summary>
        public void AddImpulse(float degreesPerSecond)
        {
            _angularVelocity += degreesPerSecond;
        }

        // Enter 只在接触第一帧触发、Stay 从第二帧开始触发，两个都要接，
        // 不然第一下最猛的撞击（Enter 那一帧）不会被压制
        void OnCollisionEnter2D(Collision2D col) => LimitImpactVelocity(col);
        void OnCollisionStay2D(Collision2D col)  => LimitImpactVelocity(col);

        void LimitImpactVelocity(Collision2D col)
        {
            if (dealsImpactForce) return;

            var playerRb = col.rigidbody;
            if (playerRb == null) return;
            var controller = col.collider.GetComponentInParent<PlayerController>();
            if (controller == null || controller.antiPushEnabled) return;

            // 只压制"因为这次碰撞产生的额外速度"，正常移动/跳跃/下落速度不受影响
            if (playerRb.linearVelocity.magnitude > maxPlayerSpeedOnContact)
                playerRb.linearVelocity = playerRb.linearVelocity.normalized * maxPlayerSpeedOnContact;
        }

        void PlayImpactSfx(float impactSpeed)
        {
            if (impactSpeed < impactSfxMinSpeed) return;
            float range = Mathf.Max(0.01f, impactSfxMaxSpeed - impactSfxMinSpeed);
            float impact01 = Mathf.Clamp01((impactSpeed - impactSfxMinSpeed) / range);
            SfxManager.Instance.PlayPivotClack(impact01);
        }

        // ─────────────────────────────────────────────────────────
#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!isDebugGizmos || pivot == null) return;

            // Unity 默认把 Gizmos.matrix 设成物体的 localToWorldMatrix，
            // 导致扇形跟着 Clock 旋转。强制还原为世界坐标系。
            Matrix4x4 savedGizmos  = Gizmos.matrix;
            Matrix4x4 savedHandles = Handles.matrix;
            Gizmos.matrix  = Matrix4x4.identity;
            Handles.matrix = Matrix4x4.identity;

            Vector3 center = pivot.position;
            float   arm    = gravityPoint != null && hingePoint != null
                ? Vector3.Distance(hingePoint.position, gravityPoint.position)
                : 1f;

            Vector2 gravDir = Application.isPlaying ? Physics2D.gravity.normalized : Vector2.down;

            // 扇形"12点钟"方向：如果有 WorldRoot，跟着它旋转（视觉一致）；
            // 没有则用真实重力反方向（世界 up）
            Vector3 upDir = ResolveReferenceUp(false);

            // 淡红色填充扇形（允许范围）
            Vector3 fromDir  = Quaternion.AngleAxis(-minAngle, Vector3.forward) * upDir;
            float sweepAngle = -(maxAngle - minAngle); // 负值 = 顺时针扫描
            Handles.color = new Color(1f, 0.15f, 0.15f, 0.18f);
            Handles.DrawSolidArc(center, Vector3.forward, fromDir, sweepAngle, arm);

            // 扇形轮廓
            Handles.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            Handles.DrawWireArc(center, Vector3.forward, fromDir, sweepAngle, arm);

            // 两条边界线（用 upDir 保持与扇形同一参考系）
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            DrawBoundLine(center, minAngle, arm, upDir);
            DrawBoundLine(center, maxAngle, arm, upDir);

            // 当前摆臂（白色）
            Gizmos.color = Color.white;
            Vector2 boardAxis = Application.isPlaying && _initialized
                ? (Vector2)(Quaternion.Euler(0f, 0f, _rb.rotation) * (Vector3)_axisUnrotated)
                : GetBoardAxisWorld();
            Vector3 armEnd = center + (Vector3)boardAxis * arm;
            Gizmos.DrawLine(center, armEnd);

            // 重力方向（橙色短线）
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawLine(center, center + (Vector3)gravDir * arm * 0.4f);

            // 圆心 / 重力点标记
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(center, 0.1f);
            if (gravityPoint != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(gravityPoint.position, 0.15f);
            }

            // 恢复原矩阵
            Gizmos.matrix  = savedGizmos;
            Handles.matrix = savedHandles;
        }

        void DrawBoundLine(Vector3 center, float angleDeg, float armLen, Vector2 upDir)
        {
            Vector3 dir = Quaternion.AngleAxis(-angleDeg, Vector3.forward) * (Vector3)upDir;
            Gizmos.DrawLine(center, center + dir * armLen);
        }
#endif
    }
}
