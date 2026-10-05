using UnityEngine;

namespace Resource.Scripts.SceneFX
{
    /// <summary>
    /// 两层背景视差：背景比关卡移动得慢，看起来更远。
    /// 以地图中心为基准：镜头离地图中心多远，背景就往同一方向错开 factor 倍（不超过 maxOffset 格）。
    /// 背景物体放在 MazeGrid 下（跟世界一起转），偏移按屏幕方向算，再换成本地坐标。
    /// </summary>
    public class BackgroundParallax : MonoBehaviour
    {
        [System.Serializable]
        public class Layer
        {
            public Transform target;
            [Tooltip("错开比例：0.18 = 背景移动速度是关卡的 82%")]
            public float factor = 0.18f;
            [Tooltip("最多错开多少格")]
            public float maxOffset = 2f;
            [HideInInspector] public Vector3 basePosition;
        }

        [Tooltip("地图中心（MazeGrid 的原点就是地图中心）")]
        public Transform mapCenter;
        public Layer[] layers = new Layer[0];

        private Camera _cam;

        private void Start()
        {
            _cam = Camera.main;
            foreach (var layer in layers)
                if (layer.target != null) layer.basePosition = layer.target.localPosition;
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null || mapCenter == null) return;
            Vector2 camOffset = (Vector2)_cam.transform.position - (Vector2)mapCenter.position;
            foreach (var layer in layers)
            {
                if (layer.target == null) continue;
                Vector2 offset = camOffset * layer.factor;
                if (offset.magnitude > layer.maxOffset) offset = offset.normalized * layer.maxOffset;
                Transform parent = layer.target.parent;
                Vector3 local = parent != null ? parent.InverseTransformVector(offset) : (Vector3)offset;
                layer.target.localPosition = layer.basePosition + local;
            }
        }
    }
}
