using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 通用 2D 平滑跟随：只改 X/Y，保留自身原本的 Z（摄像机景深、精灵图层排序都不受影响）。
    /// 用途：摄像机跟玩家、氛围粒子层跟摄像机等。
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class FollowTarget2D : MonoBehaviour
    {
        public Transform target;
        [Tooltip("平滑时间，越小跟得越紧；0 视为瞬间跟随")]
        public float smoothTime = 0.25f;
        public Vector2 offset = Vector2.zero;

        private Vector3 _velocity;

        void Start()
        {
            if (target == null)
            {
                var player = FindObjectOfType<PlayerController>();
                if (player != null) target = player.transform;
            }
        }

        void LateUpdate()
        {
            if (target == null) return;

            Vector3 goal = new Vector3(
                target.position.x + offset.x,
                target.position.y + offset.y,
                transform.position.z);
            transform.position = Vector3.SmoothDamp(transform.position, goal, ref _velocity, smoothTime);
        }
    }
}
