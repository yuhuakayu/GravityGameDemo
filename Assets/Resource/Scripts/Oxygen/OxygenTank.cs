using System.Collections;
using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>应放在 WorldRoot 下随关卡旋转；场景重载会还原已收集的实例。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D))]
    public sealed class OxygenTank : MonoBehaviour
    {
        [Header("氧气回复")]
        [SerializeField, Min(0f)] private float restoreAmount = 30f;

        [Header("视觉（建议指定仅含 Sprite 的子物体）")]
        [Tooltip("子物体会浮动。留空则直接显示根 Sprite，只执行拾取缩放，碰撞体不会浮动。")]
        [SerializeField] private Transform visual;
        [SerializeField, Min(0f)] private float bobAmplitude = 0.12f;
        [SerializeField, Min(0f)] private float bobFrequency = 1.2f;
        [SerializeField, Min(0f)] private float pickupDuration = 0.18f;
        [SerializeField, Min(1f)] private float pickupPopScale = 1.25f;

        private Collider2D[] _colliders;
        private Vector3 _visualBasePosition;
        private Vector3 _visualBaseScale;
        private bool _collected;
        private float _bobTime;
        public float RestoreAmount { get => restoreAmount; set => restoreAmount = FiniteNonNegative(value, restoreAmount); }

        private void Reset()
        {
            GetComponent<CircleCollider2D>().isTrigger = true;
        }

        private void Awake()
        {
            _colliders = GetComponents<Collider2D>();
            foreach (Collider2D trigger in _colliders)
                trigger.isTrigger = true;

            if (visual == null || (visual != transform && !visual.IsChildOf(transform)))
                visual = transform;
            _visualBasePosition = visual.localPosition;
            _visualBaseScale = visual.localScale;
        }

        private void Update()
        {
            if (_collected || visual == null || visual == transform) return;
            _bobTime += Time.deltaTime;
            visual.localPosition = _visualBasePosition + Vector3.up *
                (Mathf.Sin(_bobTime * bobFrequency * Mathf.PI * 2f) * bobAmplitude);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryCollect(other.GetComponentInParent<PlayerOxygen>());
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // 支持开始游戏时玩家已处在瓶子的触发器内。
            if (!_collected) TryCollect(other.GetComponentInParent<PlayerOxygen>());
        }

        public bool TryCollect(PlayerOxygen oxygen)
        {
            if (_collected || !isActiveAndEnabled || oxygen == null || !oxygen.IsGameplayActive)
                return false;

            _collected = true;
            foreach (Collider2D trigger in _colliders)
                trigger.enabled = false;

            oxygen.AddOxygen(restoreAmount);
            if (pickupDuration <= 0f)
                Destroy(gameObject);
            else
                StartCoroutine(PlayPickup());
            return true;
        }

        private IEnumerator PlayPickup()
        {
            float elapsed = 0f;
            while (elapsed < pickupDuration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / pickupDuration);
                float scale = progress < 0.2f
                    ? Mathf.Lerp(1f, pickupPopScale, progress / 0.2f)
                    : Mathf.Lerp(pickupPopScale, 0f, (progress - 0.2f) / 0.8f);
                if (visual != null) visual.localScale = _visualBaseScale * scale;
                yield return null;
            }
            Destroy(gameObject);
        }

        private void OnValidate()
        {
            restoreAmount = FiniteNonNegative(restoreAmount, 30f);
            bobAmplitude = FiniteNonNegative(bobAmplitude, 0.12f);
            bobFrequency = FiniteNonNegative(bobFrequency, 1.2f);
            pickupDuration = FiniteNonNegative(pickupDuration, 0.18f);
            pickupPopScale = Mathf.Max(1f, FiniteNonNegative(pickupPopScale, 1.25f));
        }

        private static float FiniteNonNegative(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0f, value);
        }
    }
}
