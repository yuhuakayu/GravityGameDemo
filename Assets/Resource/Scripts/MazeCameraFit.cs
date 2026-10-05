using UnityEngine;

namespace Resource.Scripts
{
    [RequireComponent(typeof(Camera))]
    public class MazeCameraFit : MonoBehaviour
    {
        public Transform world;
        public Vector2 mazeSize;
        public MazeCameraSettings settings;

        private Camera _cam;
        private float _size;

        public float CurrentSize => _cam != null ? _cam.orthographicSize : _size;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            RestoreDefault();
        }

        public void SetSize(float value)
        {
            _size = Mathf.Clamp(value, 4f, 20f);
            FitImmediate();
        }

        public void RestoreDefault()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) return;
            _size = settings != null && settings.TryGetSize(gameObject.scene.name, out float saved)
                ? saved : CalculateDefaultSize();
            FitImmediate();
        }

        public void FitImmediate()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam != null) _cam.orthographicSize = _size;
        }

        private float CalculateDefaultSize()
        {
            float radius = mazeSize.magnitude * 0.5f + 0.5f;
            return Mathf.Max(radius, radius / _cam.aspect);
        }
    }
}
