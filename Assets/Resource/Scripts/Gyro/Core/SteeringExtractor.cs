using UnityEngine;

namespace Resource.Scripts.Gyro
{
    public readonly struct SteeringReading
    {
        public Vector3 Axis { get; }
        public float AngularSpeed { get; }
        public float Angle { get; }
        public float FilteredSpeed { get; }
        public bool IsDegenerate { get; }

        public SteeringReading(Vector3 axis, float speed, float angle, float filteredSpeed, bool degenerate)
        {
            Axis = axis;
            AngularSpeed = speed;
            Angle = angle;
            FilteredSpeed = filteredSpeed;
            IsDegenerate = degenerate;
        }
    }

    /// <summary>All vectors are in controller-local coordinates. No Unity lifecycle or frame clock.</summary>
    public sealed class SteeringExtractor
    {
        private Vector3 _lastAxis = Vector3.forward;
        private float _lastAngle;
        private bool _hasAxis;
        private bool _crossedDegeneracy;

        public bool HasAxis => _hasAxis;
        public Vector3 LastAxis => _lastAxis;
        public bool CrossedDegeneracy => _crossedDegeneracy;

        /// <summary>Restore the wheel hemisphere held when recording began, including a crossed singular cone.</summary>
        public void SetInitialAxis(Vector3 axis, bool crossedDegeneracy)
        {
            if (!GyroSample.Finite(axis) || !GyroSample.Finite(axis.sqrMagnitude) || axis.sqrMagnitude < 0.000001f)
                throw new System.ArgumentException("Initial steering axis must be finite and nonzero.", nameof(axis));
            _lastAxis = axis.normalized;
            _hasAxis = true;
            _crossedDegeneracy = crossedDegeneracy;
        }

        public SteeringReading Extract(Vector3 calibratedGyro, Vector3 gravity)
        {
            if (!GyroSample.Finite(calibratedGyro) || !GyroSample.Finite(gravity) || gravity.sqrMagnitude < 0.000001f)
                return new SteeringReading(_lastAxis, 0f, _lastAngle, 0f, true);

            Vector3 g = gravity.normalized;
            float xDotG = Vector3.Dot(Vector3.right, g);
            bool degenerate = Mathf.Abs(xDotG) > 0.95f;
            if (!degenerate)
            {
                Vector3 candidate = Vector3.Cross(g, Vector3.right).normalized;
                // Crossing the singular cone must not reverse the previously established wheel axis.
                if (_hasAxis && _crossedDegeneracy && Vector3.Dot(candidate, _lastAxis) < 0f)
                    candidate = -candidate;
                if (_hasAxis)
                {
                    // A pitched grip can roll past sideways without entering the singular cone.
                    // Compare both axes against this sample's gyro so a real input reversal is
                    // still accepted, while a change of projection hemisphere cannot reverse it.
                    float previousSpeed = Vector3.Dot(calibratedGyro, _lastAxis);
                    float candidateSpeed = Vector3.Dot(calibratedGyro, candidate);
                    if (Mathf.Abs(candidateSpeed) < 0.00001f && Mathf.Abs(previousSpeed) > 0.00001f)
                        candidate = _lastAxis; // Preserve the reference at an exact zero crossing.
                    else if (previousSpeed * candidateSpeed < 0f)
                    {
                        candidate = -candidate;
                        _crossedDegeneracy = true; // Keep this hemisphere through zero-rate samples.
                    }
                }
                _lastAxis = candidate;
                _hasAxis = true;
            }
            else
            {
                _crossedDegeneracy = true;
            }

            float horizontalLength = (Vector3.right - xDotG * g).magnitude;
            _lastAngle = Mathf.Atan2(-xDotG, horizontalLength) * Mathf.Rad2Deg;
            // On a singular first sample there is no measured axis to preserve, so output zero.
            float speed = _hasAxis ? Vector3.Dot(calibratedGyro, _lastAxis) : 0f;
            return new SteeringReading(_lastAxis, speed, _lastAngle,
                Mathf.Max(0f, calibratedGyro.magnitude - Mathf.Abs(speed)), degenerate);
        }

        public void Reset()
        {
            _lastAxis = Vector3.forward;
            _lastAngle = 0f;
            _hasAxis = false;
            _crossedDegeneracy = false;
        }
    }
}
