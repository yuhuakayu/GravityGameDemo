using UnityEngine;

namespace Resource.Scripts.Gyro
{
    /// <summary>Stationarity uses per-axis variance plus low speed and stable gravity to reject slow turns.</summary>
    public sealed class GyroCalibration
    {
        private const float MaxStationarySpeed = 3f;
        private const float MaxAxisVariance = 0.04f;
        private const float GravityCosTolerance = 0.9998477f; // One degree.
        private Vector3 _mean;
        private Vector3 _m2;
        private Vector3 _windowGravity;
        private int _count;
        private float _stillSeconds;

        public Vector3 Bias { get; private set; }
        public bool IsCalibrated { get; private set; }
        public bool IsManualCalibration { get; private set; }
        public float StillSeconds => _stillSeconds;
        public float Progress => Mathf.Clamp01(_stillSeconds / (IsManualCalibration ? 3f : 1f));
        public string Status => IsManualCalibration ? "请把手柄平放静止 3 秒" :
            IsCalibrated ? (_stillSeconds >= 2f ? "持续校准" : "已校准") : "等待静止 1 秒";

        public Vector3 Process(GyroSample sample)
        {
            if (!sample.IsValid) return Vector3.zero;
            Vector3 g = sample.Gravity.normalized;
            bool lowMotion = (sample.Gyro - Bias).magnitude <= MaxStationarySpeed;
            float accelMagnitude = sample.Acceleration.magnitude;
            bool stableAccel = accelMagnitude < 0.0001f || Mathf.Abs(accelMagnitude - 1f) < 0.12f;
            bool stableGravity = _count == 0 || Vector3.Dot(g, _windowGravity) >= GravityCosTolerance;
            if (!lowMotion || !stableAccel || !stableGravity)
            {
                ResetWindow();
                return sample.Gyro - Bias;
            }

            if (_count == 0) _windowGravity = g;
            ++_count;
            Vector3 delta = sample.Gyro - _mean;
            _mean += delta / _count;
            Vector3 remaining = sample.Gyro - _mean;
            _m2 += Vector3.Scale(delta, remaining);
            if (_count > 4)
            {
                Vector3 variance = _m2 / (_count - 1);
                if (Mathf.Max(variance.x, Mathf.Max(variance.y, variance.z)) > MaxAxisVariance)
                {
                    ResetWindow();
                    return sample.Gyro - Bias;
                }
            }

            _stillSeconds += sample.DeltaTime;
            if (IsManualCalibration && _stillSeconds >= 3f)
            {
                Bias = _mean;
                IsCalibrated = true;
                IsManualCalibration = false;
            }
            else if (!IsManualCalibration && !IsCalibrated && _stillSeconds >= 1f)
            {
                Bias = _mean;
                IsCalibrated = true;
            }
            else if (!IsManualCalibration && IsCalibrated && _stillSeconds >= 2f)
            {
                Bias = Vector3.Lerp(Bias, _mean, 1f - Mathf.Exp(-sample.DeltaTime / 8f));
            }
            return sample.Gyro - Bias;
        }

        public void BeginManualCalibration()
        {
            IsManualCalibration = true;
            ResetWindow();
        }

        /// <summary>Restore the recording's calibration before its first raw sample is replayed.</summary>
        public void SetInitialBias(Vector3 bias)
        {
            if (!GyroSample.Finite(bias) || !GyroSample.Finite(bias.sqrMagnitude))
                throw new System.ArgumentException("Initial gyro bias must contain finite values.", nameof(bias));
            Reset();
            Bias = bias;
            IsCalibrated = true;
        }

        public void Reset()
        {
            Bias = Vector3.zero;
            IsCalibrated = false;
            IsManualCalibration = false;
            ResetWindow();
        }

        private void ResetWindow()
        {
            _count = 0;
            _stillSeconds = 0;
            _mean = Vector3.zero;
            _m2 = Vector3.zero;
            _windowGravity = Vector3.zero;
        }
    }
}
