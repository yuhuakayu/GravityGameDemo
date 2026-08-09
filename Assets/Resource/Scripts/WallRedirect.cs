using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 挂在"紧迫感"关卡的墙上：PlayerController.autoMoveMode 打开时，玩家撞到这面墙会被
    /// 强制改成往这个角度移动（世界旋转是玩家唯一能操作的事——转世界＝改变接下来撞哪面墙）。
    /// 自带一个程序生成的箭头贴图，没有真美术资源也能在场景里直接看出方向。
    /// </summary>
    public class WallRedirect : MonoBehaviour
    {
        [Tooltip("玩家撞到这面墙之后，会被强制改成往这个角度移动（0=右，90=上，180=左，270=下）")]
        public float redirectAngle = 0f;
        [Tooltip("箭头贴图颜色")]
        public Color arrowColor = new Color(1f, 0.85f, 0.3f, 1f);
        [Tooltip("箭头贴图大小（世界单位）")]
        public float arrowSize = 1f;

        /// <summary>
        /// 世界空间的实际弹开方向。redirectAngle 是相对这面墙自身的角度（跟箭头贴图的
        /// localRotation 同一套坐标系），这里再叠加墙当前的 transform.rotation——
        /// 墙是 WorldRoot 的子物体，世界转多少墙就跟着转多少，这样方向才会跟箭头贴图
        /// 实际指向的地方保持一致，而不是用一个转世界之前就定死的绝对角度。
        /// </summary>
        public Vector2 RedirectDirection
        {
            get
            {
                Vector3 localDir = Quaternion.Euler(0f, 0f, redirectAngle) * Vector3.right;
                Vector3 worldDir = transform.rotation * localDir;
                return new Vector2(worldDir.x, worldDir.y).normalized;
            }
        }

        void Start()
        {
            BuildArrowVisual();
        }

        private void BuildArrowVisual()
        {
            var arrowGO = new GameObject("RedirectArrow (Auto)");
            arrowGO.transform.SetParent(transform, false);
            arrowGO.transform.localPosition = Vector3.zero;
            arrowGO.transform.localRotation = Quaternion.Euler(0f, 0f, redirectAngle);
            arrowGO.transform.localScale = Vector3.one * arrowSize;

            var sr = arrowGO.AddComponent<SpriteRenderer>();
            sr.sprite = CreateArrowSprite(48);
            sr.color = arrowColor;
            sr.sortingOrder = 20;
        }

        /// <summary>箭头贴图：本地坐标系里指向 +X，靠 Transform 旋转到 redirectAngle</summary>
        private static Sprite CreateArrowSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size;
                    float ny = (y + 0.5f) / size - 0.5f;

                    float alpha = 0f;
                    if (nx < 0.55f)
                    {
                        if (Mathf.Abs(ny) < 0.12f) alpha = 1f; // 箭杆
                    }
                    else
                    {
                        float headT = Mathf.InverseLerp(0.55f, 1f, nx);
                        float halfWidth = Mathf.Lerp(0.3f, 0f, headT);
                        if (Mathf.Abs(ny) < halfWidth) alpha = 1f; // 箭头三角形
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
