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

        private Vector3 _startPosition;
        private Vector3 _cameraStartPosition;

        void Start()
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            _startPosition = transform.position;
            _cameraStartPosition = cameraTransform != null ? cameraTransform.position : Vector3.zero;
        }

        void LateUpdate()
        {
            if (cameraTransform == null) return;

            Vector3 camDelta = cameraTransform.position - _cameraStartPosition;
            transform.position = _startPosition + new Vector3(
                camDelta.x * parallaxFactor,
                camDelta.y * parallaxFactor,
                0f);

            if (rotationSource != null)
                transform.rotation = rotationSource.rotation;
        }
    }
}
