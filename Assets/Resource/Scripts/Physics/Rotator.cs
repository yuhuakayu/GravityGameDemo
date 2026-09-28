using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>Constant-speed kinematic obstacle, including the pillar crush acceptance fixture.</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Rotator : MonoBehaviour
    {
        [SerializeField] public float degreesPerSecond = 180f;
        [SerializeField] public bool rotateOnStart = true;
        private Rigidbody2D _body;

        private void Awake() { ConfigureBody(); }
        private void Reset() { ConfigureBody(); }

        private void ConfigureBody()
        {
            if (_body == null) _body = GetComponent<Rigidbody2D>();
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.useFullKinematicContacts = true;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
            _body.gravityScale = 0f;
        }

        private void FixedUpdate()
        {
            if (rotateOnStart) Step(Time.fixedDeltaTime);
        }

        /// <summary>One actual physics step, also callable by an isolated Editor physics fixture.</summary>
        public void Step(float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            ConfigureBody();
            _body.MoveRotation(_body.rotation + degreesPerSecond * deltaTime);
        }
    }
}
