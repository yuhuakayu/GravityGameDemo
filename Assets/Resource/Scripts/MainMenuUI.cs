using System;
using System.Collections;
using System.Collections.Generic;
using Resource.Scripts.Gyro;
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
    /// 主菜单——独立场景（MainMenu.unity），挂在这个场景里的 MainMenuUI 物体上。
    /// UI 层级本身还是运行时代码搭的，但改成了"场景里已经有就直接复用，没有才新建"，
    /// 编辑期不用进 Play 模式也能在 Hierarchy 里看到/调整这些物体；事件监听器/协程这些
    /// 没法存进场景文件的运行时绑定，每次 Start() 都会重新走一遍。
    ///
    /// 流程：标题（Start/Options/Quit）→ 点 Start，标题面板下滑淡出，关卡选择从右侧滑入 →
    /// 选关卡确认：走 SceneTransition 完全切换（非附加）到 level.sceneName 指定的场景，
    /// MainMenu 场景本身会被卸载掉。Options 面板同理，从标题面板滑入/滑出。
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("关卡列表（Level 2/3 是锁住的占位卡，纯粹为了能测手柄/键盘左右切换，没有真关卡时先留着）")]
        public List<LevelEntry> levels = new List<LevelEntry>
        {
            new LevelEntry { displayName = "Level 1", sceneName = "Stage1", unlocked = true },
            new LevelEntry { displayName = "Level 2", sceneName = "", unlocked = false },
            new LevelEntry { displayName = "Level 3", sceneName = "", unlocked = false }
        };

        private RectTransform _titlePanelRoot;
        private CanvasGroup   _titleGroup;
        private RectTransform _levelSelectRoot;
        private CanvasGroup   _levelSelectGroup;
        private RectTransform _optionsRoot;
        private CanvasGroup   _optionsGroup;

        private static readonly Color LockedLevelText = new Color32(0x5A, 0x58, 0x7C, 255);
        private readonly List<RectTransform> _levelCards = new List<RectTransform>();
        private ScrollRect _levelScroll;
        private int  _selectedLevelIndex;
        private bool _navLocked;

        private readonly List<Button> _titleButtons = new List<Button>();
        private int _titleSelectedIndex;

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
            if (_loc != null) _loc.OnLanguageChanged -= RefreshLocalizedTexts;
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
        }

        void Update()
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            if (_titlePanelRoot != null && _titlePanelRoot.gameObject.activeSelf)
                HandleTitleInput();

            if (_levelSelectGroup != null && _levelSelectGroup.gameObject.activeSelf)
                HandleLevelSelectInput();

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
            var canvas = canvasRT.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasRT.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;
            PixelUI.ConfigureCanvas(canvas);
            if (canvasRT.GetComponent<GraphicRaycaster>() == null)
                canvasRT.gameObject.AddComponent<GraphicRaycaster>();

            foreach (string name in new[] { "Background", "TorchGlow0", "TorchGlow1", "Fog0", "Fog1", "Fog2" })
                RemoveChild(canvasRT, name);
            PixelUI.Backdrop(canvasRT);
            BuildTitlePanel(canvasRT);
            BuildLevelSelectPanel(canvasRT);
            BuildOptionsPanel(canvasRT);
        }

        // ── 标题面板 ─────────────────────────────────────────────
        private void BuildTitlePanel(Transform parent)
        {
            _titlePanelRoot = GetRect(parent, "TitlePanel");
            Stretch(_titlePanelRoot);
            _titleGroup = GetGroup(_titlePanelRoot);
            _titleGroup.alpha = 1f;
            _titlePanelRoot.gameObject.SetActive(true);
            RemoveChild(_titlePanelRoot, "Logo");

            var titleRT = GetRect(_titlePanelRoot, "Text_Title");
            Place(titleRT, new Vector2(0.5f, 0.79f), Vector2.zero, new Vector2(600f, 108f));
            var title = PixelUI.EnsureText(titleRT.gameObject, 72, false, TextAnchor.MiddleCenter, PixelUI.TextLight);
            title.font = PixelUI.Theme.titleFont;
            title.text = "Upside Down";

            var buttonsRT = GetRect(_titlePanelRoot, "Buttons");
            Place(buttonsRT, new Vector2(0.5f, 0.43f), Vector2.zero, new Vector2(300f, 204f));
            var startBtn = CreateButton(buttonsRT, LocalizationManager.Instance.Get("menu.start"), new Vector2(300f, 54f), "Button_Start Game");
            PositionVerticalStack(startBtn.GetComponent<RectTransform>(), 0, 3, 72f);
            startBtn.onClick.AddListener(OnStartGameClicked);
            _localizedTexts.Add((startBtn.GetComponentInChildren<TextMeshProUGUI>(true), "menu.start"));

            var optionsBtn = CreateButton(buttonsRT, LocalizationManager.Instance.Get("menu.options"), new Vector2(300f, 54f), "Button_Options");
            PositionVerticalStack(optionsBtn.GetComponent<RectTransform>(), 1, 3, 72f);
            optionsBtn.onClick.AddListener(OnOptionsClicked);
            _localizedTexts.Add((optionsBtn.GetComponentInChildren<TextMeshProUGUI>(true), "menu.options"));

            var quitBtn = CreateButton(buttonsRT, LocalizationManager.Instance.Get("menu.quit"), new Vector2(300f, 54f), "Button_Quit Game");
            PositionVerticalStack(quitBtn.GetComponent<RectTransform>(), 2, 3, 72f);
            quitBtn.onClick.AddListener(OnQuitClicked);
            _localizedTexts.Add((quitBtn.GetComponentInChildren<TextMeshProUGUI>(true), "menu.quit"));

            _titleButtons.Clear();
            _titleButtons.Add(startBtn);
            _titleButtons.Add(optionsBtn);
            _titleButtons.Add(quitBtn);
            _titleSelectedIndex = 0;
            SetButtonFocused(startBtn, true);
        }

        /// <summary>标题页的十字键/左摇杆/键盘上下选择 + 确认（跟关卡选择页同一套轮询手柄的写法，
        /// 不依赖 InputSystemUIInputModule 的默认导航绑定，保证不管有没有配好都能用）</summary>
        private void HandleTitleInput()
        {
            if (_navLocked || _titleButtons.Count == 0) return;

            float navAxis = 0f;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.upArrowKey.wasPressedThisFrame)   navAxis = -1f;
                if (Keyboard.current.downArrowKey.wasPressedThisFrame) navAxis = 1f;
            }
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.leftStick.up.wasPressedThisFrame   || gamepad.dpad.up.wasPressedThisFrame)   navAxis = -1f;
                if (gamepad.leftStick.down.wasPressedThisFrame || gamepad.dpad.down.wasPressedThisFrame) navAxis = 1f;
            }

            if (navAxis != 0f)
            {
                int newIndex = Mathf.Clamp(_titleSelectedIndex + (int)navAxis, 0, _titleButtons.Count - 1);
                if (newIndex != _titleSelectedIndex)
                {
                    SetButtonFocused(_titleButtons[_titleSelectedIndex], false);
                    _titleSelectedIndex = newIndex;
                    SetButtonFocused(_titleButtons[_titleSelectedIndex], true);
                    SfxManager.Instance.PlayButtonHover();
                }
            }

            bool confirmPressed = (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (confirmPressed) _titleButtons[_titleSelectedIndex].onClick.Invoke();
        }

        private void PositionVerticalStack(RectTransform rt, int index, int count, float spacing)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float offset = (count - 1) * 0.5f - index;
            rt.anchoredPosition = new Vector2(0f, offset * spacing);
        }

        private void OnStartGameClicked()
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            if (_navLocked) return;
            _navLocked = true;
            SfxManager.Instance.PlayButtonClick();
            StartCoroutine(SwapPanels(_titlePanelRoot, _titleGroup, new Vector2(0f, -150f),
                                       _levelSelectRoot, _levelSelectGroup, new Vector2(400f, 0f)));
        }

        private void OnOptionsClicked()
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            if (_navLocked) return;
            _navLocked = true;
            SfxManager.Instance.PlayButtonClick();
            StartCoroutine(SwapPanels(_titlePanelRoot, _titleGroup, new Vector2(0f, -150f),
                                       _optionsRoot, _optionsGroup, new Vector2(400f, 0f)));
        }

        private void OnQuitClicked()
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            SfxManager.Instance.PlayButtonClick();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BackToTitle(RectTransform fromRoot, CanvasGroup fromGroup, Vector2 fromExitOffset)
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            if (_navLocked) return;
            _navLocked = true;
            SfxManager.Instance.PlayButtonClick();
            StartCoroutine(SwapPanels(fromRoot, fromGroup, fromExitOffset,
                                       _titlePanelRoot, _titleGroup, new Vector2(0f, -150f)));
        }

        /// <summary>通用面板切换：outRoot 滑出淡出，inRoot 从对应方向滑入淡入</summary>
        private IEnumerator SwapPanels(RectTransform outRoot, CanvasGroup outGroup, Vector2 outOffset,
                                        RectTransform inRoot, CanvasGroup inGroup, Vector2 inOffset)
        {
            yield return SlidePanel(outRoot, outGroup, Vector2.zero, outOffset, 1f, 0f, 0.5f, true);
            yield return SlidePanel(inRoot, inGroup, inOffset, Vector2.zero, 0f, 1f, 0.5f, false);
            if (inRoot == _optionsRoot) UpdateOptionsRowHighlight();
            else if (inRoot == _levelSelectRoot) UpdateCardHighlight();
            else
                for (int i = 0; i < _titleButtons.Count; i++)
                    SetButtonFocused(_titleButtons[i], i == _titleSelectedIndex);
            _navLocked = false;
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

        // ── 关卡选择面板 ─────────────────────────────────────────
        private void BuildLevelSelectPanel(Transform parent)
        {
            _levelSelectRoot = GetRect(parent, "LevelSelectPanel");
            Stretch(_levelSelectRoot);
            _levelSelectGroup = GetGroup(_levelSelectRoot);
            _levelSelectGroup.alpha = 0f;
            _levelSelectRoot.gameObject.SetActive(false);
            RemoveChild(_levelSelectRoot, "Cards");

            var title = GetRect(_levelSelectRoot, "Title");
            Place(title, new Vector2(0.5f, 1f), new Vector2(0f, -100.5f), new Vector2(372f, 69f));
            var titleImage = GetImage(title);
            titleImage.sprite = PixelUI.Theme.levelTitle;
            titleImage.color = Color.white;
            titleImage.raycastTarget = false;

            var back = CreateButton(_levelSelectRoot, LocalizationManager.Instance.Get("settings.back"), new Vector2(138f, 54f), "Button_Back");
            Place(back.GetComponent<RectTransform>(), Vector2.zero, new Vector2(102f, 66f), new Vector2(138f, 54f));
            back.onClick.AddListener(() => BackToTitle(_levelSelectRoot, _levelSelectGroup, new Vector2(400f, 0f)));
            _localizedTexts.Add((back.GetComponentInChildren<TextMeshProUGUI>(true), "settings.back"));

            var viewport = GetRect(_levelSelectRoot, "CardsViewport");
            viewport.anchorMin = new Vector2(0f, 0.5f);
            viewport.anchorMax = new Vector2(1f, 0.5f);
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.offsetMin = new Vector2(72f, -162f);
            viewport.offsetMax = new Vector2(-72f, 162f);
            GetImage(viewport).color = Color.clear;
            if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
            _levelScroll = viewport.GetComponent<ScrollRect>();
            if (_levelScroll == null) _levelScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _levelScroll.viewport = viewport;
            _levelScroll.horizontal = true;
            _levelScroll.vertical = false;
            _levelScroll.movementType = ScrollRect.MovementType.Clamped;
            _levelScroll.inertia = false;
            _levelScroll.scrollSensitivity = 30f;

            var cards = GetRect(viewport, "Cards");
            float rowWidth = Mathf.Max(0f, levels.Count * 198f - 12f);
            Place(cards, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(rowWidth, 288f));
            _levelScroll.content = cards;
            _levelCards.Clear();
            for (int i = 0; i < levels.Count; i++)
            {
                var card = BuildLevelCard(cards, levels[i], i);
                card.anchoredPosition = new Vector2((i - (levels.Count - 1) * 0.5f) * 198f, 12f);
                _levelCards.Add(card);
            }

            // The supplied font subset lacks the triangle glyphs; use the shared pixel arrows.
            var hint = GetRect(_levelSelectRoot, "ControlsHint");
            Place(hint, new Vector2(0.5f, 0f), new Vector2(0f, 54f), new Vector2(312f, 30f));
            for (int i = 0; i < 2; i++)
            {
                var arrow = GetRect(hint, i == 0 ? "Left" : "Right");
                Place(arrow, new Vector2(0f, 0.5f), new Vector2(12f + i * 21f, 0f), new Vector2(12f, 18f));
                var image = GetImage(arrow);
                image.sprite = i == 0 ? PixelUI.Theme.arrowLeftDisabled : PixelUI.Theme.arrowRightDisabled;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            var hintText = CreateText(hint, "Select   × Start   ○ Back", Vector2.zero, Vector2.one, TextAnchor.MiddleLeft, 18, "Text");
            hintText.GetComponent<RectTransform>().offsetMin = new Vector2(51f, 0f);
            hintText.GetComponent<TextMeshProUGUI>().color = new Color32(0x96, 0x94, 0xBA, 255);

            _selectedLevelIndex = 0;
            UpdateCardHighlight();
        }

        private RectTransform BuildLevelCard(Transform parent, LevelEntry level, int index)
        {
            var card = GetRect(parent, $"Card_{index + 1:00}");
            Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(186f, 252f));
            var background = GetImage(card);
            background.type = Image.Type.Simple;
            background.color = Color.white;

            var content = GetRect(card, "Content");
            Stretch(content);
            var thumb = GetRect(content, "Thumbnail");
            Place(thumb, new Vector2(0f, 1f), new Vector2(93f, -78f), new Vector2(144f, 102f));
            var thumbImage = GetImage(thumb);
            thumbImage.sprite = level.unlocked ? PixelUI.Theme.levelThumbnail : null;
            thumbImage.color = level.unlocked ? Color.white : new Color32(0x17, 0x16, 0x22, 255);
            thumbImage.raycastTarget = false;

            var lockIcon = GetRect(thumb, "Lock");
            Place(lockIcon, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 33f));
            GetImage(lockIcon).sprite = PixelUI.Theme.levelLock;
            GetImage(lockIcon).raycastTarget = false;
            lockIcon.gameObject.SetActive(!level.unlocked);
            var check = GetRect(thumb, "Cleared");
            Place(check, Vector2.one, new Vector2(-7.5f, -13.5f), new Vector2(21f, 15f));
            GetImage(check).sprite = PixelUI.Theme.levelCheck;
            GetImage(check).raycastTarget = false;
            check.gameObject.SetActive(level.unlocked && level.cleared);

            var number = CreateText(content, (index + 1).ToString("00"), Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, 34, "Number");
            Place(number.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -174f), new Vector2(150f, 42f));
            number.GetComponent<TextMeshProUGUI>().color = level.unlocked ? PixelUI.TextLight : LockedLevelText;
            var name = CreateText(content, level.unlocked ? level.displayName : "???", Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, 17, "Name");
            Place(name.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -201f), new Vector2(150f, 36f));
            var nameText = name.GetComponent<TextMeshProUGUI>();
            nameText.color = level.unlocked ? PixelUI.TextLight : LockedLevelText;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            var status = CreateText(content, !level.unlocked ? "LOCKED" : level.cleared ? "CLEARED" : "", Vector2.zero, Vector2.one, TextAnchor.MiddleCenter, 12, "Status");
            Place(status.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(150f, 18f));
            status.GetComponent<TextMeshProUGUI>().color = level.unlocked ? PixelUI.OxygenNormal : LockedLevelText;

            var button = card.GetComponent<Button>();
            if (button == null) button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (GyroRuntime.ConsoleCapturesInput || _navLocked) return;
                _selectedLevelIndex = index;
                UpdateCardHighlight();
                ConfirmLevelSelection();
            });
            return card;
        }

        private void UpdateCardHighlight()
        {
            for (int i = 0; i < _levelCards.Count; i++)
            {
                bool selected = i == _selectedLevelIndex;
                var card = _levelCards[i];
                var image = card.GetComponent<Image>();
                image.sprite = selected ? PixelUI.Theme.levelCardSelected
                    : levels[i].unlocked ? PixelUI.Theme.levelCardNormal : PixelUI.Theme.levelCardLocked;
                image.color = selected && !levels[i].unlocked ? new Color(0.65f, 0.65f, 0.65f, 1f) : Color.white;
                // Selected artwork is already raised by 3 pixels; only its contents need the same offset.
                card.Find("Content").GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, selected ? 9f : 0f);
            }
            if (_levelCards.Count == 0) return;
            Canvas.ForceUpdateCanvases();
            float halfView = _levelScroll.viewport.rect.width * 0.5f;
            var selectedCard = _levelCards[_selectedLevelIndex];
            float offset = _levelScroll.content.anchoredPosition.x;
            float left = selectedCard.anchoredPosition.x - selectedCard.rect.width * 0.5f + offset;
            float right = left + selectedCard.rect.width;
            if (left < -halfView) offset += -halfView - left;
            else if (right > halfView) offset -= right - halfView;
            float limit = Mathf.Max(0f, (_levelScroll.content.rect.width - _levelScroll.viewport.rect.width) * 0.5f);
            _levelScroll.StopMovement();
            _levelScroll.content.anchoredPosition = new Vector2(Mathf.Clamp(offset, -limit, limit), 0f);
        }
        private void HandleLevelSelectInput()
        {
            if (_navLocked) return;

            float navAxis = 0f;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame)  navAxis = -1f;
                if (Keyboard.current.rightArrowKey.wasPressedThisFrame) navAxis = 1f;
            }
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.leftStick.left.wasPressedThisFrame  || gamepad.dpad.left.wasPressedThisFrame)  navAxis = -1f;
                if (gamepad.leftStick.right.wasPressedThisFrame || gamepad.dpad.right.wasPressedThisFrame) navAxis = 1f;
            }

            if (navAxis != 0f && _levelCards.Count > 0)
            {
                int newIndex = Mathf.Clamp(_selectedLevelIndex + (int)navAxis, 0, _levelCards.Count - 1);
                if (newIndex != _selectedLevelIndex)
                {
                    _selectedLevelIndex = newIndex;
                    UpdateCardHighlight();
                    SfxManager.Instance.PlayButtonHover();
                }
            }

            bool confirmPressed = (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);
            if (confirmPressed) ConfirmLevelSelection();

            bool backPressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);
            if (backPressed) BackToTitle(_levelSelectRoot, _levelSelectGroup, new Vector2(400f, 0f));
        }

        private void ConfirmLevelSelection()
        {
            if (GyroRuntime.ConsoleCapturesInput) return;
            if (_navLocked) return;
            if (_levelCards.Count == 0 || _selectedLevelIndex >= levels.Count) return;

            var level = levels[_selectedLevelIndex];
            if (!level.unlocked)
            {
                SfxManager.Instance.PlayWallBump(); // 借用撞墙音效当"选不了"的提示
                return;
            }

            SfxManager.Instance.PlayButtonClick();
            _navLocked = true;
            GameFlowState.HasEnteredGame = true;

            if (string.IsNullOrEmpty(level.sceneName))
            {
                Debug.LogError($"[MainMenuUI] 关卡 \"{level.displayName}\" 没有配置 sceneName，无法跳转。");
                _navLocked = false;
                return;
            }

            // 走场景转场（黑幕合上→加载→展开），非附加方式完全切到目标关卡场景，
            // MainMenu 场景本身会被卸载。
            SceneTransition.Instance.LoadScene(level.sceneName);
        }

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
            if (backPressed) { BackToTitle(_optionsRoot, _optionsGroup, new Vector2(400f, 0f)); return; }

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

            ApplyOptionsRowInput(adjustAxis, adjustPressed, confirmPressed);
        }

        private void ApplyOptionsRowInput(float adjustAxis, bool adjustPressed, bool confirmPressed)
        {
            var settings = SettingsManager.Instance;
            switch (_optionsSelectedIndex)
            {
                case 0:
                    if (adjustPressed) { _optionsMasterDrag.SetValue(_optionsMasterDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayButtonHover(); }
                    break;
                case 1:
                    if (adjustPressed) { _optionsMusicDrag.SetValue(_optionsMusicDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayButtonHover(); }
                    break;
                case 2:
                    if (adjustPressed) { _optionsSfxDrag.SetValue(_optionsSfxDrag.Value + adjustAxis * 0.1f); SfxManager.Instance.PlayButtonHover(); }
                    break;
                case 3:
                    if (adjustPressed)
                    {
                        int dir = adjustAxis > 0f ? 1 : -1;
                        settings.SetResolutionIndex(Mathf.Clamp(settings.resolutionIndex + dir, 0, settings.CommonResolutions.Length - 1));
                        RefreshResolutionRow(settings);
                        SfxManager.Instance.PlayButtonHover();
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
                        SfxManager.Instance.PlayButtonHover();
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
            backBtn.onClick.AddListener(() => BackToTitle(_optionsRoot, _optionsGroup, new Vector2(400f, 0f)));
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
            exit.callback.AddListener(_ => SetButtonFocused(button, _titleButtons.Count > _titleSelectedIndex && _titleButtons[_titleSelectedIndex] == button));
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
