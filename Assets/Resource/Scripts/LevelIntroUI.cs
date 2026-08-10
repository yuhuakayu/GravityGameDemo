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

        private static readonly Color ButtonColor = new Color(0.3f, 0.22f, 0.15f, 1f);

        private Camera _cam;
        private FollowTarget2D _camFollow;
        private bool _isPreviewing;
        private GameObject _canvasGO;

        void Start()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                Debug.LogWarning("[LevelIntroUI] 找不到 Main Camera，跳过开局浏览过场。");
                return;
            }

            EnsureEventSystem();
            FreezeGameplay();
            BuildUI();

            _camFollow = _cam.GetComponent<FollowTarget2D>();
            if (_camFollow != null) _camFollow.enabled = false;

            _cam.transform.position = new Vector3(boundsCenter.x, boundsCenter.y, _cam.transform.position.z);

            _isPreviewing = true;
        }

        void Update()
        {
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
            if (player != null) player.enabled = false;
            var rotator = FindObjectOfType<WorldRotator>();
            if (rotator != null) rotator.enabled = false;
        }

        private void OnStartGameClicked()
        {
            if (!_isPreviewing) return;
            _isPreviewing = false;
            SfxManager.Instance.PlayButtonClick();

            var player = FindObjectOfType<PlayerController>();
            if (player != null) player.enabled = true;
            var rotator = FindObjectOfType<WorldRotator>();
            if (rotator != null) rotator.enabled = true;

            if (_camFollow != null) _camFollow.enabled = true;

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
                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight  = 0.5f;
                canvasGO.AddComponent<GraphicRaycaster>();
            }
            _canvasGO = canvasGO;
            canvasGO.SetActive(true);

            // 操作提示（左上角，跟 GameHUD 的关卡名标签同一种点锚点写法，不用拉伸锚点，
            // 避免 sizeDelta/anchoredPosition 在拉伸轴上语义变化导致位置算错）
            var hintGO = CreateText(canvasGO.transform, "Text_Hint",
                "左右扳机：缩放视野\n左摇杆：移动视角",
                new Vector2(0f, 1f), new Vector2(0f, 1f), TextAnchor.UpperLeft, 26);
            var hintRT = hintGO.GetComponent<RectTransform>();
            hintRT.pivot = new Vector2(0f, 1f);
            if (existingCanvas == null)
            {
                hintRT.anchoredPosition = new Vector2(24f, -20f);
                hintRT.sizeDelta = new Vector2(500f, 80f);
            }

            // 开始游戏按钮（右下角）
            var startBtn = CreateButton(canvasGO.transform, "开始游戏", new Vector2(280f, 64f), ButtonColor);
            var startRT = startBtn.GetComponent<RectTransform>();
            startRT.anchorMin = startRT.anchorMax = new Vector2(1f, 0f);
            startRT.pivot = new Vector2(1f, 0f);
            if (existingCanvas == null) startRT.anchoredPosition = new Vector2(-30f, 30f);
            startBtn.onClick.RemoveAllListeners();
            startBtn.onClick.AddListener(OnStartGameClicked);
        }

        private GameObject CreateText(Transform parent, string goName, string text, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize)
        {
            var existing = parent.Find(goName);
            if (existing != null)
            {
                var existingText = existing.GetComponent<Text>();
                if (existingText != null) existingText.text = text;
                return existing.gameObject;
            }

            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return go;
        }

        private Button CreateButton(Transform parent, string label, Vector2 size, Color color)
        {
            string goName = $"Button_{label}";
            var existing = parent.Find(goName);
            GameObject go;
            Image img;
            Button btn;

            if (existing != null)
            {
                go = existing.gameObject;
                img = go.GetComponent<Image>();
                btn = go.GetComponent<Button>();
                var existingLabel = go.GetComponentInChildren<Text>();
                if (existingLabel != null) existingLabel.text = label;
            }
            else
            {
                go = new GameObject(goName, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = size;

                img = go.AddComponent<Image>();
                img.color = color;

                btn = go.AddComponent<Button>();
                btn.targetGraphic = img;

                var textGO = new GameObject("Label", typeof(RectTransform));
                textGO.transform.SetParent(go.transform, false);
                var textRT = textGO.GetComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.offsetMin = Vector2.zero;
                textRT.offsetMax = Vector2.zero;
                var text = textGO.AddComponent<Text>();
                text.text = label;
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 24;
                text.color = Color.white;
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;
            }

            return btn;
        }
    }
}
