#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// Attachable PlayerLoop bridge for the Editor-only pendulum validation controller.
    /// No Editor-assembly reference is needed, and no test code is included in builds.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class PendulumValidationLoopBridge : MonoBehaviour
    {
        private Action _update, _fixedUpdate, _destroyed;

        public void Bind(Action update, Action fixedUpdate, Action destroyed)
        {
            _update = update;
            _fixedUpdate = fixedUpdate;
            _destroyed = destroyed;
        }

        private void Update() { _update?.Invoke(); }
        private void FixedUpdate() { _fixedUpdate?.Invoke(); }

        private void OnDestroy()
        {
            Action callback = _destroyed;
            _update = _fixedUpdate = _destroyed = null;
            callback?.Invoke();
        }
    }
}
#endif
