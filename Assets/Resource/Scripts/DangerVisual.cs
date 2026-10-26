using UnityEngine;
using UnityEngine.Tilemaps;

namespace Resource.Scripts
{
    [DisallowMultipleComponent]
    public sealed class DangerVisual : MonoBehaviour
    {
        public static Color Red(float alpha = 1f) => new Color(1f, .24f, .32f, alpha);

        private SpriteRenderer[] sprites;
        private LineRenderer[] lines;
        private Tilemap[] maps;
        private Color[] spriteColors, lineStarts, lineEnds, mapColors;

        private void Awake()
        {
            sprites = GetComponentsInChildren<SpriteRenderer>(true);
            lines = GetComponentsInChildren<LineRenderer>(true);
            maps = GetComponentsInChildren<Tilemap>(true);
            spriteColors = new Color[sprites.Length];
            mapColors = new Color[maps.Length];
            lineStarts = new Color[lines.Length]; lineEnds = new Color[lines.Length];
            for (int i = 0; i < sprites.Length; i++) spriteColors[i] = sprites[i].color;
            for (int i = 0; i < maps.Length; i++) mapColors[i] = maps[i].color;
            for (int i = 0; i < lines.Length; i++) { lineStarts[i] = lines[i].startColor; lineEnds[i] = lines[i].endColor; }
            Apply();
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (sprites == null) return;
            for (int i = 0; i < sprites.Length; i++) if (sprites[i] != null) sprites[i].color = Red(spriteColors[i].a);
            for (int i = 0; i < maps.Length; i++) if (maps[i] != null) maps[i].color = Red(mapColors[i].a);
            for (int i = 0; i < lines.Length; i++) if (lines[i] != null)
            {
                lines[i].startColor = RedLine(lineStarts[i]);
                lines[i].endColor = RedLine(lineEnds[i]);
            }
        }

        private static Color RedLine(Color original)
        {
            float light = Mathf.Max(original.r, original.g, original.b);
            return new Color(light, light * .24f, light * .32f, original.a);
        }

        private void OnDisable()
        {
            if (sprites == null) return;
            for (int i = 0; i < sprites.Length; i++) if (sprites[i] != null) sprites[i].color = spriteColors[i];
            for (int i = 0; i < maps.Length; i++) if (maps[i] != null) maps[i].color = mapColors[i];
            for (int i = 0; i < lines.Length; i++) if (lines[i] != null)
            { lines[i].startColor = lineStarts[i]; lines[i].endColor = lineEnds[i]; }
        }
    }
}
