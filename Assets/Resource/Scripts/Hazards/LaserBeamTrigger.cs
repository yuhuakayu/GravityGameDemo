using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>Trigger 单独放在光束子物体上，发射器的实体碰撞体不造成死亡。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class LaserBeamTrigger : MonoBehaviour
    {
        [SerializeField] private LaserEmitter emitter;

        public void Configure(LaserEmitter source) => emitter = source;

        private void OnTriggerEnter2D(Collider2D other) => CheckPlayer(other);
        private void OnTriggerStay2D(Collider2D other) => CheckPlayer(other);

        private void CheckPlayer(Collider2D other)
        {
            if (emitter != null) emitter.TryKill(other);
        }
    }
}
