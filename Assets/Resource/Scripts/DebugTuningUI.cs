using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Resource.Scripts
{
    /// <summary>
    /// 调试用参数面板（运行时自建 UI，不是正式游戏 UI）。按 F1 显示/隐藏。
    /// 拉条 + 数字，直接实时改 WorldRotator / PlayerController / SfxManager 的字段，
    /// 顶部还有一块只读的实时手柄输入读数，方便对 Steam Input 映射。
    ///
    /// Pivot 摆锤（PivotPendulum）没有放进来——场景里可能有多个实例，不适合塞进
    /// 同一个通用面板，需要的话单独再做。
    /// 由 PlayerController.Start() 自动创建。
    /// </summary>
    public class DebugTuningUI : MonoBehaviour
    {
        private const float RowWidth = 420f; // 兜底宽度，防止 VerticalLayoutGroup 的 childControlWidth 没生效时行宽塌缩

        private GameObject _panelRoot;
        private Transform  _content;
        private Text       _readoutText;

        private WorldRotator    _worldRotator;
        private PlayerController _player;
        private DS5GyroReader   _gyroReader;

        void Start()
        {
            EnsureEventSystem();
            BuildUI();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                _panelRoot.SetActive(!_panelRoot.activeSelf);

            UpdateReadout();
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem (Auto)");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private void UpdateReadout()
        {
            if (_readoutText == null) return;

            var gp = Gamepad.current;
            float stickX = gp != null ? gp.rightStick.x.ReadValue() : 0f;
            float l2 = gp != null ? gp.leftTrigger.ReadValue() : 0f;
            float r2 = gp != null ? gp.rightTrigger.ReadValue() : 0f;
            string gpName = gp != null ? gp.displayName : "无手柄";

            float steeringAngle = _worldRotator != null ? _worldRotator.SteeringAngleReadout : 0f;
            bool grounded = _player != null && _player.IsGrounded;

            if (_gyroReader == null)
                _gyroReader = FindObjectOfType<DS5GyroReader>();
            string gyroLine = _gyroReader != null
                ? $"陀螺仪: {(_gyroReader.IsAvailable ? "已连接" : "未连接")}  速度: {_gyroReader.GyroVelocity:+0.000;-0.000}"
                : "陀螺仪: 场景里没有 DS5GyroReader";

            _readoutText.text =
                $"手柄: {gpName}\n" +
                $"右摇杆 X: {stickX:+0.000;-0.000}\n" +
                $"L2: {l2:0.000}   R2: {r2:0.000}\n" +
                $"{gyroLine}\n" +
                $"方向盘角度: {steeringAngle:F1}°\n" +
                $"是否落地: {(grounded ? "是" : "否")}";
        }

        // ── 运行时搭 UI ─────────────────────────────────────────
        private void BuildUI()
        {
            _worldRotator = FindObjectOfType<WorldRotator>();
            _player       = FindObjectOfType<PlayerController>();

            var canvasGO = new GameObject("DebugTuningCanvas (Auto)");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 800; // HUD(10) < 暂停菜单(500) < 这个(800) < 转场虹膜(1000)
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            _panelRoot = new GameObject("Panel");
            _panelRoot.transform.SetParent(canvasGO.transform, false);
            var panelRT = _panelRoot.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(1f, 1f);
            panelRT.anchorMax = new Vector2(1f, 1f);
            panelRT.pivot = new Vector2(1f, 1f);
            panelRT.anchoredPosition = new Vector2(-20f, -20f);
            panelRT.sizeDelta = new Vector2(460f, 760f);
            var panelImg = _panelRoot.AddComponent<Image>();
            panelImg.color = new Color(0.06f, 0.06f, 0.08f, 0.9f);

            // 标题
            var title = CreateText(_panelRoot.transform, "调试面板（F1 显示/隐藏）",
                new Vector2(0f, 1f), new Vector2(1f, 1f), TextAnchor.MiddleCenter, 20);
            var titleRT = title.GetComponent<RectTransform>();
            titleRT.anchoredPosition = new Vector2(16f, -18f);
            titleRT.sizeDelta = new Vector2(-32f, 36f);

            // 实时读数
            var readoutGO = CreateText(_panelRoot.transform, "", new Vector2(0f, 1f), new Vector2(1f, 1f), TextAnchor.UpperLeft, 15);
            var readoutRT = readoutGO.GetComponent<RectTransform>();
            readoutRT.anchoredPosition = new Vector2(16f, -62f);
            readoutRT.sizeDelta = new Vector2(-32f, 150f);
            _readoutText = readoutGO.GetComponent<Text>();
            _readoutText.color = new Color(0.6f, 0.9f, 1f);

            // 滚动区域
            var scrollGO = new GameObject("Scroll", typeof(RectTransform));
            scrollGO.transform.SetParent(_panelRoot.transform, false);
            var scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = Vector2.zero;
            scrollRT.anchorMax = Vector2.one;
            scrollRT.offsetMin = new Vector2(8f, 8f);
            scrollRT.offsetMax = new Vector2(-8f, -220f);
            var scrollRect = scrollGO.AddComponent<ScrollRect>();

            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var viewportRT = viewportGO.GetComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            viewportGO.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f); // 透明但要有 Graphic 才能当 Mask 目标
            viewportGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            var vLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            vLayout.spacing = 6f;
            vLayout.padding = new RectOffset(4, 4, 4, 4);
            vLayout.childControlHeight = false;
            vLayout.childControlWidth  = true;
            vLayout.childForceExpandHeight = false;
            vLayout.childForceExpandWidth  = true;
            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRT;
            scrollRect.content  = contentRT;
            scrollRect.horizontal = false;
            scrollRect.vertical   = true;

            _content = contentGO.transform;

            // ── 分组：世界旋转 ──
            AddSectionLabel("世界旋转");
            if (_worldRotator != null)
            {
                AddFloatRow("旋转速度", 10f, 360f, _worldRotator.rotateSpeed, v => _worldRotator.rotateSpeed = v);
                AddToggleRow("优先用陀螺仪（而非摇杆）", _worldRotator.useGyroIfAvailable, v => _worldRotator.useGyroIfAvailable = v);
                AddFloatRow("摇杆死区", 0f, 0.5f, _worldRotator.stickDeadzone, v => _worldRotator.stickDeadzone = v);
                AddFloatRow("摇杆平滑强度", 1f, 30f, _worldRotator.stickSmoothing, v => _worldRotator.stickSmoothing = v);
                AddFloatRow("方向持续时间(秒)", 0f, 0.5f, _worldRotator.stickSustainTime, v => _worldRotator.stickSustainTime = v);
                AddToggleRow("限制方向盘范围", _worldRotator.limitSteeringRange, v => _worldRotator.limitSteeringRange = v);
                AddFloatRow("方向盘范围", 90f, 2160f, _worldRotator.steeringRange, v => _worldRotator.steeringRange = v);
                AddFloatRow("空中旋转延迟帧数", 0f, 60f, _worldRotator.airborneDelayFrames, v => _worldRotator.airborneDelayFrames = Mathf.RoundToInt(v));
            }

            // ── 分组：手柄震动 ──
            AddSectionLabel("手柄震动（走路时）");
            if (_player != null)
            {
                AddToggleRow("震动开关", _player.rumbleEnabled, v => _player.rumbleEnabled = v);
                AddFloatRow("低频强度", 0f, 1f, _player.rumbleLowFreq, v => _player.rumbleLowFreq = v);
                AddFloatRow("高频强度", 0f, 1f, _player.rumbleHighFreq, v => _player.rumbleHighFreq = v);
            }

            // ── 分组：玩家移动 ──
            AddSectionLabel("玩家移动");
            if (_player != null)
            {
                AddFloatRow("最大移速", 1f, 20f, _player.maxMoveSpeed, v => _player.maxMoveSpeed = v);
                AddFloatRow("跳跃力度", 1f, 30f, _player.jumpForce, v => _player.jumpForce = v);
            }

            // ── 分组：音效 ──
            AddSectionLabel("音效");
            AddToggleRow("总开关", SfxManager.Instance.sfxEnabled, v => SfxManager.Instance.sfxEnabled = v);

            // 动态搭出来的 UI 布局默认要等到下一帧才会重新计算，这里强制立刻刷新一次，
            // 避免第一帧渲染出来的时候行宽/行高还是没算过的默认值
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);

            _panelRoot.SetActive(false); // 默认隐藏，按 F1 呼出
        }

        private void AddSectionLabel(string text)
        {
            var go = CreateText(_content, text, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, 17);
            go.GetComponent<Text>().fontStyle = FontStyle.Bold;
            go.GetComponent<Text>().color = new Color(1f, 0.8f, 0.4f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;
            le.minHeight = 26f;
            le.preferredWidth = RowWidth;
            le.minWidth = RowWidth;
        }

        private void AddFloatRow(string label, float min, float max, float initial, System.Action<float> setter)
        {
            var row = new GameObject($"Row_{label}", typeof(RectTransform));
            row.transform.SetParent(_content, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 58f;
            le.minHeight = 58f;
            le.preferredWidth = RowWidth;
            le.minWidth = RowWidth;

            // 上半：标签，占满整行宽度
            CreateText(row.transform, label, new Vector2(0f, 0.55f), new Vector2(1f, 1f), TextAnchor.LowerCenter, 15);

            // 下半：滑条 + 数值
            var barGO = new GameObject("Bar", typeof(RectTransform));
            barGO.transform.SetParent(row.transform, false);
            var barRT = barGO.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0f, 0.05f);
            barRT.anchorMax = new Vector2(0.78f, 0.5f);
            barRT.offsetMin = Vector2.zero;
            barRT.offsetMax = Vector2.zero;
            var barImg = barGO.AddComponent<Image>();
            barImg.color = new Color(0.16f, 0.16f, 0.2f, 1f);

            var fillGO = new GameObject("Fill", typeof(RectTransform));
            fillGO.transform.SetParent(barGO.transform, false);
            var fillRT = fillGO.GetComponent<RectTransform>();
            float t0 = max > min ? Mathf.InverseLerp(min, max, initial) : 0f;
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = new Vector2(Mathf.Clamp01(t0), 1f);
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.62f, 0.95f, 1f);

            var valueGO = CreateText(row.transform, initial.ToString("F2"), new Vector2(0.8f, 0.05f), new Vector2(1f, 0.5f), TextAnchor.MiddleRight, 14);
            var valueText = valueGO.GetComponent<Text>();

            var dragger = barGO.AddComponent<DebugSliderDrag>();
            dragger.Init(barRT, fillRT, min, max, value =>
            {
                setter(value);
                valueText.text = value.ToString("F2");
            });
        }

        private void AddToggleRow(string label, bool initial, System.Action<bool> setter)
        {
            var row = new GameObject($"Row_{label}", typeof(RectTransform));
            row.transform.SetParent(_content, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 56f;
            le.minHeight = 56f;
            le.preferredWidth = RowWidth;
            le.minWidth = RowWidth;

            // 上半：标签，占满整行宽度
            CreateText(row.transform, label, new Vector2(0f, 0.55f), new Vector2(1f, 1f), TextAnchor.LowerCenter, 15);

            // 下半：开关
            var toggleGO = new GameObject("Toggle", typeof(RectTransform));
            toggleGO.transform.SetParent(row.transform, false);
            var toggleRT = toggleGO.GetComponent<RectTransform>();
            toggleRT.anchorMin = new Vector2(0f, 0.05f);
            toggleRT.anchorMax = new Vector2(0.14f, 0.5f);
            toggleRT.offsetMin = Vector2.zero;
            toggleRT.offsetMax = Vector2.zero;

            var bgImg = toggleGO.AddComponent<Image>();
            bgImg.color = new Color(0.16f, 0.16f, 0.2f, 1f);

            var checkGO = new GameObject("Checkmark", typeof(RectTransform));
            checkGO.transform.SetParent(toggleGO.transform, false);
            var checkRT = checkGO.GetComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.15f, 0.15f);
            checkRT.anchorMax = new Vector2(0.85f, 0.85f);
            checkRT.offsetMin = Vector2.zero;
            checkRT.offsetMax = Vector2.zero;
            var checkImg = checkGO.AddComponent<Image>();
            checkImg.color = new Color(0.35f, 0.85f, 0.45f, 1f);

            var toggle = toggleGO.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.isOn = initial;
            toggle.onValueChanged.AddListener(v => setter(v));
        }

        private GameObject CreateText(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize)
        {
            var go = new GameObject("Text", typeof(RectTransform));
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
            t.horizontalOverflow = HorizontalWrapMode.Overflow; // 宁可溢出也不要把字截断/挤成竖排
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return go;
        }
    }

    /// <summary>
    /// 简易滑条拖拽逻辑，不用 Unity 内置 Slider 的多层子物体结构，点击/拖拽这个条上任意位置
    /// 直接按鼠标 X 位置换算成数值，同步更新 Fill 的宽度。
    /// </summary>
    public class DebugSliderDrag : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        private RectTransform _barRect;
        private RectTransform _fillRect;
        private float _min;
        private float _max;
        private System.Action<float> _onChanged;

        /// <summary>当前值，供手柄十字键这类没法直接拖拽的输入方式读取后再叠加步进用</summary>
        public float Value { get; private set; }

        public void Init(RectTransform barRect, RectTransform fillRect, float min, float max, System.Action<float> onChanged)
        {
            _barRect = barRect;
            _fillRect = fillRect;
            _min = min;
            _max = max;
            _onChanged = onChanged;
            Value = Mathf.Lerp(min, max, fillRect.anchorMax.x);
        }

        public void OnPointerDown(PointerEventData eventData) => Apply(eventData);
        public void OnDrag(PointerEventData eventData) => Apply(eventData);

        private void Apply(PointerEventData eventData)
        {
            if (_barRect == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _barRect, eventData.position, eventData.pressEventCamera, out Vector2 local);

            float width = _barRect.rect.width;
            if (width <= 0f) return;

            float t = Mathf.Clamp01((local.x - _barRect.rect.xMin) / width);
            SetValue(Mathf.Lerp(_min, _max, t));
        }

        /// <summary>非鼠标输入（手柄十字键/摇杆）用这个直接改值，同步拉条外观并触发回调</summary>
        public void SetValue(float value)
        {
            value = Mathf.Clamp(value, _min, _max);
            float t = _max > _min ? (value - _min) / (_max - _min) : 0f;
            _fillRect.anchorMax = new Vector2(t, 1f);
            Value = value;
            _onChanged?.Invoke(value);
        }
    }
}
