using UnityEngine;

namespace Resource.Scripts.Gyro
{
    /// <summary>Maps a calibrated physical wheel's full travel to 360 clockwise-positive degrees.</summary>
    public static class PhysicalWheelMapper
    {
        public static bool IsValid(float left, float center, float right) =>
            GyroSample.Finite(left) && GyroSample.Finite(center) && GyroSample.Finite(right) &&
            ((left < center && center < right) || (right < center && center < left));

        public static float MapAngle(float raw, float left, float center, float right, bool invert)
        {
            if (!IsValid(left, center, right) || !GyroSample.Finite(raw)) return 0f;
            bool onLeft = right > left ? raw <= center : raw >= center;
            float end = onLeft ? left : right;
            float fraction = Mathf.Clamp01((float)(((double)raw - center) / ((double)end - center)));
            float angle = fraction * (onLeft ? -180f : 180f);
            return invert ? -angle : angle;
        }
    }
}
