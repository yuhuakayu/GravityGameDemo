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

        private Vector3 _startPosition;
        private Vector3 _cameraStartPosition;
        private WorldRotator _rotator;

        void Start()
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            _startPosition = transform.position;
            _cameraStartPosition = cameraTransform != null ? cameraTransform.position : Vector3.zero;

            if (rotationSource != null)
                _rotator = rotationSource.GetComponent<WorldRotator>();
        }

        void LateUpdate()
        {
            if (cameraTransform == null) return;

            Vector3 camDelta = cameraTransform.position - _cameraStartPosition;
            Vector3 flatPos = _startPosition + new Vector3(
                camDelta.x * parallaxFactor,
                camDelta.y * parallaxFactor,
                0f);

            if (rotationSource != null)
            {
                bool isRotating = _rotator != null && _rotator.IsRotating;
                if (rotationPivot != null && isRotating)
                {
                    Vector3 pivot = rotationPivot.position;
                    Vector3 rotatedOffset = rotationSource.rotation * (flatPos - pivot);
                    transform.position = pivot + rotatedOffset;
                }
                else
                {
                    transform.position = flatPos;
                }
                transform.rotation = rotationSource.rotation;
            }
            else
            {
                transform.position = flatPos;
            }
        }
    }
}
