using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Resource.Scripts
{
    /// <summary>挂在屏幕 Canvas 上，氧气条只在事件触发时更新数值。</summary>
    [DisallowMultipleComponent]
    public sealed class OxygenHUD : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private PlayerOxygen oxygen;
        [SerializeField] private Image fillImage;
        [SerializeField, HideInInspector] private Text valueText;
        [SerializeField] private TextMeshProUGUI valueLabel;
        [SerializeField] private string label = "O2";

        [Header("颜色与低氧警告")]
        [SerializeField, HideInInspector] private Color normalColor = new Color32(0x3F, 0xD6, 0xE0, 0xFF);
        [SerializeField, HideInInspector] private Color lowOxygenColor = new Color32(0xF4, 0xA8, 0xB8, 0xFF);
        [SerializeField, Range(0f, 1f)] private float lowOxygenThreshold = 0.25f;
        [SerializeField, Min(0f)] private float blinkFrequency = 2f;
        [SerializeField, Range(0f, 1f)] private float minBlinkAlpha = 0.3f;

        private PlayerOxygen _subscribedOxygen;
        private bool _isLow;

        private void Awake()
        {
            ApplyPixelStyle();
        }

        public void ApplyPixelStyle()
        {
            normalColor = PixelUI.OxygenNormal;
            lowOxygenColor = PixelUI.OxygenLow;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) PixelUI.ConfigureCanvas(canvas);

            var panelRect = GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = Vector2.zero;
            panelRect.anchoredPosition = new Vector2(24f, 24f);
            panelRect.sizeDelta = new Vector2(300f, 84f);
            var panel = GetComponent<Image>();
            if (panel != null)
            {
                panel.sprite = PixelUI.Theme.panel;
                panel.type = Image.Type.Sliced;
                panel.color = Color.white;
                panel.raycastTarget = false;
            }

            var labelTransform = valueLabel != null ? valueLabel.transform
                : valueText != null ? valueText.transform : transform.Find("OxygenValue");
            if (labelTransform != null)
            {
                valueLabel = PixelUI.EnsureText(labelTransform.gameObject, 18, false, TextAnchor.MiddleLeft, PixelUI.TextLight);
                valueText = null;
                var labelRect = valueLabel.rectTransform;
                labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = Vector2.zero;
                labelRect.anchoredPosition = new Vector2(18f, 45f);
                labelRect.sizeDelta = new Vector2(264f, 27f);
            }

            if (fillImage == null) return;
            var track = fillImage.transform.parent.GetComponent<Image>();
            track.sprite = PixelUI.Theme.barFrame;
            track.type = Image.Type.Sliced;
            track.color = Color.white;
            track.raycastTarget = false;
            var trackRect = track.rectTransform;
            trackRect.anchorMin = trackRect.anchorMax = trackRect.pivot = Vector2.zero;
            trackRect.anchoredPosition = new Vector2(18f, 18f);
            trackRect.sizeDelta = new Vector2(264f, 24f);

            fillImage.sprite = PixelUI.Theme.barFill;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.raycastTarget = false;
            var fillRect = fillImage.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(6f, 6f);
            fillRect.offsetMax = new Vector2(-6f, -6f);
            UpdateWarningColor();
        }

        private void OnEnable()
        {
            Bind(oxygen);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>换玩家时显式重新绑定；不会逐帧搜索场景。</summary>
        public void Bind(PlayerOxygen source)
        {
            Unsubscribe();
            oxygen = source;
            if (source != null)
            {
                if (isActiveAndEnabled)
                {
                    _subscribedOxygen = source;
                    _subscribedOxygen.OxygenChanged += OnOxygenChanged;
                }
                OnOxygenChanged(source.CurrentOxygen, source.MaxOxygen);
            }
            else
            {
                OnOxygenChanged(0f, 0f);
            }
        }

        private void Unsubscribe()
        {
            if (_subscribedOxygen != null)
                _subscribedOxygen.OxygenChanged -= OnOxygenChanged;
            _subscribedOxygen = null;
        }

        private void OnOxygenChanged(float current, float maximum)
        {
            float normalized = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            _isLow = maximum > 0f && normalized < lowOxygenThreshold;
            if (fillImage != null)
            {
                fillImage.fillAmount = normalized;
                UpdateWarningColor();
            }
            if (valueLabel != null)
                valueLabel.text = $"{label}  {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
        }

        private void Update()
        {
            if (!_isLow || fillImage == null || blinkFrequency <= 0f) return;
            UpdateWarningColor();
        }

        private void UpdateWarningColor()
        {
            if (fillImage == null) return;
            if (!_isLow || blinkFrequency <= 0f)
            {
                fillImage.color = _isLow ? lowOxygenColor : normalColor;
                return;
            }
            // 自然耗氧也会每帧广播；事件与 Update 使用同一颜色计算，避免执行顺序覆盖闪烁。
            float pulse = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * blinkFrequency * Mathf.PI * 2f);
            Color color = lowOxygenColor;
            color.a *= Mathf.Lerp(minBlinkAlpha, 1f, pulse);
            fillImage.color = color;
        }
    }
}
