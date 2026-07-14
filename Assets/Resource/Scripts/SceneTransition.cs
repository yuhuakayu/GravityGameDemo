using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Resource.Scripts
{
    /// <summary>
    /// 场景转场管理器（虹膜/圆形遮罩版，运行时自建 UI，不需要美术资源）。
    ///
    /// 用法：SceneTransition.Instance.LoadScene("NextLevel");
    /// 流程：虹膜收拢盖黑 → 停留一下 → 加载新场景 → 虹膜张开露出新场景。
    /// 玩家控制的禁用/恢复由调用方（比如 GoalDoor）负责，不属于这个通用系统的职责，
    /// 可以传 onComplete 回调，在虹膜张开完成后再恢复操作。
    /// </summary>
    public class SceneTransition : MonoBehaviour
    {
        private static SceneTransition _instance;
        public static SceneTransition Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("SceneTransition (Auto)");
                    go.AddComponent<SceneTransition>(); // Awake 里会赋值 _instance
                }
                return _instance;
            }
        }

        [Header("时长（秒）")]
        public float closeDuration = 0.4f;
        public float holdDuration  = 0.15f;
        public float openDuration  = 0.4f;

        private const float BaseCircleSize = 100f;
        private readonly Vector2 _refResolution = new Vector2(1920f, 1080f);

        private RectTransform _iris;
        private float _maxScale;
        private bool  _isTransitioning;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            BuildUI();
        }

        /// <summary>加载新场景并播放虹膜转场。onComplete 会在虹膜完全张开后调用（适合在里面恢复玩家操作）。</summary>
        public void LoadScene(string sceneName, Action onComplete = null)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning("[SceneTransition] 上一次转场还没结束，忽略这次调用。");
                return;
            }
            StartCoroutine(DoTransition(sceneName, onComplete));
        }

        private IEnumerator DoTransition(string sceneName, Action onComplete)
        {
            _isTransitioning = true;

            SfxManager.Instance.PlaySceneTransition();

            yield return AnimateIris(0f, _maxScale, closeDuration);
            yield return new WaitForSecondsRealtime(holdDuration);

            SceneManager.LoadScene(sceneName);
            yield return null; // 等一帧，确保新场景的对象都已经 Awake/Start

            yield return AnimateIris(_maxScale, 0f, openDuration);

            _isTransitioning = false;
            onComplete?.Invoke();
        }

        private IEnumerator AnimateIris(float fromScale, float toScale, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
                k = k * k * (3f - 2f * k); // smoothstep，收拢/张开更顺滑
                float s = Mathf.Lerp(fromScale, toScale, k);
                _iris.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            _iris.localScale = new Vector3(toScale, toScale, 1f);
        }

        // ── 运行时搭 UI ─────────────────────────────────────────
        private void BuildUI()
        {
            var canvasGO = new GameObject("TransitionCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000; // 盖在 HUD / 暂停菜单之上

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _refResolution;
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            var irisGO = new GameObject("Iris");
            irisGO.transform.SetParent(canvasGO.transform, false);
            _iris = irisGO.AddComponent<RectTransform>();
            _iris.anchorMin = _iris.anchorMax = new Vector2(0.5f, 0.5f);
            _iris.pivot = new Vector2(0.5f, 0.5f);
            _iris.sizeDelta = new Vector2(BaseCircleSize, BaseCircleSize);
            _iris.anchoredPosition = Vector2.zero;

            var img = irisGO.AddComponent<Image>();
            img.sprite = CreateCircleSprite(128);
            img.color = Color.black;

            float refDiag = Mathf.Sqrt(_refResolution.x * _refResolution.x + _refResolution.y * _refResolution.y);
            _maxScale = (refDiag * 1.3f) / BaseCircleSize;

            _iris.localScale = Vector3.zero; // 默认全开，不挡屏幕
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
                    float alpha = Mathf.Clamp01(r - dist); // 约 1px 羽化边缘
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
