using System;
using System.Collections;
using System.Collections.Generic;
using Resource.Scripts.Gyro;
using Resource.Scripts.UI.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Resource.Scripts
{
    [Serializable]
    public class LevelEntry
    {
        public string displayName = "Level 1";
        [Tooltip("要跳转到的场景名（Build Settings 里的场景名，不是路径）。锁住的占位卡不能进入，可以留空。")]
        public string sceneName = "";
        public bool unlocked = true;
        [Tooltip("仅控制选关卡片的通关标记；不负责解锁或写入存档。")]
        public bool cleared;
    }

    /// <summary>
    /// 主菜单总控：初始化、设置面板、语言刷新与关卡转场。
    /// 标题和选关由 MenuPresentation 构建；Options 保留原有实现。
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("关卡列表（由 LevelOrder.UpdateLevelSelection 写入）")]
        public List<LevelEntry> levels = new List<LevelEntry>
        {
            new LevelEntry { displayName = "Level 1", sceneName = "Stage1", unlocked = true },
            new LevelEntry { displayName = "Level 2", sceneName = "", unlocked = false },
            new LevelEntry { displayName = "Level 3", sceneName = "", unlocked = false }
        };

        private MenuPresentation _menuPresentation;
        private RectTransform _optionsRoot;
        private CanvasGroup   _optionsGroup;
        private CanvasGroup _menuGroup;
        private SceneTransition _sceneTransition;
        private bool MenuInputBlocked => GyroRuntime.ConsoleCapturesInput
            || (_sceneTransition != null && _sceneTransition.IsTransitioning);

        private bool _navLocked;

        private LocalizationManager _loc;
        private readonly List<(TextMeshProUGUI text, string key)> _localizedTexts = new List<(TextMeshProUGUI, string)>();
        private TextMeshProUGUI _optionsLanguageLabel;

        private readonly List<Button> _optionsRows = new List<Button>();
        private int _optionsSelectedIndex;
        private DebugSliderDrag _optionsMasterDrag, _optionsMusicDrag, _optionsSfxDrag;
        private TextMeshProUGUI _optionsResolutionLabel;
        private Button _optionsResolutionPrev, _optionsResolutionNext;
        private Toggle _optionsFullscreenToggle;

        void Start()
        {
            // 玩家已经在主菜单选过一次关卡了——不管是"重新开始"重载同一个场景，
            // 还是以后有多关卡时切到下一关，都不该再弹一次主菜单，直接进游戏。
            if (GameFlowState.HasEnteredGame)
            {
                Destroy(gameObject);
                return;
            }

            EnsureEventSystem();
            FreezeGameplay();
            BuildUI();
            _sceneTransition = SceneTransition.Instance;
            _sceneTransition.TransitionStateChanged += SetTransitionInputLocked;
            _sceneTransition.PlayStartupOpening();
            SetTransitionInputLocked(_sceneTransition.IsTransitioning);
            _loc = LocalizationManager.Instance;
            _loc.OnLanguageChanged += RefreshLocalizedTexts;
            StartCoroutine(PauseAmbientAudioNextFrame());
        }

        /// <summary>晚一帧再暂停场景环境音（比如蜡烛噼啪声），保证蜡烛等物体自己的 Start() 已经把
        /// AudioSource 建好并开始播放，不然这一帧还没找到那个音源就暂停不到</summary>
        private IEnumerator PauseAmbientAudioNextFrame()
        {
            yield return null;
            SetAmbientAudioPaused(true);
        }

        /// <summary>菜单盖在游戏画面上的时候，场景里已经在放的环境音也要一起停掉，不然还没点
        /// Start Game 就能听到游戏里的声音。SfxManager 自己那几个音源（菜单按钮音效用的）不受影响。</summary>
        private void SetAmbientAudioPaused(bool paused)
        {
            foreach (var src in FindObjectsOfType<AudioSource>())
            {
                if (src.GetComponent<SfxManager>() != null) continue;
                if (paused) src.Pause();
                else src.UnPause();
            }
        }

        void OnDestroy()
        {
            if (_sceneTransition != null) _sceneTransition.TransitionStateChanged -= SetTransitionInputLocked;
            if (_loc != null) _loc.OnLanguageChanged -= RefreshLocalizedTexts;
        }

        private void SetTransitionInputLocked(bool locked)
        {
            if (_menuGroup != null)
            {
                _menuGroup.interactable = !locked;
                _menuGroup.blocksRaycasts = !locked;
            }
            // 自定义滑条不继承 Selectable，禁用它们也阻止已经开始的拖动。
            if (_optionsMasterDrag != null) _optionsMasterDrag.enabled = !locked;
            if (_optionsMusicDrag != null) _optionsMusicDrag.enabled = !locked;
            if (_optionsSfxDrag != null) _optionsSfxDrag.enabled = !locked;
        }

        /// <summary>用 Localization 表里的 key 建文字，并且登记下来，语言切换时统一刷新</summary>
        private GameObject CreateLocalizedText(Transform parent, string key, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize, string goName = "Text")
        {
            var go = CreateText(parent, LocalizationManager.Instance.Get(key), anchorMin, anchorMax, align, fontSize, goName);
            _localizedTexts.Add((go.GetComponent<TextMeshProUGUI>(), key));
            return go;
        }

        private void RefreshLocalizedTexts()
        {
            foreach (var (text, key) in _localizedTexts)
                if (text != null) text.text = LocalizationManager.Instance.Get(key);
            if (_optionsLanguageLabel != null)
                _optionsLanguageLabel.text = LocalizationManager.Instance.LanguageName(LocalizationManager.Instance.CurrentLanguage);
            if (_menuPresentation != null) _menuPresentation.RefreshLocalizedTexts();
        }

        void Update()
        {
            if (MenuInputBlocked) return;
            if (_optionsGroup != null && _optionsGroup.gameObject.activeSelf)
                HandleOptionsInput();
        }

        // ── 冻结/恢复游戏 ────────────────────────────────────────
        private void FreezeGameplay()
        {
            var player = FindObjectOfType<PlayerController>();
            if (player != null) player.enabled = false;
            var rotator = FindObjectOfType<WorldRotator>();
            if (rotator != null) rotator.enabled = false;
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem (Auto)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ── 搭建整体结构 ─────────────────────────────────────────
        private void BuildUI()
        {
            _localizedTexts.Clear();
            var canvasRT = GetRect(transform, "MainMenuCanvas (Auto)");
            _menuGroup = GetGroup(canvasRT);
            var canvas = canvasRT.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasRT.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;
            PixelUI.ConfigureCanvas(canvas);
            if (canvasRT.GetComponent<GraphicRaycaster>() == null)
                canvasRT.gameObject.AddComponent<GraphicRaycaster>();

            foreach (string name in new[] { "TitlePanel", "LevelSelectPanel", "PixelBackground", "Backdrop", "Background", "TorchGlow0", "TorchGlow1", "Fog0", "Fog1", "Fog2" })
                RemoveChild(canvasRT, name);
            BuildOptionsPanel(canvasRT);

            var menu = new GameObject("MenuPresentation", typeof(RectTransform));
            _menuPresentation = menu.AddComponent<MenuPresentation>();
            _menuPresentation.Build(transform, levels, OnOptionsClicked, LoadLevel, () => MenuInputBlocked);
            _menuPresentation.ShowTitle(true);
        }

        private void OnOptionsClicked()
        {
            if (MenuInputBlocked || _navLocked) return;
            _navLocked = true;
            _menuPresentation.gameObject.SetActive(false);
            StartCoroutine(ShowOptions());
        }

        private IEnumerator ShowOptions()
        {
            yield return SlidePanel(_optionsRoot, _optionsGroup, new Vector2(400f, 0f), Vector2.zero, 0f, 1f, 0.5f, false);
            UpdateOptionsRowHighlight();
            _navLocked = false;
        }

        private void BackToTitle()
        {
            if (MenuInputBlocked || _navLocked) return;
            _navLocked = true;
            SfxManager.Instance.PlayUIBack();
            StartCoroutine(ReturnFromOptions());
        }

        private IEnumerator ReturnFromOptions()
        {
            yield return SlidePanel(_optionsRoot, _optionsGroup, Vector2.zero, new Vector2(400f, 0f), 1f, 0f, 0.5f, true);
            _menuPresentation.gameObject.SetActive(true);
            RefreshLocalizedTexts();
            _menuPresentation.ShowTitle(false);
            _navLocked = false;
        }

        private void LoadLevel(string sceneName)
        {
            if (MenuInputBlocked || _navLocked) return;
            _navLocked = true;
            GameFlowState.HasEnteredGame = true;
            SceneTransition.Instance.LoadScene(sceneName);
        }

        private IEnumerator SlidePanel(RectTransform rt, CanvasGroup cg, Vector2 fromOffset, Vector2 toOffset,
                                        float fromAlpha, float toAlpha, float duration, bool deactivateAtEnd)
        {
            rt.gameObject.SetActive(true);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = EaseOutCubic(Mathf.Clamp01(t / duration));
                rt.anchoredPosition = Vector2.Lerp(fromOffset, toOffset, k);
                cg.alpha = Mathf.Lerp(fromAlpha, toAlpha, k);
                yield return null;
            }
            rt.anchoredPosition = toOffset;
            cg.alpha = toAlpha;
            if (deactivateAtEnd) rt.gameObject.SetActive(false);
        }

        private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

        private void UpdateOptionsRowHighlight()
        {
            for (int i = 0; i < _optionsRows.Count; i++)
                PixelUI.SetFocused(_optionsRows[i], i == _optionsSelectedIndex);
        }

        /// <summary>Options 面板的十字键/摇杆/键盘导航：上下选行，左右改值，跟关卡选择/游戏内设置面板同一套写法</summary>
        private void HandleOptionsInput()
        {
            if (_navLocked) return;

            var gamepad = Gamepad.current;

            bool backPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);
            if (backPressed) { BackToTitle(); return; }

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
            if (navAxis != 0f && _optionsRows.Count > 0)
            {
                int newIndex = Mathf.Clamp(_optionsSelectedIndex + (int)navAxis, 0, _optionsRows.Count - 1);
                if (newIndex != _optionsSelectedIndex)
                {
                    _optionsSelectedIndex = newIndex;
                    UpdateOptionsRowHighlight();
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

            ApplyOptionsRowInput(adjustAxis, adjustPressed, confirmPressed);
        }

        private void ApplyOptionsRowInput(float adjustAxis, bool adjustPressed, bool confirmPressed)
        {
            var settings = SettingsManager.Instance;
            switch (_optionsSelectedIndex)
            {
                case 0:
                    if (adjustPressed) { _optionsMasterDrag.SetValue(_optionsMasterDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayUIMove(); }
                    break;
                case 1:
                    if (adjustPressed) { _optionsMusicDrag.SetValue(_optionsMusicDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayUIMove(); }
                    break;
                case 2:
                    if (adjustPressed) { _optionsSfxDrag.SetValue(_optionsSfxDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayUIMove(); }
                    break;
                case 3:
                    if (adjustPressed)
                    {
                        int dir = adjustAxis > 0f ? 1 : -1;
                        settings.SetResolutionIndex(Mathf.Clamp(settings.resolutionIndex + dir, 0, settings.CommonResolutions.Length - 1));
                        RefreshResolutionRow(settings);
                        SfxManager.Instance.PlayUIMove();
                    }
                    break;
                case 4:
                    if (adjustPressed || confirmPressed)
                    {
                        _optionsFullscreenToggle.isOn = !_optionsFullscreenToggle.isOn;
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
            }
        }

        // ── 设置面板 ─────────────────────────────────────────────
        private void BuildOptionsPanel(Transform parent)
        {
            _optionsRoot = GetRect(parent, "OptionsPanel");
            Stretch(_optionsRoot);
            _optionsGroup = GetGroup(_optionsRoot);
            _optionsGroup.alpha = 0f;
            _optionsRoot.gameObject.SetActive(false);

            var panelRT = GetRect(_optionsRoot, "PixelPanel");
            Place(panelRT, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540f, 720f));
            panelRT.SetAsFirstSibling();
            var panelImage = GetImage(panelRT);
            panelImage.sprite = PixelUI.Theme.panel;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = Color.white;
            panelImage.raycastTarget = false;

            var titleGO = CreateLocalizedText(_optionsRoot, "settings.title", new Vector2(0f, 0.86f), new Vector2(1f, 0.97f), TextAnchor.MiddleCenter, 36, "Text_Title");
            PixelUI.EnsureText(titleGO, 36, true, TextAnchor.MiddleCenter, PixelUI.TextLight);
            var backBtn = CreateButton(_optionsRoot, LocalizationManager.Instance.Get("settings.back"), new Vector2(240f, 54f), "Button_Back");
            Place(backBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(240f, 54f));
            backBtn.onClick.AddListener(() => BackToTitle());
            _localizedTexts.Add((backBtn.GetComponentInChildren<TextMeshProUGUI>(true), "settings.back"));

            var contentRT = GetRect(_optionsRoot, "Content");
            Place(contentRT, new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(492f, 508f));
            var layout = contentRT.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = contentRT.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var settings = SettingsManager.Instance;
            _optionsRows.Clear();
            AddOptionsGroup(contentRT, "Group_Audio", "settings.group.audio");
            _optionsMasterDrag = AddOptionsSlider(contentRT, "Row_Master", "settings.master", settings.masterVolume, settings.SetMasterVolume);
            _optionsMusicDrag = AddOptionsSlider(contentRT, "Row_Music", "settings.music", settings.musicVolume, settings.SetMusicVolume);
            _optionsSfxDrag = AddOptionsSlider(contentRT, "Row_Sfx", "settings.sfx", settings.sfxVolume, settings.SetSfxVolume);
            AddOptionsGroup(contentRT, "Group_Display", "settings.group.display");
            AddOptionsResolutionRow(contentRT, settings);
            AddOptionsToggleRow(contentRT, "Row_Fullscreen", "settings.fullscreen", settings.fullscreen, settings.SetFullscreen);
            AddOptionsGroup(contentRT, "Group_Language", "settings.group.language");
            AddOptionsLanguageRow(contentRT);
            _optionsSelectedIndex = 0;
            UpdateOptionsRowHighlight();
        }

        private void AddOptionsGroup(Transform parent, string name, string key)
        {
            var group = GetRect(parent, name);
            group.SetAsLastSibling();
            SetLayoutHeight(group, 32f);
            var label = CreateLocalizedText(group, key, new Vector2(0.02f, 0.16f), new Vector2(0.98f, 1f), TextAnchor.MiddleLeft, 18);
            PixelUI.EnsureText(label, 18, false, TextAnchor.MiddleLeft, PixelUI.TextLight);
            var lineRT = GetRect(group, "Divider");
            lineRT.anchorMin = new Vector2(0.02f, 0f);
            lineRT.anchorMax = new Vector2(0.98f, 0f);
            lineRT.pivot = new Vector2(0.5f, 0f);
            lineRT.offsetMin = Vector2.zero;
            lineRT.offsetMax = new Vector2(0f, 3f);
            var line = GetImage(lineRT);
            line.sprite = PixelUI.Theme.dividerDot;
            line.type = Image.Type.Tiled;
            line.color = Color.white;
            line.raycastTarget = false;
        }

        private RectTransform CreateOptionsRow(Transform parent, string rowName, float height)
        {
            var row = GetRect(parent, rowName);
            row.SetAsLastSibling();
            SetLayoutHeight(row, height);
            var button = row.GetComponent<Button>();
            if (button == null) button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = GetImage(row);
            button.targetGraphic.raycastTarget = true;
            PixelUI.StyleButton(button);
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            int rowIndex = _optionsRows.Count;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                _optionsSelectedIndex = rowIndex;
                UpdateOptionsRowHighlight();
            });
            _optionsRows.Add(button);
            return row;
        }

        private void AddOptionsLabel(RectTransform row, string key)
        {
            var label = CreateLocalizedText(row, key, new Vector2(0.04f, 0.08f), new Vector2(0.52f, 0.95f), TextAnchor.MiddleLeft, 21);
            PixelUI.EnsureText(label, 21, true, TextAnchor.MiddleLeft, PixelUI.TextDark);
        }

        private DebugSliderDrag AddOptionsSlider(Transform parent, string rowName, string labelKey, float initial, Action<float> setter)
        {
            var row = CreateOptionsRow(parent, rowName, 54f);
            AddOptionsLabel(row, labelKey);
            var barRT = GetRect(row, "Bar");
            barRT.anchorMin = new Vector2(0.57f, 0.31f);
            barRT.anchorMax = new Vector2(0.85f, 0.67f);
            barRT.offsetMin = barRT.offsetMax = Vector2.zero;
            var barImage = GetImage(barRT);
            barImage.sprite = PixelUI.Theme.barFrame;
            barImage.type = Image.Type.Sliced;
            barImage.color = Color.white;

            var fillArea = GetRect(barRT, "FillArea");
            Stretch(fillArea);
            fillArea.offsetMin = new Vector2(3f, 3f);
            fillArea.offsetMax = new Vector2(-3f, -3f);
            var existingFill = barRT.Find("Fill");
            if (existingFill != null) existingFill.SetParent(fillArea, false);
            var fillRT = GetRect(fillArea, "Fill");
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = new Vector2(Mathf.Clamp01(initial), 1f);
            fillRT.offsetMin = fillRT.offsetMax = Vector2.zero;
            var fillImage = GetImage(fillRT);
            fillImage.sprite = PixelUI.Theme.barFill;
            fillImage.type = Image.Type.Sliced;
            fillImage.color = PixelUI.Accent;
            fillImage.raycastTarget = false;

            var valueGO = CreateText(row, Mathf.RoundToInt(initial * 100f).ToString(), new Vector2(0.86f, 0f), new Vector2(0.98f, 1f), TextAnchor.MiddleCenter, 18, "Text_Value");
            var valueText = PixelUI.EnsureText(valueGO, 18, false, TextAnchor.MiddleCenter, PixelUI.TextDark);
            var dragger = barRT.GetComponent<DebugSliderDrag>();
            if (dragger == null) dragger = barRT.gameObject.AddComponent<DebugSliderDrag>();
            fillImage.enabled = initial > 0f;
            dragger.Init(barRT, fillRT, 0f, 1f, value =>
            {
                setter(value);
                fillImage.enabled = value > 0f;
                valueText.text = Mathf.RoundToInt(value * 100f).ToString();
            });
            return dragger;
        }

        private void AddOptionsResolutionRow(Transform parent, SettingsManager settings)
        {
            var row = CreateOptionsRow(parent, "Row_Resolution", 54f);
            AddOptionsLabel(row, "settings.display");
            _optionsResolutionPrev = CreateArrow(row, false);
            _optionsResolutionNext = CreateArrow(row, true);
            var labelGO = CreateText(row, ResolutionLabel(settings), new Vector2(0.62f, 0f), new Vector2(0.90f, 1f), TextAnchor.MiddleCenter, 18, "Text_Value");
            _optionsResolutionLabel = PixelUI.EnsureText(labelGO, 18, false, TextAnchor.MiddleCenter, PixelUI.TextDark);
            _optionsResolutionPrev.onClick.AddListener(() =>
            {
                settings.SetResolutionIndex(Mathf.Max(0, settings.resolutionIndex - 1));
                RefreshResolutionRow(settings);
            });
            _optionsResolutionNext.onClick.AddListener(() =>
            {
                settings.SetResolutionIndex(Mathf.Min(settings.CommonResolutions.Length - 1, settings.resolutionIndex + 1));
                RefreshResolutionRow(settings);
            });
            RefreshResolutionRow(settings);
        }

        private void RefreshResolutionRow(SettingsManager settings)
        {
            _optionsResolutionLabel.text = ResolutionLabel(settings);
            _optionsResolutionPrev.interactable = settings.resolutionIndex > 0;
            _optionsResolutionNext.interactable = settings.resolutionIndex < settings.CommonResolutions.Length - 1;
        }

        private static string ResolutionLabel(SettingsManager settings)
        {
            var res = settings.CommonResolutions[settings.resolutionIndex];
            return $"{res.x} × {res.y}";
        }

        private void AddOptionsLanguageRow(Transform parent)
        {
            var row = CreateOptionsRow(parent, "Row_Language", 54f);
            AddOptionsLabel(row, "settings.language");
            var prevBtn = CreateArrow(row, false);
            var nextBtn = CreateArrow(row, true);
            var loc = LocalizationManager.Instance;
            var labelGO = CreateText(row, loc.LanguageName(loc.CurrentLanguage), new Vector2(0.62f, 0f), new Vector2(0.90f, 1f), TextAnchor.MiddleCenter, 18, "Text_Value");
            _optionsLanguageLabel = PixelUI.EnsureText(labelGO, 18, false, TextAnchor.MiddleCenter, PixelUI.TextDark);
            prevBtn.onClick.AddListener(() => loc.CycleLanguage(-1));
            nextBtn.onClick.AddListener(() => loc.CycleLanguage(1));
        }

        private Button CreateArrow(RectTransform row, bool right)
        {
            var button = CreateButton(row, "", new Vector2(27f, 36f), right ? "Button_Next" : "Button_Prev");
            Place(button.GetComponent<RectTransform>(), new Vector2(right ? 0.94f : 0.57f, 0.5f), Vector2.zero, new Vector2(27f, 36f));
            PixelUI.StyleArrow(button, right);
            return button;
        }

        private void AddOptionsToggleRow(Transform parent, string rowName, string labelKey, bool initial, Action<bool> setter)
        {
            var row = CreateOptionsRow(parent, rowName, 54f);
            AddOptionsLabel(row, labelKey);
            var toggleRT = GetRect(row, "Toggle");
            Place(toggleRT, new Vector2(0.94f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            var bgImage = GetImage(toggleRT);
            var checkRT = GetRect(toggleRT, "Checkmark");
            Stretch(checkRT);
            var checkImage = GetImage(checkRT);
            _optionsFullscreenToggle = toggleRT.GetComponent<Toggle>();
            if (_optionsFullscreenToggle == null) _optionsFullscreenToggle = toggleRT.gameObject.AddComponent<Toggle>();
            _optionsFullscreenToggle.targetGraphic = bgImage;
            _optionsFullscreenToggle.graphic = checkImage;
            PixelUI.StyleToggle(_optionsFullscreenToggle);
            _optionsFullscreenToggle.onValueChanged.RemoveAllListeners();
            _optionsFullscreenToggle.SetIsOnWithoutNotify(initial);
            _optionsFullscreenToggle.onValueChanged.AddListener(v => setter(v));
        }

        // ── 通用按钮 / 文字 / 布局 ───────────────────────────────
        private Button CreateButton(Transform parent, string label, Vector2 size, string goName = null)
        {
            goName ??= $"Button_{label}";
            var rt = GetRect(parent, goName);
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
            RemoveBorders(rt);
            var button = rt.GetComponent<Button>();
            if (button == null) button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = GetImage(rt);
            PixelUI.StyleButton(button);
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            var labelGO = CreateText(rt, label, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, 24, "Label");
            PixelUI.EnsureText(labelGO, 24, true, TextAnchor.MiddleCenter, PixelUI.TextDark);
            button.onClick.RemoveAllListeners();

            var trigger = rt.GetComponent<EventTrigger>();
            if (trigger == null) trigger = rt.gameObject.AddComponent<EventTrigger>();
            trigger.triggers.Clear();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (!button.interactable) return;
                SfxManager.Instance.PlayButtonHover();
                SetButtonFocused(button, true);
            });
            trigger.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => SetButtonFocused(button, false));
            trigger.triggers.Add(exit);
            return button;
        }

        private void SetButtonFocused(Button button, bool focused)
        {
            PixelUI.SetFocused(button, focused);
        }

        private GameObject CreateText(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize, string goName = "Text")
        {
            var rt = GetRect(parent, goName);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var label = PixelUI.EnsureText(rt.gameObject, fontSize, fontSize >= 21, align, PixelUI.TextLight);
            label.text = text;
            label.raycastTarget = false;
            return rt.gameObject;
        }

        private static RectTransform GetRect(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing.GetComponent<RectTransform>();
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static Image GetImage(RectTransform rt)
        {
            var image = rt.GetComponent<Image>();
            return image != null ? image : rt.gameObject.AddComponent<Image>();
        }

        private static CanvasGroup GetGroup(RectTransform rt)
        {
            var group = rt.GetComponent<CanvasGroup>();
            return group != null ? group : rt.gameObject.AddComponent<CanvasGroup>();
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
        }

        private static void SetLayoutHeight(RectTransform rt, float height)
        {
            var element = rt.GetComponent<LayoutElement>();
            if (element == null) element = rt.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = height;
            element.flexibleHeight = 0f;
            rt.localScale = Vector3.one;
        }

        private static void RemoveBorders(Transform parent)
        {
            RemoveChild(parent, "Border_Top");
            RemoveChild(parent, "Border_Bottom");
            RemoveChild(parent, "Border_Left");
            RemoveChild(parent, "Border_Right");
        }

        private static void RemoveChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child == null) return;
            child.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

    }
}
