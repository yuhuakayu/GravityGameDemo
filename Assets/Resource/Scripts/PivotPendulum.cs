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
    ///   - Rigidbody2D (Kinematic) 仅用于碰撞检测，不推开其他物体
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
    ///   Clock  （此脚本 + Rigidbody2D + Collider2D，不要放在 WorldRoot 下）
    ///     Circle   → 拖入 Pivot        （固定圆心，游戏中不移动）
    ///     Grivity  → 拖入 GravityPoint  （球/锤，决定初始臂长和角度）
    /// </summary>
    public class PivotPendulum : MonoBehaviour
    {
        // ── 调试 ─────────────────────────────────────────────────
        [Header("调试")]
        public bool isDebugLog    = false;   // 控制台 Log
        public bool isDebugGizmos = false;   // Scene / Game 视图画图

        // ── 关键点 ───────────────────────────────────────────────
        [Header("关键点")]
        [Tooltip("固定圆心（Circle），游戏过程中世界坐标不变")]
        public Transform pivot;
        [Tooltip("重力点（Grivity），决定初始臂长和起始角度")]
        public Transform gravityPoint;
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

        // ── 延迟响应 ─────────────────────────────────────────────
        [Header("延迟响应")]
        [Tooltip("世界旋转停止后，钟摆等待多少秒才开始摆动（0 = 立即响应）")]
        public float gravityDelay = 0.2f;
        [Tooltip("WorldRoot GameObject（可选）。填入后，世界旋转时自动触发延迟；留空则延迟不生效")]
        public Transform worldRoot;

        // ── 运行时私有变量 ───────────────────────────────────────
        private Rigidbody2D _rb;
        private float _angle;             // 当前角度（顺时针，0 = 12点钟）
        private float _angularVelocity;   // 度/秒，正值 = 顺时针

        private float   _armLength;
        private Vector2 _pivotWorldPos;    // pivot 世界坐标（Start 缓存，固定不变）
        private Vector2 _pivotOffsetLocal; // pivot 在 Clock 本地坐标系的偏移
        private float   _localArmAngle;    // arm 在 Clock 本地空间从 up 方向顺时针的角度

        private float _delayTimer    = 0f; // 剩余延迟时间
        private float _prevWorldAngle = 0f; // 上一帧 worldRoot 的 Z 角度

        // ─────────────────────────────────────────────────────────
        void Start()
        {
            if (pivot == null || gravityPoint == null)
            {
                if (isDebugLog) Debug.LogWarning("[PivotPendulum] 未设置 Pivot 或 GravityPoint！");
                enabled = false;
                return;
            }

            // 缓存几何信息（此后不再改变）
            _armLength = Vector3.Distance(pivot.position, gravityPoint.position);
            if (_armLength < 0.001f)
            {
                Debug.LogError("[PivotPendulum] Pivot 和 GravityPoint 位置重叠，臂长为 0，脚本已禁用！");
                enabled = false;
                return;
            }
            _pivotWorldPos    = pivot.position;
            _pivotOffsetLocal = transform.InverseTransformPoint(pivot.position);

            // arm 在 Clock 本地空间中从 up 方向顺时针的角度
            Vector3 armLocal = transform.InverseTransformDirection(
                (gravityPoint.position - pivot.position).normalized);
            _localArmAngle = Mathf.Atan2(armLocal.x, armLocal.y) * Mathf.Rad2Deg;

            // 初始角度：从"12点钟方向"顺时针算到当前 arm 方向
            // 有 worldRoot 时用它的 up 作为参考系（与 FixedUpdate 和 Gizmo 保持一致）
            Vector2 upDir  = worldRoot != null
                ? (Vector2)worldRoot.up
                : -(Vector2)Physics2D.gravity.normalized;
            Vector2 curArm = ((Vector2)(gravityPoint.position - pivot.position)).normalized;
            _angle = -Vector2.SignedAngle(upDir, curArm); // SignedAngle=CCW正，取反得CW正

            _angularVelocity = 0f;

            // Rigidbody2D → Kinematic（不推开物体，但参与碰撞检测）
            _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody2D>();
            _rb.bodyType     = RigidbodyType2D.Kinematic;
            _rb.gravityScale = 0f;

            // 延迟响应：记录 worldRoot 初始角度
            _prevWorldAngle = worldRoot != null ? worldRoot.eulerAngles.z : 0f;
            _delayTimer     = 0f;

            if (isDebugLog)
                Debug.Log($"[PivotPendulum] 初始化完成 | armLength={_armLength:F2} | initAngle={_angle:F1}°");
        }

        // ─────────────────────────────────────────────────────────
        void FixedUpdate()
        {
            if (_rb == null || pivot == null) return;

            float   dt      = Time.fixedDeltaTime;
            Vector2 gravDir = Physics2D.gravity.normalized;   // 重力方向（世界空间，用于计算力矩）
            // 12点钟参考方向：有 worldRoot 时跟着它转，与 Gizmo 扇形保持一致
            Vector2 upDir   = worldRoot != null
                ? (Vector2)worldRoot.up
                : -gravDir;

            // ── 延迟检测：世界正在旋转时持续重置计时器 ───────────
            if (worldRoot != null)
            {
                float curAngle = worldRoot.eulerAngles.z;
                if (Mathf.Abs(Mathf.DeltaAngle(_prevWorldAngle, curAngle)) > 0.01f)
                    _delayTimer = gravityDelay; // 旋转中：一直保持延迟
                _prevWorldAngle = curAngle;
            }

            bool inDelay = _delayTimer > 0f;
            if (inDelay) _delayTimer -= dt;

            // ── 1. 当前 arm 方向（从 upDir 顺时针 _angle 度）─────
            //   AngleAxis(-_angle, forward) = Unity 约定 CCW 正，取负 = 顺时针
            Vector2 armDir = Quaternion.AngleAxis(-_angle, Vector3.forward) * upDir;

            if (!inDelay)
            {
                // ── 2. 顺时针切线方向（_angle 增大的方向）────────
                Vector2 tangentCW = Quaternion.AngleAxis(-90f, Vector3.forward) * armDir;

                // ── 3. 角加速度（重力在切线方向的投影 / 臂长）────
                float tangGrav = Vector2.Dot(gravDir * gravity, tangentCW);
                float alpha    = (tangGrav / _armLength) * Mathf.Rad2Deg;

                // ── 4. 积分 ──────────────────────────────────────
                _angularVelocity += alpha * dt;
                _angularVelocity *= Mathf.Max(0f, 1f - angularDamping * dt); // 阻尼
            }
            else
            {
                // 延迟期间：冻结速度，钟摆静止等待
                _angularVelocity = 0f;
            }

            _angle += _angularVelocity * dt;

            // ── 5. 边界限制 ───────────────────────────────────────
            if (_angle < minAngle)
            {
                _angle           = minAngle;
                _angularVelocity = Mathf.Abs(_angularVelocity) * bounciness;  // 正=顺时针弹回
            }
            else if (_angle > maxAngle)
            {
                _angle           = maxAngle;
                _angularVelocity = -Mathf.Abs(_angularVelocity) * bounciness; // 负=逆时针弹回
            }

            // ── 6. 计算限制后的 arm 方向 ──────────────────────────
            Vector2 finalArm = Quaternion.AngleAxis(-_angle, Vector3.forward) * upDir;

            // ── 7. 计算 Clock 需要的世界旋转角（Z 轴）─────────────
            //   推导：newZRot = _localArmAngle - worldArmAngle
            //   （两个角度都用"从Y轴顺时针"约定，和Unity的CCW约定方向相反）
            float worldArmAngle = Mathf.Atan2(finalArm.x, finalArm.y) * Mathf.Rad2Deg;
            float newZRot       = _localArmAngle - worldArmAngle;

            // ── 8. 计算 Clock 需要的世界位置（保证 pivot 固定在原处）
            Vector2 pivotOffsetWorld = Quaternion.Euler(0f, 0f, newZRot) * (Vector3)_pivotOffsetLocal;
            Vector2 newPos           = _pivotWorldPos - pivotOffsetWorld;

            // ── 9. 驱动 Kinematic Rigidbody2D ────────────────────
            // NaN 防护：任一值异常时重置，避免 Unity Assertion 报错
            if (float.IsNaN(newZRot) || float.IsNaN(newPos.x) || float.IsNaN(newPos.y))
            {
                Debug.LogWarning("[PivotPendulum] 检测到 NaN，已重置速度！");
                _angularVelocity = 0f;
                _angle = Mathf.Clamp(0f, minAngle, maxAngle);
                return;
            }
            _rb.MoveRotation(newZRot);
            _rb.MovePosition(newPos);

            // ── 10. 调试 ──────────────────────────────────────────
            if (isDebugLog)
                Debug.Log($"[Pendulum] angle={_angle:F1}° | angVel={_angularVelocity:F1}°/s | delay={_delayTimer:F2}s");

            if (isDebugGizmos)
            {
                Vector3 origin = player != null ? player.position : (Vector3)_pivotWorldPos;
                Debug.DrawRay(origin, gravDir * 3f, new Color(1f, 0.5f, 0f));
            }
        }

        /// <summary>给摆锤施加角冲量（度/秒），可用于碰撞触发摆动</summary>
        public void AddImpulse(float degreesPerSecond)
        {
            _angularVelocity += degreesPerSecond;
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
            float   arm    = gravityPoint != null
                ? Vector3.Distance(pivot.position, gravityPoint.position)
                : 1f;

            Vector2 gravDir = Application.isPlaying ? Physics2D.gravity.normalized : Vector2.down;

            // 扇形"12点钟"方向：如果有 WorldRoot，跟着它旋转（视觉一致）；
            // 没有则用真实重力反方向（世界 up）
            Vector3 upDir = worldRoot != null
                ? (Vector3)worldRoot.up
                : -(Vector3)gravDir;

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
            Vector3 armEnd = Application.isPlaying
                ? center + (Vector3)(Quaternion.AngleAxis(-_angle, Vector3.forward) * (Vector2)upDir) * arm
                : (gravityPoint != null ? gravityPoint.position : center + upDir * arm);
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
