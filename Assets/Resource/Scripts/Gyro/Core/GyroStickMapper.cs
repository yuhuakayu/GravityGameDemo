using UnityEngine;

namespace Resource.Scripts.Gyro
{
    public sealed class GyroStickMapper
    {
        public float Map(float wheelDegreesPerSecond, GyroSettings settings)
        {
            if (!GyroSample.Finite(wheelDegreesPerSecond)) return 0f;
            float speed = Mathf.Abs(wheelDegreesPerSecond);
            // Explicit zero avoids the minimum output producing movement when the deadzone is zero.
            if (speed <= 0f || speed < settings.speedDeadzone) return 0f;
            speed *= settings.sensitivity;
            if (settings.precisionSpeed > 0f && speed < settings.precisionSpeed)
                speed *= speed / settings.precisionSpeed;
            float t = Mathf.Clamp01((speed - settings.minInputSpeed) /
                Mathf.Max(0.0001f, settings.maxInputSpeed - settings.minInputSpeed));
            t = Mathf.Pow(t, Mathf.Max(0.01f, settings.outputCurve));
            float output = Mathf.Lerp(settings.minOutput, settings.maxOutput, t) * Mathf.Sign(wheelDegreesPerSecond);
            return Mathf.Clamp(settings.invertDirection ? -output : output, -1f, 1f);
        }
    }
}
