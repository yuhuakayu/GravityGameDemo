using Resource.Scripts.Gyro;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Resource.Scripts
{
    /// <summary>
    /// 关卡开局的"浏览过场"：正式开始前，玩家可以用左摇杆挪摄像机、左右扳机缩放视野，
    /// 先看一遍关卡布局，看完点右下角"开始游戏"（或手柄确认键）再正式进入。
    ///
    /// 浏览期间玩家和世界旋转都会被冻结，摄像机原本的 FollowTarget2D 跟随暂时关掉，
    /// 点了开始之后再恢复。可浏览范围用 Debug.DrawLine 画黄色线框——只在 Scene 视图 /
    /// 开了 Gizmos 的 Game 视图里看得到，纯粹是给关卡设计用的调试辅助，不是正式游戏 UI。
    /// 具体范围数值（boundsCenter/boundsSize）在 Inspector 里手动调。
    ///
    /// 这一版先用手柄左摇杆 + 左右扳机，以后要换成陀螺仪控制视角的话，
    /// 只需要把 HandlePan 里读摇杆的那几行换成读陀螺仪，ApplyPan/ApplyZoom 不用动。
    /// </summary>
    public class LevelIntroUI : MonoBehaviour
    {
        [Header("可浏览范围（黄色线框，Debug.DrawLine 画的，只在编辑器里看得到）")]
        public Vector2 boundsCenter = Vector2.zero;
        public Vector2 boundsSize = new Vector2(30f, 16f);

        [Header("左摇杆移动视角")]
        public float panSpeed = 8f;

        [Header("左右扳机缩放视野（右扳机拉近/缩小视野，左扳机拉远/放大视野）")]
        public float zoomSpeed = 6f;
        public float minOrthoSize = 3f;
        public float maxOrthoSize = 15f;

        private Camera _cam;
        private FollowTarget2D _camFollow;
        private CameraZoomController _camZoom;
        private bool _isPreviewing;
        private GameObject _canvasGO;
        private LocalizationManager _loc;
        private TextMeshProUGUI _startLabel;

        public bool IsPreviewing => _isPreviewing;

        void Start()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                Debug.LogWarning("[LevelIntroUI] 找不到 Main Camera，跳过开局浏览过场。");
                var activePlayer = FindObjectOfType<PlayerController>();
                if (activePlayer != null) activePlayer.BeginGameplay();
                return;
            }

            EnsureEventSystem();
            FreezeGameplay();
            _loc = LocalizationManager.Instance;
            BuildUI();
            _loc.OnLanguageChanged += RefreshLocalizedTexts;

            _camFollow = _cam.GetComponent<FollowTarget2D>();
            if (_camFollow != null) _camFollow.enabled = false;

            // 游玩中的镜头缩放组件：浏览模式期间禁用，避免跟这里自己的 ApplyZoom 同时响应同一个扳机输入
            _camZoom = _cam.GetComponent<CameraZoomController>();
            if (_camZoom == null) _camZoom = _cam.gameObject.AddComponent<CameraZoomController>();
            _camZoom.enabled = false;

            _cam.transform.position = new Vector3(boundsCenter.x, boundsCenter.y, _cam.transform.position.z);

            _isPreviewing = true;
        }

        void OnDestroy()
        {
            if (_loc != null) _loc.OnLanguageChanged -= RefreshLocalizedTexts;
        }

        private void RefreshLocalizedTexts()
        {
            if (_startLabel != null) _startLabel.text = _loc.Get("menu.start");
        }

        void Update()
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            if (!_isPreviewing || _cam == null) return;

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                ApplyPan(gamepad.leftStick.ReadValue());

                float l2 = gamepad.leftTrigger.ReadValue();
                float r2 = gamepad.rightTrigger.ReadValue();
                if (l2 < 0.05f) l2 = 0f;
                if (r2 < 0.05f) r2 = 0f;
                ApplyZoom(r2 - l2);
            }

            // 跟主菜单/关卡内设置面板同一套确认键：键盘 Enter 或手柄南键（×/A）
            bool confirmPressed = (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (confirmPressed) OnStartGameClicked();

            DrawBoundsGizmo();
        }

        /// <summary>拆成单独方法方便测试：直接传摇杆值调用，不用真的接手柄。</summary>
        private void ApplyPan(Vector2 stickInput)
        {
            if (stickInput.sqrMagnitude < 0.0001f) return;

            Vector3 pos = _cam.transform.position;
            pos.x += stickInput.x * panSpeed * Time.unscaledDeltaTime;
            pos.y += stickInput.y * panSpeed * Time.unscaledDeltaTime;

            float halfW = boundsSize.x * 0.5f;
            float halfH = boundsSize.y * 0.5f;
            pos.x = Mathf.Clamp(pos.x, boundsCenter.x - halfW, boundsCenter.x + halfW);
            pos.y = Mathf.Clamp(pos.y, boundsCenter.y - halfH, boundsCenter.y + halfH);

            _cam.transform.position = pos;
        }

        /// <summary>同上，拆成单独方法方便测试。zoomInput：正值拉近（缩小 orthographicSize），负值拉远。</summary>
        private void ApplyZoom(float zoomInput)
        {
            if (Mathf.Abs(zoomInput) < 0.001f) return;

            _cam.orthographicSize = Mathf.Clamp(
                _cam.orthographicSize - zoomInput * zoomSpeed * Time.unscaledDeltaTime,
                minOrthoSize, maxOrthoSize);
        }

        private void DrawBoundsGizmo()
        {
            float halfW = boundsSize.x * 0.5f;
            float halfH = boundsSize.y * 0.5f;
            Vector3 bl = new Vector3(boundsCenter.x - halfW, boundsCenter.y - halfH, 0f);
            Vector3 br = new Vector3(boundsCenter.x + halfW, boundsCenter.y - halfH, 0f);
            Vector3 tr = new Vector3(boundsCenter.x + halfW, boundsCenter.y + halfH, 0f);
            Vector3 tl = new Vector3(boundsCenter.x - halfW, boundsCenter.y + halfH, 0f);
            Debug.DrawLine(bl, br, Color.yellow);
            Debug.DrawLine(br, tr, Color.yellow);
            Debug.DrawLine(tr, tl, Color.yellow);
            Debug.DrawLine(tl, bl, Color.yellow);
        }

        private void FreezeGameplay()
        {
            var player = FindObjectOfType<PlayerController>();
            if (player != null)
            {
                player.EnterPreview();
                player.enabled = false;
            }
            var rotator = FindObjectOfType<WorldRotator>();
            if (rotator != null) rotator.enabled = false;

            // 光禁用脚本挡不住物理引擎：Rigidbody2D 的重力/惯性还是会照常模拟，
            // 玩家会在浏览画面里悄悄往下掉。直接把时间冻结，连物理一起停——
            // 摇杆平移/扳机缩放走的是 Time.unscaledDeltaTime，不受影响。
            Time.timeScale = 0f;
        }

        private void OnStartGameClicked()
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            BeginGameplayFromPreview();
        }

        /// <summary>转场完成后可调用此入口跳过预览，复用按钮的完整恢复流程。</summary>
        public void BeginGameplayFromPreview()
        {
            if (!_isPreviewing) return;
            _isPreviewing = false;
            Time.timeScale = 1f;
            SfxManager.Instance.PlayButtonClick();

            var player = FindObjectOfType<PlayerController>();
            if (player != null)
            {
                player.enabled = true;
                player.BeginGameplay();
            }
            var rotator = FindObjectOfType<WorldRotator>();
            if (rotator != null) rotator.enabled = true;

            if (_camFollow != null) _camFollow.enabled = true;
            if (_camZoom != null) _camZoom.enabled = true;

            if (_canvasGO != null) _canvasGO.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem (Auto)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ── 运行时搭 UI（找场景里已有的就复用，没有才照默认值新建）─────────────
        private void BuildUI()
        {
            var existingCanvas = transform.Find("LevelIntroCanvas (Auto)");
            GameObject canvasGO;
            if (existingCanvas != null)
            {
                canvasGO = existingCanvas.gameObject;
            }
            else
            {
                canvasGO = new GameObject("LevelIntroCanvas (Auto)");
                canvasGO.transform.SetParent(transform, false);
                var canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 700; // 盖在 HUD(10)/暂停(500) 之上，压在设置面板(600)之上一点，但在转场虹膜(1000)之下
                canvasGO.AddComponent<GraphicRaycaster>();
            }
            PixelUI.ConfigureCanvas(canvasGO.GetComponent<Canvas>());
            _canvasGO = canvasGO;
            canvasGO.SetActive(true);

            foreach (string hintName in new[] { "HintPanel", "Row_Stick", "Row_Trigger" })
            {
                var hint = canvasGO.transform.Find(hintName);
                if (hint == null) continue;
                hint.gameObject.SetActive(false);
                Destroy(hint.gameObject);
            }

            // 开始游戏按钮（右下角）
            var startBtn = CreateButton(canvasGO.transform, _loc.Get("menu.start"), new Vector2(300f, 54f));
            _startLabel = startBtn.GetComponentInChildren<TextMeshProUGUI>();
            var startRT = startBtn.GetComponent<RectTransform>();
            startRT.anchorMin = startRT.anchorMax = new Vector2(1f, 0f);
            startRT.pivot = new Vector2(1f, 0f);
            startRT.anchoredPosition = new Vector2(-24f, 24f);
            startBtn.onClick.RemoveAllListeners();
            startBtn.onClick.AddListener(OnStartGameClicked);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(startBtn.gameObject);
        }

        private GameObject CreateText(Transform parent, string goName, string text, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize)
        {
            var existing = parent.Find(goName);
            var go = existing != null ? existing.gameObject : new GameObject(goName, typeof(RectTransform));
            if (existing == null) go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = PixelUI.EnsureText(go, fontSize, false, align, PixelUI.TextLight);
            t.text = text;
            return go;
        }

        private Button CreateButton(Transform parent, string label, Vector2 size)
        {
            const string goName = "Button_StartGame";
            var existing = parent.Find(goName) ?? parent.Find("Button_开始游戏");
            GameObject go;
            Image img;
            Button btn;

            if (existing != null)
            {
                go = existing.gameObject;
                go.name = goName;
                img = go.GetComponent<Image>();
                btn = go.GetComponent<Button>();
            }
            else
            {
                go = new GameObject(goName, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                img = go.AddComponent<Image>();
                btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
            }
            go.GetComponent<RectTransform>().sizeDelta = size;
            PixelUI.StyleButton(btn);
            var textGO = CreateText(go.transform, "Label", label, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, 24);
            PixelUI.EnsureText(textGO, 24, true, TextAnchor.MiddleCenter, PixelUI.TextDark);
            return btn;
        }
    }
}
