using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 随手放一个蜡烛装饰（用 Torch_01 顶替——项目素材包里没有专门的蜡烛贴图，
    /// 这是资源包里最小/最简单的火焰道具，视觉上凑合当蜡烛用，以后有真素材直接换掉 candlePrefabPath 就行）。
    /// 自动挂 TorchLight2D，带暖光闪烁 + 噼啪声循环（音效受 SfxManager.sfxEnabled 总开关控制）。
    /// 由 PlayerController.Start() 自动创建。
    /// </summary>
    public class CandleSpawner : MonoBehaviour
    {
        [Tooltip("蜡烛用的预制体路径（相对 Resources 目录），默认顶替成最小的火把")]
        public string candlePrefabPath = "Prefabs/Torches/Torch_01";
        [Tooltip("生成位置，随便找了个玩家出生点附近的位置")]
        public Vector3 spawnPosition = new Vector3(-2f, 1.2f, 0f);

        void Start()
        {
            if (transform.Find("Candle (Auto)") != null) return; // 场景里已经摆好了，不重复生成

            var prefab = Resources.Load<GameObject>(candlePrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[CandleSpawner] 找不到蜡烛预制体：{candlePrefabPath}");
                return;
            }

            var candle = Instantiate(prefab, spawnPosition, Quaternion.identity, transform);
            candle.name = "Candle (Auto)";
            candle.AddComponent<TorchLight2D>();
        }
    }
}
