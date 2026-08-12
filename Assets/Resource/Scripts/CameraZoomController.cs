using UnityEngine;
using UnityEngine.InputSystem;

namespace Resource.Scripts
{
    /// <summary>
    /// 游玩过程中用左右扳机缩放镜头，手感跟 LevelIntroUI 浏览模式里的 ApplyZoom 一致（连续按帧
    /// 累加，不是跳到目标值）。挂在 Main Camera 上，跟 FollowTarget2D 共存——那个只改
    /// transform.position，这个只改 orthographicSize，互不相干。
    ///
    /// 由 LevelIntroUI 负责在浏览模式期间禁用这个组件（避免跟浏览模式自己的缩放同时响应同一个
    /// 扳机输入），点"开始游戏"之后再启用。死亡重开关卡会整个重载场景，orthographicSize 跟着
    /// 场景文件里的默认值恢复，这里不需要额外写重置逻辑。
    /// </summary>
    public class CameraZoomController : MonoBehaviour
    {
        [Header("左右扳机缩放镜头（游玩中，手感跟浏览模式一致）")]
        public float zoomSpeed = 6f;
        public float minOrthoSize = 3f;
        public float maxOrthoSize = 15f;

        private Camera _cam;

        void Awake()
        {
            // 用 Awake 不用 Start：LevelIntroUI 在浏览模式期间会 AddComponent 之后立刻把这个组件
            // 禁用掉，Start() 要等到第一次被启用才会跑，Awake() 不受 enabled 影响，AddComponent
            // 那一刻就会执行，确保 _cam 一直是就绪的。
            _cam = GetComponent<Camera>();
        }

        void Update()
        {
            if (_cam == null) return;

            var gamepad = Gamepad.current;
            if (gamepad == null) return;

            float l2 = gamepad.leftTrigger.ReadValue();
            float r2 = gamepad.rightTrigger.ReadValue();
            if (l2 < 0.05f) l2 = 0f;
            if (r2 < 0.05f) r2 = 0f;

            ApplyZoom(r2 - l2);
        }

        /// <summary>拆成单独方法方便测试：直接传扳机差值调用，不用真的接手柄（跟 LevelIntroUI.ApplyZoom 同一个思路）。
        /// zoomInput：正值拉近（缩小 orthographicSize），负值拉远。</summary>
        private void ApplyZoom(float zoomInput)
        {
            if (Mathf.Abs(zoomInput) < 0.001f) return;

            _cam.orthographicSize = Mathf.Clamp(
                _cam.orthographicSize - zoomInput * zoomSpeed * Time.deltaTime,
                minOrthoSize, maxOrthoSize);
        }
    }
}
