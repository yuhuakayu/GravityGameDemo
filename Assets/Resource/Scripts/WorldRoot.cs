using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Resource.Scripts.Gyro;

namespace Resource.Scripts
{
    [DefaultExecutionOrder(-250)]
    [RequireComponent(typeof(Rigidbody2D))]
    public class WorldRotator : MonoBehaviour
    {
        [Header("调试")]
        public bool isDebugLog = false;
        [Tooltip("每 0.5 秒打出所有手柄名称和右摇杆 X 值，用于排查陀螺仪")]
        public bool isDebugScanGamepads = false;
        private float _scanTimer;

        [Header("旋转设置")]
        [Tooltip("旋转速度（度/秒），键盘 Q/E 和手柄右摇杆都用这个值")]
        public float rotateSpeed = 90f;

        [Header("物理旋转限制")]
        [Min(0f), Tooltip("实际世界旋转的最大角速度（度/秒）")]
        public float maxAngularSpeed = 120f;
        [Min(0.01f), Tooltip("每个真实物理步允许的最大转角；剩余目标在后续物理步消费")]
        public float maxStepAngle = 3f;
        [Range(0.05f, 0.49f), Tooltip("最远几何点每步位移不超过玩家最小碰撞厚度的此比例；存在独立摆板时根运动只使用该预算的 70%，为摆板自身运动预留 30%。接触附近还受穿透修复预算限制。")]
        public float maxPointTravelFraction = 0.45f;
        [Tooltip("开：按几何远点位移限速并预测夹缝（防穿墙更严格，但远处有墙时旋转会很慢）。关（默认）：恢复原来的旋转手感，只保留每步最大转角限制，防穿墙交给碰撞层修正和 CrushGuard。")]
        public bool useGeometrySafetyClamp = false;
        [Min(0.02f), Tooltip("速度模式最多保留多少秒的待执行输入，避免长期积压后松手仍持续旋转；角度模式始终使用最新目标")]
        public float maxInputLagSeconds = 0.25f;
        [Tooltip("跟随世界根节点的独立几何刚体（例如带 CompositeCollider2D 的 Tilemap）。保存为 Kinematic 后也需保留此引用；旧场景中的 Static 后代会自动识别。不要加入独立运动的柱子或玩家。")]
        public Rigidbody2D[] attachedGeometryBodies = new Rigidbody2D[0];
        private Rigidbody2D _body;
        private float _lastBodyAngle;
        private float _targetClockwiseAngle;
        private bool _incrementalTarget;
        private double _lastIncrementalInputTime;
        private bool _angleInitialized;
        private int _inputRevision = -1;
        private readonly List<AttachedGeometryBody> _attachedBodies = new List<AttachedGeometryBody>();
        private Collider2D[] _movingGeometry;
        private bool _hasIndependentPendulumGeometry;
        public Vector2 NextPosition { get; private set; }
        public float NextAngle { get; private set; }
        public bool HasPendingPhysicsPose { get; private set; }
        public Vector2 NextUp => Quaternion.Euler(0f, 0f, NextAngle) * Vector2.up;
        public float LastAppliedStepAngle { get; private set; }

        private struct AttachedGeometryBody
        {
            public Rigidbody2D Body;
            // World-unit offset with only the root's initial rotation removed; retains scale.
            public Vector2 Offset;
            public float RelativeAngle;
        }

        // Retained for old serialized scenes; active source selection is stored in GyroSettings.
        [HideInInspector] public bool useGyroIfAvailable = false;
        private WorldRotationInput _rotationInput;

        [Header("物理右摇杆平滑")]
        [Tooltip("摇杆信号死区，低于这个值视为 0")]
        public float stickDeadzone = 0.05f;
        [Tooltip("摇杆信号平滑强度：越大跟手但越容易被晃动带出小尖峰，越小越稳但转动手感会有延迟")]
        [Range(1f, 30f)]
        public float stickSmoothing = 12f;
        [Tooltip("摇杆信号要朝同一个方向持续这么多秒，才会被当作真实转动输入；晃动通常是短促的、方向来回跳的，达不到这个时长会被过滤掉")]
        public float stickSustainTime = 0.08f;

        [Header("旋转中心")]
        [Tooltip("留空则自动找场景里的 PlayerController 当旋转中心；手动拖一个 Transform 可以覆盖")]
        public Transform pivot;
        [Tooltip("自动模式下，玩家在空中时旋转中心用几帧之前的玩家坐标（而不是当前帧），落地后改用当前坐标")]
        public int airborneDelayFrames = 10;

        private PlayerController _autoPlayer;
        private readonly Queue<Vector3> _positionHistory = new Queue<Vector3>();
        private int _pivotFrame = -1;
        private Vector3? _framePivot;

        [Header("方向盘范围")]
        [Tooltip("不勾选 = 无限旋转，不做范围限制")]
        public bool limitSteeringRange = false;
        [Tooltip("方向盘总转角（720 = 左右各 360°），仅在 limitSteeringRange 打开时生效")]
        public float steeringRange = 720f;

        [Header("── 实时监控 ──")]
        [Range(-360f, 360f)]
        [SerializeField] private float _steeringAngle = 0f;
        /// <summary>当前方向盘角度（只读，调试面板用）</summary>
        public float SteeringAngleReadout { get { SynchronizeActualAngle(); return _steeringAngle; } }
        /// <summary>Clockwise-positive convention used by the gyro; Unity Z uses the opposite sign.</summary>
        public float ClockwiseAngleReadout => -SteeringAngleReadout;
        public float TargetClockwiseAngleReadout => _targetClockwiseAngle;
        public WorldRotationInput RotationInput => _rotationInput;
        private double _lastRotationTime = double.NegativeInfinity;
        private float _lastRotateSpeed;
        private float _pendingAudioDegrees;
        private SfxManager _rotationAudio;

        /// <summary>实际旋转与本步旋转请求；自动移动可在玩家侧按此信号暂停。</summary>
        public bool IsRotating { get; private set; }

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            if (_body == null) _body = gameObject.AddComponent<Rigidbody2D>();
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.useFullKinematicContacts = true;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _body.gravityScale = 0f;
            InitializeAngle();
            CacheAttachedGeometryBodies();
            RefreshMotionGeometry();
            _rotationInput = GetComponent<WorldRotationInput>();
            if (_rotationInput == null) _rotationInput = gameObject.AddComponent<WorldRotationInput>();
        }

        private void OnEnable()
        {
            DiscardPendingRotation();
            if (_rotationInput != null) _rotationInput.ResetContinuity();
        }
        private void OnDisable()
        {
            IsRotating = false;
            _lastRotationTime = double.NegativeInfinity;
            _lastRotateSpeed = 0f;
            if (_rotationAudio != null) _rotationAudio.StopWorldRotation();
            DiscardPendingRotation();
            if (_rotationInput != null) _rotationInput.ResetContinuity();
        }

        private void InitializeAngle()
        {
            if (_body == null || _angleInitialized) return;
            _lastBodyAngle = _body.rotation;
            _steeringAngle = Mathf.DeltaAngle(0f, _lastBodyAngle);
            _targetClockwiseAngle = -_steeringAngle;
            _angleInitialized = true;
        }

        void Update()
        {
            SynchronizeActualAngle();
            WorldRotationCommand command = _rotationInput.Evaluate(this);
            if (_inputRevision != _rotationInput.ContinuityRevision)
            {
                DiscardPendingRotation();
                _inputRevision = _rotationInput.ContinuityRevision;
            }
            if (command.Blocked) DiscardPendingRotation();
            else
            {
                if (command.HasTarget) SetTargetAngle(command.TargetAngle);
                else QueueClockwiseDelta(command.ClockwiseDelta);
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (command.Blocked)
            {
                _lastRotationTime = double.NegativeInfinity;
                _lastRotateSpeed = 0f;
            }
            // Keep audio and parallax rotation state across sensor gaps, without freezing gameplay
            // or adding any transform movement or extra gyro integration time.
            IsRotating = !command.Blocked && now - _lastRotationTime <= GyroProcessor.HoldSeconds;

            if (command.Blocked)
            {
                if (_rotationAudio != null) _rotationAudio.StopWorldRotation();
            }
            else
            {
                if (_rotationAudio == null) _rotationAudio = SfxManager.Instance;
                _rotationAudio.UpdateWorldRotation(this, _pendingAudioDegrees, IsRotating ? _lastRotateSpeed : 0f);
            }
            _pendingAudioDegrees = 0f;

            // Keep the airborne pivot history advancing on frames without a sensor callback.
            ResolveFramePivot();

            if (isDebugLog)
                Debug.Log($"[WorldRotator] clockwise={ClockwiseAngleReadout:F1}° target={_targetClockwiseAngle:F1}° source={_rotationInput.SourceLabel}");

            // 手柄扫描
            if (isDebugScanGamepads)
            {
                _scanTimer += Time.deltaTime;
                if (_scanTimer >= 0.5f)
                {
                    _scanTimer = 0f;
                    var sb = new System.Text.StringBuilder("[GamepadScan] ");
                    if (Gamepad.all.Count == 0) sb.Append("无手柄");
                    foreach (var gp in Gamepad.all)
                        sb.Append($"[{gp.name}] R.x={gp.rightStick.x.ReadValue():+0.000;-0.000}  ");
                    Debug.Log(sb.ToString());
                }
            }
        }

        private void FixedUpdate() => StepPhysics(Time.fixedDeltaTime);

        public void StepPhysics(float dt)
        {
            HasPendingPhysicsPose = false;
            LastAppliedStepAngle = 0f;
            SynchronizeActualAngle();
            // Recheck live gameplay gates: Update may not run between consecutive physics steps.
            if (dt <= 0f || _rotationInput == null || _rotationInput.IsGameplayBlocked(this))
            {
                DiscardPendingRotation();
                return;
            }

            // Geometry safety may lower the usable angular speed. Bound stale rate input by
            // elapsed time as well as degrees so releasing the control cannot leave a long tail.
            if (_incrementalTarget && Time.realtimeSinceStartupAsDouble - _lastIncrementalInputTime >
                Mathf.Max(0.02f, maxInputLagSeconds))
            {
                _targetClockwiseAngle = ClockwiseAngleReadout;
                _incrementalTarget = false;
            }

            float stepLimit = Mathf.Min(Mathf.Max(0f, maxAngularSpeed) * dt,
                Mathf.Max(0.01f, maxStepAngle));
            float clockwiseStep = Mathf.Clamp(_targetClockwiseAngle - ClockwiseAngleReadout,
                -stepLimit, stepLimit);
            float unityStep = -clockwiseStep;
            Vector2 nextPosition = _body.position;
            Vector3? pivotPosition = ResolveFramePivot();
            Vector2 center = pivotPosition.HasValue ? (Vector2)pivotPosition.Value : _body.position;
            if (_autoPlayer == null)
                foreach (var candidate in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                    if (candidate.gameObject.scene == gameObject.scene) { _autoPlayer = candidate; break; }
            // Root transport must not consume the entire shared travel budget every step:
            // an independently simulated board needs room to respond to gravity afterwards.
            float rootTravelFraction = maxPointTravelFraction * (_hasIndependentPendulumGeometry ? .7f : 1f);
            if (useGeometrySafetyClamp)
                unityStep = KinematicMotionSafety.ClampRotationStep(_movingGeometry, center, unityStep,
                    _autoPlayer, rootTravelFraction);
            if (pivotPosition.HasValue)
            {
                nextPosition = center + (Vector2)(Quaternion.Euler(0f, 0f, unityStep)
                    * (Vector3)(_body.position - center));
            }

            // Unity only uses the final MoveRotation request in one physics step. A loop here
            // would NOT create collision substeps. Limit each real FixedUpdate and retain the
            // remaining target for subsequent simulations, without Physics2D.Simulate/global edits.
            _body.MovePosition(nextPosition);
            float nextAngle = _body.rotation + unityStep;
            _body.MoveRotation(nextAngle);
            NextPosition = nextPosition;
            NextAngle = nextAngle;
            HasPendingPhysicsPose = true;
            LastAppliedStepAngle = unityStep;
            if (Mathf.Abs(unityStep) > 0.000001f)
            {
                IsRotating = true;
                _lastRotationTime = Time.realtimeSinceStartupAsDouble;
            }
            else IsRotating = Time.realtimeSinceStartupAsDouble - _lastRotationTime <= GyroProcessor.HoldSeconds;
            Quaternion nextOrientation = Quaternion.Euler(0f, 0f, nextAngle);
            foreach (AttachedGeometryBody geometry in _attachedBodies)
            {
                if (geometry.Body == null || !geometry.Body.gameObject.activeInHierarchy) continue;
                // A Composite requires its own body. Drive its absolute target explicitly:
                // applying a delta to its already-parented pose could apply the root twice.
                geometry.Body.MovePosition(nextPosition + (Vector2)(nextOrientation * (Vector3)geometry.Offset));
                geometry.Body.MoveRotation(nextAngle + geometry.RelativeAngle);
            }
        }

        /// <summary>Queue the latest clockwise target; physics applies it over bounded real steps.</summary>
        public void SetTargetAngle(float clockwiseAngle)
        {
            if (float.IsNaN(clockwiseAngle) || float.IsInfinity(clockwiseAngle)) return;
            _incrementalTarget = false;
            _targetClockwiseAngle = ClampSteeringAngle(clockwiseAngle);
        }

        /// <summary>Discard pre-pause/recenter input so resume never catches up old movement.</summary>
        public void DiscardPendingRotation()
        {
            HasPendingPhysicsPose = false;
            LastAppliedStepAngle = 0f;
            _incrementalTarget = false;
            SynchronizeActualAngle();
            _pendingAudioDegrees = 0f;
            _lastRotateSpeed = 0f;
            _targetClockwiseAngle = -_steeringAngle;
            if (_body == null) return;
            NextPosition = _body.position;
            NextAngle = _body.rotation;
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
            foreach (AttachedGeometryBody geometry in _attachedBodies)
            {
                if (geometry.Body == null) continue;
                geometry.Body.linearVelocity = Vector2.zero;
                geometry.Body.angularVelocity = 0f;
            }
        }

        /// <summary>Map a visual hierarchy point directly to the pending physical root pose.
        /// Removing the render transform first avoids Rigidbody interpolation introducing a lag.</summary>
        public Vector2 GetPointAtNextPose(Transform point)
        {
            Vector2 offset = Quaternion.Inverse(transform.rotation) * (point.position - transform.position);
            Vector2 position = HasPendingPhysicsPose ? NextPosition : _body.position;
            float angle = HasPendingPhysicsPose ? NextAngle : _body.rotation;
            return position + (Vector2)(Quaternion.Euler(0f, 0f, angle) * (Vector3)offset);
        }

        public void RefreshMotionGeometry()
        {
            var geometry = new List<Collider2D>(GetComponentsInChildren<Collider2D>(true));
            _hasIndependentPendulumGeometry = false;
            // These boards have independent bodies outside the root hierarchy, but their hinges
            // and reference axes follow it. Include them in the root's transport travel budget.
            foreach (var pendulum in FindObjectsByType<PivotPendulum>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (pendulum.worldRoot != null && (pendulum.worldRoot == transform || pendulum.worldRoot.IsChildOf(transform)))
                    foreach (var collider in pendulum.GetComponentsInChildren<Collider2D>(true))
                    {
                        if (!geometry.Contains(collider)) geometry.Add(collider);
                        if (collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy &&
                            collider.attachedRigidbody != _body)
                            _hasIndependentPendulumGeometry = true;
                    }
            _movingGeometry = geometry.ToArray();
        }

        private void CacheAttachedGeometryBodies()
        {
            _attachedBodies.Clear();
            var configuredBodies = new HashSet<Rigidbody2D>();
            if (attachedGeometryBodies != null)
                foreach (Rigidbody2D body in attachedGeometryBodies)
                    if (body != null) configuredBodies.Add(body);

            foreach (Rigidbody2D body in GetComponentsInChildren<Rigidbody2D>(true))
            {
                if (body == _body || (!configuredBodies.Contains(body) && body.bodyType != RigidbodyType2D.Static)) continue;
                // Leave independent movers and all geometry below them under their own control.
                bool independentParent = false;
                for (Transform parent = body.transform.parent; parent != null && parent != transform; parent = parent.parent)
                    if (parent.GetComponent<Rigidbody2D>() != null) { independentParent = true; break; }
                if (independentParent || body.GetComponent<PlayerController>() != null ||
                    body.GetComponent<PivotPendulum>() != null || body.GetComponent<Rotator>() != null) continue;

                _attachedBodies.Add(new AttachedGeometryBody
                {
                    Body = body,
                    Offset = Quaternion.Euler(0f, 0f, -_body.rotation) * (Vector3)(body.position - _body.position),
                    RelativeAngle = Mathf.DeltaAngle(_body.rotation, body.rotation)
                });
                body.bodyType = RigidbodyType2D.Kinematic;
                body.useFullKinematicContacts = true;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.gravityScale = 0f;
            }
        }

        /// <summary>Call at the beginning and end of a solar pulse; resuming angle mode rebases the wheel.</summary>
        public void SetSolarPulseActive(bool active)
        {
            if (_rotationInput == null) _rotationInput = GetComponent<WorldRotationInput>();
            if (_rotationInput != null) _rotationInput.SetSolarPulseActive(active);
            if (active)
            {
                DiscardPendingRotation();
                IsRotating = false;
                _lastRotationTime = double.NegativeInfinity;
                _lastRotateSpeed = 0f;
                if (_rotationAudio != null) _rotationAudio.StopWorldRotation();
            }
        }

        private void QueueClockwiseDelta(float clockwiseDelta)
        {
            if (float.IsNaN(clockwiseDelta) || float.IsInfinity(clockwiseDelta)) return;
            if (Mathf.Abs(clockwiseDelta) < 0.000001f) return;
            _incrementalTarget = true;
            _lastIncrementalInputTime = Time.realtimeSinceStartupAsDouble;
            float current = ClockwiseAngleReadout;
            float effectiveSpeed = Mathf.Min(Mathf.Max(0f, maxAngularSpeed),
                Mathf.Max(0.01f, maxStepAngle) / Mathf.Max(0.0001f, Time.fixedDeltaTime));
            float maxBacklog = effectiveSpeed * Mathf.Max(0.02f, maxInputLagSeconds);
            _targetClockwiseAngle = ClampSteeringAngle(Mathf.Clamp(_targetClockwiseAngle + clockwiseDelta,
                current - maxBacklog, current + maxBacklog));
        }

        private float ClampSteeringAngle(float target)
        {
            if (limitSteeringRange)
            {
                float halfRange = Mathf.Max(0f, steeringRange) * 0.5f;
                target = Mathf.Clamp(target, -halfRange, halfRange);
            }
            return target;
        }

        private void SynchronizeActualAngle()
        {
            if (_body == null) return;
            InitializeAngle();
            float actualDelta = Mathf.DeltaAngle(_lastBodyAngle, _body.rotation);
            _lastBodyAngle = _body.rotation;
            _steeringAngle += actualDelta;
            if (Mathf.Abs(actualDelta) <= 0.00001f) return;
            _lastRotationTime = Time.realtimeSinceStartupAsDouble;
            _pendingAudioDegrees += Mathf.Abs(actualDelta);
            _lastRotateSpeed = Time.fixedDeltaTime > 0f
                ? Mathf.Abs(actualDelta) / Time.fixedDeltaTime : 0f;
        }

        private Vector3? ResolveFramePivot()
        {
            if (_pivotFrame == Time.frameCount) return _framePivot;
            _pivotFrame = Time.frameCount;
            _framePivot = pivot != null ? pivot.position : ResolveAutoPivotPosition();
            return _framePivot;
        }

        /// <summary>
        /// 自动旋转中心：玩家落地时用"当前帧"坐标；在空中时改用"N 帧之前"的坐标
        /// （历史队列，每帧入队一次，超过 airborneDelayFrames 就把最老的一个丢出去当结果）。
        /// </summary>
        private Vector3? ResolveAutoPivotPosition()
        {
            if (_autoPlayer == null)
                _autoPlayer = FindObjectOfType<PlayerController>();
            if (_autoPlayer == null)
                return null;

            Vector3 currentPos = _autoPlayer.transform.position;

            _positionHistory.Enqueue(currentPos);
            while (_positionHistory.Count > Mathf.Max(1, airborneDelayFrames))
                _positionHistory.Dequeue();

            if (_autoPlayer.IsGrounded)
                return currentPos;

            return _positionHistory.Peek(); // 队列里最老的一个，也就是"N 帧之前"的坐标
        }
    }
}
