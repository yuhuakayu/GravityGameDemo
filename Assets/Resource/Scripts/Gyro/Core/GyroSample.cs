using UnityEngine;

namespace Resource.Scripts.Gyro
{
    /// <summary>One sensor callback. DeltaTime is the sensor interval, never a render-frame delta.</summary>
    public readonly struct GyroSample
    {
        public float DeltaTime { get; }
        public Vector3 Gyro { get; }
        public Vector3 Acceleration { get; }
        public Vector3 Gravity { get; }
        public double TimestampSeconds { get; }

        public GyroSample(float deltaTime, Vector3 gyro, Vector3 acceleration, Vector3 gravity,
            double timestampSeconds = 0)
        {
            DeltaTime = deltaTime;
            Gyro = gyro;
            Acceleration = acceleration;
            Gravity = gravity;
            TimestampSeconds = timestampSeconds;
        }

        public bool IsValid => Finite(DeltaTime) && DeltaTime > 0 && DeltaTime <= 0.1f &&
            Finite(Gyro) && Finite(Acceleration) && Finite(Gravity) && Gravity.sqrMagnitude > 0.000001f &&
            !double.IsNaN(TimestampSeconds) && !double.IsInfinity(TimestampSeconds);

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
