using TMPro;
using UnityEngine;

namespace Resource.Scripts.UI.Menu
{
    public sealed class MenuTextOutline : MonoBehaviour
    {
        readonly TextMeshProUGUI[] copies = new TextMeshProUGUI[8];
        TextMeshProUGUI source;

        public static MenuTextOutline Attach(TextMeshProUGUI source)
        {
            var outline = source.GetComponent<MenuTextOutline>();
            if (outline != null) return outline;
            outline = source.gameObject.AddComponent<MenuTextOutline>();
            outline.source = source;
            for (int i = 0; i < outline.copies.Length; i++)
            {
                var copy = new GameObject(source.name + " Outline " + i, typeof(RectTransform), typeof(TextMeshProUGUI))
                    .GetComponent<TextMeshProUGUI>();
                copy.rectTransform.SetParent(source.rectTransform.parent, false);
                copy.raycastTarget = false;
                copy.enableAutoSizing = false;
                outline.copies[i] = copy;
            }
            outline.SetVisible(outline.isActiveAndEnabled);
            outline.LateUpdate();
            return outline;
        }

        void LateUpdate()
        {
            RectTransform rect = source.rectTransform;
            int index = 0;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                TextMeshProUGUI copy = copies[index++];
                RectTransform copyRect = copy.rectTransform;
                if (copyRect.parent != rect.parent) copyRect.SetParent(rect.parent, false);
                if (copyRect.GetSiblingIndex() > rect.GetSiblingIndex()) copyRect.SetSiblingIndex(rect.GetSiblingIndex());
                copyRect.anchorMin = rect.anchorMin;
                copyRect.anchorMax = rect.anchorMax;
                copyRect.pivot = rect.pivot;
                copyRect.sizeDelta = rect.sizeDelta;
                float pixel = source.fontSize < 30f ? 2f : 3f;
                copyRect.anchoredPosition3D = rect.anchoredPosition3D + new Vector3(x * pixel, y * pixel, 0f);
                copyRect.localRotation = rect.localRotation;
                copyRect.localScale = rect.localScale;
                copy.enabled = source.enabled;
                copy.text = source.text;
                copy.font = source.font;
                copy.fontSharedMaterial = source.fontSharedMaterial;
                copy.fontSize = source.fontSize;
                copy.fontStyle = source.fontStyle;
                copy.characterSpacing = source.characterSpacing;
                copy.alignment = source.alignment;
                copy.textWrappingMode = source.textWrappingMode;
                copy.overflowMode = source.overflowMode;
                copy.margin = source.margin;
                copy.fontFeatures.Clear();
                copy.fontFeatures.AddRange(source.fontFeatures);
                copy.color = new Color(24f / 255f, 22f / 255f, 46f / 255f, source.color.a);
            }
        }

        void OnEnable() => SetVisible(true);
        void OnDisable() => SetVisible(false);

        void SetVisible(bool visible)
        {
            foreach (TextMeshProUGUI copy in copies)
                if (copy != null) copy.gameObject.SetActive(visible);
        }

        void OnDestroy()
        {
            foreach (TextMeshProUGUI copy in copies)
                if (copy != null) Destroy(copy.gameObject);
        }
    }
}
