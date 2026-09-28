using UnityEngine;
using UnityEngine.UI;

namespace Resource.Scripts
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PixelUIBackground : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            Color top = new Color32(0x23, 0x22, 0x3A, 255);
            Color bottom = new Color32(0x15, 0x14, 0x1F, 255);
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    Color shade = y == 0 ? bottom : top;
                    if (x != 1) shade = new Color(shade.r * 0.83f, shade.g * 0.83f, shade.b * 0.83f, 1f);
                    mesh.AddVert(new Vector3(rect.xMin + rect.width * x * 0.5f, y == 0 ? rect.yMin : rect.yMax), shade, Vector2.zero);
                }
            }
            mesh.AddTriangle(0, 3, 4);
            mesh.AddTriangle(0, 4, 1);
            mesh.AddTriangle(1, 4, 5);
            mesh.AddTriangle(1, 5, 2);
        }
    }
}
