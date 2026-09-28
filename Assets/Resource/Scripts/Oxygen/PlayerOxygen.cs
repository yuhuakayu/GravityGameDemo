using System;
using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>玩家氧气。关卡重载生成新玩家时回满；死亡复用 PlayerController 的入口。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerOxygen : MonoBehaviour
    {
        [Header("氧气")]
        [SerializeField, Min(0.01f)] private float maxOxygen = 100f;
        [SerializeField, Min(0f)] private float drainPerSecond = 2f;
        [SerializeField, Min(0f)] private float dashCost = 10f;

        private PlayerController _player;
        private float _currentOxygen;

        public float CurrentOxygen => _currentOxygen;
        public float MaxOxygen
        {
            get => maxOxygen;
            set
            {
                if (!IsFinite(value) || value <= 0f) return;
                maxOxygen = value;
                _currentOxygen = Mathf.Min(_currentOxygen, maxOxygen);
                OxygenChanged?.Invoke(_currentOxygen, maxOxygen);
            }
        }
        public float DrainPerSecond { get => drainPerSecond; set { if (IsFinite(value)) drainPerSecond = Mathf.Max(0f, value); } }
        public float DashCost { get => dashCost; set { if (IsFinite(value)) dashCost = Mathf.Max(0f, value); } }
        public float NormalizedOxygen => maxOxygen > 0f ? Mathf.Clamp01(_currentOxygen / maxOxygen) : 0f;
        public bool IsGameplayActive => isActiveAndEnabled && _player != null && _player.IsGameplayActive;

        /// <summary>参数依次为当前值、最大值。订阅后可用公开属性立即刷新 UI。</summary>
        public event Action<float, float> OxygenChanged;

        private void Awake()
        {
            ValidateSettings();
            _player = GetComponent<PlayerController>();
            _currentOxygen = maxOxygen;
            OxygenChanged?.Invoke(_currentOxygen, maxOxygen);
        }

        private void Update()
        {
            if (!IsGameplayActive || drainPerSecond <= 0f) return;
            SetCurrentOxygen(_currentOxygen - drainPerSecond * Time.deltaTime);
        }

        public void AddOxygen(float amount)
        {
            if (!IsFinite(amount) || amount <= 0f || _player == null || _player.IsDead) return;
            SetCurrentOxygen(_currentOxygen + amount);
        }

        /// <summary>预览、暂停、死亡、非法数量或余额不足时失败，且不改变氧气。</summary>
        public bool TryConsume(float amount)
        {
            if (!IsGameplayActive || !IsFinite(amount) || amount < 0f || _currentOxygen < amount)
                return false;

            SetCurrentOxygen(_currentOxygen - amount);
            return true;
        }

        private void SetCurrentOxygen(float value)
        {
            float next = Mathf.Clamp(value, 0f, maxOxygen);
            if (_currentOxygen == next) return;

            _currentOxygen = next;
            // 先锁定死亡，再广播，防止订阅者在 0 氧气这一帧补氧绕过死亡。
            if (_currentOxygen <= 0f && _player != null && !_player.IsDead)
                _player.Die();

            OxygenChanged?.Invoke(_currentOxygen, maxOxygen);
        }

        private void OnValidate()
        {
            ValidateSettings();
        }

        private void ValidateSettings()
        {
            maxOxygen = IsFinite(maxOxygen) ? Mathf.Max(0.01f, maxOxygen) : 100f;
            drainPerSecond = IsFinite(drainPerSecond) ? Mathf.Max(0f, drainPerSecond) : 2f;
            dashCost = IsFinite(dashCost) ? Mathf.Max(0f, dashCost) : 10f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
