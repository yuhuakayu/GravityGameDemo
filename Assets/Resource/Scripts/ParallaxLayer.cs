using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 视差滚动层，根据摄像机位移的一定比例移动自己，制造前后景的空间层次感。
    /// factor = 1：跟世界完全同步（相当于普通中景）。
    /// 0 &lt; factor &lt; 1：比摄像机移动得慢 —— 背景，越接近 0 感觉越远。
    /// factor &gt; 1：比摄像机移动得快 —— 前景，制造贴近镜头的错觉。
    /// </summary>
    public class ParallaxLayer : MonoBehaviour
    {
        public float parallaxFactor = 0.3f;
        public Transform cameraTransform;

        [Tooltip("不为空时，这一层会跟着这个 Transform 的旋转同步转动（比如世界旋转的枢轴）——" +
                 "背景贴图属于场景本体的一部分，世界转的时候背景不跟着转会显得背景和前景是两个不同步的图层")]
        public Transform rotationSource;

        [Tooltip("旋转的中心点（一般是玩家）：世界正在转的那几帧，位置会绕这个点转，跟世界旋转的中心保持一致，" +
                 "而不是绕图层自己的锚点原地转。旋转一停就解除绑定，回到普通的摄像机视差跟随，" +
                 "不会一直绑着玩家（不然平时走位时背景的视差移动也会被这个旋转换算带偏）")]
        public Transform rotationPivot;

        private Vector3 _lastCameraPos;
        private Quaternion _lastSourceRotation;
        private WorldRotator _rotator;

        void Start()
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            _lastCameraPos = cameraTransform != null ? cameraTransform.position : Vector3.zero;
            _lastSourceRotation = rotationSource != null ? rotationSource.rotation : Quaternion.identity;

            if (rotationSource != null)
                _rotator = rotationSource.GetComponent<WorldRotator>();
        }

        /// <summary>
        /// 全部走"这一帧的增量"，不是每帧从某个固定基准重新算一次绝对位置——
        /// WorldRotator.IsRotating 没有滞回，摇杆抖动会导致它单帧内 true/false 来回跳，
        /// 之前每帧在"绝对位置 A"和"绝对位置 B"之间直接切换，一跳就是一次瞬移，看起来像抽搐。
        /// 改成每帧只叠加"这一帧摄像机挪了多少"+"这一帧世界转了多少"，isRotating 跳变
        /// 顶多让某一帧少转一点/多转一点，位置永远是连续的，不会瞬移。
        /// </summary>
        void LateUpdate()
        {
            if (cameraTransform == null) return;

            Vector3 camFrameDelta = cameraTransform.position - _lastCameraPos;
            transform.position += new Vector3(
                camFrameDelta.x * parallaxFactor,
                camFrameDelta.y * parallaxFactor,
                0f);
            _lastCameraPos = cameraTransform.position;

            if (rotationSource != null)
            {
                bool isRotating = _rotator != null && _rotator.IsRotating;
                if (rotationPivot != null && isRotating)
                {
                    Quaternion frameDeltaRotation = rotationSource.rotation * Quaternion.Inverse(_lastSourceRotation);
                    Vector3 pivot = rotationPivot.position;
                    transform.position = pivot + frameDeltaRotation * (transform.position - pivot);
                }
                transform.rotation = rotationSource.rotation;
                _lastSourceRotation = rotationSource.rotation;
            }
        }
    }
}
