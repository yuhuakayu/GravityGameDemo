using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Resource.Scripts
{
    /// <summary>
    /// 运行时自建的 HUD + 暂停菜单（没有对应美术资源，先用纯色 UI 占位，逻辑和交互都是完整的）。
    ///   HUD：左上角关卡名，右上角设置按钮。
    ///   暂停菜单：点设置按钮或按 ESC 打开，继续 / 重开本关 / 退出游戏，按钮有 hover/点击反馈，面板有淡入淡出。
    /// 由 PlayerController.Start() 自动创建。
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("关卡信息")]
        public string levelLabel = "Level 1";

        private static readonly Color PanelColor  = new Color(0.08f, 0.07f, 0.1f, 0.92f);
        private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color ButtonColor = new Color(0.3f, 0.22f, 0.15f, 1f);

        private GameObject  _pausePanelRoot;
        private CanvasGroup _pauseCanvasGroup;
        private bool _isPaused;
        private readonly List<Button> _pauseButtons = new List<Button>();
        private int _pauseSelectedIndex;
        private readonly Dictionary<Button, Image[]> _buttonBorders = new Dictionary<Button, Image[]>();

        private GameObject  _settingsPanelRoot;
        private CanvasGroup _settingsCanvasGroup;
        private bool _isSettingsOpen;
        private readonly List<Image> _settingsRowBg = new List<Image>();
        private int _settingsSelectedIndex;
        private DebugSliderDrag _masterDrag, _musicDrag, _sfxDrag;
        private Text _resolutionLabel;
        private Toggle _fullscreenToggle;

        private LocalizationManager _loc;
        private readonly List<(Text text, string key)> _localizedTexts = new List<(Text, string)>();
        private Text _settingsLanguageLabel;

        void Start()
        {
            EnsureEventSystem();
            BuildHud();
            BuildPauseMenu();
            BuildSettingsPanel();
            _loc = LocalizationManager.Instance;
            _loc.OnLanguageChanged += RefreshLocalizedTexts;
        }

        void OnDestroy()
        {
            if (_loc != null) _loc.OnLanguageChanged -= RefreshLocalizedTexts;
        }

        /// <summary>用 Localization 表里的 key 建文字，并且登记下来，语言切换时统一刷新</summary>
        private GameObject CreateLocalizedText(Transform parent, string key, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize)
        {
            var go = CreateSettingsText(parent, LocalizationManager.Instance.Get(key), anchorMin, anchorMax, align, fontSize);
            _localizedTexts.Add((go.GetComponent<Text>(), key));
            return go;
        }

        private void RefreshLocalizedTexts()
        {
            foreach (var (text, key) in _localizedTexts)
                if (text != null) text.text = LocalizationManager.Instance.Get(key);
            if (_settingsLanguageLabel != null)
                _settingsLanguageLabel.text = LocalizationManager.Instance.LanguageName(LocalizationManager.Instance.CurrentLanguage);
        }

        void Update()
        {
            if (_isSettingsOpen)
            {
                HandleSettingsInput();
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                TogglePause();

            // 手柄的 Home/Guide 键在绝大多数平台上是系统保留的（打开 Xbox/PS 自带的系统菜单），
            // 游戏本身收不到；这里用行业惯例的 Start/Options 键代替，效果等价。
            // 按一下打开的是第一层菜单（继续/设置/重新开始/退出游戏），要进设置得在里面再选一次。
            var gamepad = Gamepad.current;
            if (gamepad != null && gamepad.startButton.wasPressedThisFrame)
                TogglePause();

            if (_isPaused)
                HandlePauseInput();
        }

        private void TogglePause()
        {
            if (_isPaused) ClosePause();
            else OpenPause();
        }

        private void OpenPause()
        {
            _isPaused = true;
            Time.timeScale = 0f;
            _pausePanelRoot.SetActive(true);
            StartCoroutine(FadeCanvasGroup(_pauseCanvasGroup, 0f, 1f, 0.2f));
            SfxManager.Instance.PlayButtonClick();
            _pauseSelectedIndex = 0;
            UpdatePauseButtonHighlight();
        }

        private void ClosePause()
        {
            _isPaused = false;
            Time.timeScale = 1f;
            var root = _pausePanelRoot;
            StartCoroutine(FadeCanvasGroup(_pauseCanvasGroup, 1f, 0f, 0.2f, () => root.SetActive(false)));
            SfxManager.Instance.PlayButtonClick();
        }

        private void UpdatePauseButtonHighlight()
        {
            for (int i = 0; i < _pauseButtons.Count; i++)
                SetButtonFocused(_pauseButtons[i], i == _pauseSelectedIndex);
        }

        /// <summary>暂停菜单（第一层：继续/设置/重新开始/退出游戏）的十字键/摇杆导航</summary>
        private void HandlePauseInput()
        {
            if (_pauseButtons.Count == 0) return;

            var gamepad = Gamepad.current;

            bool closePressed = gamepad != null && gamepad.buttonEast.wasPressedThisFrame;
            if (closePressed) { ClosePause(); return; }

            float navAxis = 0f;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.upArrowKey.wasPressedThisFrame)   navAxis = -1f;
                if (Keyboard.current.downArrowKey.wasPressedThisFrame) navAxis = 1f;
            }
            if (gamepad != null)
            {
                if (gamepad.leftStick.up.wasPressedThisFrame   || gamepad.dpad.up.wasPressedThisFrame)   navAxis = -1f;
                if (gamepad.leftStick.down.wasPressedThisFrame || gamepad.dpad.down.wasPressedThisFrame) navAxis = 1f;
            }
            if (navAxis != 0f)
            {
                int newIndex = Mathf.Clamp(_pauseSelectedIndex + (int)navAxis, 0, _pauseButtons.Count - 1);
                if (newIndex != _pauseSelectedIndex)
                {
                    _pauseSelectedIndex = newIndex;
                    UpdatePauseButtonHighlight();
                    SfxManager.Instance.PlayButtonHover();
                }
            }

            bool confirmPressed = (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (confirmPressed)
                _pauseButtons[_pauseSelectedIndex].onClick.Invoke();
        }

        private void OnRestartClicked()
        {
            Time.timeScale = 1f;
            _isPaused = false;
            _pausePanelRoot.SetActive(false);
            SfxManager.Instance.PlayButtonClick();
            SceneTransition.Instance.LoadScene(SceneManager.GetActiveScene().name);
        }

        // ── 设置面板（手柄 Start 键直接打开，也可以从暂停菜单里点进来；十字键/摇杆上下选行，左右改值）──
        private void OpenSettings()
        {
            _isSettingsOpen = true;
            Time.timeScale = 0f;
            _settingsPanelRoot.SetActive(true);
            StartCoroutine(FadeCanvasGroup(_settingsCanvasGroup, 0f, 1f, 0.2f));
            SfxManager.Instance.PlayButtonClick();
            _settingsSelectedIndex = 0;
            UpdateSettingsRowHighlight();
        }

        private void CloseSettings()
        {
            _isSettingsOpen = false;
            if (!_isPaused) Time.timeScale = 1f; // 从暂停菜单里打开的话，关掉设置要回到暂停状态而不是直接恢复游戏
            var root = _settingsPanelRoot;
            StartCoroutine(FadeCanvasGroup(_settingsCanvasGroup, 1f, 0f, 0.2f, () => root.SetActive(false)));
            SfxManager.Instance.PlayButtonClick();
        }

        private void UpdateSettingsRowHighlight()
        {
            for (int i = 0; i < _settingsRowBg.Count; i++)
                _settingsRowBg[i].color = i == _settingsSelectedIndex
                    ? new Color(1f, 0.85f, 0.3f, 0.22f)
                    : new Color(0f, 0f, 0f, 0f);
        }

        private void HandleSettingsInput()
        {
            var gamepad = Gamepad.current;

            bool closePressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (gamepad != null && (gamepad.buttonEast.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame));
            if (closePressed) { CloseSettings(); return; }

            float navAxis = 0f;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.upArrowKey.wasPressedThisFrame)   navAxis = -1f;
                if (Keyboard.current.downArrowKey.wasPressedThisFrame) navAxis = 1f;
            }
            if (gamepad != null)
            {
                if (gamepad.leftStick.up.wasPressedThisFrame   || gamepad.dpad.up.wasPressedThisFrame)   navAxis = -1f;
                if (gamepad.leftStick.down.wasPressedThisFrame || gamepad.dpad.down.wasPressedThisFrame) navAxis = 1f;
            }
            if (navAxis != 0f)
            {
                int newIndex = Mathf.Clamp(_settingsSelectedIndex + (int)navAxis, 0, _settingsRowBg.Count - 1);
                if (newIndex != _settingsSelectedIndex)
                {
                    _settingsSelectedIndex = newIndex;
                    UpdateSettingsRowHighlight();
                    SfxManager.Instance.PlayButtonHover();
                }
            }

            float adjustAxis = 0f;
            bool adjustPressed = false;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame)  { adjustAxis = -1f; adjustPressed = true; }
                if (Keyboard.current.rightArrowKey.wasPressedThisFrame) { adjustAxis = 1f; adjustPressed = true; }
            }
            if (gamepad != null)
            {
                if (gamepad.leftStick.left.wasPressedThisFrame  || gamepad.dpad.left.wasPressedThisFrame)  { adjustAxis = -1f; adjustPressed = true; }
                if (gamepad.leftStick.right.wasPressedThisFrame || gamepad.dpad.right.wasPressedThisFrame) { adjustAxis = 1f; adjustPressed = true; }
            }

            bool confirmPressed = (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);

            ApplySettingsRowInput(adjustAxis, adjustPressed, confirmPressed);
        }

        private void ApplySettingsRowInput(float adjustAxis, bool adjustPressed, bool confirmPressed)
        {
            var settings = SettingsManager.Instance;
            switch (_settingsSelectedIndex)
            {
                case 0:
                    if (adjustPressed) { _masterDrag.SetValue(_masterDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayButtonHover(); }
                    break;
                case 1:
                    if (adjustPressed) { _musicDrag.SetValue(_musicDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayButtonHover(); }
                    break;
                case 2:
                    if (adjustPressed) { _sfxDrag.SetValue(_sfxDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayButtonHover(); }
                    break;
                case 3:
                    if (adjustPressed)
                    {
                        int dir = adjustAxis > 0f ? 1 : -1;
                        settings.SetResolutionIndex((settings.resolutionIndex + dir + settings.CommonResolutions.Length) % settings.CommonResolutions.Length);
                        _resolutionLabel.text = ResolutionLabel(settings);
                        SfxManager.Instance.PlayButtonHover();
                    }
                    break;
                case 4:
                    if (adjustPressed || confirmPressed)
                    {
                        _fullscreenToggle.isOn = !_fullscreenToggle.isOn;
                        SfxManager.Instance.PlayButtonClick();
                    }
                    break;
                case 5:
                    if (adjustPressed)
                    {
                        int dir = adjustAxis > 0f ? 1 : -1;
                        LocalizationManager.Instance.CycleLanguage(dir);
                        SfxManager.Instance.PlayButtonHover();
                    }
                    break;
                case 6:
                    if (confirmPressed) CloseSettings();
                    break;
            }
        }

        private void OnReturnToMainMenuClicked()
        {
            Time.timeScale = 1f;
            _isPaused = false;
            _pausePanelRoot.SetActive(false);
            SfxManager.Instance.PlayButtonClick();
            GameFlowState.HasEnteredGame = false; // 回主菜单了，下次重新进这个场景要再显示一次主菜单
            SceneTransition.Instance.LoadScene(SceneManager.GetActiveScene().name);
        }

        // ── EventSystem（UGUI 按钮点击必须有这个才会响应输入）─────────────
        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem (Auto)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ── HUD ──────────────────────────────────────────────────
        private void BuildHud()
        {
            var canvasGO = new GameObject("HUDCanvas (Auto)");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // 左上角关卡名
            var labelGO = new GameObject("LevelLabel", typeof(RectTransform));
            labelGO.transform.SetParent(canvasGO.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = labelRT.anchorMax = new Vector2(0f, 1f);
            labelRT.pivot = new Vector2(0f, 1f);
            labelRT.anchoredPosition = new Vector2(24f, -20f);
            labelRT.sizeDelta = new Vector2(300f, 50f);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = levelLabel;
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 28;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.UpperLeft;
            labelText.raycastTarget = false;

            // 右上角齿轮按钮：点开是 继续/设置/重新开始/退出游戏 的菜单，设置在里面是单独一层
            // （没有美术资源，用程序生成的齿轮剪影当图标，不用文字）
            var settingsBtn = CreateButton(canvasGO.transform, "", new Vector2(56f, 56f), ButtonColor);
            var settingsRT = settingsBtn.GetComponent<RectTransform>();
            settingsRT.anchorMin = settingsRT.anchorMax = new Vector2(1f, 1f);
            settingsRT.pivot = new Vector2(1f, 1f);
            settingsRT.anchoredPosition = new Vector2(-24f, -20f);
            settingsBtn.onClick.AddListener(TogglePause);

            var gearIconGO = new GameObject("GearIcon", typeof(RectTransform));
            gearIconGO.transform.SetParent(settingsBtn.transform, false);
            var gearIconRT = gearIconGO.GetComponent<RectTransform>();
            gearIconRT.anchorMin = new Vector2(0.18f, 0.18f);
            gearIconRT.anchorMax = new Vector2(0.82f, 0.82f);
            gearIconRT.offsetMin = Vector2.zero;
            gearIconRT.offsetMax = Vector2.zero;
            var gearIconImg = gearIconGO.AddComponent<Image>();
            gearIconImg.sprite = CreateGearIconSprite(64);
            gearIconImg.color = Color.white;
            gearIconImg.raycastTarget = false;
        }

        // ── 暂停菜单 ─────────────────────────────────────────────
        private void BuildPauseMenu()
        {
            var canvasGO = new GameObject("PauseCanvas (Auto)");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // 盖在 HUD 之上，但在转场虹膜（1000）之下
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            _pausePanelRoot = canvasGO;
            _pauseCanvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _pauseCanvasGroup.alpha = 0f;

            // 半透明遮罩
            var overlayGO = new GameObject("Overlay", typeof(RectTransform));
            overlayGO.transform.SetParent(canvasGO.transform, false);
            var overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            var overlayImg = overlayGO.AddComponent<Image>();
            overlayImg.color = OverlayColor;

            // 面板
            var panelGO = new GameObject("Panel", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelRT = panelGO.GetComponent<RectTransform>();
            panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(420f, 440f);
            panelRT.anchoredPosition = Vector2.zero;
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = PanelColor;

            // 标题
            var titleGO = new GameObject("Title", typeof(RectTransform));
            titleGO.transform.SetParent(panelGO.transform, false);
            var titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.anchoredPosition = new Vector2(0f, -24f);
            titleRT.sizeDelta = new Vector2(0f, 60f);
            var titleText = titleGO.AddComponent<Text>();
            titleText.text = "已暂停";
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 34;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.raycastTarget = false;

            // 四个按钮：继续 / 设置 / 重新开始 / 退出游戏
            var resumeBtn = CreateButton(panelGO.transform, "继续", new Vector2(320f, 56f), ButtonColor);
            PositionInPanel(resumeBtn.GetComponent<RectTransform>(), 0);
            resumeBtn.onClick.AddListener(ClosePause);

            var settingsMenuBtn = CreateButton(panelGO.transform, LocalizationManager.Instance.Get("settings.title"), new Vector2(320f, 56f), ButtonColor);
            PositionInPanel(settingsMenuBtn.GetComponent<RectTransform>(), 1);
            settingsMenuBtn.onClick.AddListener(OpenSettings);
            _localizedTexts.Add((settingsMenuBtn.GetComponentInChildren<Text>(), "settings.title"));

            var restartBtn = CreateButton(panelGO.transform, "重新开始", new Vector2(320f, 56f), ButtonColor);
            PositionInPanel(restartBtn.GetComponent<RectTransform>(), 2);
            restartBtn.onClick.AddListener(OnRestartClicked);

            var exitBtn = CreateButton(panelGO.transform, "返回主菜单", new Vector2(320f, 56f), ButtonColor);
            PositionInPanel(exitBtn.GetComponent<RectTransform>(), 3);
            exitBtn.onClick.AddListener(OnReturnToMainMenuClicked);

            _pauseButtons.Add(resumeBtn);
            _pauseButtons.Add(settingsMenuBtn);
            _pauseButtons.Add(restartBtn);
            _pauseButtons.Add(exitBtn);

            canvasGO.SetActive(false);
        }

        /// <summary>把按钮竖直排布在面板里，index 0 在最上面</summary>
        private void PositionInPanel(RectTransform rt, int index)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 60f - index * 80f);
        }

        // ── 设置面板 UI ──────────────────────────────────────────
        private void BuildSettingsPanel()
        {
            var canvasGO = new GameObject("SettingsCanvas (Auto)");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 600; // 盖在暂停菜单（500）之上
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            _settingsPanelRoot = canvasGO;
            _settingsCanvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _settingsCanvasGroup.alpha = 0f;

            var overlayGO = new GameObject("Overlay", typeof(RectTransform));
            overlayGO.transform.SetParent(canvasGO.transform, false);
            var overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            overlayGO.AddComponent<Image>().color = OverlayColor;

            var panelGO = new GameObject("Panel", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelRT = panelGO.GetComponent<RectTransform>();
            panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(560f, 540f);
            panelGO.AddComponent<Image>().color = PanelColor;

            var titleGO = new GameObject("Title", typeof(RectTransform));
            titleGO.transform.SetParent(panelGO.transform, false);
            var titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.anchoredPosition = new Vector2(0f, -24f);
            titleRT.sizeDelta = new Vector2(0f, 50f);
            var titleText = titleGO.AddComponent<Text>();
            titleText.text = LocalizationManager.Instance.Get("settings.title");
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 30;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.raycastTarget = false;
            _localizedTexts.Add((titleText, "settings.title"));

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(panelGO.transform, false);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0.5f, 0.5f);
            contentRT.anchorMax = new Vector2(0.5f, 0.5f);
            contentRT.sizeDelta = new Vector2(460f, 440f);
            contentRT.anchoredPosition = new Vector2(0f, -20f);
            var layout = contentGO.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            var settings = SettingsManager.Instance;

            _masterDrag = AddSettingsSlider(contentGO.transform, "settings.master", settings.masterVolume, settings.SetMasterVolume);
            _musicDrag  = AddSettingsSlider(contentGO.transform, "settings.music", settings.musicVolume, settings.SetMusicVolume);
            _sfxDrag    = AddSettingsSlider(contentGO.transform, "settings.sfx", settings.sfxVolume, settings.SetSfxVolume);
            AddSettingsResolutionRow(contentGO.transform, settings);
            AddSettingsFullscreenRow(contentGO.transform, settings);
            AddSettingsLanguageRow(contentGO.transform);
            AddSettingsCloseRow(contentGO.transform);

            canvasGO.SetActive(false);
        }

        /// <summary>设置面板里的一行：自带一个可高亮的背景（十字键选中这一行时会被染黄）</summary>
        private RectTransform CreateSettingsRow(Transform parent, float height)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rt = row.GetComponent<RectTransform>();
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var bg = row.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            bg.raycastTarget = false; // 高亮用的背景，不能挡住同一行里拉杆/按钮的点击
            _settingsRowBg.Add(bg);
            return rt;
        }

        /// <summary>程序生成的齿轮剪影图标（没有美术资源，用几何算出来：外圈带齿，中间镂空）</summary>
        private static Sprite CreateGearIconSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = size * 0.5f;
            float outerRadius = r * 0.9f;
            float innerRadius = r * 0.62f; // 齿根半径
            float holeRadius  = r * 0.28f; // 中间的孔
            const int teeth = 8;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    float toothPhase = Mathf.Repeat(angle / (Mathf.PI * 2f) * teeth, 1f);
                    float edgeRadius = toothPhase < 0.5f ? outerRadius : innerRadius;

                    float alpha;
                    if (dist < holeRadius) alpha = 0f;
                    else if (dist < edgeRadius - 1f) alpha = 1f;
                    else alpha = Mathf.Clamp01(edgeRadius - dist + 1f); // 边缘轻微羽化，不那么锯齿

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private GameObject CreateSettingsText(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(10f, 0f);
            rt.offsetMax = new Vector2(-10f, 0f);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = align;
            t.raycastTarget = false; // 纯文字标签不需要挡点击
            return go;
        }

        private DebugSliderDrag AddSettingsSlider(Transform parent, string labelKey, float initial, System.Action<float> setter)
        {
            var row = CreateSettingsRow(parent, 56f);
            CreateLocalizedText(row.transform, labelKey, new Vector2(0f, 0.5f), new Vector2(1f, 1f), TextAnchor.LowerLeft, 17);

            var barGO = new GameObject("Bar", typeof(RectTransform));
            barGO.transform.SetParent(row.transform, false);
            var barRT = barGO.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0f, 0.05f);
            barRT.anchorMax = new Vector2(1f, 0.45f);
            barRT.offsetMin = Vector2.zero;
            barRT.offsetMax = Vector2.zero;
            var barImg = barGO.AddComponent<Image>();
            barImg.color = new Color(0.16f, 0.16f, 0.2f, 1f);

            var fillGO = new GameObject("Fill", typeof(RectTransform));
            fillGO.transform.SetParent(barGO.transform, false);
            var fillRT = fillGO.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = new Vector2(Mathf.Clamp01(initial), 1f);
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            fillGO.AddComponent<Image>().color = new Color(0.3f, 0.62f, 0.95f, 1f);

            var dragger = barGO.AddComponent<DebugSliderDrag>();
            dragger.Init(barRT, fillRT, 0f, 1f, setter);
            return dragger;
        }

        private void AddSettingsResolutionRow(Transform parent, SettingsManager settings)
        {
            var row = CreateSettingsRow(parent, 46f);
            CreateLocalizedText(row.transform, "settings.display", new Vector2(0f, 0f), new Vector2(0.35f, 1f), TextAnchor.MiddleLeft, 17);

            var prevBtn = CreateButton(row.transform, "<", new Vector2(40f, 34f), ButtonColor);
            var prevRT = prevBtn.GetComponent<RectTransform>();
            prevRT.anchorMin = prevRT.anchorMax = new Vector2(0.45f, 0.5f);

            _resolutionLabel = CreateSettingsText(row.transform, ResolutionLabel(settings), new Vector2(0.53f, 0f), new Vector2(0.8f, 1f), TextAnchor.MiddleCenter, 15).GetComponent<Text>();

            var nextBtn = CreateButton(row.transform, ">", new Vector2(40f, 34f), ButtonColor);
            var nextRT = nextBtn.GetComponent<RectTransform>();
            nextRT.anchorMin = nextRT.anchorMax = new Vector2(0.9f, 0.5f);

            prevBtn.onClick.AddListener(() =>
            {
                settings.SetResolutionIndex((settings.resolutionIndex - 1 + settings.CommonResolutions.Length) % settings.CommonResolutions.Length);
                _resolutionLabel.text = ResolutionLabel(settings);
            });
            nextBtn.onClick.AddListener(() =>
            {
                settings.SetResolutionIndex((settings.resolutionIndex + 1) % settings.CommonResolutions.Length);
                _resolutionLabel.text = ResolutionLabel(settings);
            });
        }

        private static string ResolutionLabel(SettingsManager settings)
        {
            var res = settings.CommonResolutions[settings.resolutionIndex];
            return $"{res.x} x {res.y}";
        }

        private void AddSettingsFullscreenRow(Transform parent, SettingsManager settings)
        {
            var row = CreateSettingsRow(parent, 40f);
            CreateLocalizedText(row.transform, "settings.fullscreen", new Vector2(0f, 0f), new Vector2(0.6f, 1f), TextAnchor.MiddleLeft, 17);

            var toggleGO = new GameObject("Toggle", typeof(RectTransform));
            toggleGO.transform.SetParent(row.transform, false);
            var toggleRT = toggleGO.GetComponent<RectTransform>();
            toggleRT.anchorMin = new Vector2(0.8f, 0.15f);
            toggleRT.anchorMax = new Vector2(0.95f, 0.85f);
            toggleRT.offsetMin = Vector2.zero;
            toggleRT.offsetMax = Vector2.zero;
            var bgImg = toggleGO.AddComponent<Image>();
            bgImg.color = new Color(0.16f, 0.16f, 0.2f, 1f);

            var checkGO = new GameObject("Check", typeof(RectTransform));
            checkGO.transform.SetParent(toggleGO.transform, false);
            var checkRT = checkGO.GetComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.15f, 0.15f);
            checkRT.anchorMax = new Vector2(0.85f, 0.85f);
            checkRT.offsetMin = Vector2.zero;
            checkRT.offsetMax = Vector2.zero;
            var checkImg = checkGO.AddComponent<Image>();
            checkImg.color = new Color(0.35f, 0.85f, 0.45f, 1f);

            _fullscreenToggle = toggleGO.AddComponent<Toggle>();
            _fullscreenToggle.targetGraphic = bgImg;
            _fullscreenToggle.graphic = checkImg;
            _fullscreenToggle.isOn = settings.fullscreen;
            _fullscreenToggle.onValueChanged.AddListener(v => settings.SetFullscreen(v));
        }

        private void AddSettingsLanguageRow(Transform parent)
        {
            var row = CreateSettingsRow(parent, 44f);
            CreateLocalizedText(row.transform, "settings.language", new Vector2(0f, 0f), new Vector2(0.35f, 1f), TextAnchor.MiddleLeft, 17);

            var prevBtn = CreateButton(row.transform, "<", new Vector2(40f, 34f), ButtonColor);
            var prevRT = prevBtn.GetComponent<RectTransform>();
            prevRT.anchorMin = prevRT.anchorMax = new Vector2(0.45f, 0.5f);

            var loc = LocalizationManager.Instance;
            _settingsLanguageLabel = CreateSettingsText(row.transform, loc.LanguageName(loc.CurrentLanguage), new Vector2(0.53f, 0f), new Vector2(0.8f, 1f), TextAnchor.MiddleCenter, 15).GetComponent<Text>();

            var nextBtn = CreateButton(row.transform, ">", new Vector2(40f, 34f), ButtonColor);
            var nextRT = nextBtn.GetComponent<RectTransform>();
            nextRT.anchorMin = nextRT.anchorMax = new Vector2(0.9f, 0.5f);

            prevBtn.onClick.AddListener(() => loc.CycleLanguage(-1));
            nextBtn.onClick.AddListener(() => loc.CycleLanguage(1));
        }

        private void AddSettingsCloseRow(Transform parent)
        {
            var row = CreateSettingsRow(parent, 56f);
            var closeBtn = CreateButton(row.transform, LocalizationManager.Instance.Get("settings.close"), new Vector2(200f, 46f), ButtonColor);
            var closeRT = closeBtn.GetComponent<RectTransform>();
            closeRT.anchorMin = closeRT.anchorMax = new Vector2(0.5f, 0.5f);
            closeBtn.onClick.AddListener(CloseSettings);
            _localizedTexts.Add((closeBtn.GetComponentInChildren<Text>(), "settings.close"));
        }

        // ── 通用按钮（纯色背景 + 文字 + hover/点击反馈，没有美术资源，先用纯色占位）──
        private Button CreateButton(Transform parent, string label, Vector2 size, Color color)
        {
            var go = new GameObject($"Button_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = color;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.fadeDuration     = 0.08f;
            btn.colors = colors;

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
            text.fontSize = 22;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            // 选中/悬停时外围包一圈黄线，跟主菜单那边同一套视觉
            var borders = new Image[4];
            const float borderThickness = 5f;
            borders[0] = CreateBorderBar(go.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), borderThickness);
            borders[1] = CreateBorderBar(go.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), borderThickness);
            borders[2] = CreateBorderBar(go.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), borderThickness);
            borders[3] = CreateBorderBar(go.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), borderThickness);
            _buttonBorders[btn] = borders;

            btn.onClick.AddListener(() => StartCoroutine(PunchScale(rt)));

            var trigger = go.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener(_ =>
            {
                SfxManager.Instance.PlayButtonHover();
                SetButtonFocused(btn, true);
            });
            trigger.triggers.Add(enterEntry);

            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener(_ => SetButtonFocused(btn, false));
            trigger.triggers.Add(exitEntry);

            return btn;
        }

        private Image CreateBorderBar(Transform parent, Vector2 anchorMin, Vector2 anchorMax, float thickness)
        {
            var go = new GameObject("BorderBar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(-thickness, -thickness);
            rt.offsetMax = new Vector2(thickness, thickness);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0.85f, 0.3f, 0f);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>统一的按钮"聚焦"视觉：黄色描边，鼠标悬停和手柄/键盘选中都走这一个方法</summary>
        private void SetButtonFocused(Button btn, bool focused)
        {
            btn.transform.localScale = Vector3.one * (focused ? 1.08f : 1f);
            if (_buttonBorders.TryGetValue(btn, out var borders))
            {
                Color c = focused ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(1f, 0.85f, 0.3f, 0f);
                foreach (var b in borders) b.color = c;
            }
        }

        private IEnumerator PunchScale(RectTransform rt)
        {
            const float duration = 0.15f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI); // 0→1→0
                rt.localScale = Vector3.one * (1f + k * 0.12f);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration, System.Action onComplete = null)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            cg.alpha = to;
            onComplete?.Invoke();
        }
    }
}
