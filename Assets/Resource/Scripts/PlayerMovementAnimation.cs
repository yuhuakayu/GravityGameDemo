using UnityEngine;

namespace Resource.Scripts
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(PlayerController))]
    public sealed class PlayerMovementAnimation : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Animator animator;
        [SerializeField, Min(0f), Tooltip("忽略小于此距离的物理浮点抖动；使用实际位移，不使用按键或速度。")]
        private float movementThreshold = 0.0001f;

        private static readonly int IsMovingId = Animator.StringToHash("IsMoving");
        private Rigidbody2D _body;
        private Vector2 _lastPosition;
        private double _lastPhysicsTime;

        public bool IsMoving { get; private set; }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            if (player == null) player = GetComponent<PlayerController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable()
        {
            ResetSample(_body.position, Time.fixedTimeAsDouble);
        }

        private void LateUpdate()
        {
            // Read after the physics solver and PlayerController/CrushGuard corrections.
            // Rigidbody position excludes renderer interpolation and squash/stretch.
            SamplePosition(_body.position, Time.fixedTimeAsDouble);
        }

        public void SamplePosition(Vector2 position, double physicsTime)
        {
            if (player == null || !player.IsGameplayActive)
            {
                ResetSample(position, physicsTime);
                return;
            }

            Vector2 displacement = position - _lastPosition;
            // Several render frames can share one physics step. Keep the last result
            // until another step is available, instead of flickering Run/Idle at high FPS.
            if (physicsTime == _lastPhysicsTime && displacement.sqrMagnitude == 0f) return;
            _lastPosition = position;
            _lastPhysicsTime = physicsTime;
            SetMoving(displacement.sqrMagnitude > movementThreshold * movementThreshold);

            float horizontal = Vector2.Dot(displacement, transform.right);
            if (IsMoving && Mathf.Abs(horizontal) > movementThreshold)
                player.FaceDirection(Mathf.Sign(horizontal));
        }

        private void ResetSample(Vector2 position, double physicsTime)
        {
            _lastPosition = position;
            _lastPhysicsTime = physicsTime;
            SetMoving(false);
        }

        private void SetMoving(bool moving)
        {
            IsMoving = moving;
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetBool(IsMovingId, moving);
        }

        private void OnDisable()
        {
            SetMoving(false);
        }
    }
}
