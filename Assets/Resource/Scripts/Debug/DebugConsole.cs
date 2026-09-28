using System.Collections.Generic;
using Resource.Scripts.Gyro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Debugging
{
    [DefaultExecutionOrder(-250)]
    public sealed class DebugConsole : MonoBehaviour
    {
        private static DebugConsole _instance;
        private readonly List<BaseInputModule> _disabledModules = new List<BaseInputModule>();
        private readonly Vector2[] _scroll = new Vector2[4];
        private SceneTab _sceneTab;
        private GyroTab _gyroTab;
        private GameParamTab _gameTab;
        private PerformanceTab _performanceTab;
        private GyroDiagnosticsPanel _diagnosticsPanel;
        private GUISkin _skin;
        private Texture2D _background;
        private Font _font;
        private bool _chordHeld;
        private bool _pauseHeld;
        private float _resumeTimeScale;
        private bool _appliedScaleForScene;
        private bool _pendingTimeScale;
        private PlayerController _player;
        private LevelIntroUI _intro;
        private GameHUD _hud;
        private string _saveMessage;
        private Rect _titleBarPixelRect;
        private Rect _windowStartRect;
        private Vector2 _windowPointerStart;
        private Vector2 _windowScreenSize;
        private int _windowControlId;
        private int _resizeEdges;
        private bool _windowPointerActive;

        public static DebugConsole Current => _instance;
        public bool IsOpen { get; private set; }
        public GyroRuntime Runtime { get; private set; }
        public WorldRotator World { get; private set; }
        public GyroDiagnosticsPanel DiagnosticsPanel => _diagnosticsPanel;
        public float ContentWidth { get; private set; }
        public float EffectiveUiScale { get; private set; }
        public Rect PanelPixelRect => ConsoleWindowLayout.GetRect(Runtime.Settings, WindowScreenSize);
        private static Vector2 WindowScreenSize => new Vector2(Screen.width, Screen.height);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance == null) new GameObject("Debug Console").AddComponent<DebugConsole>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void Initialize()
        {
            Runtime = GyroRuntime.Instance;
            if (_gyroTab != null) return;
            _sceneTab = new SceneTab();
            _gyroTab = new GyroTab(Runtime);
            _gameTab = new GameParamTab();
            _performanceTab = new PerformanceTab();
            _diagnosticsPanel = new GyroDiagnosticsPanel(Runtime);
        }

        private void OnEnable()
        {
            if (_instance == null) _instance = this;
            if (_instance != this) return;
            Initialize();
            SceneManager.sceneLoaded += OnSceneLoaded;
            CacheScene();
        }

        private void CacheScene()
        {
            World = FindFirstObjectByType<WorldRotator>();
            _player = FindFirstObjectByType<PlayerController>();
            _intro = FindFirstObjectByType<LevelIntroUI>();
            _hud = FindFirstObjectByType<GameHUD>();
            _appliedScaleForScene = false;
            _pendingTimeScale = false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Release the previous scene's pause before the new preview Start freezes gameplay.
            SetOpen(false);
            CacheScene();
        }

        private void Update()
        {
            _performanceTab.Tick(Time.unscaledDeltaTime);
            _diagnosticsPanel.Tick();
            bool chord = GyroRuntime.IsConsoleChordHeld;
            bool toggle = Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame;
            if (toggle || (chord && !_chordHeld)) SetOpen(!IsOpen);
            _chordHeld = chord;
            if (_pendingTimeScale && !OtherSystemPaused && !_pauseHeld)
            {
                Time.timeScale = Runtime.Settings.timeScale;
                _pendingTimeScale = false;
            }
            if (!_appliedScaleForScene && _player != null && _player.IsGameplayActive && Time.timeScale > 0f)
            {
                Time.timeScale = Runtime.Settings.timeScale;
                _appliedScaleForScene = true;
            }
        }

        public void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            GyroRuntime.ConsoleCapturesInput = open;
            if (open)
            {
                // IMGUI does not consume UGUI clicks. Preserve only modules that were enabled.
                _disabledModules.Clear();
                foreach (var module in FindObjectsByType<BaseInputModule>(FindObjectsSortMode.None))
                {
                    if (!module.enabled) continue;
                    _disabledModules.Add(module);
                    module.enabled = false;
                }
                if (Runtime.Settings.pauseOnOpen) AcquirePause();
            }
            else
            {
                ReleaseWindowPointer();
                ReleasePause();
                foreach (var module in _disabledModules) if (module != null) module.enabled = true;
                _disabledModules.Clear();
            }
        }

        private bool OtherSystemPaused => (_intro != null && _intro.IsPreviewing) ||
            (_hud != null && (_hud.IsPaused || _hud.IsSettingsOpen));

        private void AcquirePause()
        {
            if (_pauseHeld) return;
            _resumeTimeScale = Time.timeScale;
            _pauseHeld = true;
            Time.timeScale = 0f;
        }

        private void ReleasePause()
        {
            if (!_pauseHeld) return;
            Time.timeScale = OtherSystemPaused ? 0f : _resumeTimeScale;
            _pauseHeld = false;
        }

        public void SetTimeScale(float scale)
        {
            scale = Mathf.Clamp(scale, 0f, 4f);
            Runtime.Settings.timeScale = scale;
            if (OtherSystemPaused) { _pendingTimeScale = true; return; }
            _pendingTimeScale = false;
            if (_pauseHeld) _resumeTimeScale = scale;
            else Time.timeScale = scale;
        }

        public void SetPauseOnOpen(bool enabled)
        {
            Runtime.Settings.pauseOnOpen = enabled;
            if (!IsOpen) return;
            if (enabled) AcquirePause(); else ReleasePause();
        }

        public void JumpToScene(string path)
        {
            if (SceneTransition.Instance.IsTransitioning || string.IsNullOrEmpty(path)) return;
            bool skipPreview = Runtime.Settings.skipPreview;
            SetOpen(false);
            // The existing main menu intentionally destroys itself after the initial entry.
            // An explicit debug jump back to it starts a new menu visit.
            if (System.IO.Path.GetFileNameWithoutExtension(path) == "MainMenu") GameFlowState.HasEnteredGame = false;
            SceneTransition.Instance.LoadScene(path, () =>
            {
                if (!skipPreview) return;
                var intro = FindFirstObjectByType<LevelIntroUI>();
                if (intro != null) intro.BeginGameplayFromPreview();
            });
        }

        private void EnsureSkin()
        {
            if (_skin != null) return;
            _skin = Instantiate(GUI.skin);
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 16);
            _skin.font = _font;
            _skin.label.fontSize = 16;
            _skin.label.normal.textColor = new Color(0.9f, 0.94f, 0.98f);
            _skin.label.wordWrap = true;
            _skin.button.fontSize = _skin.toggle.fontSize = _skin.textField.fontSize = 16;
            _skin.button.padding = new RectOffset(10, 10, 6, 6);
            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0.035f, 0.045f, 0.065f, 0.96f));
            _background.Apply();
            _skin.box.normal.background = _background;
            _skin.box.padding = new RectOffset(12, 12, 10, 10);
        }

        private static int ResizeEdgesAt(Rect rect, Vector2 pointer, float grip)
        {
            Rect hitBounds = Rect.MinMaxRect(rect.xMin - grip, rect.yMin - grip,
                rect.xMax + grip, rect.yMax + grip);
            if (!hitBounds.Contains(pointer)) return 0;
            int edges = 0;
            if (Mathf.Abs(pointer.x - rect.xMin) <= grip) edges |= 1;
            else if (Mathf.Abs(pointer.x - rect.xMax) <= grip) edges |= 2;
            if (Mathf.Abs(pointer.y - rect.yMin) <= grip) edges |= 4;
            else if (Mathf.Abs(pointer.y - rect.yMax) <= grip) edges |= 8;
            return edges;
        }

        private void ReleaseWindowPointer()
        {
            if (_windowPointerActive && GUIUtility.hotControl == _windowControlId)
                GUIUtility.hotControl = 0;
            _windowPointerActive = false;
            _resizeEdges = 0;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ReleaseWindowPointer();
        }

        private void HandleWindowPointer(Rect rect, float scale)
        {
            _windowControlId = GUIUtility.GetControlID(0x47594357, FocusType.Passive);
            Event current = Event.current;
            if (_windowPointerActive && (_windowScreenSize != WindowScreenSize ||
                GUIUtility.hotControl != _windowControlId)) ReleaseWindowPointer();

            switch (current.GetTypeForControl(_windowControlId))
            {
                case EventType.MouseDown:
                    if (current.button != 0 || GUIUtility.hotControl != 0) break;
                    int edges = ResizeEdgesAt(rect, current.mousePosition, Mathf.Clamp(6f * scale, 6f, 16f));
                    if (edges == 0 && !_titleBarPixelRect.Contains(current.mousePosition)) break;
                    _windowPointerActive = true;
                    _resizeEdges = edges;
                    _windowPointerStart = current.mousePosition;
                    _windowStartRect = rect;
                    _windowScreenSize = WindowScreenSize;
                    GUIUtility.hotControl = _windowControlId;
                    GUIUtility.keyboardControl = 0;
                    current.Use();
                    break;
                case EventType.MouseDrag:
                    if (!_windowPointerActive || current.button != 0) break;
                    Vector2 delta = current.mousePosition - _windowPointerStart;
                    Rect next = _windowStartRect;
                    if (_resizeEdges == 0) next.position += delta;
                    else next = ConsoleWindowLayout.ResizeRect(next, delta, _resizeEdges, WindowScreenSize);
                    ConsoleWindowLayout.StoreRect(Runtime.Settings, next, WindowScreenSize);
                    _saveMessage = null;
                    current.Use();
                    break;
                case EventType.MouseUp:
                    if (!_windowPointerActive || current.button != 0) break;
                    ReleaseWindowPointer();
                    current.Use();
                    break;
                case EventType.KeyDown:
                    if (!_windowPointerActive || current.keyCode != KeyCode.Escape) break;
                    ConsoleWindowLayout.StoreRect(Runtime.Settings, _windowStartRect, WindowScreenSize);
                    ReleaseWindowPointer();
                    current.Use();
                    break;
            }
        }

        private void DrawWindowFrame(Rect rect, float scale)
        {
            if (Event.current.type != EventType.Repaint) return;
            int edges = _windowPointerActive ? _resizeEdges :
                ResizeEdgesAt(rect, Event.current.mousePosition, Mathf.Clamp(6f * scale, 6f, 16f));
            Color oldColor = GUI.color;
            Color border = new Color(0.3f, 0.38f, 0.46f, 0.85f);
            Color highlight = new Color(0.3f, 0.85f, 1f);
            float line = Mathf.Max(1f, scale);
            GUI.color = (edges & 1) != 0 ? highlight : border;
            GUI.DrawTexture(new Rect(rect.x, rect.y, line, rect.height), Texture2D.whiteTexture);
            GUI.color = (edges & 2) != 0 ? highlight : border;
            GUI.DrawTexture(new Rect(rect.xMax - line, rect.y, line, rect.height), Texture2D.whiteTexture);
            GUI.color = (edges & 4) != 0 ? highlight : border;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, line), Texture2D.whiteTexture);
            GUI.color = (edges & 8) != 0 ? highlight : border;
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - line, rect.width, line), Texture2D.whiteTexture);
            GUI.color = (edges & 10) != 0 ? highlight : new Color(0.6f, 0.7f, 0.8f);
            for (int i = 0; i < 3; i++)
                GUI.DrawTexture(new Rect(rect.xMax - (5f + i * 4f) * scale,
                    rect.yMax - (5f + i * 4f) * scale, (2f + i * 4f) * scale, line), Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnsureSkin();
            GUISkin oldSkin = GUI.skin;
            Matrix4x4 oldMatrix = GUI.matrix;
            int oldDepth = GUI.depth;
            GUI.skin = _skin;
            Rect pixelBounds = PanelPixelRect;
            // Screen coverage is independent of text scale. Fit the controls when the window is small.
            float scale = Mathf.Min(Runtime.Settings.uiScale, pixelBounds.width / 600f, pixelBounds.height / 460f);
            EffectiveUiScale = Mathf.Max(0.1f, scale);
            scale = EffectiveUiScale;
            GUI.matrix = Matrix4x4.identity;
            HandleWindowPointer(pixelBounds, scale);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            GUI.depth = -2000;
            Rect bounds = new Rect(pixelBounds.x / scale, pixelBounds.y / scale,
                pixelBounds.width / scale, pixelBounds.height / scale);
            ContentWidth = Mathf.Max(1f, bounds.width - 44f);
            GUILayout.BeginArea(bounds, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("调试控制台 · 拖动标题栏移动", "~ / Options + Create 开关；拖动边缘或角落调整大小"),
                GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                Rect title = GUILayoutUtility.GetLastRect();
                _titleBarPixelRect = new Rect(pixelBounds.x + title.x * scale,
                    pixelBounds.y + title.y * scale, title.width * scale, title.height * scale);
            }
            if (GUILayout.Button("重置窗口", GUILayout.Width(88)))
            {
                ConsoleWindowLayout.Reset(Runtime.Settings);
                _saveMessage = null;
            }
            if (GUILayout.Button("保存设置", GUILayout.Width(96)))
            {
                Runtime.SaveSettings();
                _saveMessage = Runtime.SettingsStatus;
            }
            if (GUILayout.Button("X", GUILayout.Width(42))) SetOpen(false);
            GUILayout.EndHorizontal();
            Runtime.Settings.selectedTab = GUILayout.Toolbar(Runtime.Settings.selectedTab,
                new[] { "场景跳转", "陀螺仪", "游戏参数", "性能" }, GUILayout.Height(34));
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent($"占比 {pixelBounds.width / Screen.width:P0} × {pixelBounds.height / Screen.height:P0}",
                "宽 × 高；拖动边缘可分别调整，滑条同时调整宽高"), GUILayout.Width(160));
            float nextFraction = GUILayout.HorizontalSlider(
                Runtime.Settings.consoleScreenFraction, 0.4f, 1f, GUILayout.MinWidth(90));
            bool applyFraction = !Mathf.Approximately(nextFraction, Runtime.Settings.consoleScreenFraction);
            if (GUILayout.Button("50%", GUILayout.Width(52))) { nextFraction = 0.5f; applyFraction = true; }
            if (GUILayout.Button("70%", GUILayout.Width(52))) { nextFraction = 0.7f; applyFraction = true; }
            if (GUILayout.Button("100%", GUILayout.Width(58))) { nextFraction = 1f; applyFraction = true; }
            if (applyFraction)
            {
                ConsoleWindowLayout.ApplyFraction(Runtime.Settings, nextFraction, WindowScreenSize);
                _saveMessage = null;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"UI 缩放 {Runtime.Settings.uiScale:F2}x", GUILayout.Width(140));
            Runtime.Settings.uiScale = GUILayout.HorizontalSlider(Runtime.Settings.uiScale, 0.75f, 3f, GUILayout.MinWidth(90));
            if (scale < Runtime.Settings.uiScale - 0.01f)
                GUILayout.Label($"适配窗口：{scale:F2}x", GUILayout.Width(155));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"时间 {Runtime.Settings.timeScale:F2}x", GUILayout.Width(110));
            float nextTime = GUILayout.HorizontalSlider(Runtime.Settings.timeScale, 0f, 4f, GUILayout.MinWidth(90));
            if (!Mathf.Approximately(nextTime, Runtime.Settings.timeScale)) SetTimeScale(nextTime);
            if (GUILayout.Button("1x", GUILayout.Width(44))) SetTimeScale(1f);
            if (GUILayout.Button("2x", GUILayout.Width(44))) SetTimeScale(2f);
            if (GUILayout.Button("4x", GUILayout.Width(44))) SetTimeScale(4f);
            bool pause = GUILayout.Toggle(Runtime.Settings.pauseOnOpen, "打开时暂停", GUILayout.Width(140));
            if (pause != Runtime.Settings.pauseOnOpen) SetPauseOnOpen(pause);
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_saveMessage)) GUILayout.Label(_saveMessage);
            if (OtherSystemPaused) GUILayout.Label("预览 / 游戏菜单持有暂停；调整时间倍率不会解除它的暂停。");
            GUILayout.Space(10);
            int tab = Runtime.Settings.selectedTab;
            _scroll[tab] = GUILayout.BeginScrollView(_scroll[tab]);
            switch (tab)
            {
                case 0: _sceneTab.Draw(this); break;
                case 1: _gyroTab.Draw(this); break;
                case 2: _gameTab.Draw(this); break;
                case 3: _performanceTab.Draw(this); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
            DrawWindowFrame(pixelBounds, scale);
            GUI.matrix = oldMatrix;
            GUI.skin = oldSkin;
            GUI.depth = oldDepth;
        }

        public void DrawDiagnostics() { _diagnosticsPanel.Draw(this); }

        private void OnDisable()
        {
            SetOpen(false);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _gyroTab?.Dispose();
            _gyroTab = null;
            _diagnosticsPanel?.Dispose();
            _diagnosticsPanel = null;
        }
        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_skin != null) Destroy(_skin);
            if (_font != null) Destroy(_font);
            if (_background != null) Destroy(_background);
        }
    }
}
