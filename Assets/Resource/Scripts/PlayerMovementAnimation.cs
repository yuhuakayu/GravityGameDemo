using UnityEngine;

namespace Resource.Scripts
{
    [DefaultExecutionOrder(100)]
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
        private static readonly int LandStateId = Animator.StringToHash("Base Layer.Land");
        private static readonly int DieStateId = Animator.StringToHash("Base Layer.Die");
        private float _airborneTime;
        private int _landingFeedbackVersion;
        private bool _landingFeedbackPending;
        private bool _pausedForDeath;
        private float _speedBeforeDeath;
        private AnimatorUpdateMode _updateModeBeforeDeath;

        public bool IsFloating { get; private set; }

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable()
        {
            if (UpdateDeathPause()) return;
            _airborneTime = 0f;
            _landingFeedbackVersion = player != null ? player.LandingFeedbackVersion : 0;
            _landingFeedbackPending = false;
            SetFloating(false, true);
        }

        private void Update()
        {
            UpdateDeathPause();
        }

        private void LateUpdate()
        {
            SampleGrounded(player != null && player.IsGrounded, Time.deltaTime);
        }

        public void SampleGrounded(bool grounded, float deltaTime)
        {
            if (UpdateDeathPause()) return;
            if (player == null || !player.IsGameplayActive)
            {
                _airborneTime = 0f;
                _landingFeedbackVersion = player != null ? player.LandingFeedbackVersion : 0;
                _landingFeedbackPending = false;
                if (IsFloating) SetFloating(false, true);
                return;
            }

            _landingFeedbackPending |= _landingFeedbackVersion != player.LandingFeedbackVersion;
            _landingFeedbackVersion = player.LandingFeedbackVersion;
            if (grounded)
            {
                _airborneTime = 0f;
                SetFloating(false, IsFloating && !_landingFeedbackPending, _landingFeedbackPending);
                _landingFeedbackPending = false;
                return;
            }

            _airborneTime += Mathf.Max(0f, deltaTime);
            if (_airborneTime >= airborneDelay) SetFloating(true);
        }

        private bool UpdateDeathPause()
        {
            bool dead = player != null && player.IsDead;
            if (animator != null)
            {
                if (dead && !_pausedForDeath)
                {
                    _speedBeforeDeath = animator.speed;
                    _updateModeBeforeDeath = animator.updateMode;
                    animator.speed = 0f;
                    _pausedForDeath = true;
                }
                else if (!dead && _pausedForDeath)
                {
                    animator.speed = _speedBeforeDeath;
                    animator.updateMode = _updateModeBeforeDeath;
                    _pausedForDeath = false;
                }
            }
            return dead;
        }

        public void FreezeForDeath()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            UpdateDeathPause();
        }

        public float PlayDeath()
        {
            FreezeForDeath();
            if (animator == null || animator.runtimeAnimatorController == null || !animator.HasState(0, DieStateId))
                return 0f;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.speed = 1f;
            animator.Play(DieStateId, 0, 0f);
            animator.Update(0f);
            return animator.GetCurrentAnimatorStateInfo(0).length;
        }

        private void SetFloating(bool floating, bool reset = false, bool forceLanding = false)
        {
            if (player != null && player.IsDead) return;
            if (IsFloating == floating && !reset && !forceLanding) return;
            IsFloating = floating;
            if (animator == null || animator.runtimeAnimatorController == null) return;

            animator.SetBool(IsFloatingId, floating);
            if (!floating && animator.isActiveAndEnabled)
            {
                // Display the first landing frame before this render; resets still use Idle.
                animator.Play(reset ? IdleStateId : LandStateId, 0, 0f);
                animator.Update(0f);
            }
        }
    }
}
