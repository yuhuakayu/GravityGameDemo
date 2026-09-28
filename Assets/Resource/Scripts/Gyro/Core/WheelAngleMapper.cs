using UnityEngine;

namespace Resource.Scripts.Gyro
{
    /// <summary>Clockwise-positive world targets. Neutral preserves the current world orientation.</summary>
    public sealed class WheelAngleMapper
    {
        private readonly GyroSettings _settings;
        private float _worldAtCenter;
        private bool _blocked;

        public float CenterAngle { get; private set; }
        public float TargetAngle { get; private set; }
        public float OutputAngle { get; private set; }
        public bool IsBlocked => _blocked;

        public WheelAngleMapper(GyroSettings settings) { _settings = settings; }

        public void EnterAngleMode(float theta, float currentWorldAngle) => Recenter(theta, currentWorldAngle);

        public void Recenter(float theta, float currentWorldAngle)
        {
            CenterAngle = theta;
            _worldAtCenter = currentWorldAngle;
            TargetAngle = currentWorldAngle;
            OutputAngle = currentWorldAngle;
        }

        public void SetBlocked(bool blocked, float theta, float currentWorldAngle)
        {
            if (blocked || _blocked != blocked) Recenter(theta, currentWorldAngle);
            _blocked = blocked;
        }

        public float Evaluate(float theta, float sampleDeltaTime)
        {
            if (_blocked || !GyroSample.Finite(theta)) return OutputAngle;
            float delta = theta - CenterAngle;
            if (Mathf.Abs(delta) < _settings.angleDeadzone) delta = 0f;
            float direction = _settings.invertDirection ? -1f : 1f;
            TargetAngle = _worldAtCenter + delta * _settings.angleMultiplier * direction;
            if (_settings.angleSmoothTime <= 0f)
                OutputAngle = TargetAngle;
            else if (sampleDeltaTime > 0f && GyroSample.Finite(sampleDeltaTime))
                OutputAngle = Mathf.Lerp(OutputAngle, TargetAngle,
                    1f - Mathf.Exp(-sampleDeltaTime / _settings.angleSmoothTime));
            return OutputAngle;
        }
    }
}
