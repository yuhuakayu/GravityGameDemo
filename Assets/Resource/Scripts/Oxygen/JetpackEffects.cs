using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>喷口粒子沿本地 +Z 轴发射；把 +Z 转向冲刺反方向，保持粒子参数在 Inspector 可调。</summary>
    [DisallowMultipleComponent]
    public sealed class JetpackEffects : MonoBehaviour
    {
        [Header("喷口与特效")]
        [SerializeField] private Transform nozzle;
        [SerializeField] private ParticleSystem exhaustParticles;
        [SerializeField] private AudioSource jetpackAudio;
        [SerializeField] private TrailRenderer dashTrail;
        [SerializeField] private bool enableTrail;
        [Tooltip("随喷射方向把喷口移到玩家身后；关闭后使用场景中手动摆放的位置。")]
        [SerializeField] private bool positionBehindDash = true;
        [SerializeField, Min(0f)] private float nozzleDistance = 0.35f;
        [SerializeField] private Vector2 nozzleLocalOffset = new Vector2(0f, 0.05f);

        private bool _playing;

        private void Awake()
        {
            if (exhaustParticles != null)
                exhaustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (jetpackAudio != null) jetpackAudio.Stop();
            if (dashTrail != null)
            {
                dashTrail.emitting = false;
                dashTrail.Clear();
            }
        }

        public void BeginDash(Vector2 worldDirection)
        {
            _playing = true;
            UpdateDirection(worldDirection);
            if (exhaustParticles != null) exhaustParticles.Play(true);
            if (jetpackAudio != null && jetpackAudio.clip != null) jetpackAudio.Play();
            if (dashTrail != null)
            {
                dashTrail.Clear();
                dashTrail.emitting = enableTrail;
            }
        }

        public void UpdateDirection(Vector2 worldDirection)
        {
            if (!_playing || nozzle == null || worldDirection.sqrMagnitude < 0.0001f) return;
            Vector3 exhaustDirection = -(Vector3)worldDirection.normalized;
            if (positionBehindDash)
                nozzle.position = transform.TransformPoint(nozzleLocalOffset) + exhaustDirection * nozzleDistance;
            // ParticleSystem 的 Cone 沿本地 Z 轴喷出；用 forward 直接对准平面内的反向矢量。
            nozzle.rotation = Quaternion.LookRotation(exhaustDirection, Vector3.forward);
        }

        public void EndDash()
        {
            _playing = false;
            if (exhaustParticles != null)
                exhaustParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (jetpackAudio != null) jetpackAudio.Stop();
            if (dashTrail != null) dashTrail.emitting = false;
        }

        private void OnDisable() => EndDash();
    }
}
