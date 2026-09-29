using System.Collections;
using System.Collections.Generic;
using Resource.Scripts.Gyro;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Resource.Scripts
{
    /// <summary>
    /// HUD、暂停菜单和设置页使用统一的像素 UI。
    ///   HUD：左上角关卡名，右上角设置按钮。
    ///   暂停菜单：点设置按钮或按 ESC 打开，继续 / 重开本关 / 退出游戏，按钮有 hover/点击反馈，面板有淡入淡出。
    /// UI 层级改成了"场景里已经有就直接复用，没有才新建"，编辑期不进 Play 模式也能在
    /// Hierarchy 里看到/调整这些物体；事件监听器/协程这些没法存进场景文件的运行时绑定，
    /// 每次 Start() 都会重新走一遍。
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("关卡信息")]
        public string levelLabel = "Level 1";

        private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.6f);

        private GameObject  _pausePanelRoot;
        private CanvasGroup _pauseCanvasGroup;
        private bool _isPaused;
        private float _timeScaleBeforePause = 1f;
        private readonly List<Button> _pauseButtons = new List<Button>();
        private int _pauseSelectedIndex;

        private GameObject  _settingsPanelRoot;
        private CanvasGroup _settingsCanvasGroup;
        private bool _isSettingsOpen;
        private float _timeScaleBeforeSettings = 1f;
        private readonly List<Button> _settingsRows = new List<Button>();
        private int _settingsSelectedIndex;
        private DebugSliderDrag _masterDrag, _musicDrag, _sfxDrag;
        private TextMeshProUGUI _resolutionLabel;
        private Button _resolutionPrev, _resolutionNext;
        private Toggle _fullscreenToggle;

        private LocalizationManager _loc;
        private readonly List<(TextMeshProUGUI text, string key)> _localizedTexts = new List<(TextMeshProUGUI, string)>();
        private TextMeshProUGUI _settingsLanguageLabel;

        public bool IsPaused => _isPaused;
        public bool IsSettingsOpen => _isSettingsOpen;

        void Start()
        {
            _localizedTexts.Clear();
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
        private GameObject CreateLocalizedText(Transform parent, string key, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize, string goName = "Text")
        {
            var go = CreateSettingsText(parent, LocalizationManager.Instance.Get(key), anchorMin, anchorMax, align, fontSize, goName);
            _localizedTexts.Add((go.GetComponent<TextMeshProUGUI>(), key));
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
            if (GyroRuntime.ConsoleCapturesInput) return;
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
            if (GyroRuntime.ConsoleCapturesInput) return;
            if (_isPaused) ClosePause();
            else OpenPause();
        }

        private void OpenPause()
        {
            if (_isPaused || GyroRuntime.ConsoleCapturesInput) return;
            _timeScaleBeforePause = _isSettingsOpen ? _timeScaleBeforeSettings : Time.timeScale;
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
            if (!_isPaused || GyroRuntime.ConsoleCapturesInput) return;
            _isPaused = false;
            Time.timeScale = _isSettingsOpen ? 0f : _timeScaleBeforePause;
            var root = _pausePanelRoot;
            StartCoroutine(FadeCanvasGroup(_pauseCanvasGroup, 1f, 0f, 0.2f, () => root.SetActive(false)));
            SfxManager.Instance.PlayUIBack();
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
                    SfxManager.Instance.PlayUIMove();
                }
            }

            bool confirmPressed = (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (confirmPressed)
                _pauseButtons[_pauseSelectedIndex].onClick.Invoke();
        }

        private void OnRestartClicked()
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            Time.timeScale = 1f;
            _isPaused = false;
            _pausePanelRoot.SetActive(false);
            SfxManager.Instance.PlayButtonClick();
            SceneTransition.Instance.LoadScene(SceneManager.GetActiveScene().name);
        }

        // ── 设置面板（手柄 Start 键直接打开，也可以从暂停菜单里点进来；十字键/摇杆上下选行，左右改值）──
        private void OpenSettings()
        {
            if (_isSettingsOpen || GyroRuntime.ConsoleCapturesInput) return;
            // 保存底层玩法倍率，而不是嵌套暂停产生的 0；两层关闭顺序都能正确恢复。
            _timeScaleBeforeSettings = _isPaused ? _timeScaleBeforePause : Time.timeScale;
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
            if (!_isSettingsOpen || GyroRuntime.ConsoleCapturesInput) return;
            _isSettingsOpen = false;
            Time.timeScale = _isPaused ? 0f : _timeScaleBeforeSettings;
            var root = _settingsPanelRoot;
            StartCoroutine(FadeCanvasGroup(_settingsCanvasGroup, 1f, 0f, 0.2f, () => root.SetActive(false)));
            SfxManager.Instance.PlayUIBack();
        }

        private void UpdateSettingsRowHighlight()
        {
            for (int i = 0; i < _settingsRows.Count; i++)
                PixelUI.SetFocused(_settingsRows[i], i == _settingsSelectedIndex);
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
                int newIndex = Mathf.Clamp(_settingsSelectedIndex + (int)navAxis, 0, _settingsRows.Count - 1);
                if (newIndex != _settingsSelectedIndex)
                {
                    _settingsSelectedIndex = newIndex;
                    UpdateSettingsRowHighlight();
                    SfxManager.Instance.PlayUIMove();
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
            switch (_settingsSelectedIndex)
            {
                case 0:
                    if (adjustPressed) { _masterDrag.SetValue(_masterDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayUIMove(); }
                    break;
                case 1:
                    if (adjustPressed) { _musicDrag.SetValue(_musicDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayUIMove(); }
                    break;
                case 2:
                    if (adjustPressed) { _sfxDrag.SetValue(_sfxDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayUIMove(); }
                    break;
                case 3:
                    if (adjustPressed)
                    {
                        int dir = adjustAxis > 0f ? 1 : -1;
                        ChangeResolution(dir);
                        SfxManager.Instance.PlayUIMove();
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
                        SfxManager.Instance.PlayUIMove();
                    }
                    break;
                case 6:
                    if (confirmPressed) CloseSettings();
                    break;
            }
        }

        private void OnReturnToMainMenuClicked()
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            Time.timeScale = 1f;
            _isPaused = false;
            _pausePanelRoot.SetActive(false);
            SfxManager.Instance.PlayUIBack();
            GameFlowState.HasEnteredGame = false; // 主菜单现在是独立场景，回去就用不上这个标记了，保留只是不影响其它地方的判断
            SceneTransition.Instance.LoadScene("MainMenu");
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
            var canvas = EnsureCanvas("HUDCanvas (Auto)", 10);
            var label = EnsureRect(canvas.transform, "LevelLabel");
            label.anchorMin = label.anchorMax = new Vector2(0f, 1f);
            label.pivot = new Vector2(0f, 1f);
            label.anchoredPosition = new Vector2(24f, -20f);
            label.sizeDelta = new Vector2(300f, 40f);
            PixelUI.EnsureText(label.gameObject, 21, true, TextAnchor.UpperLeft, PixelUI.TextLight).text = levelLabel;

            var settingsBtn = CreateButton(canvas.transform, LocalizationManager.Instance.Get("settings.title"), new Vector2(150f, 54f), "Button_Settings");
            var settingsRT = settingsBtn.GetComponent<RectTransform>();
            settingsRT.anchorMin = settingsRT.anchorMax = new Vector2(1f, 1f);
            settingsRT.pivot = new Vector2(1f, 1f);
            settingsRT.anchoredPosition = new Vector2(-24f, -20f);
            settingsBtn.onClick.AddListener(TogglePause);
            _localizedTexts.Add((settingsBtn.GetComponentInChildren<TextMeshProUGUI>(), "settings.title"));
            RemoveDecoration(settingsBtn.transform, "GearIcon");
        }

        // ── 暂停菜单 ─────────────────────────────────────────────
        private void BuildPauseMenu()
        {
            var canvas = EnsureCanvas("PauseCanvas (Auto)", 500);
            _pausePanelRoot = canvas.gameObject;
            _pauseCanvasGroup = canvas.GetComponent<CanvasGroup>();
            if (_pauseCanvasGroup == null) _pauseCanvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            _pauseCanvasGroup.alpha = 0f;

            var overlay = EnsureRect(canvas.transform, "Overlay");
            Stretch(overlay);
            var overlayImage = EnsureImage(overlay);
            overlayImage.sprite = null;
            overlayImage.color = OverlayColor;

            var panel = CreatePanel(canvas.transform, new Vector2(420f, 440f));
            var title = CreateLocalizedText(panel, "pause.title", new Vector2(0f, 1f), Vector2.one, TextAnchor.MiddleCenter, 34, "Title");
            PositionTitle(title.GetComponent<RectTransform>(), 60f);

            var resumeBtn = CreateButton(panel, LocalizationManager.Instance.Get("pause.resume"), new Vector2(300f, 54f), "Button_Resume", "Button_继续");
            _localizedTexts.Add((resumeBtn.GetComponentInChildren<TextMeshProUGUI>(), "pause.resume"));
            PositionInPanel(resumeBtn.GetComponent<RectTransform>(), 0);
            resumeBtn.onClick.AddListener(ClosePause);

            var settingsBtn = CreateButton(panel, LocalizationManager.Instance.Get("settings.title"), new Vector2(300f, 54f), "Button_SettingsMenu");
            PositionInPanel(settingsBtn.GetComponent<RectTransform>(), 1);
            settingsBtn.onClick.AddListener(OpenSettings);
            _localizedTexts.Add((settingsBtn.GetComponentInChildren<TextMeshProUGUI>(), "settings.title"));

            var restartBtn = CreateButton(panel, LocalizationManager.Instance.Get("pause.restart"), new Vector2(300f, 54f), "Button_Restart", "Button_重新开始");
            _localizedTexts.Add((restartBtn.GetComponentInChildren<TextMeshProUGUI>(), "pause.restart"));
            PositionInPanel(restartBtn.GetComponent<RectTransform>(), 2);
            restartBtn.onClick.AddListener(OnRestartClicked);

            var exitBtn = CreateButton(panel, LocalizationManager.Instance.Get("pause.main_menu"), new Vector2(300f, 54f), "Button_MainMenu", "Button_返回主菜单");
            _localizedTexts.Add((exitBtn.GetComponentInChildren<TextMeshProUGUI>(), "pause.main_menu"));
            PositionInPanel(exitBtn.GetComponent<RectTransform>(), 3);
            exitBtn.onClick.AddListener(OnReturnToMainMenuClicked);

            _pauseButtons.Clear();
            _pauseButtons.Add(resumeBtn);
            _pauseButtons.Add(settingsBtn);
            _pauseButtons.Add(restartBtn);
            _pauseButtons.Add(exitBtn);
            canvas.gameObject.SetActive(false);
        }

        private void PositionInPanel(RectTransform rt, int index)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 60f - index * 72f);
        }

        // ── 设置面板 UI ──────────────────────────────────────────
        private void BuildSettingsPanel()
        {
            var canvas = EnsureCanvas("SettingsCanvas (Auto)", 600);
            _settingsPanelRoot = canvas.gameObject;
            _settingsCanvasGroup = canvas.GetComponent<CanvasGroup>();
            if (_settingsCanvasGroup == null) _settingsCanvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            _settingsCanvasGroup.alpha = 0f;
            RemoveDecoration(canvas.transform, "Overlay");
            PixelUI.Backdrop(canvas.transform);
            canvas.transform.Find("PixelBackground").GetComponent<Graphic>().raycastTarget = true;

            var panel = CreatePanel(canvas.transform, new Vector2(520f, 680f));
            var title = CreateLocalizedText(panel, "settings.title", new Vector2(0f, 1f), Vector2.one, TextAnchor.MiddleCenter, 36, "Title");
            PositionTitle(title.GetComponent<RectTransform>(), 56f);

            var content = EnsureRect(panel, "Content");
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(484f, 560f);
            content.anchoredPosition = new Vector2(0f, -90f);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset();
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var settings = SettingsManager.Instance;
            _settingsRows.Clear();
            AddSettingsGroup(content, "Group_Audio", "settings.group.audio");
            _masterDrag = AddSettingsSlider(content, "Row_Master", "settings.master", settings.masterVolume, settings.SetMasterVolume);
            _musicDrag = AddSettingsSlider(content, "Row_Music", "settings.music", settings.musicVolume, settings.SetMusicVolume);
            _sfxDrag = AddSettingsSlider(content, "Row_Sfx", "settings.sfx", settings.sfxVolume, settings.SetSfxVolume);
            AddSettingsGroup(content, "Group_Display", "settings.group.display");
            AddSettingsResolutionRow(content, settings);
            AddSettingsFullscreenRow(content, settings);
            AddSettingsGroup(content, "Group_Language", "settings.group.language");
            AddSettingsLanguageRow(content);
            AddSettingsCloseRow(content);
            canvas.gameObject.SetActive(false);
        }

        private void AddSettingsGroup(Transform parent, string name, string key)
        {
            var group = EnsureRect(parent, name);
            group.SetAsLastSibling();
            SetRowHeight(group, 26f);
            var label = CreateLocalizedText(group, key, Vector2.zero, Vector2.one, TextAnchor.UpperLeft, 18);
            label.GetComponent<TextMeshProUGUI>().color = PixelUI.TextLight;
            var line = EnsureRect(group, "Divider");
            line.anchorMin = Vector2.zero;
            line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f);
            line.anchoredPosition = Vector2.zero;
            line.sizeDelta = new Vector2(0f, 3f);
            var image = EnsureImage(line);
            image.sprite = PixelUI.Theme.dividerDot;
            image.type = Image.Type.Tiled;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private RectTransform CreateSettingsRow(Transform parent, string name, float height)
        {
            var row = EnsureRect(parent, name);
            row.SetAsLastSibling();
            SetRowHeight(row, height);
            var button = row.GetComponent<Button>();
            if (button == null) button = row.gameObject.AddComponent<Button>();
            var background = EnsureImage(row);
            background.raycastTarget = true;
            button.targetGraphic = background;
            PixelUI.StyleButton(button);
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            int index = _settingsRows.Count;
            _settingsRows.Add(button);
            var trigger = row.GetComponent<EventTrigger>();
            if (trigger == null) trigger = row.gameObject.AddComponent<EventTrigger>();
            trigger.triggers.Clear();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                _settingsSelectedIndex = index;
                UpdateSettingsRowHighlight();
            });
            trigger.triggers.Add(enter);
            return row;
        }

        private GameObject CreateSettingsText(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize, string goName = "Text")
        {
            var rt = EnsureRect(parent, goName);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(14f, 0f);
            rt.offsetMax = new Vector2(-14f, 0f);
            var label = PixelUI.EnsureText(rt.gameObject, fontSize, fontSize >= 21, align, fontSize > 24 ? PixelUI.TextLight : PixelUI.TextDark);
            label.text = text;
            return rt.gameObject;
        }

        private DebugSliderDrag AddSettingsSlider(Transform parent, string rowName, string labelKey, float initial, System.Action<float> setter)
        {
            var row = CreateSettingsRow(parent, rowName, 50f);
            CreateLocalizedText(row, labelKey, Vector2.zero, new Vector2(0.47f, 1f), TextAnchor.MiddleLeft, 21);
            var valueLabel = CreateSettingsText(row, Mathf.RoundToInt(initial * 100f).ToString(), new Vector2(0.85f, 0f), Vector2.one, TextAnchor.MiddleCenter, 18, "Text_Value").GetComponent<TextMeshProUGUI>();
            var bar = EnsureRect(row, "Bar");
            bar.anchorMin = new Vector2(0.47f, 0.5f);
            bar.anchorMax = new Vector2(0.85f, 0.5f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.offsetMin = new Vector2(0f, -12f);
            bar.offsetMax = new Vector2(0f, 12f);
            var barImage = EnsureImage(bar);
            barImage.sprite = PixelUI.Theme.barFrame;
            barImage.type = Image.Type.Sliced;
            barImage.color = Color.white;

            var fill = bar.Find("Fill") as RectTransform;
            var track = EnsureRect(bar, "Track");
            Stretch(track);
            track.offsetMin = new Vector2(6f, 6f);
            track.offsetMax = new Vector2(-6f, -6f);
            if (fill == null) fill = EnsureRect(track, "Fill");
            else fill.SetParent(track, false);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(initial), 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            var fillImage = EnsureImage(fill);
            fillImage.sprite = PixelUI.Theme.barFill;
            fillImage.type = Image.Type.Simple;
            fillImage.color = PixelUI.Accent;
            fillImage.raycastTarget = false;

            var dragger = bar.GetComponent<DebugSliderDrag>();
            if (dragger == null) dragger = bar.gameObject.AddComponent<DebugSliderDrag>();
            dragger.Init(bar, fill, 0f, 1f, value =>
            {
                setter(value);
                valueLabel.text = Mathf.RoundToInt(value * 100f).ToString();
            });
            return dragger;
        }

        private void AddSettingsResolutionRow(Transform parent, SettingsManager settings)
        {
            var row = CreateSettingsRow(parent, "Row_Resolution", 50f);
            CreateLocalizedText(row, "settings.display", Vector2.zero, new Vector2(0.5f, 1f), TextAnchor.MiddleLeft, 21);
            _resolutionPrev = CreateArrow(row, false);
            _resolutionLabel = CreateSettingsText(row, ResolutionLabel(settings), new Vector2(0.58f, 0f), new Vector2(0.92f, 1f), TextAnchor.MiddleCenter, 18, "Text_Value").GetComponent<TextMeshProUGUI>();
            _resolutionNext = CreateArrow(row, true);
            _resolutionPrev.onClick.AddListener(() => ChangeResolution(-1));
            _resolutionNext.onClick.AddListener(() => ChangeResolution(1));
            RefreshResolution();
        }

        private void ChangeResolution(int direction)
        {
            var settings = SettingsManager.Instance;
            int index = Mathf.Clamp(settings.resolutionIndex + direction, 0, settings.CommonResolutions.Length - 1);
            if (index != settings.resolutionIndex) settings.SetResolutionIndex(index);
            RefreshResolution();
        }

        private void RefreshResolution()
        {
            var settings = SettingsManager.Instance;
            _resolutionLabel.text = ResolutionLabel(settings);
            _resolutionPrev.interactable = settings.resolutionIndex > 0;
            _resolutionNext.interactable = settings.resolutionIndex < settings.CommonResolutions.Length - 1;
        }

        private static string ResolutionLabel(SettingsManager settings)
        {
            var res = settings.CommonResolutions[settings.resolutionIndex];
            return $"{res.x} x {res.y}";
        }

        private void AddSettingsFullscreenRow(Transform parent, SettingsManager settings)
        {
            var row = CreateSettingsRow(parent, "Row_Fullscreen", 50f);
            CreateLocalizedText(row, "settings.fullscreen", Vector2.zero, new Vector2(0.8f, 1f), TextAnchor.MiddleLeft, 21);
            var toggleRT = EnsureRect(row, "Toggle");
            toggleRT.anchorMin = toggleRT.anchorMax = new Vector2(1f, 0.5f);
            toggleRT.pivot = new Vector2(1f, 0.5f);
            toggleRT.anchoredPosition = new Vector2(-18f, 0f);
            toggleRT.sizeDelta = new Vector2(30f, 30f);
            var check = EnsureRect(toggleRT, "Check");
            Stretch(check);
            _fullscreenToggle = toggleRT.GetComponent<Toggle>();
            if (_fullscreenToggle == null) _fullscreenToggle = toggleRT.gameObject.AddComponent<Toggle>();
            _fullscreenToggle.targetGraphic = EnsureImage(toggleRT);
            _fullscreenToggle.graphic = EnsureImage(check);
            _fullscreenToggle.onValueChanged.RemoveAllListeners();
            _fullscreenToggle.isOn = settings.fullscreen;
            PixelUI.StyleToggle(_fullscreenToggle);
            _fullscreenToggle.onValueChanged.AddListener(settings.SetFullscreen);
        }

        private void AddSettingsLanguageRow(Transform parent)
        {
            var row = CreateSettingsRow(parent, "Row_Language", 50f);
            CreateLocalizedText(row, "settings.language", Vector2.zero, new Vector2(0.5f, 1f), TextAnchor.MiddleLeft, 21);
            var previous = CreateArrow(row, false);
            var loc = LocalizationManager.Instance;
            _settingsLanguageLabel = CreateSettingsText(row, loc.LanguageName(loc.CurrentLanguage), new Vector2(0.58f, 0f), new Vector2(0.92f, 1f), TextAnchor.MiddleCenter, 18, "Text_Value").GetComponent<TextMeshProUGUI>();
            var next = CreateArrow(row, true);
            previous.onClick.AddListener(() => loc.CycleLanguage(-1));
            next.onClick.AddListener(() => loc.CycleLanguage(1));
        }

        private void AddSettingsCloseRow(Transform parent)
        {
            var row = CreateSettingsRow(parent, "Row_Close", 54f);
            row.GetComponent<Image>().enabled = false;
            var close = CreateButton(row, LocalizationManager.Instance.Get("settings.close"), new Vector2(300f, 54f), "Button_Close");
            var rt = close.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            close.onClick.AddListener(CloseSettings);
            _settingsRows[_settingsRows.Count - 1] = close;
            _localizedTexts.Add((close.GetComponentInChildren<TextMeshProUGUI>(), "settings.close"));
        }

        private Button CreateArrow(Transform parent, bool right)
        {
            var button = CreateButton(parent, "", new Vector2(30f, 30f), right ? "Button_Next" : "Button_Prev");
            var rt = button.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(right ? 0.95f : 0.56f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            PixelUI.StyleArrow(button, right);
            return button;
        }

        // 保留场景物体名称和事件入口，对已有 UI 同样应用字体、尺寸和贴图。
        private Button CreateButton(Transform parent, string label, Vector2 size, string goName = null, string legacyName = null)
        {
            if (legacyName != null && parent.Find(goName) == null)
            {
                var legacy = parent.Find(legacyName);
                if (legacy != null) legacy.name = goName;
            }
            var rt = EnsureRect(parent, goName ?? $"Button_{label}");
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
            var button = rt.GetComponent<Button>();
            if (button == null) button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = EnsureImage(rt);
            PixelUI.StyleButton(button);
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            var text = EnsureRect(rt, "Label");
            Stretch(text);
            PixelUI.EnsureText(text.gameObject, 24, true, TextAnchor.MiddleCenter, PixelUI.TextDark).text = label;
            RemoveDecoration(rt, "Border_Top");
            RemoveDecoration(rt, "Border_Bottom");
            RemoveDecoration(rt, "Border_Left");
            RemoveDecoration(rt, "Border_Right");
            button.onClick.RemoveAllListeners();

            var trigger = rt.GetComponent<EventTrigger>();
            if (trigger == null) trigger = rt.gameObject.AddComponent<EventTrigger>();
            trigger.triggers.Clear();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                SfxManager.Instance.PlayButtonHover();
                SetButtonFocused(button, true);
            });
            trigger.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => SetButtonFocused(button, false));
            trigger.triggers.Add(exit);
            return button;
        }

        private void SetButtonFocused(Button button, bool focused) => PixelUI.SetFocused(button, focused);

        private Canvas EnsureCanvas(string name, int order)
        {
            var rt = EnsureRect(transform, name);
            var canvas = rt.GetComponent<Canvas>();
            if (canvas == null) canvas = rt.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            PixelUI.ConfigureCanvas(canvas);
            if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private RectTransform CreatePanel(Transform parent, Vector2 size)
        {
            var panel = EnsureRect(parent, "Panel");
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = size;
            var image = EnsureImage(panel);
            image.sprite = PixelUI.Theme.panel;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            return panel;
        }

        private static RectTransform EnsureRect(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing.GetComponent<RectTransform>();
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image EnsureImage(RectTransform rt)
        {
            var image = rt.GetComponent<Image>();
            return image != null ? image : rt.gameObject.AddComponent<Image>();
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void SetRowHeight(RectTransform rt, float height)
        {
            var element = rt.GetComponent<LayoutElement>();
            if (element == null) element = rt.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = height;
            element.flexibleHeight = 0f;
            rt.sizeDelta = new Vector2(0f, height);
        }

        private static void PositionTitle(RectTransform rt, float height)
        {
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -20f);
            rt.sizeDelta = new Vector2(0f, height);
        }

        private static void RemoveDecoration(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child == null) return;
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
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
