using UnityEngine;

namespace Resource.Scripts
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(PlayerController))]
    public sealed class PlayerMovementAnimation : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Animator animator;
        [SerializeField, Min(0f), Tooltip("连续离地达到此时长才切换浮空，避免地面小颠簸造成闪动。")]
        private float airborneDelay = 0.08f;

        private static readonly int IsFloatingId = Animator.StringToHash("IsFloating");
        private static readonly int IdleStateId = Animator.StringToHash("Base Layer.Idle");
        private float _airborneTime;

        public bool IsFloating { get; private set; }

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable()
        {
            _airborneTime = 0f;
            SetFloating(false, true);
        }

        private void LateUpdate()
        {
            SampleGrounded(player != null && player.IsGrounded, Time.deltaTime);
        }

        public void SampleGrounded(bool grounded, float deltaTime)
        {
            if (player == null || !player.IsGameplayActive || grounded)
            {
                _airborneTime = 0f;
                SetFloating(false);
                return;
            }

            _airborneTime += Mathf.Max(0f, deltaTime);
            if (_airborneTime >= airborneDelay) SetFloating(true);
        }

        private void SetFloating(bool floating, bool reset = false)
        {
            if (IsFloating == floating && !reset) return;
            IsFloating = floating;
            if (animator == null || animator.runtimeAnimatorController == null) return;

            animator.SetBool(IsFloatingId, floating);
            if (!floating && animator.isActiveAndEnabled)
            {
                // Landing must display the first breathing frame before this render.
                animator.Play(IdleStateId, 0, 0f);
                animator.Update(0f);
            }
        }
    }
}
