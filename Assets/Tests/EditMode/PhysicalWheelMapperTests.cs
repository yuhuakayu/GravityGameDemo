using NUnit.Framework;
using Resource.Scripts.Gyro;

namespace Resource.Tests.Gyro
{
    public sealed class PhysicalWheelMapperTests
    {
        [TestCase(-1f, -180f)]
        [TestCase(0f, 0f)]
        [TestCase(1f, 180f)]
        public void EndpointsAndCenterCover360Degrees(float raw, float expected)
        {
            Assert.That(PhysicalWheelMapper.MapAngle(raw, -1f, 0f, 1f, false), Is.EqualTo(expected));
        }

        [TestCase(-0.3f, -90f)]
        [TestCase(0.4f, 90f)]
        public void AsymmetricCalibrationMapsEachHalfSeparately(float raw, float expected)
        {
            Assert.That(PhysicalWheelMapper.MapAngle(raw, -0.8f, 0.2f, 0.6f, false),
                Is.EqualTo(expected).Within(0.001f));
        }

        [TestCase(1f, -180f)]
        [TestCase(0.625f, -90f)]
        [TestCase(0.25f, 0f)]
        [TestCase(-0.375f, 90f)]
        [TestCase(-1f, 180f)]
        public void ReversedRawAxisPreservesPhysicalLeftAndRight(float raw, float expected)
        {
            Assert.That(PhysicalWheelMapper.MapAngle(raw, 1f, 0.25f, -1f, false), Is.EqualTo(expected));
        }

        [TestCase(-2f, -1f, 1f, -180f)]
        [TestCase(2f, -1f, 1f, 180f)]
        [TestCase(2f, 1f, -1f, -180f)]
        [TestCase(-2f, 1f, -1f, 180f)]
        public void InputBeyondEndpointsIsClamped(float raw, float left, float right, float expected)
        {
            Assert.That(PhysicalWheelMapper.MapAngle(raw, left, 0f, right, false), Is.EqualTo(expected));
        }

        [Test]
        public void DirectionInversionChangesOnlyTheSign()
        {
            Assert.That(PhysicalWheelMapper.MapAngle(-0.5f, -1f, 0f, 1f, true), Is.EqualTo(90f));
            Assert.That(PhysicalWheelMapper.MapAngle(0.5f, -1f, 0f, 1f, true), Is.EqualTo(-90f));
        }

        [TestCase(0f, 0f, 1f)]
        [TestCase(-1f, 1f, 1f)]
        [TestCase(-1f, 2f, 1f)]
        [TestCase(float.NaN, 0f, 1f)]
        [TestCase(-1f, float.PositiveInfinity, 1f)]
        public void InvalidCalibrationProducesNoAngle(float left, float center, float right)
        {
            Assert.That(PhysicalWheelMapper.IsValid(left, center, right), Is.False);
            Assert.That(PhysicalWheelMapper.MapAngle(0.5f, left, center, right, false), Is.Zero);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void NonfiniteInputProducesNoAngle(float raw)
        {
            Assert.That(PhysicalWheelMapper.MapAngle(raw, -1f, 0f, 1f, false), Is.Zero);
        }

        [Test]
        public void SanitizingWheelCalibrationPreservesExistingModesAndParameters()
        {
            var settings = new GyroSettings
            {
                mode = GyroControlMode.Wheel, sensitivity = 7f, angleMultiplier = 2f,
                wheelLeft = float.NaN, wheelCenter = float.PositiveInfinity,
                wheelRight = float.NegativeInfinity, wheelCalibrated = true
            };
            settings.Sanitize();
            Assert.That((int)GyroControlMode.Speed, Is.Zero);
            Assert.That((int)GyroControlMode.Angle, Is.EqualTo(1));
            Assert.That((int)settings.mode, Is.EqualTo(2));
            Assert.That(settings.sensitivity, Is.EqualTo(7f));
            Assert.That(settings.angleMultiplier, Is.EqualTo(2f));
            Assert.That(settings.wheelLeft, Is.EqualTo(-1f));
            Assert.That(settings.wheelCenter, Is.Zero);
            Assert.That(settings.wheelRight, Is.EqualTo(1f));
            Assert.That(settings.wheelCalibrated, Is.False);
        }
    }
}
