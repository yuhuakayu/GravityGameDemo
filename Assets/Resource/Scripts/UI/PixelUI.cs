using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Resource.Scripts
{
    public static class PixelUI
    {
        private static PixelUITheme _theme;
        public static PixelUITheme Theme => _theme != null ? _theme : (_theme = Resources.Load<PixelUITheme>("PixelUITheme"));

        public static readonly Color TextDark = new Color32(0x25, 0x24, 0x3A, 255);
        public static readonly Color TextLight = new Color32(0xCF, 0xCC, 0xE3, 255);
        public static readonly Color PanelColor = new Color32(0x3A, 0x39, 0x5A, 255);
        public static readonly Color DividerColor = new Color32(0x6E, 0x6C, 0x97, 255);
        public static readonly Color Accent = new Color32(0xF4, 0xA8, 0xB8, 255);
        public static readonly Color OxygenNormal = new Color32(0x3F, 0xD6, 0xE0, 255);
        public static readonly Color OxygenLow = Accent;

        public static void ConfigureCanvas(Canvas canvas)
        {
            canvas.pixelPerfect = true;
            canvas.pixelPerfect = true;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
        }

        public static TextMeshProUGUI EnsureText(GameObject target, int size, bool bold, TextAnchor align, Color color)
        {
            var legacy = target.GetComponent<Text>();
            string previousText = legacy != null ? legacy.text : null;
            // A GameObject can have only one Graphic; remove the old text before adding TMP.
            if (legacy != null) Object.DestroyImmediate(legacy);
            var text = target.GetComponent<TextMeshProUGUI>();
            if (text == null) text = target.AddComponent<TextMeshProUGUI>();
            if (previousText != null) text.text = previousText;
            text.font = bold ? Theme.boldFont : Theme.regularFont;
            text.fontStyle = FontStyles.Normal;
            text.fontFeatures.Clear(); // 位图字形不使用会产生小数间距的矢量字距调整。
            text.fontFeatures.Clear(); // 位图字形不使用会产生小数间距的矢量字距调整。
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            switch (align)
            {
                case TextAnchor.UpperLeft: text.alignment = TextAlignmentOptions.TopLeft; break;
                case TextAnchor.UpperCenter: text.alignment = TextAlignmentOptions.Top; break;
                case TextAnchor.UpperRight: text.alignment = TextAlignmentOptions.TopRight; break;
                case TextAnchor.MiddleLeft: text.alignment = TextAlignmentOptions.MidlineLeft; break;
                case TextAnchor.MiddleCenter: text.alignment = TextAlignmentOptions.Midline; break;
                case TextAnchor.MiddleRight: text.alignment = TextAlignmentOptions.MidlineRight; break;
                case TextAnchor.LowerLeft: text.alignment = TextAlignmentOptions.BottomLeft; break;
                case TextAnchor.LowerCenter: text.alignment = TextAlignmentOptions.Bottom; break;
                default: text.alignment = TextAlignmentOptions.BottomRight; break;
            }
            return text;
        }

        public static void StyleButton(Button button)
        {
            var image = button.GetComponent<Image>();
            if (image == null) image = button.gameObject.AddComponent<Image>();
            image.sprite = Theme.buttonNormal;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = Theme.buttonSelected,
                pressedSprite = Theme.buttonSelected,
                selectedSprite = Theme.buttonSelected,
                disabledSprite = Theme.buttonDisabled
            };
        }

        public static void StyleArrow(Button button, bool right)
        {
            var image = button.GetComponent<Image>();
            if (image == null) image = button.gameObject.AddComponent<Image>();
            image.sprite = right ? Theme.arrowRight : Theme.arrowLeft;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = image.sprite,
                pressedSprite = image.sprite,
                selectedSprite = image.sprite,
                disabledSprite = right ? Theme.arrowRightDisabled : Theme.arrowLeftDisabled
            };
        }

        public static void StyleToggle(Toggle toggle)
        {
            var background = toggle.targetGraphic as Image;
            if (background != null)
            {
                background.sprite = Theme.checkOff;
                background.type = Image.Type.Simple;
                background.preserveAspect = true;
                background.color = Color.white;
            }
            var check = toggle.graphic as Image;
            if (check != null)
            {
                check.sprite = Theme.checkOn;
                check.type = Image.Type.Simple;
                check.preserveAspect = true;
                check.color = Color.white;
                check.rectTransform.anchorMin = Vector2.zero;
                check.rectTransform.anchorMax = Vector2.one;
                check.rectTransform.offsetMin = check.rectTransform.offsetMax = Vector2.zero;
            }
            toggle.transition = Selectable.Transition.None;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
        }

        public static void SetFocused(Selectable selectable, bool focused)
        {
            if (selectable == null) return;
            if (focused) selectable.OnSelect(null);
            else selectable.OnDeselect(null);
        }

        public static void Backdrop(Transform parent)
        {
            var existing = parent.Find("PixelBackground");
            var go = existing != null ? existing.gameObject : new GameObject("PixelBackground", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var graphic = go.GetComponent<PixelUIBackground>();
            if (graphic == null) graphic = go.AddComponent<PixelUIBackground>();
            graphic.raycastTarget = false;
        }
    }
}
