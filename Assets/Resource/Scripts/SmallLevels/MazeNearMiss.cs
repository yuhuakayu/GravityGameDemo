using System.Collections.Generic;
using UnityEngine;

namespace Resource.Scripts.SmallLevels
{
    [DefaultExecutionOrder(-100)]
    public sealed class MazeNearMiss : MonoBehaviour
    {
        private PlayerController _player;
        private WorldRotator _world;
        private NearMissFeedback _feedback;
        private readonly List<BoxCollider2D> _spikes = new List<BoxCollider2D>();
        private LaserEmitter[] _lasers;
        private float[] _lastCross;
        private bool[] _wasOn;

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerController>();
            _world = FindFirstObjectByType<WorldRotator>();
            _feedback = FindFirstObjectByType<NearMissFeedback>();
            foreach (var box in FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None))
                if (box.gameObject.scene == gameObject.scene && box.name.StartsWith("Spikes_")) _spikes.Add(box);
            var lasers = new List<LaserEmitter>();
            foreach (var laser in FindObjectsByType<LaserEmitter>(FindObjectsSortMode.None))
                if (laser.gameObject.scene == gameObject.scene) lasers.Add(laser);
            _lasers = lasers.ToArray();
            _lastCross = new float[_lasers.Length];
            _wasOn = new bool[_lasers.Length];
            for (int i = 0; i < _lasers.Length; i++)
            {
                _lastCross[i] = float.NegativeInfinity;
                _wasOn[i] = _lasers[i].IsBeamActive;
            }
        }

        private void FixedUpdate()
        {
            if (_player == null || !_player.IsGameplayActive || _feedback == null) return;
            NearMissFeedback.GetPlayerCircles(_player, out Vector2 a, out Vector2 b, out float radius);
            bool fast = Mathf.Abs(_player.IntendedVelocity.y) > 2.5f || (_world != null && _world.IsRotating);
            if (fast)
                foreach (var box in _spikes)
                    if (box != null && box.enabled && Mathf.Min(BoxDistance(box, a), BoxDistance(box, b)) < radius + 0.55f)
                        _feedback.NearMiss("spike");
            for (int i = 0; i < _lasers.Length; i++)
            {
                var laser = _lasers[i];
                if (laser == null || !laser.isActiveAndEnabled) continue;
                float distance = Mathf.Min(SegmentDistance(a, laser.BeamStart, laser.BeamEnd),
                    SegmentDistance(b, laser.BeamStart, laser.BeamEnd));
                bool on = laser.IsBeamActive;
                if (on)
                {
                    if ((fast && distance < radius + 0.5f) || (!_wasOn[i] && Time.time - _lastCross[i] < 0.45f))
                        _feedback.NearMiss("laser");
                }
                else if (distance < radius + 0.04f) _lastCross[i] = Time.time;
                _wasOn[i] = on;
            }
        }

        private static float BoxDistance(BoxCollider2D box, Vector2 point)
        {
            Vector2 local = box.transform.InverseTransformPoint(point);
            Vector2 half = box.size * 0.5f;
            Vector2 nearest = new Vector2(Mathf.Clamp(local.x, box.offset.x - half.x, box.offset.x + half.x),
                Mathf.Clamp(local.y, box.offset.y - half.y, box.offset.y + half.y));
            return Vector2.Distance(point, box.transform.TransformPoint(nearest));
        }

        private static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            float t = delta.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude) : 0f;
            return Vector2.Distance(point, start + delta * t);
        }
    }
}
