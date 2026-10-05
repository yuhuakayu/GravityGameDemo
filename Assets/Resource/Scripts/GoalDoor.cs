using System.Collections;
using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 关卡终点门（带重力锁）。玩家进入触发区域、并且门已解锁时：禁用玩家操作、停止摄像机跟随、播放开门音效，
    /// 然后交给 SceneTransition 播放虹膜转场并加载下一关（新场景里的玩家/摄像机默认就是
    /// 启用状态，所以"恢复操作"不需要额外代码——新场景本身就是"恢复好"的状态）。
    ///
    /// 重力锁（gravityLock）：门上的小表盘里有一根指针，永远指向真正的「下」。
    /// 门的朝上方向和屏幕朝上方向的夹角不超过 unlockAngle 时解锁，超过 unlockAngle + relockMargin 再锁上。
    /// 门可以装在地上、天花板或墙上（整个门物体旋转），判断用的是门自己的朝上方向，所以都适用。
    /// 玩家碰到锁住的门时表盘闪红；之后只要玩家还在门里、门一转正就过关。
    ///
    /// 要求：这个物体的 Collider2D 勾选 Is Trigger；nextSceneName 要加进 Build Settings。
    /// </summary>
    public class GoalDoor : MonoBehaviour
    {
        [Header("下一关")]
        [Tooltip("要加载的场景名（须已加入 Build Settings）")]
        public string nextSceneName;

        [Header("门动画")]
        [SerializeField] private Animator doorAnimator;
        [SerializeField] private AnimationClip openAnimation;

        [Header("重力锁")]
        [Tooltip("打开 = 门正立才能过关；关掉 = 和以前一样，碰到就过关")]
        public bool gravityLock;
        [Tooltip("门歪的角度不超过这个值就解锁（度）")]
        [Range(1f, 60f)] public float unlockAngle = 20f;
        [Tooltip("解锁后要歪到 unlockAngle + 这个值才重新锁上，避免在边缘来回闪（度）")]
        [Range(0f, 10f)] public float relockMargin = 3f;
        [Tooltip("盖在门上的表盘图层")]
        [SerializeField] private SpriteRenderer lockOverlay;
        [SerializeField] private Sprite overlayOpen;      // 解锁：表盘青色，门缝亮
        [SerializeField] private Sprite overlayLocked;    // 锁住：表盘暗，门缝熄灭
        [SerializeField] private Sprite overlayHint;      // 玩家碰到锁住的门：表盘闪红
        [Tooltip("指针（小钟摆），物体位置 = 表盘中心")]
        [SerializeField] private Transform needle;
        [Tooltip("指针摆长（单位），越长摆得越慢")]
        public float needleLength = 0.25f;
        [Tooltip("指针阻尼，越小晃得越久")]
        public float needleDamping = 0.22f;

        private bool _triggered;
        private bool _unlocked = true;
        private bool _warnedNoScene;
        private PlayerController _player;
        private int _playerColliders;
        private float _hintTimer;
        private float _needleAngle, _needleVelocity;
        private Vector2 _pivotPosition, _pivotVelocity;
        private bool _pivotReady;

        public bool IsOpening => _triggered;
        public bool IsUnlocked => _unlocked;
        /// <summary>门的朝上方向和屏幕朝上方向的夹角（度）。重力始终朝屏幕下方。</summary>
        public float TiltAngle => Vector2.Angle(transform.up, Vector2.up);

        void Start()
        {
            _unlocked = !gravityLock || TiltAngle <= unlockAngle;
            ApplyOverlay();
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;
            _player = player;
            _playerColliders++;
            if (!_triggered && !_unlocked && _playerColliders == 1) _hintTimer = 1f;
            TryOpen();
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null || player != _player) return;
            _playerColliders = Mathf.Max(0, _playerColliders - 1);
            if (_playerColliders == 0) _player = null;
        }

        void Update()
        {
            if (_triggered) return;
            if (!gravityLock) _unlocked = true;
            else
            {
                float tilt = TiltAngle;
                if (_unlocked && tilt > unlockAngle + relockMargin) _unlocked = false;
                else if (!_unlocked && tilt <= unlockAngle) _unlocked = true;
            }
            if (_hintTimer > 0f) _hintTimer -= Time.deltaTime;
            ApplyOverlay();
            TryOpen();
        }

        void LateUpdate()
        {
            if (needle == null || !gravityLock) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // 指针 = 挂在表盘中心的小钟摆：被重力拉向屏幕下方；世界转动时枢轴被甩着走，指针会晃一下再停住
            Vector2 position = needle.position;
            if (!_pivotReady) { _pivotPosition = position; _pivotVelocity = Vector2.zero; _pivotReady = true; }
            Vector2 velocity = (position - _pivotPosition) / dt;
            Vector2 acceleration = (velocity - _pivotVelocity) / dt;
            if (acceleration.magnitude > 40f) acceleration = acceleration.normalized * 40f;
            _pivotPosition = position;
            _pivotVelocity = velocity;

            float g = Physics2D.gravity.magnitude;
            float length = Mathf.Max(0.05f, needleLength);
            float w0 = Mathf.Sqrt(g / length);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float a = (-acceleration.x * Mathf.Cos(_needleAngle) - (g + acceleration.y) * Mathf.Sin(_needleAngle)) / length
                          - 2f * needleDamping * w0 * _needleVelocity;
                _needleVelocity += a * h;
                _needleAngle += _needleVelocity * h;
            }
            // 指针图朝下画，0 度 = 指向屏幕正下方（世界坐标）
            needle.rotation = Quaternion.Euler(0f, 0f, _needleAngle * Mathf.Rad2Deg);
        }

        private void TryOpen()
        {
            if (_triggered || _player == null || !_unlocked) return;
            if (_player.IsDead || !_player.isActiveAndEnabled) return;
            if (string.IsNullOrEmpty(nextSceneName))
            {
                if (!_warnedNoScene) Debug.LogWarning("[GoalDoor] 没有设置 nextSceneName，无法转场。");
                _warnedNoScene = true;
                return;
            }

            _triggered = true;
            _hintTimer = 0f;
            ApplyOverlay();

            var player = _player;
            player.enabled = false;
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;

            var cam = Camera.main;
            if (cam != null)
            {
                var follow = cam.GetComponent<FollowTarget2D>();
                if (follow != null) follow.enabled = false;
            }

            SfxManager.Instance.PlayDoorOpen();
            StartCoroutine(DoGoalSequence());
        }

        private void ApplyOverlay()
        {
            if (lockOverlay == null) return;
            lockOverlay.enabled = gravityLock;
            if (needle != null && needle.gameObject.activeSelf != gravityLock) needle.gameObject.SetActive(gravityLock);
            if (!gravityLock) return;
            Sprite sprite;
            if (_triggered || _unlocked) sprite = overlayOpen;
            else if (_hintTimer > 0f && Mathf.FloorToInt(_hintTimer * 8f) % 2 == 0) sprite = overlayHint;
            else sprite = overlayLocked;
            if (sprite != null && lockOverlay.sprite != sprite) lockOverlay.sprite = sprite;
        }

        /// <summary>开门声后 0.3 秒播放过关声，等待动画和原有延迟后进入虹膜转场。</summary>
        private IEnumerator DoGoalSequence()
        {
            float openDuration = 0f;
            if (doorAnimator != null && openAnimation != null)
            {
                doorAnimator.Play("Open", 0, 0f);
                openDuration = openAnimation.length;
            }

            yield return new WaitForSecondsRealtime(0.3f);
            SfxManager.Instance.PlayStageComplete();
            if (openDuration > 0.3f)
                yield return new WaitForSecondsRealtime(openDuration - 0.3f);
            yield return new WaitForSecondsRealtime(0.6f);
            if (nextSceneName == "MainMenu") GameFlowState.HasEnteredGame = false;
            SceneTransition.Instance.LoadScene(nextSceneName);
        }
    }
}
