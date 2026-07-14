using System;
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
    [Serializable]
    public class LevelEntry
    {
        public string displayName = "Level 1";
        [Tooltip("留空 = 当前场景")]
        public string sceneName = "";
        public bool unlocked = true;
    }

    /// <summary>
    /// 主菜单——运行时自动生成的全屏遮罩层，不是独立 Unity 场景（原因：没法可视化验证
    /// 手写的新场景文件对不对，跟之前不敢手写 Tilemap 是一个道理）。
    ///
    /// 流程：标题（Start/Options/Quit）→ 点 Start，标题面板下滑淡出，关卡选择从右侧滑入 →
    /// 选关卡确认：如果目标就是当前场景，直接隐藏菜单、恢复玩家操作；否则走 SceneTransition
    /// 加载对应场景。Options 面板同理，从标题面板滑入/滑出。
    ///
    /// 由 PlayerController.Start() 自动创建，创建时会先冻结玩家和世界旋转，直到进入游戏。
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("关卡列表（Level 2/3 是锁住的占位卡，纯粹为了能测手柄/键盘左右切换，没有真关卡时先留着）")]
        public List<LevelEntry> levels = new List<LevelEntry>
        {
            new LevelEntry { displayName = "Level 1", sceneName = "", unlocked = true },
            new LevelEntry { displayName = "Level 2", sceneName = "", unlocked = false },
            new LevelEntry { displayName = "Level 3", sceneName = "", unlocked = false }
        };

        private static readonly Color ButtonColor = new Color(0.3f, 0.22f, 0.15f, 1f);
        private static readonly Color PanelBg     = new Color(0.35f, 0.28f, 0.4f, 0.95f);
        private static readonly Color LockedBg    = new Color(0.15f, 0.15f, 0.15f, 0.9f);

        private RectTransform _titlePanelRoot;
        private CanvasGroup   _titleGroup;
        private RectTransform _levelSelectRoot;
        private CanvasGroup   _levelSelectGroup;
        private RectTransform _optionsRoot;
        private CanvasGroup   _optionsGroup;

        private readonly List<RectTransform> _levelCards = new List<RectTransform>();
        private readonly List<Vector2> _levelCardBasePositions = new List<Vector2>();
        private readonly List<Image[]> _levelCardBorders = new List<Image[]>();
        private int  _selectedLevelIndex;
        private bool _navLocked;

        private readonly List<Button> _titleButtons = new List<Button>();
        private int _titleSelectedIndex;
        private readonly Dictionary<Button, Image[]> _buttonBorders = new Dictionary<Button, Image[]>();

        private LocalizationManager _loc;
        private readonly List<(Text text, string key)> _localizedTexts = new List<(Text, string)>();
        private Text _optionsLanguageLabel;

        private readonly List<Image> _optionsRowBg = new List<Image>();
        private int _optionsSelectedIndex;
        private DebugSliderDrag _optionsMasterDrag, _optionsMusicDrag, _optionsSfxDrag;
        private Text _optionsResolutionLabel;
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
        private GameObject CreateLocalizedText(Transform parent, string key, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align, int fontSize)
        {
            var go = CreateText(parent, LocalizationManager.Instance.Get(key), anchorMin, anchorMax, align, fontSize);
            _localizedTexts.Add((go.GetComponent<Text>(), key));
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
            var canvasGO = new GameObject("MainMenuCanvas (Auto)");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000; // 盖住游戏内其他所有 UI
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            BuildAtmosphere(canvasGO.transform);
            BuildTitlePanel(canvasGO.transform);
            BuildLevelSelectPanel(canvasGO.transform);
            BuildOptionsPanel(canvasGO.transform);
        }

        // ── 氛围背景：地牢背景图 + 火把光晕闪烁 + 缓慢飘的雾气 ─────
        private void BuildAtmosphere(Transform parent)
        {
            var sprite = Resources.Load<Sprite>("Backgrounds/DungeonA");
            if (sprite != null)
            {
                var bgGO = new GameObject("Background");
                bgGO.transform.SetParent(parent, false);
                var bgRT = bgGO.AddComponent<RectTransform>();
                bgRT.anchorMin = Vector2.zero;
                bgRT.anchorMax = Vector2.one;
                bgRT.offsetMin = Vector2.zero;
                bgRT.offsetMax = Vector2.zero;
                var bgImg = bgGO.AddComponent<Image>();
                bgImg.sprite = sprite;
                bgImg.color  = new Color(0.5f, 0.45f, 0.65f, 1f);
                StartCoroutine(BreatheScale(bgRT, 1f, 1.04f, 6f));
            }

            var glowSprite = CreateCircleSprite(64);

            // 顶部中间原本还有一个火把光晕，跟设置面板标题文字挤在一起会重叠，去掉只留两侧
            Vector2[] torchPositions = { new Vector2(0.15f, 0.75f), new Vector2(0.85f, 0.75f) };
            foreach (var pos in torchPositions)
            {
                var glowGO = new GameObject("TorchGlow");
                glowGO.transform.SetParent(parent, false);
                var glowRT = glowGO.AddComponent<RectTransform>();
                glowRT.anchorMin = glowRT.anchorMax = pos;
                glowRT.sizeDelta = new Vector2(220f, 220f);
                var glowImg = glowGO.AddComponent<Image>();
                glowImg.sprite = glowSprite;
                glowImg.color  = new Color(1f, 0.6f, 0.25f, 0.35f);
                StartCoroutine(Flicker(glowImg, 0.25f, 0.45f));
            }

            for (int i = 0; i < 3; i++)
            {
                var fogGO = new GameObject($"Fog{i}");
                fogGO.transform.SetParent(parent, false);
                var fogRT = fogGO.AddComponent<RectTransform>();
                fogRT.anchorMin = fogRT.anchorMax = new Vector2(0.5f, 0.15f + i * 0.15f);
                fogRT.sizeDelta = new Vector2(900f, 260f);
                var fogImg = fogGO.AddComponent<Image>();
                fogImg.sprite = glowSprite;
                fogImg.color  = new Color(0.5f, 0.5f, 0.7f, 0.06f);
                StartCoroutine(DriftFog(fogRT, fogImg, i));
            }
        }

        private IEnumerator BreatheScale(RectTransform rt, float min, float max, float period)
        {
            float t = 0f;
            while (true)
            {
                t += Time.unscaledDeltaTime;
                float k = (Mathf.Sin(t / period * Mathf.PI * 2f) + 1f) * 0.5f;
                float s = Mathf.Lerp(min, max, k);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
        }

        private IEnumerator Flicker(Image img, float minA, float maxA)
        {
            Color baseColor = img.color;
            float seed = UnityEngine.Random.Range(0f, 100f);
            while (true)
            {
                float n = Mathf.PerlinNoise(seed, Time.unscaledTime * 3f);
                float a = Mathf.Lerp(minA, maxA, n);
                img.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
                yield return null;
            }
        }

        private IEnumerator DriftFog(RectTransform rt, Image img, int seedOffset)
        {
            Vector2 baseAnchor = rt.anchorMin;
            float t = seedOffset * 3f;
            while (true)
            {
                t += Time.unscaledDeltaTime * 0.1f;
                float dx = Mathf.Sin(t) * 0.15f;
                float a  = Mathf.Lerp(0.03f, 0.08f, (Mathf.Sin(t * 0.7f) + 1f) * 0.5f);
                rt.anchorMin = rt.anchorMax = new Vector2(baseAnchor.x + dx, baseAnchor.y);
                var c = img.color;
                img.color = new Color(c.r, c.g, c.b, a);
                yield return null;
            }
        }

        // ── 标题面板 ─────────────────────────────────────────────
        private void BuildTitlePanel(Transform parent)
        {
            var panelGO = new GameObject("TitlePanel", typeof(RectTransform));
            panelGO.transform.SetParent(parent, false);
            _titlePanelRoot = panelGO.GetComponent<RectTransform>();
            _titlePanelRoot.anchorMin = Vector2.zero;
            _titlePanelRoot.anchorMax = Vector2.one;
            _titlePanelRoot.offsetMin = Vector2.zero;
            _titlePanelRoot.offsetMax = Vector2.zero;
            _titleGroup = panelGO.AddComponent<CanvasGroup>();

            var titleGO = CreateText(panelGO.transform, "UpSide Down", new Vector2(0f, 0.68f), new Vector2(1f, 0.85f), TextAnchor.MiddleCenter, 60);
            titleGO.GetComponent<Text>().fontStyle = FontStyle.Bold;

            var buttonsGO = new GameObject("Buttons", typeof(RectTransform));
            buttonsGO.transform.SetParent(panelGO.transform, false);
            var buttonsRT = buttonsGO.GetComponent<RectTransform>();
            buttonsRT.anchorMin = buttonsRT.anchorMax = new Vector2(0.5f, 0.45f);
            buttonsRT.sizeDelta = new Vector2(360f, 260f);

            var startBtn = CreateButton(buttonsRT, "Start Game", new Vector2(320f, 64f), ButtonColor);
            PositionVerticalStack(startBtn.GetComponent<RectTransform>(), 0, 3, 84f);
            startBtn.onClick.AddListener(OnStartGameClicked);

            var optionsBtn = CreateButton(buttonsRT, LocalizationManager.Instance.Get("menu.options"), new Vector2(320f, 64f), ButtonColor);
            PositionVerticalStack(optionsBtn.GetComponent<RectTransform>(), 1, 3, 84f);
            optionsBtn.onClick.AddListener(OnOptionsClicked);
            _localizedTexts.Add((optionsBtn.GetComponentInChildren<Text>(), "menu.options"));

            var quitBtn = CreateButton(buttonsRT, "Quit Game", new Vector2(320f, 64f), ButtonColor);
            PositionVerticalStack(quitBtn.GetComponent<RectTransform>(), 2, 3, 84f);
            quitBtn.onClick.AddListener(OnQuitClicked);

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
            if (_navLocked) return;
            _navLocked = true;
            SfxManager.Instance.PlayButtonClick();
            StartCoroutine(SwapPanels(_titlePanelRoot, _titleGroup, new Vector2(0f, -150f),
                                       _levelSelectRoot, _levelSelectGroup, new Vector2(400f, 0f)));
        }

        private void OnOptionsClicked()
        {
            if (_navLocked) return;
            _navLocked = true;
            SfxManager.Instance.PlayButtonClick();
            StartCoroutine(SwapPanels(_titlePanelRoot, _titleGroup, new Vector2(0f, -150f),
                                       _optionsRoot, _optionsGroup, new Vector2(400f, 0f)));
        }

        private void OnQuitClicked()
        {
            SfxManager.Instance.PlayButtonClick();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BackToTitle(RectTransform fromRoot, CanvasGroup fromGroup, Vector2 fromExitOffset)
        {
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
            var panelGO = new GameObject("LevelSelectPanel", typeof(RectTransform));
            panelGO.transform.SetParent(parent, false);
            _levelSelectRoot = panelGO.GetComponent<RectTransform>();
            _levelSelectRoot.anchorMin = Vector2.zero;
            _levelSelectRoot.anchorMax = Vector2.one;
            _levelSelectRoot.offsetMin = Vector2.zero;
            _levelSelectRoot.offsetMax = Vector2.zero;
            _levelSelectGroup = panelGO.AddComponent<CanvasGroup>();
            _levelSelectGroup.alpha = 0f;
            panelGO.SetActive(false);

            var backBtn = CreateButton(panelGO.transform, "Back", new Vector2(140f, 50f), ButtonColor);
            var backRT = backBtn.GetComponent<RectTransform>();
            backRT.anchorMin = backRT.anchorMax = new Vector2(0f, 1f);
            backRT.pivot = new Vector2(0f, 1f);
            backRT.anchoredPosition = new Vector2(30f, -30f);
            backBtn.onClick.AddListener(() => BackToTitle(_levelSelectRoot, _levelSelectGroup, new Vector2(400f, 0f)));

            var cardsRowGO = new GameObject("Cards", typeof(RectTransform));
            cardsRowGO.transform.SetParent(panelGO.transform, false);
            var cardsRowRT = cardsRowGO.GetComponent<RectTransform>();
            cardsRowRT.anchorMin = cardsRowRT.anchorMax = new Vector2(0.5f, 0.5f);
            cardsRowRT.sizeDelta = new Vector2(1600f, 500f);

            float cardWidth = 220f;
            float spacing = 40f;
            int count = Mathf.Max(1, levels.Count);
            for (int i = 0; i < levels.Count; i++)
            {
                var card = BuildLevelCard(cardsRowGO.transform, levels[i], i);
                float x = (i - (count - 1) * 0.5f) * (cardWidth + spacing);
                var basePos = new Vector2(x, 0f);
                card.anchoredPosition = basePos;
                _levelCards.Add(card);
                _levelCardBasePositions.Add(basePos);
            }

            _selectedLevelIndex = 0;
            UpdateCardHighlight();
        }

        private RectTransform BuildLevelCard(Transform parent, LevelEntry level, int index)
        {
            var cardGO = new GameObject($"Card_{level.displayName}", typeof(RectTransform));
            cardGO.transform.SetParent(parent, false);
            var cardRT = cardGO.GetComponent<RectTransform>();
            cardRT.anchorMin = cardRT.anchorMax = new Vector2(0.5f, 0.5f);
            cardRT.pivot = new Vector2(0.5f, 0.5f);
            cardRT.sizeDelta = new Vector2(220f, 320f);

            var bgImg = cardGO.AddComponent<Image>();
            bgImg.color = level.unlocked ? PanelBg : LockedBg;

            // 高亮边框：4 根贴边的细条，不是一整块实心矩形——实心矩形当子物体会盖住 bgImg
            var borderImages = new Image[4];
            const float borderThickness = 6f;
            borderImages[0] = CreateBorderBar(cardGO.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), borderThickness); // 上
            borderImages[1] = CreateBorderBar(cardGO.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), borderThickness); // 下
            borderImages[2] = CreateBorderBar(cardGO.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), borderThickness); // 左
            borderImages[3] = CreateBorderBar(cardGO.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), borderThickness); // 右
            _levelCardBorders.Add(borderImages);

            var thumbGO = new GameObject("Thumb", typeof(RectTransform));
            thumbGO.transform.SetParent(cardGO.transform, false);
            var thumbRT = thumbGO.GetComponent<RectTransform>();
            thumbRT.anchorMin = new Vector2(0.5f, 1f);
            thumbRT.anchorMax = new Vector2(0.5f, 1f);
            thumbRT.pivot = new Vector2(0.5f, 1f);
            thumbRT.anchoredPosition = new Vector2(0f, -20f);
            thumbRT.sizeDelta = new Vector2(160f, 160f);
            var thumbImg = thumbGO.AddComponent<Image>();
            thumbImg.sprite = CreateCircleSprite(96);
            thumbImg.color  = level.unlocked ? new Color(0.55f, 0.75f, 0.6f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);

            CreateText(cardGO.transform, level.displayName, new Vector2(0f, 0f), new Vector2(1f, 0.28f), TextAnchor.MiddleCenter, 20);

            if (!level.unlocked)
            {
                var lockGO = CreateText(cardGO.transform, "LOCKED", new Vector2(0f, 0.55f), new Vector2(1f, 0.75f), TextAnchor.MiddleCenter, 16);
                lockGO.GetComponent<Text>().color = new Color(1f, 0.4f, 0.4f);
            }

            var button = cardGO.AddComponent<Button>();
            button.targetGraphic = bgImg;
            int capturedIndex = index;
            button.onClick.AddListener(() =>
            {
                _selectedLevelIndex = capturedIndex;
                UpdateCardHighlight();
                ConfirmLevelSelection();
            });

            return cardRT;
        }

        private void UpdateCardHighlight()
        {
            for (int i = 0; i < _levelCards.Count; i++)
            {
                bool selected = i == _selectedLevelIndex;
                var rt = _levelCards[i];
                rt.localScale = Vector3.one * (selected ? 1.15f : 0.9f);

                var borders = _levelCardBorders[i];
                Color borderColor = selected ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(1f, 0.85f, 0.3f, 0f);
                for (int b = 0; b < borders.Length; b++)
                    borders[b].color = borderColor;

                var cg = rt.GetComponent<CanvasGroup>();
                if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = selected ? 1f : 0.6f;
            }
        }

        private void HandleLevelSelectInput()
        {
            // 选中的卡片缓慢上下浮动，其它卡片保持在基准位置
            for (int i = 0; i < _levelCards.Count; i++)
            {
                Vector2 basePos = _levelCardBasePositions[i];
                if (i == _selectedLevelIndex)
                {
                    float bob = Mathf.Sin(Time.unscaledTime * 2.5f) * 10f;
                    _levelCards[i].anchoredPosition = basePos + new Vector2(0f, bob);
                }
                else
                {
                    _levelCards[i].anchoredPosition = basePos;
                }
            }

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
            GameFlowState.HasEnteredGame = true; // 之后重开本关/切下一关都不用再弹主菜单了

            // 统一走场景转场（黑幕合上→加载→展开），跟"重新开始"用的是同一套过渡效果，
            // 就算目标就是当前场景也一样重新加载一遍——反正上面这行标记已经保证不会再弹主菜单了。
            string targetScene = string.IsNullOrEmpty(level.sceneName) ? SceneManager.GetActiveScene().name : level.sceneName;
            SceneTransition.Instance.LoadScene(targetScene);
        }

        private void UpdateOptionsRowHighlight()
        {
            for (int i = 0; i < _optionsRowBg.Count; i++)
                _optionsRowBg[i].color = i == _optionsSelectedIndex
                    ? new Color(1f, 0.85f, 0.3f, 0.22f)
                    : new Color(0f, 0f, 0f, 0f);
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
            if (navAxis != 0f && _optionsRowBg.Count > 0)
            {
                int newIndex = Mathf.Clamp(_optionsSelectedIndex + (int)navAxis, 0, _optionsRowBg.Count - 1);
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
                        settings.SetResolutionIndex((settings.resolutionIndex + dir + settings.CommonResolutions.Length) % settings.CommonResolutions.Length);
                        _optionsResolutionLabel.text = ResolutionLabel(settings);
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
            var panelGO = new GameObject("OptionsPanel", typeof(RectTransform));
            panelGO.transform.SetParent(parent, false);
            _optionsRoot = panelGO.GetComponent<RectTransform>();
            _optionsRoot.anchorMin = Vector2.zero;
            _optionsRoot.anchorMax = Vector2.one;
            _optionsRoot.offsetMin = Vector2.zero;
            _optionsRoot.offsetMax = Vector2.zero;
            _optionsGroup = panelGO.AddComponent<CanvasGroup>();
            _optionsGroup.alpha = 0f;
            panelGO.SetActive(false);

            var backBtn = CreateButton(panelGO.transform, LocalizationManager.Instance.Get("settings.back"), new Vector2(140f, 50f), ButtonColor);
            var backRT = backBtn.GetComponent<RectTransform>();
            backRT.anchorMin = backRT.anchorMax = new Vector2(0f, 1f);
            backRT.pivot = new Vector2(0f, 1f);
            backRT.anchoredPosition = new Vector2(30f, -30f);
            backBtn.onClick.AddListener(() => BackToTitle(_optionsRoot, _optionsGroup, new Vector2(400f, 0f)));
            _localizedTexts.Add((backBtn.GetComponentInChildren<Text>(), "settings.back"));

            CreateLocalizedText(panelGO.transform, "settings.title", new Vector2(0f, 0.85f), new Vector2(1f, 0.98f), TextAnchor.MiddleCenter, 32);

            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(panelGO.transform, false);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = contentRT.anchorMax = new Vector2(0.5f, 0.5f);
            contentRT.sizeDelta = new Vector2(520f, 420f);
            var layout = contentGO.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 18f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            var settings = SettingsManager.Instance;

            _optionsMasterDrag = AddOptionsSlider(contentGO.transform, "settings.master", settings.masterVolume, settings.SetMasterVolume);
            _optionsMusicDrag  = AddOptionsSlider(contentGO.transform, "settings.music",  settings.musicVolume,  settings.SetMusicVolume);
            _optionsSfxDrag    = AddOptionsSlider(contentGO.transform, "settings.sfx",    settings.sfxVolume,    settings.SetSfxVolume);
            AddOptionsResolutionRow(contentGO.transform, settings);
            AddOptionsToggleRow(contentGO.transform, "settings.fullscreen", settings.fullscreen, settings.SetFullscreen);
            AddOptionsLanguageRow(contentGO.transform);

            _optionsSelectedIndex = 0;
            UpdateOptionsRowHighlight();
        }

        /// <summary>Options 面板里的一行，自带可高亮的背景，供十字键/摇杆选中时染黄</summary>
        private RectTransform CreateOptionsRow(Transform parent, float height)
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
            _optionsRowBg.Add(bg);
            return rt;
        }

        private DebugSliderDrag AddOptionsSlider(Transform parent, string labelKey, float initial, Action<float> setter)
        {
            var row = CreateOptionsRow(parent, 60f);

            CreateLocalizedText(row.transform, labelKey, new Vector2(0f, 0.5f), new Vector2(1f, 1f), TextAnchor.LowerCenter, 18);

            var barGO = new GameObject("Bar", typeof(RectTransform));
            barGO.transform.SetParent(row.transform, false);
            var barRT = barGO.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0f, 0f);
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
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.62f, 0.95f, 1f);

            var dragger = barGO.AddComponent<DebugSliderDrag>();
            dragger.Init(barRT, fillRT, 0f, 1f, setter);
            return dragger;
        }

        private void AddOptionsResolutionRow(Transform parent, SettingsManager settings)
        {
            var row = CreateOptionsRow(parent, 50f);

            CreateLocalizedText(row.transform, "settings.display", new Vector2(0f, 0f), new Vector2(0.4f, 1f), TextAnchor.MiddleLeft, 18);

            var prevBtn = CreateButton(row.transform, "<", new Vector2(50f, 40f), ButtonColor);
            var prevRT = prevBtn.GetComponent<RectTransform>();
            prevRT.anchorMin = prevRT.anchorMax = new Vector2(0.45f, 0.5f);

            var labelGO = CreateText(row.transform, ResolutionLabel(settings), new Vector2(0.53f, 0f), new Vector2(0.82f, 1f), TextAnchor.MiddleCenter, 16);
            _optionsResolutionLabel = labelGO.GetComponent<Text>();

            var nextBtn = CreateButton(row.transform, ">", new Vector2(50f, 40f), ButtonColor);
            var nextRT = nextBtn.GetComponent<RectTransform>();
            nextRT.anchorMin = nextRT.anchorMax = new Vector2(0.9f, 0.5f);

            prevBtn.onClick.AddListener(() =>
            {
                settings.SetResolutionIndex((settings.resolutionIndex - 1 + settings.CommonResolutions.Length) % settings.CommonResolutions.Length);
                _optionsResolutionLabel.text = ResolutionLabel(settings);
            });
            nextBtn.onClick.AddListener(() =>
            {
                settings.SetResolutionIndex((settings.resolutionIndex + 1) % settings.CommonResolutions.Length);
                _optionsResolutionLabel.text = ResolutionLabel(settings);
            });
        }

        private static string ResolutionLabel(SettingsManager settings)
        {
            var res = settings.CommonResolutions[settings.resolutionIndex];
            return $"{res.x} x {res.y}";
        }

        private void AddOptionsLanguageRow(Transform parent)
        {
            var row = CreateOptionsRow(parent, 50f);

            CreateLocalizedText(row.transform, "settings.language", new Vector2(0f, 0f), new Vector2(0.4f, 1f), TextAnchor.MiddleLeft, 18);

            var prevBtn = CreateButton(row.transform, "<", new Vector2(50f, 40f), ButtonColor);
            var prevRT = prevBtn.GetComponent<RectTransform>();
            prevRT.anchorMin = prevRT.anchorMax = new Vector2(0.45f, 0.5f);

            var loc = LocalizationManager.Instance;
            var labelGO = CreateText(row.transform, loc.LanguageName(loc.CurrentLanguage), new Vector2(0.53f, 0f), new Vector2(0.82f, 1f), TextAnchor.MiddleCenter, 16);
            _optionsLanguageLabel = labelGO.GetComponent<Text>();

            var nextBtn = CreateButton(row.transform, ">", new Vector2(50f, 40f), ButtonColor);
            var nextRT = nextBtn.GetComponent<RectTransform>();
            nextRT.anchorMin = nextRT.anchorMax = new Vector2(0.9f, 0.5f);

            prevBtn.onClick.AddListener(() => loc.CycleLanguage(-1));
            nextBtn.onClick.AddListener(() => loc.CycleLanguage(1));
        }

        private void AddOptionsToggleRow(Transform parent, string labelKey, bool initial, Action<bool> setter)
        {
            var row = CreateOptionsRow(parent, 44f);

            CreateLocalizedText(row.transform, labelKey, new Vector2(0f, 0f), new Vector2(0.6f, 1f), TextAnchor.MiddleLeft, 18);

            var toggleGO = new GameObject("Toggle", typeof(RectTransform));
            toggleGO.transform.SetParent(row.transform, false);
            var toggleRT = toggleGO.GetComponent<RectTransform>();
            toggleRT.anchorMin = new Vector2(0.75f, 0.15f);
            toggleRT.anchorMax = new Vector2(0.95f, 0.85f);
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

            _optionsFullscreenToggle = toggleGO.AddComponent<Toggle>();
            _optionsFullscreenToggle.targetGraphic = bgImg;
            _optionsFullscreenToggle.graphic = checkImg;
            _optionsFullscreenToggle.isOn = initial;
            _optionsFullscreenToggle.onValueChanged.AddListener(v => setter(v));
        }

        // ── 通用按钮 / 文字 / 圆形贴图 ────────────────────────────
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
            colors.highlightedColor = new Color(1.05f, 1.05f, 1.05f, 1f);
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

            // 选中/悬停时外围包一圈黄线，跟关卡选择卡片同一套边框
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

        /// <summary>统一的按钮"聚焦"视觉：放大 + 黄色描边，鼠标悬停和手柄/键盘选中都走这一个方法</summary>
        private void SetButtonFocused(Button btn, bool focused)
        {
            var rt = btn.GetComponent<RectTransform>();
            StartCoroutine(HoverScale(rt, focused ? 1.05f : 1f));
            if (_buttonBorders.TryGetValue(btn, out var borders))
            {
                Color c = focused ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(1f, 0.85f, 0.3f, 0f);
                foreach (var b in borders) b.color = c;
            }
        }

        private IEnumerator PunchScale(RectTransform rt)
        {
            const float duration = 0.12f;
            Vector3 baseScale = Vector3.one;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                rt.localScale = Vector3.Lerp(baseScale * 0.9f, baseScale, k);
                yield return null;
            }
            rt.localScale = baseScale;
        }

        private IEnumerator HoverScale(RectTransform rt, float targetMul)
        {
            Vector3 start = rt.localScale;
            Vector3 target = Vector3.one * targetMul;
            const float duration = 0.1f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                rt.localScale = Vector3.Lerp(start, target, t / duration);
                yield return null;
            }
            rt.localScale = target;
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
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow   = VerticalWrapMode.Overflow;
            t.raycastTarget = false; // 纯文字标签不需要挡点击，之前设置面板标题就因为这个盖住了 Back 按钮
            return go;
        }

        /// <summary>贴在 anchorMin~anchorMax 那条边上的细条，用于拼出一圈高亮边框（不挡住中间内容）</summary>
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
            img.raycastTarget = false; // 纯装饰用的描边，不能挡住按钮/卡片自己的点击
            return img;
        }

        private static Sprite CreateCircleSprite(int size)
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
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(r - dist);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
