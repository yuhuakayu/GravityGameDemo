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
        [SerializeField] private Text valueText;
        [SerializeField] private string label = "O2";

        [Header("颜色与低氧警告")]
        [SerializeField] private Color normalColor = new Color(0.2f, 0.85f, 0.95f, 1f);
        [SerializeField] private Color lowOxygenColor = new Color(1f, 0.18f, 0.15f, 1f);
        [SerializeField, Range(0f, 1f)] private float lowOxygenThreshold = 0.25f;
        [SerializeField, Min(0f)] private float blinkFrequency = 2f;
        [SerializeField, Range(0f, 1f)] private float minBlinkAlpha = 0.3f;

        private PlayerOxygen _subscribedOxygen;
        private bool _isLow;

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
            if (valueText != null)
                valueText.text = $"{label}  {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
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
