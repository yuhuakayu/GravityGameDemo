using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Resource.Scripts
{
    public class PlayerController : MonoBehaviour
    {
        [Header("调试")]
        public bool isDebugLog    = false;   // 控制台 Log
        public bool isDebugGizmos = false;   // Scene 画图

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

        [Header("跑步/落地手感（挤压拉伸）")]
        public float runStretchAmount = 0.08f;
        public float runSquashAmount = 0.06f;
        public float landStretchAmount = 0.15f;
        public float landSquashAmount = 0.25f;
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
        [Tooltip("打开后：手柄/键盘的左右移动完全失效，重力也会被关掉，玩家按固定方向自动滑行，" +
                 "方向只能靠撞到 WallRedirect 墙来改变——世界旋转变成玩家唯一能做的操作，" +
                 "转世界＝改变接下来会撞上哪面墙。撞到 HazardKill 物体直接死亡重开本关")]
        public bool autoMoveMode = false;
        [Tooltip("自动滑行的速度")]
        public float autoMoveSpeed = 6f;
        [Tooltip("自动移动模式下的初始移动方向（角度，0=右，90=上，180=左，270=下）")]
        public float autoMoveStartAngle = 0f;
        private Vector2 _autoMoveDir = Vector2.right;
        private bool _isDead = false;

        private Rigidbody2D rb;
        private bool isGrounded = false;
        public bool IsGrounded => isGrounded;
        private bool isTouchingWallLeft = false;
        private bool isTouchingWallRight = false;
        private float debugTimer = 0f;
        private SpriteRenderer _spriteRenderer;

        private Vector3 _spriteBaseScale = Vector3.one;
        private float _runStretch;
        private float _landImpact01;
        private float _landImpactTimer;
        private float _footstepDistance;
        private ParticleSystem _dustTrail;

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

            if (_spriteRenderer != null)
                _spriteBaseScale = _spriteRenderer.transform.localScale;

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

            if (FindObjectOfType<CandleSpawner>() == null)
                new GameObject("CandleSpawner (Auto)").AddComponent<CandleSpawner>();

            if (FindObjectOfType<MainMenuUI>() == null)
                new GameObject("MainMenuUI (Auto)").AddComponent<MainMenuUI>();

            BuildPlayerLight();
        }

        void BuildPlayerLight()
        {
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
            CheckGround();
            CheckWalls();
            HandleMovement();
            if (autoMoveMode) CheckAutoMoveFootContact();
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
            if (autoMoveMode)
            {
                HandleAutoMove();
                return;
            }

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

            // Sprite 翻转：向左 → flipX=true，向右 → flipX=false
            if (_spriteRenderer != null && moveInput != 0f)
                _spriteRenderer.flipX = moveInput > 0f;

            UpdateFootsteps(moveInput);
            UpdateSquashStretch(Mathf.Abs(moveInput));
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
        /// </summary>
        void HandleAutoMove()
        {
            if (_isDead) return;

            bool blocked = (_autoMoveDir.x > 0f && isTouchingWallRight) ||
                           (_autoMoveDir.x < 0f && isTouchingWallLeft);
            float targetX = blocked ? 0f : _autoMoveDir.x * autoMoveSpeed;
            rb.linearVelocity = new Vector2(targetX, rb.linearVelocity.y);

            if (_spriteRenderer != null && Mathf.Abs(_autoMoveDir.x) > 0.01f)
                _spriteRenderer.flipX = _autoMoveDir.x > 0f;

            UpdateFootsteps(targetX);
            UpdateSquashStretch(1f);
        }

        void HandleJump()
        {
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
                    rb.linearVelocity = new Vector2(
                        rb.linearVelocity.x, jumpForce);
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
                TriggerLandSquash(impact01);
            }
        }

        void OnCollisionExit2D(Collision2D col)
        {
            if (autoMoveMode) return;
            isGrounded = false;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            // 同上，自动移动模式下这里不再处理，统一走 CheckAutoMoveFootContact 的脚底检测。
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

        /// <summary>撞到致命物体：停下、放死亡音效、重新加载当前场景</summary>
        void Die()
        {
            if (_isDead) return;
            _isDead = true;
            rb.linearVelocity = Vector2.zero;
            SfxManager.Instance.PlayPlayerDeath();
            SceneTransition.Instance.LoadScene(SceneManager.GetActiveScene().name);
        }

        // ── 跑步手感 / 脚步声 / 扬尘（项目里没有现成的沙尘美术资源，用运行时生成的 ParticleSystem）──
        void BuildDustTrail()
        {
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

        void UpdateSquashStretch(float targetStretch01)
        {
            if (_spriteRenderer == null) return;

            _runStretch = Mathf.Lerp(_runStretch, targetStretch01, Time.fixedDeltaTime * 10f);

            _landImpactTimer += Time.fixedDeltaTime;
            float landFactor = _landImpact01 * Mathf.Exp(-_landImpactTimer * 12f);

            float stretchX = 1f + _runStretch * runStretchAmount + landFactor * landStretchAmount;
            float squashY  = 1f - _runStretch * runSquashAmount  - landFactor * landSquashAmount;

            Vector3 s = _spriteBaseScale;
            s.x *= stretchX;
            s.y *= squashY;
            _spriteRenderer.transform.localScale = s;
        }

        void TriggerLandSquash(float impact01)
        {
            _landImpact01    = impact01;
            _landImpactTimer = 0f;
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
