using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Resource.Scripts
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [Header("调试")]
        public bool isDebugLog    = false;   // 控制台 Log
        public bool isDebugGizmos = false;   // Scene 画图

        [Header("移动设置")]
        public float maxMoveSpeed = 6f;
        public float jumpForce = 12f;
        [Tooltip("关闭后禁用键盘和手柄跳跃，保留正常移动与重力。")]
        public bool jumpEnabled = true;

        [Header("防止世界几何推动玩家")]
        public bool antiPushEnabled = true;
        public bool logVelocityDelta;
        [SerializeField] private Vector2 intendedVelocity;
        public Vector2 IntendedVelocity => intendedVelocity;
        public Vector2 LastPhysicsVelocityDelta { get; private set; }
        public Vector2 LastRejectedVelocityDelta { get; private set; }
        public Vector2 LastRejectedDisplacement { get; private set; }
        private readonly List<ContactPoint2D> _motionContacts = new List<ContactPoint2D>(16);
        private CrushGuard _crushGuard;
        private float _ownedGravityScale;
        private bool _motionInitialized;
        private bool _selfVelocityActive;
        private bool _jumpQueued;
        private bool _physicsStepPending;
        private Vector2 _stepStartPosition;
        private Vector2 _submittedVelocity;
        private Coroutine _afterPhysics;

        public float SimulatedGravityScale
        {
            get { InitializeMotion(); SynchronizeAntiPush(); return _selfVelocityActive ? _ownedGravityScale : rb.gravityScale; }
            set
            {
                InitializeMotion(); SynchronizeAntiPush();
                _ownedGravityScale = Mathf.Max(0f, value);
                rb.gravityScale = _selfVelocityActive ? 0f : _ownedGravityScale;
            }
        }

        [Header("地面检测")]
        public Transform groundCheck;
        public float groundCheckRadius = 0.2f;
        public LayerMask groundLayer;

        [Header("墙壁检测")]
        public Transform wallCheckLeft;
        public Transform wallCheckRight;
        public float wallCheckRadius = 0.2f;
        public LayerMask wallLayer;

        [Header("落地音效")]
        public float maxLandImpactSpeed = 15f;

        [Header("脚步声 / 扬尘")]
        [Tooltip("每移动这么多世界单位触发一次脚步声 + 扬尘")]
        public float footstepInterval = 1.4f;

        [Header("手柄震动（走路时）")]
        [Tooltip("标准双马达震动，不是 DualSense 扳机阻力那种——Unity 标准 Input System 拿不到扳机专属震动")]
        public bool rumbleEnabled = true;
        [Range(0f, 1f)] public float rumbleLowFreq = 0.15f;
        [Range(0f, 1f)] public float rumbleHighFreq = 0.05f;

        [Header("紧迫感玩法：自动移动 + 撞墙强制转向（默认关，不影响原本手动移动的关卡）")]
        [Tooltip("打开后：手柄/键盘的左右移动失效，玩家持续横向自动移动并正常受重力影响，" +
                 "方向只能靠撞到 WallRedirect 墙来改变——世界旋转变成玩家唯一能做的操作，" +
                 "转世界＝改变接下来会撞上哪面墙。撞到 HazardKill 物体直接死亡重开本关")]
        public bool autoMoveMode = false;
        [Tooltip("自动滑行的速度")]
        public float autoMoveSpeed = 6f;
        [Tooltip("自动移动模式下的初始移动方向（角度，0=右，90=上，180=左，270=下）")]
        public float autoMoveStartAngle = 0f;
        [Tooltip("世界旋转时暂停自动横向输入；重力与防穿透仍运行。")]
        public bool pauseAutoMoveWhileRotating = true;
        [Min(0f), Tooltip("IsRotating 连续为 false 达到此时长后恢复自动移动，过滤单帧抖动。")]
        public float autoMoveResumeDelay = 0.15f;
        [Tooltip("留空时在开始玩法时寻找当前场景的世界旋转器。")]
        public WorldRotator autoMoveRotationSource;
        public bool AutoMovePausedForRotation { get; private set; }
        public float AutoMoveVelocityContribution { get; private set; }
        private float _rotationQuietSeconds;
        private Vector2 _autoMoveDir = Vector2.right;
        private bool _isDead = false;
        private bool _gameplayStarted;
        public bool IsDead => _isDead;
        public bool IsGameplayActive => _gameplayStarted && isActiveAndEnabled && !_isDead && Time.timeScale > 0f;

        public void BeginGameplay()
        {
            InitializeMotion();
            if (autoMoveRotationSource == null)
                foreach (var world in FindObjectsByType<WorldRotator>(FindObjectsSortMode.None))
                    if (world.gameObject.scene == gameObject.scene) { autoMoveRotationSource = world; break; }
            intendedVelocity = Vector2.zero;
            _jumpQueued = false;
            AutoMovePausedForRotation = false;
            _rotationQuietSeconds = 0f;
            _gameplayStarted = true;
        }

        public void EnterPreview()
        {
            _gameplayStarted = false;
            intendedVelocity = Vector2.zero;
            _jumpQueued = false;
            _physicsStepPending = false;
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }

        [Header("死亡：红光闪烁")]
        [Tooltip("红光淡入淡出的总时长（秒）")]
        public float deathFlashDuration = 0.5f;
        [Tooltip("闪烁的发光颜色")]
        public Color deathFlashColor = Color.red;
        [Tooltip("闪烁最亮的时候有多亮，对应 AllIn1SpriteShader 的 _Glow（0~100）")]
        public float deathFlashPeakGlow = 40f;
        private Material _deathFlashMaterial;

        private Rigidbody2D rb;
        private bool isGrounded = false;
        public bool IsGrounded => isGrounded;
        private bool isTouchingWallLeft = false;
        private bool isTouchingWallRight = false;
        private float debugTimer = 0f;
        private SpriteRenderer _spriteRenderer;

        private float _footstepDistance;
        private ParticleSystem _dustTrail;

        private void Awake() { InitializeMotion(); }

        private void OnEnable()
        {
            InitializeMotion();
            _afterPhysics = StartCoroutine(AfterPhysics());
        }

        private void InitializeMotion()
        {
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (_crushGuard == null) _crushGuard = GetComponent<CrushGuard>();
            if (_crushGuard == null) _crushGuard = gameObject.AddComponent<CrushGuard>();
            if (_motionInitialized) return;
            _motionInitialized = true;
            _ownedGravityScale = rb.gravityScale;
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.constraints |= RigidbodyConstraints2D.FreezeRotation;
            SynchronizeAntiPush();
        }

        private void SynchronizeAntiPush()
        {
            if (_selfVelocityActive == antiPushEnabled) return;
            if (antiPushEnabled)
            {
                _ownedGravityScale = rb.gravityScale;
                // Never import momentum left by the previous collision solver when enabling protection.
                intendedVelocity = Vector2.zero;
                rb.gravityScale = 0f;
                rb.linearVelocity = Vector2.zero;
            }
            else rb.gravityScale = _ownedGravityScale;
            _selfVelocityActive = antiPushEnabled;
            _physicsStepPending = false;
        }

        private IEnumerator AfterPhysics()
        {
            var wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait;
                CompletePhysicsStep(Time.fixedDeltaTime);
            }
        }

        void Start()
        {
            // 必须最先访问：SettingsManager.Awake() 会把 SfxManager.sfxEnabled 设成 true，
            // 下面 CandleSpawner/TorchLight2D 等会立刻调用 AttachTorchLoop，
            // 而 AttachTorchLoop 只在挂载那一刻判断一次总开关，晚了就再也不会响。
            _ = SettingsManager.Instance;

            rb = GetComponent<Rigidbody2D>();
            // 在子级 PlayerIM 上查找 SpriteRenderer
            Transform playerIM = transform.Find("PlayerIM");
            if (playerIM != null)
                _spriteRenderer = playerIM.GetComponent<SpriteRenderer>();
            if (_spriteRenderer == null)
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            if (autoMoveMode)
            {
                float rad = autoMoveStartAngle * Mathf.Deg2Rad;
                _autoMoveDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
                // 重力保持组件上配置的值（跟 Stage1 的玩家一致，默认 1），不再清零——
                // HandleAutoMove() 只覆盖滑行方向那根轴，另一根轴留给重力正常影响。
            }

            BuildDustTrail();

            if (FindObjectOfType<LevelAtmosphere>() == null)
                new GameObject("LevelAtmosphere (Auto)").AddComponent<LevelAtmosphere>();

            if (FindObjectOfType<GameHUD>() == null)
                new GameObject("GameHUD (Auto)").AddComponent<GameHUD>();

            if (FindObjectOfType<DebugTuningUI>() == null)
                new GameObject("DebugTuningUI (Auto)").AddComponent<DebugTuningUI>();

            if (FindObjectOfType<LevelIntroUI>() == null)
                new GameObject("LevelIntroUI (Auto)").AddComponent<LevelIntroUI>();

            if (FindObjectOfType<CandleSpawner>() == null)
                new GameObject("CandleSpawner (Auto)").AddComponent<CandleSpawner>();

            BuildPlayerLight();
        }

        void BuildPlayerLight()
        {
            if (transform.Find("PlayerLight2D (Auto)") != null) return; // 场景里已经摆好了

            var lightGO = new GameObject("PlayerLight2D (Auto)");
            lightGO.transform.SetParent(transform, false);
            lightGO.transform.localPosition = Vector3.zero;

            var light = lightGO.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = new Color(1f, 0.92f, 0.75f);
            light.intensity = 1.1f;
            light.pointLightOuterRadius = 6f;
            light.pointLightInnerRadius = 1.5f;
            light.falloffIntensity = 0.5f;
        }

        void FixedUpdate()
        {
            BeginPhysicsStep(Time.fixedDeltaTime);
        }

        public void BeginPhysicsStep(float deltaTime)
        {
            InitializeMotion();
            SynchronizeAntiPush();
            if (!IsGameplayActive || rb.bodyType != RigidbodyType2D.Dynamic || !rb.simulated || deltaTime <= 0f)
            {
                _physicsStepPending = false;
                return;
            }
            CheckGround();
            CheckWalls();
            UpdateAutoMoveRotationPause(deltaTime);
            AutoMoveVelocityContribution = 0f;
            _stepStartPosition = rb.position;
            if (_selfVelocityActive)
            {
                BuildIntendedVelocity(deltaTime);
                intendedVelocity = ProjectAgainstContacts(intendedVelocity);
                rb.linearVelocity = intendedVelocity;
            }
            else
                HandleMovement();
            _submittedVelocity = _selfVelocityActive ? intendedVelocity : rb.linearVelocity;
            if (_selfVelocityActive) _crushGuard.CaptureBeforePhysics(deltaTime);
            _physicsStepPending = true;
            if (autoMoveMode) CheckAutoMoveFootContact();
        }

        private void BuildIntendedVelocity(float dt)
        {
            float input = autoMoveMode ? _autoMoveDir.x : ReadMoveInput();
            if (autoMoveMode && AutoMovePausedForRotation) input = 0f;
            if ((_autoMoveDir.x > 0f && isTouchingWallRight || _autoMoveDir.x < 0f && isTouchingWallLeft) && autoMoveMode)
                input = 0f;
            if (!autoMoveMode && ((input < 0f && isTouchingWallLeft) || (input > 0f && isTouchingWallRight))) input = 0f;
            intendedVelocity.x = input * (autoMoveMode ? autoMoveSpeed : maxMoveSpeed);
            if (autoMoveMode) AutoMoveVelocityContribution = intendedVelocity.x;
            if (jumpEnabled && _jumpQueued && isGrounded && !autoMoveMode) intendedVelocity.y = jumpForce;
            _jumpQueued = false;
            intendedVelocity += Physics2D.gravity * (_ownedGravityScale * dt);
            UpdateFootsteps(input);
            UpdateRumble(input);
        }

        private void UpdateAutoMoveRotationPause(float dt)
        {
            if (!autoMoveMode || !pauseAutoMoveWhileRotating || autoMoveRotationSource == null)
            {
                AutoMovePausedForRotation = false;
                _rotationQuietSeconds = 0f;
                return;
            }
            if (autoMoveRotationSource.IsRotating)
            {
                AutoMovePausedForRotation = true;
                _rotationQuietSeconds = 0f;
            }
            else if (AutoMovePausedForRotation)
            {
                _rotationQuietSeconds += dt;
                if (_rotationQuietSeconds + 0.000001f >= Mathf.Max(0f, autoMoveResumeDelay))
                    AutoMovePausedForRotation = false;
            }
        }

        public float ReadMoveInput()
        {
            if (Gyro.GyroRuntime.ConsoleCapturesInput) return 0f;
            float input = 0f;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input = -1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input = 1f;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                float left = pad.leftTrigger.ReadValue(), right = pad.rightTrigger.ReadValue();
                if (left < 0.05f) left = 0f;
                if (right < 0.05f) right = 0f;
                if (left > 0f || right > 0f) input = right - left;
            }
            return input;
        }

        private Vector2 ProjectAgainstContacts(Vector2 velocity)
        {
            _motionContacts.Clear();
            var filter = new ContactFilter2D();
            int mask = _crushGuard != null ? _crushGuard.worldGeometryMask.value : (groundLayer.value | wallLayer.value);
            filter.SetLayerMask(mask != 0 ? mask : Physics2D.GetLayerCollisionMask(gameObject.layer));
            filter.useTriggers = false;
            rb.GetContacts(filter, _motionContacts);
            // Project only our own velocity; never add a surface's velocity or friction impulse.
            for (int pass = 0; pass < 3; pass++)
                foreach (var contact in _motionContacts)
                {
                    Vector2 normal = contact.normal;
                    float intoSurface = Vector2.Dot(velocity, normal);
                    if (intoSurface < 0f) velocity -= normal * intoSurface;
                }
            return velocity;
        }

        public void SetIntendedVelocity(Vector2 velocity)
        {
            InitializeMotion();
            intendedVelocity = velocity;
            rb.linearVelocity = velocity;
        }

        public void CompletePhysicsStep(float deltaTime)
        {
            if (!_physicsStepPending) return;
            _physicsStepPending = false;
            if (!IsGameplayActive || rb.bodyType != RigidbodyType2D.Dynamic) return;
            Vector2 rawVelocity = rb.linearVelocity;
            LastPhysicsVelocityDelta = rawVelocity - _submittedVelocity;
            LastRejectedVelocityDelta = Vector2.zero;
            LastRejectedDisplacement = Vector2.zero;
            if (antiPushEnabled)
            {
                intendedVelocity = ProjectAgainstContacts(intendedVelocity);
                LastRejectedVelocityDelta = rawVelocity - intendedVelocity;
                rb.linearVelocity = intendedVelocity;

                // Box2D may already have displaced us through a moving surface's impulse.
                // Retain only the progress requested on each axis. Use the velocity submitted
                // before simulation: projecting the whole step against a newly hit floor/wall
                // would erase legitimate travel before impact and cause hovering at landings.
                // A surface cannot reverse an axis, move an idle axis, or exceed our own step;
                // any necessary separation is handled by CrushGuard's shared position budget.
                Vector2 ownStep = _submittedVelocity * deltaTime;
                Vector2 actualStep = rb.position - _stepStartPosition;
                Vector2 allowedStep = new Vector2(
                    Mathf.Clamp(actualStep.x, Mathf.Min(0f, ownStep.x), Mathf.Max(0f, ownStep.x)),
                    Mathf.Clamp(actualStep.y, Mathf.Min(0f, ownStep.y), Mathf.Max(0f, ownStep.y)));
                Vector2 controlledPosition = _stepStartPosition + allowedStep;
                LastRejectedDisplacement = rb.position - controlledPosition;
                if (LastRejectedDisplacement.sqrMagnitude > 0.000000000001f)
                    rb.position = controlledPosition;
            }
            _crushGuard.ResolveAfterPhysics(deltaTime);
            if (logVelocityDelta)
                Debug.Log($"[AntiPush] enabled={antiPushEnabled} intended={_submittedVelocity:F4} raw={rawVelocity:F4} " +
                    $"solverDelta={LastPhysicsVelocityDelta:F4} rejected={LastRejectedVelocityDelta:F4} final={rb.linearVelocity:F4} " +
                    $"gravityStep={(Physics2D.gravity * (_ownedGravityScale * deltaTime)):F4} correction={_crushGuard.LastCorrection:F4}", this);
        }

        void Update()
        {
            HandleJump();

            if (isDebugLog)
            {
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
            bool wasTouchingLeft  = isTouchingWallLeft;
            bool wasTouchingRight = isTouchingWallRight;

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

            if ((isTouchingWallLeft && !wasTouchingLeft) ||
                (isTouchingWallRight && !wasTouchingRight))
                SfxManager.Instance.PlayWallBump();
        }

        void HandleMovement()
        {
            if (_isDead) return; // 死亡后立刻锁输入，手动移动模式之前没有这个判断，自动移动模式里 HandleAutoMove() 自己也有一份

            if (autoMoveMode)
            {
                HandleAutoMove();
                return;
            }

            float moveInput = 0f;

            if (Gyro.GyroRuntime.ConsoleCapturesInput)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                return;
            }

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
                if (isDebugLog) Debug.Log("左边碰墙！禁止向左移动");
                moveInput = 0f;
            }

            // 右边碰墙 → 禁止向右移动
            if (isTouchingWallRight && moveInput > 0)
            {
                if (isDebugLog) Debug.Log("右边碰墙！禁止向右移动");
                moveInput = 0f;
            }

            rb.linearVelocity = new Vector2(
                moveInput * maxMoveSpeed,
                rb.linearVelocity.y
            );

            UpdateFootsteps(moveInput);
            UpdateRumble(moveInput);
        }

        /// <summary>
        /// 自动滑行：Y 轴永远只交给重力，绝不会出现强行往上飘的效果——玩家默认就是
        /// 一直在往下掉。真正被 _autoMoveDir 控制的只有横向（X）：跟 Stage1 手动移动
        /// 同一套三个判定点（groundCheck / wallCheckLeft / wallCheckRight），只有撞到
        /// "前进方向那一侧"的墙才会暂停横向移动——往右走时只看右边检测器，左边碰墙
        /// 不管；往左走时只看左边检测器，右边碰墙不管（同方向才挡，不同方向不挡）。
        /// 一旦挡住那一侧的检测器不再碰墙，横向移动自动恢复。
        /// 方向只能靠撞 WallRedirect 墙来改变。
        ///
        /// 横向自动移动由旋转暂停开关控制，重力/掉落照常计算。
        /// </summary>
        void HandleAutoMove()
        {
            if (_isDead) return;

            bool wallBlocked = (_autoMoveDir.x > 0f && isTouchingWallRight) ||
                                (_autoMoveDir.x < 0f && isTouchingWallLeft);
            float targetX = wallBlocked || AutoMovePausedForRotation ? 0f : _autoMoveDir.x * autoMoveSpeed;
            AutoMoveVelocityContribution = targetX;
            rb.linearVelocity = new Vector2(targetX, rb.linearVelocity.y);

            UpdateFootsteps(targetX);
        }

        void HandleJump()
        {
            if (!jumpEnabled) return;
            if (Gyro.GyroRuntime.ConsoleCapturesInput) return;
            if (_isDead) return; // 死亡后立刻锁输入
            if (autoMoveMode) return; // 自动移动模式没有跳跃，方向完全靠撞墙决定

            bool jumpPressed = false;

            if (Keyboard.current != null &&
                Keyboard.current.spaceKey.wasPressedThisFrame)
                jumpPressed = true;

            var gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)
                jumpPressed = true;

            if (jumpPressed)
            {
                if (isDebugLog) Debug.Log($"跳跃尝试 | isGrounded:{isGrounded}");
                if (isGrounded)
                {
                    if (antiPushEnabled) _jumpQueued = true;
                    else rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                    if (isDebugLog) Debug.Log("跳跃成功！");

                    SfxManager.Instance.PlayJump();
                    EmitDust(2);
                }
            }
        }

        void OnCollisionEnter2D(Collision2D col)
        {
            // 自动移动模式下，撞墙转向/触雷死亡改成只认脚底（见 CheckAutoMoveFootContact），
            // 身体其它部位撞上不算，这里直接跳过。
            if (autoMoveMode) return;

            bool wasGrounded = isGrounded;

            foreach (ContactPoint2D contact in col.contacts)
                if (contact.normal.y > 0.5f)
                    isGrounded = true;

            if (isGrounded && !wasGrounded)
            {
                float impact01 = Mathf.Clamp01(Mathf.Abs(col.relativeVelocity.y) / maxLandImpactSpeed);
                SfxManager.Instance.PlayLand(impact01);
                EmitDust(3);
            }
        }

        void OnCollisionExit2D(Collision2D col)
        {
            if (autoMoveMode) return;
            isGrounded = false;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            // 自动移动模式下这里不处理，统一走 CheckAutoMoveFootContact 的脚底检测——
            // 那套是刻意做成"只认脚底"的，这里如果也响应会变成身体侧面碰一下就死，跟设计冲突。
            if (autoMoveMode) return;
            if (_isDead) return;

            if (other.GetComponent<HazardKill>() != null)
                Die();
        }

        private bool _footTouchingSpecial = false;

        /// <summary>
        /// 自动移动模式下，只有脚底（groundCheck 那个检测点，跟手动模式判断"有没有踩到地面"
        /// 用的是同一个点）碰到 WallRedirect/HazardKill 才会触发效果——身体撞到侧面或头顶
        /// 不算，必须是脚踩上去。跟 CheckGround()/CheckWalls() 一样用 OverlapCircle 轮询，
        /// 而不是用碰撞回调，这样不用额外挂子物体碰撞体。
        /// </summary>
        void CheckAutoMoveFootContact()
        {
            if (groundCheck == null) return;

            // OverlapCircle（单结果版）会先命中玩家自己的碰撞体——脚底检测点本来就在
            // 玩家自身碰撞体范围内，永远轮不到真正的墙，所以这里用 All 版本再手动排除自己。
            var hits = Physics2D.OverlapCircleAll(groundCheck.position, groundCheckRadius);
            Collider2D found = null;
            foreach (var h in hits)
            {
                if (h.attachedRigidbody == rb) continue;
                if (h.GetComponent<WallRedirect>() != null || h.GetComponent<HazardKill>() != null)
                {
                    found = h;
                    break;
                }
            }

            bool touchingNow = found != null;
            if (touchingNow && !_footTouchingSpecial)
                HandleAutoMoveCollision(found.gameObject);

            _footTouchingSpecial = touchingNow;
        }

        /// <summary>自动移动模式下脚底碰到东西：WallRedirect 改变方向，HazardKill 直接死亡重开</summary>
        void HandleAutoMoveCollision(GameObject other)
        {
            if (_isDead) return;

            var hazard = other.GetComponent<HazardKill>();
            if (hazard != null)
            {
                Die();
                return;
            }

            var redirect = other.GetComponent<WallRedirect>();
            if (redirect != null)
            {
                _autoMoveDir = redirect.RedirectDirection;
                SfxManager.Instance.PlayWallBump();
            }
        }

        /// <summary>
        /// 统一的死亡入口：锁输入+冻结物理 → 播放死亡音效 → 红光闪烁淡入淡出 → 走现成的场景转场重开本关。
        /// 以后别的死亡原因（比如掉出边界）也直接调这个方法，不用另外写一套流程。
        /// </summary>
        public void Die()
        {
            if (_isDead) return;
            _isDead = true;
            intendedVelocity = Vector2.zero;
            _jumpQueued = false;
            _physicsStepPending = false;
            if (rb == null) rb = GetComponent<Rigidbody2D>(); // 极端情况下 Start() 还没跑到就被外部触发（比如浏览模式切游戏那一帧），做个兜底
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic; // 光锁输入不够，重力还在算，会让玩家在闪光的时候继续往下掉，干脆把物理也冻住（反正马上要重开关卡，不用管恢复）
            SfxManager.Instance.PlayPlayerDeath();
            StartCoroutine(DeathSequence());
        }

        /// <summary>红光淡入淡出，结束后交给 SceneTransition 重开本关——本关的 LevelIntroUI 会在场景重载后
        /// 自己重新冻结玩法、弹出浏览界面，摄像机大小也会跟着场景重载恢复默认值，这里不用额外处理。</summary>
        private IEnumerator DeathSequence()
        {
            if (_spriteRenderer != null)
                _spriteRenderer.material = GetOrCreateDeathFlashMaterial();

            float t = 0f;
            while (t < deathFlashDuration)
            {
                t += Time.deltaTime;
                float glow = Mathf.Sin(Mathf.Clamp01(t / deathFlashDuration) * Mathf.PI) * deathFlashPeakGlow;
                if (_deathFlashMaterial != null) _deathFlashMaterial.SetFloat("_Glow", glow);
                yield return null;
            }

            SceneTransition.Instance.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>懒加载死亡红光材质。只在死亡那一刻才赋给 SpriteRenderer——这个 shader 没有
        /// URP 2D 需要的 Universal2D 光照通道，如果一直挂着，玩家身上就会从此收不到 Light2D
        /// （火把光、玩家自己的点光源、场景整体氛围光）的照明，是个很容易被忽略的视觉倒退。
        /// 死亡后马上就要重开场景了，不需要再换回原来的材质。</summary>
        private Material GetOrCreateDeathFlashMaterial()
        {
            if (_deathFlashMaterial != null) return _deathFlashMaterial;

            var shader = Shader.Find("AllIn1SpriteShader/AllIn1SpriteShader");
            _deathFlashMaterial = new Material(shader);
            _deathFlashMaterial.EnableKeyword("GLOW_ON");
            _deathFlashMaterial.SetColor("_GlowColor", deathFlashColor);
            _deathFlashMaterial.SetFloat("_Glow", 0f);
            return _deathFlashMaterial;
        }

        // ── 跑步手感 / 脚步声 / 扬尘（项目里没有现成的沙尘美术资源，用运行时生成的 ParticleSystem）──
        void BuildDustTrail()
        {
            var existing = transform.Find("DustTrail (Auto)");
            if (existing != null)
            {
                _dustTrail = existing.GetComponent<ParticleSystem>();
                return;
            }

            var dustGO = new GameObject("DustTrail (Auto)");
            dustGO.transform.SetParent(transform, false);
            dustGO.transform.localPosition = groundCheck != null
                ? transform.InverseTransformPoint(groundCheck.position)
                : Vector3.zero;

            _dustTrail = dustGO.AddComponent<ParticleSystem>();
            var main = _dustTrail.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
            main.startColor = new Color(0.75f, 0.72f, 0.68f, 0.4f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = _dustTrail.emission;
            emission.rateOverTime = 0f;

            var shape = _dustTrail.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.05f;

            var colorOverLifetime = _dustTrail.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            colorOverLifetime.color = grad;

            var psRenderer = _dustTrail.GetComponent<ParticleSystemRenderer>();
            // 之前没手动给材质，URP 下 ParticleSystemRenderer 的默认材质会掉成粉紫色的
            // "shader 缺失" 占位色，所以扬尘看起来是紫的——这里显式指定一个软圆点纹理 + Sprites/Default。
            psRenderer.material = GetOrCreateDustMaterial();
            if (_spriteRenderer != null)
            {
                psRenderer.sortingLayerID = _spriteRenderer.sortingLayerID;
                psRenderer.sortingOrder   = _spriteRenderer.sortingOrder - 1;
            }
        }

        private static Material _dustMaterial;

        private static Material GetOrCreateDustMaterial()
        {
            if (_dustMaterial != null) return _dustMaterial;
            _dustMaterial = new Material(Shader.Find("Sprites/Default"));
            _dustMaterial.mainTexture = CreateSoftDotTexture(32);
            return _dustMaterial;
        }

        private static Texture2D CreateSoftDotTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float dist = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / r);
                    float alpha = (1f - dist) * (1f - dist);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        void EmitDust(int count)
        {
            if (_dustTrail == null) return;
            var emitParams = new ParticleSystem.EmitParams
            {
                position = groundCheck != null ? groundCheck.position : transform.position
            };
            _dustTrail.Emit(emitParams, count);
        }

        void UpdateFootsteps(float moveInput)
        {
            if (isGrounded && Mathf.Abs(moveInput) > 0.1f)
            {
                _footstepDistance += Mathf.Abs(rb.linearVelocity.x) * Time.fixedDeltaTime;
                if (_footstepDistance >= footstepInterval)
                {
                    _footstepDistance = 0f;
                    SfxManager.Instance.PlayFootstep(Mathf.Abs(moveInput));
                    EmitDust(UnityEngine.Random.Range(1, 3));
                }
            }
            else
            {
                _footstepDistance = footstepInterval * 0.5f; // 停下时保留一半进度，避免起步立刻踩一次
            }
        }

        void UpdateRumble(float moveInput)
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return;

            if (!rumbleEnabled || !isGrounded || Mathf.Abs(moveInput) < 0.1f)
            {
                gamepad.SetMotorSpeeds(0f, 0f);
                return;
            }

            float speed01 = Mathf.Abs(moveInput);
            gamepad.SetMotorSpeeds(rumbleLowFreq * speed01, rumbleHighFreq * speed01);
        }

        void OnDisable()
        {
            if (_afterPhysics != null) StopCoroutine(_afterPhysics);
            _afterPhysics = null;
            _physicsStepPending = false;
            if (_crushGuard != null) _crushGuard.ResolveAfterPhysics(0f);
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }

        void OnDrawGizmos()
        {
            if (!isDebugGizmos) return;

            // 地面检测圆（绿/红）
            if (groundCheck != null)
            {
                Gizmos.color = isGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, groundCheck.position);
            }

            // 左墙检测圆（蓝/青）
            if (wallCheckLeft != null)
            {
                Gizmos.color = isTouchingWallLeft ? Color.blue : Color.cyan;
                Gizmos.DrawWireSphere(wallCheckLeft.position, wallCheckRadius);
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, wallCheckLeft.position);
            }

            // 右墙检测圆（黄/白）
            if (wallCheckRight != null)
            {
                Gizmos.color = isTouchingWallRight ? Color.yellow : Color.white;
                Gizmos.DrawWireSphere(wallCheckRight.position, wallCheckRadius);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, wallCheckRight.position);
            }
        }
    }
}
