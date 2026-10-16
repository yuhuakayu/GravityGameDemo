using UnityEngine;

namespace Resource.Scripts.SmallLevels
{
    [DefaultExecutionOrder(20)]
    [RequireComponent(typeof(Camera))]
    public sealed class SmallLevelCamera : MonoBehaviour
    {
        public Transform level;
        public PlayerController player;
        public Vector2 boundsCenter;
        public Vector2 boundsSize;
        public bool sky;

        private Camera _camera;
        private Vector2 _position;
        private float _shake;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            RestoreDefault();
        }

        public void RestoreDefault()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            float span = Mathf.Max(boundsSize.x, boundsSize.y);
            _camera.orthographicSize = Mathf.Max((span + (sky ? 2f : 3f)) * 0.5f,
                (span + (sky ? 7f : 8f)) / (2f * _camera.aspect));
            FitImmediate();
        }

        public void FitImmediate()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            _position = FollowPoint();
            ApplyPosition(Vector2.zero);
        }

        public void Shake(float amplitude) => _shake = Mathf.Max(_shake, amplitude);

        private Vector2 FollowPoint()
        {
            Vector2 center = level != null
                ? (Vector2)level.TransformPoint(new Vector3(boundsCenter.x, -boundsCenter.y, 0f))
                : boundsCenter;
            Vector2 feet = player != null ? NearMissFeedback.FeetPosition(player) + Vector2.up * 0.8f : center;
            return center + 0.4f * (feet - center);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            _position += (FollowPoint() - _position) * (1f - Mathf.Exp(-(sky ? 6f : 7f) * dt));
            Vector2 offset = dt > 0f ? new Vector2(Random.value - 0.5f, Random.value - 0.5f) * (_shake * 0.9f) : Vector2.zero;
            ApplyPosition(offset);
            _shake *= Mathf.Exp(-6f * dt);
        }

        private void ApplyPosition(Vector2 offset)
        {
            transform.position = new Vector3(_position.x + offset.x,
                _position.y + _camera.orthographicSize * 0.06f + offset.y, transform.position.z);
        }
    }
}
