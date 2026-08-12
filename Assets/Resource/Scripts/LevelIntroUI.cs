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
        private CameraZoomController _camZoom;
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

            // 游玩中的镜头缩放组件：浏览模式期间禁用，避免跟这里自己的 ApplyZoom 同时响应同一个扳机输入
            _camZoom = _cam.GetComponent<CameraZoomController>();
            if (_camZoom == null) _camZoom = _cam.gameObject.AddComponent<CameraZoomController>();
            _camZoom.enabled = false;

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

            // 光禁用脚本挡不住物理引擎：Rigidbody2D 的重力/惯性还是会照常模拟，
            // 玩家会在浏览画面里悄悄往下掉。直接把时间冻结，连物理一起停——
            // 摇杆平移/扳机缩放走的是 Time.unscaledDeltaTime，不受影响。
            Time.timeScale = 0f;
        }

        private void OnStartGameClicked()
        {
            if (!_isPreviewing) return;
            _isPreviewing = false;
            Time.timeScale = 1f;
            SfxManager.Instance.PlayButtonClick();

            var player = FindObjectOfType<PlayerController>();
            if (player != null) player.enabled = true;
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
                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight  = 0.5f;
                canvasGO.AddComponent<GraphicRaycaster>();
            }
            _canvasGO = canvasGO;
            canvasGO.SetActive(true);

            // 操作提示（左上角）：图标 + 文字两行。摇杆和扳机图标都用美术自己做的手柄按键图
            // （Resources/Icons/ControllerButtons/ 下），不再是程序生成的占位图形
            var stickIcon = Resources.Load<Sprite>(ControllerIconResourceDir + "ls_icon_32");
            BuildHintRow(canvasGO.transform, "Row_Stick", stickIcon, "左摇杆：移动视角", 0);
            BuildTriggerHintRow(canvasGO.transform, "Row_Trigger", "左右扳机：缩放视野", 1);

            // 开始游戏按钮（右下角）
            var startBtn = CreateButton(canvasGO.transform, "开始游戏", new Vector2(280f, 64f), ButtonColor);
            var startRT = startBtn.GetComponent<RectTransform>();
            startRT.anchorMin = startRT.anchorMax = new Vector2(1f, 0f);
            startRT.pivot = new Vector2(1f, 0f);
            if (existingCanvas == null) startRT.anchoredPosition = new Vector2(-30f, 30f);
            startBtn.onClick.RemoveAllListeners();
            startBtn.onClick.AddListener(OnStartGameClicked);
        }

        /// <summary>一行操作提示：左边一个图标，右边文字，rowIndex 决定竖直排布的第几行（0 在最上面）。</summary>
        private void BuildHintRow(Transform parent, string rowName, Sprite icon, string label, int rowIndex)
        {
            var existingRow = parent.Find(rowName);
            GameObject rowGO;
            if (existingRow != null)
            {
                rowGO = existingRow.gameObject;
            }
            else
            {
                rowGO = new GameObject(rowName, typeof(RectTransform));
                rowGO.transform.SetParent(parent, false);
                var rowRT = rowGO.GetComponent<RectTransform>();
                rowRT.anchorMin = rowRT.anchorMax = new Vector2(0f, 1f);
                rowRT.pivot = new Vector2(0f, 1f);
                // 这一行本身刚新建（走到这个 else 分支就说明场景里原来没有），才需要摆位置；
                // 之前这里错判成"整个 Canvas 是不是新建的"，导致两行都判定成"不是新建"从而
                // 都没摆位置，全部叠在 RectTransform 默认的 (0,0) 上，看起来像文字糊在一起。
                // Y 起点从 -84 开始（不是 -20）：GameHUD 的关卡名标签也在左上角，从 -20 往下
                // 占了大概 50 高，紧挨着摆会跟它糊在一起，往下让开一段。
                rowRT.anchoredPosition = new Vector2(24f, -84f - rowIndex * 52f);
                rowRT.sizeDelta = new Vector2(360f, 48f);
            }

            var existingIcon = rowGO.transform.Find("Icon");
            Image iconImg;
            if (existingIcon != null)
            {
                iconImg = existingIcon.GetComponent<Image>();
            }
            else
            {
                var iconGO = new GameObject("Icon", typeof(RectTransform));
                iconGO.transform.SetParent(rowGO.transform, false);
                var iconRT = iconGO.GetComponent<RectTransform>();
                iconRT.anchorMin = new Vector2(0f, 0.5f);
                iconRT.anchorMax = new Vector2(0f, 0.5f);
                iconRT.pivot = new Vector2(0f, 0.5f);
                iconRT.anchoredPosition = Vector2.zero;
                iconRT.sizeDelta = new Vector2(80f, 80f); // 摇杆图标先调到 60，又再放大一圈到 80，比扳机徽标（36）明显更醒目
                iconImg = iconGO.AddComponent<Image>();
            }
            iconImg.sprite = icon;
            iconImg.color = Color.white;

            bool labelIsNew = rowGO.transform.Find("Text_Label") == null;
            var labelGO = CreateText(rowGO.transform, "Text_Label", label,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), TextAnchor.MiddleLeft, 22);
            if (labelIsNew) // 只在刚新建时摆位置，复用现成物体不覆盖（CreateText 内部已经处理了文字内容的复用）
            {
                var labelRT = labelGO.GetComponent<RectTransform>();
                labelRT.pivot = new Vector2(0f, 0.5f);
                labelRT.anchoredPosition = new Vector2(92f, 0f); // 图标又变宽了（60→80），文字继续让开
                labelRT.sizeDelta = new Vector2(300f, 40f);
            }
        }

        /// <summary>手柄按键美术图（美术自己截图做的，不是程序生成的），放在 Resources 下按名字加载。
        /// 图本身已经画好了黑底白字的徽标+字母，不用再叠字 Text。</summary>
        private const string ControllerIconResourceDir = "Icons/ControllerButtons/";

        /// <summary>扳机提示行：跟通用 BuildHintRow 不一样，这一行要并排放 L2/R2 两个徽标图标（美术素材图），
        /// 再接说明文字，所以单独写一个方法。两个徽标按各自贴图的宽高比顺序排布，不挤成正方形。</summary>
        private void BuildTriggerHintRow(Transform parent, string rowName, string label, int rowIndex)
        {
            var existingRow = parent.Find(rowName);
            GameObject rowGO;
            if (existingRow != null)
            {
                rowGO = existingRow.gameObject;
            }
            else
            {
                rowGO = new GameObject(rowName, typeof(RectTransform));
                rowGO.transform.SetParent(parent, false);
                var rowRT = rowGO.GetComponent<RectTransform>();
                rowRT.anchorMin = rowRT.anchorMax = new Vector2(0f, 1f);
                rowRT.pivot = new Vector2(0f, 1f);
                rowRT.anchoredPosition = new Vector2(24f, -84f - rowIndex * 52f);
                rowRT.sizeDelta = new Vector2(360f, 48f);
            }

            const float badgeHeight = 36f;
            const float badgeGap = 8f;
            float x = 0f;
            x += BuildIconBadge(rowGO.transform, "Badge_L2", "L2", x, badgeHeight) + badgeGap;
            x += BuildIconBadge(rowGO.transform, "Badge_R2", "R2", x, badgeHeight) + badgeGap;

            bool labelIsNew = rowGO.transform.Find("Text_Label") == null;
            var labelGO = CreateText(rowGO.transform, "Text_Label", label,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), TextAnchor.MiddleLeft, 22);
            if (labelIsNew)
            {
                var labelRT = labelGO.GetComponent<RectTransform>();
                labelRT.pivot = new Vector2(0f, 0.5f);
                labelRT.sizeDelta = new Vector2(260f, 40f);
            }
            // 每次都重新摆放到两个徽标算出来的实际宽度之后（徽标宽度是贴图决定的，不是写死的常量）
            var labelRTAlways = labelGO.GetComponent<RectTransform>();
            labelRTAlways.anchoredPosition = new Vector2(x + 4f, 0f);
        }

        /// <summary>单个手柄按键徽标：从 Resources/Icons/ControllerButtons/ 下加载同名贴图，
        /// 按贴图原始宽高比缩放到指定高度。返回这个徽标实际占用的宽度，方便调用方摆下一个的位置。</summary>
        private float BuildIconBadge(Transform parent, string goName, string iconName, float xOffset, float height)
        {
            var sprite = Resources.Load<Sprite>(ControllerIconResourceDir + iconName);
            if (sprite == null)
            {
                Debug.LogWarning($"[LevelIntroUI] 找不到手柄按键图标：{ControllerIconResourceDir}{iconName}");
                return height; // 找不到就退化成方形占位，好歹不会把后面的东西叠一起
            }
            float width = height * (sprite.rect.width / sprite.rect.height);

            var existing = parent.Find(goName);
            GameObject badgeGO;
            Image img;
            if (existing != null)
            {
                badgeGO = existing.gameObject;
                img = badgeGO.GetComponent<Image>();
            }
            else
            {
                badgeGO = new GameObject(goName, typeof(RectTransform));
                badgeGO.transform.SetParent(parent, false);
                img = badgeGO.AddComponent<Image>();
                img.color = Color.white;
                img.raycastTarget = false;
            }

            var rt = badgeGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(xOffset, 0f);
            rt.sizeDelta = new Vector2(width, height);
            img.sprite = sprite;

            return width;
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
