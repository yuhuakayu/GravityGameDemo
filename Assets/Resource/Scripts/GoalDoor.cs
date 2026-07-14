using System.Collections;
using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 关卡终点门。玩家进入触发区域后：禁用玩家操作、停止摄像机跟随、播放开门音效，
    /// 然后交给 SceneTransition 播放虹膜转场并加载下一关（新场景里的玩家/摄像机默认就是
    /// 启用状态，所以"恢复操作"不需要额外代码——新场景本身就是"恢复好"的状态）。
    ///
    /// 要求：这个物体的 Collider2D 勾选 Is Trigger；nextSceneName 要加进 Build Settings。
    /// </summary>
    public class GoalDoor : MonoBehaviour
    {
        [Header("下一关")]
        [Tooltip("要加载的场景名（须已加入 Build Settings）")]
        public string nextSceneName;

        private bool _triggered;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (_triggered) return;

            if (string.IsNullOrEmpty(nextSceneName))
            {
                Debug.LogWarning("[GoalDoor] 没有设置 nextSceneName，无法转场。");
                return;
            }

            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            _triggered = true;

            player.enabled = false;
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;

            var cam = Camera.main;
            if (cam != null)
            {
                var follow = cam.GetComponent<FollowTarget2D>();
                if (follow != null) follow.enabled = false;
            }

            SfxManager.Instance.PlayDoorOpen();
            StartCoroutine(DoGoalSequence());
        }

        /// <summary>先播通关上升音阶，播完再进虹膜转场（跟文档要的顺序一致，不是同时糊在一起）</summary>
        private IEnumerator DoGoalSequence()
        {
            SfxManager.Instance.PlayStageComplete();
            yield return new WaitForSecondsRealtime(0.6f);
            SceneTransition.Instance.LoadScene(nextSceneName);
        }
    }
}
