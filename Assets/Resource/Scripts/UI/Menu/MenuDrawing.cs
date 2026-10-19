using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Resource.Scripts.UI.Menu
{
    public static class MenuDrawing
    {
        public static Color RGB(int r, int g, int b, float alpha = 1f) => new Color(r / 255f, g / 255f, b / 255f, alpha);

        public static RectTransform Node(Transform parent, string name, float x, float y, float width, float height, bool center = true)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = center ? Vector2.one * 0.5f : new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            Position(rect, x, y);
            return rect;
        }

        public static void Position(RectTransform rect, float x, float y) => rect.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
        public static void Rotation(RectTransform rect, float radians) => rect.localRotation = Quaternion.Euler(0f, 0f, -radians * Mathf.Rad2Deg);

        public static Image Image(Transform parent, string name, Sprite sprite, float x, float y, float width, float height, bool center = true)
        {
            var image = Node(parent, name, x, y, width, height, center).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        public static Image Box(Transform parent, string name, Color color, float x, float y, float width, float height, bool center = true)
        {
            var image = Image(parent, name, null, x, y, width, height, center);
            image.color = color;
            return image;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string value, float x, float y, int size, bool bold,
            Color color, float spacing = 0f, bool outline = false, bool center = true)
        {
            var rect = Node(parent, name, x, y, 800f, size * 1.7f, center);
            var text = PixelUI.EnsureText(rect.gameObject, size, bold, center ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft, color);
            text.characterSpacing = spacing * 3f;
            text.text = value;
            text.ForceMeshUpdate();
            rect.sizeDelta = new Vector2(Mathf.Ceil(text.preferredWidth + 6f), size * 1.7f);
            if (outline)
                MenuTextOutline.Attach(text);
            return text;
        }

        public static void Fit(TextMeshProUGUI text)
        {
            text.ForceMeshUpdate();
            var size = text.rectTransform.sizeDelta;
            text.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(text.preferredWidth + 6f), size.y);
        }

        public static void Line(RectTransform rect, Vector2 from, Vector2 to, float width)
        {
            Vector2 delta = to - from;
            Position(rect, (from.x + to.x) * 0.5f, (from.y + to.y) * 0.5f);
            rect.sizeDelta = new Vector2(width, delta.magnitude);
            Rotation(rect, Mathf.Atan2(-delta.x, delta.y));
        }
    }
}
