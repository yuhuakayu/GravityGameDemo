using System;
using NUnit.Framework;
using Resource.Scripts.Gyro;
using UnityEngine;

namespace Resource.Tests.Gyro
{
    /// <summary>Independent regressions for sensor/render clock boundaries and ambiguous stationary input.</summary>
    public sealed class GyroAdditionalTests
    {
        private const float SampleDelta = 1f / 250f;

        [Test]
        public void SensorClock_BurstTraceAt250HzHasSameWorldRotationAt60_144_600Fps()
        {
            // Deliberately aligned bursts expose the aliasing hidden by a smooth or constant input.
            // Integrating EndFrame()*renderDelta would give approximately 144/145/179.7 degrees.
            int[] frameRates = { 60, 144, 600 };
            double[] angles = new double[frameRates.Length];
            for (int run = 0; run < frameRates.Length; ++run)
            {
                var processor = new GyroProcessor(new GyroSettings());
                int sampleIndex = 0;
                int emptyFrames = 0;
                for (int frame = 1; frame <= 8 * frameRates[run]; ++frame)
                {
                    double now = frame / (double)frameRates[run];
                    int before = sampleIndex;
                    while (sampleIndex < 2000 && (sampleIndex + 1) / 250.0 <= now + 1e-9)
                    {
                        ++sampleIndex;
                        float speed = sampleIndex % 5 == 0 ? 90f : 0f;
                        Assert.That(processor.ProcessSample(Sample(speed, Vector3.down, sampleIndex / 250.0)), Is.True);
                    }

                    if (before == sampleIndex) ++emptyFrames;
                    float readout = processor.EndFrame(now);
                    Assert.That(readout, Is.InRange(-1f, 1f));
                    // This is the runtime contract: the frame schedules consumption, never integration.
                    angles[run] += processor.ConsumeStickIntegral() * 90.0;
                    Assert.That(processor.ConsumeStickIntegral(), Is.Zero, "Samples must not rotate the world twice.");
                }

                Assert.That(sampleIndex, Is.EqualTo(2000));
                Assert.That(angles[run], Is.EqualTo(144.0).Within(0.0001));
                if (frameRates[run] == 600) Assert.That(emptyFrames, Is.GreaterThan(0));
                TestContext.WriteLine($"250 Hz burst trace at {frameRates[run]} FPS: {angles[run]:F9} degrees; {emptyFrames} frames without new samples.");
            }

            double spread = Math.Max(angles[0], Math.Max(angles[1], angles[2])) -
                Math.Min(angles[0], Math.Min(angles[1], angles[2]));
            Assert.That(spread / 144.0, Is.LessThan(0.01));
        }

        [Test]
        public void HeldFrameReadout_DoesNotInventSensorTimeOrReplayAfterDisconnect()
        {
            var processor = new GyroProcessor(new GyroSettings());
            processor.ProcessSample(Sample(90f, Vector3.down, 1));
            Assert.That(processor.EndFrame(1), Is.EqualTo(1f));
            Assert.That(processor.ConsumeStickIntegral(), Is.EqualTo(SampleDelta).Within(1e-9));

            Assert.That(processor.EndFrame(1.019), Is.EqualTo(1f));
            Assert.That(processor.ConsumeStickIntegral(), Is.Zero);
            Assert.That(processor.EndFrame(1.021), Is.Zero);
            processor.ProcessSample(Sample(90f, Vector3.down, 2));
            processor.Disconnect();
            Assert.That(processor.ConsumeStickIntegral(), Is.Zero, "Disconnected input must not catch up after reconnect.");
            Assert.That(processor.EndFrame(2), Is.Zero);
        }

        [TestCase(1, TestName = "ModeA_TiltedContinuousRightInput_Reaches360WithoutReversing")]
        [TestCase(-1, TestName = "ModeA_TiltedContinuousLeftInput_Reaches360WithoutReversing")]
        public void ModeA_TiltedContinuousInput_Reaches360WithoutReversing(int direction)
        {
            var processor = new GyroProcessor(new GyroSettings { mode = GyroControlMode.Speed });
            const float rollSpeed = 45f;
            const float worldSpeed = 90f;
            float tilt = 30f * Mathf.Deg2Rad;
            double worldAngle = 0;
            double minimumSignedStep = double.PositiveInfinity;
            bool sampledSideOn = false;
            int samples = 0;
            for (int index = 0; index < 10000 && direction * worldAngle < 360.0; ++index)
            {
                // At index 500 the roll is exactly +/-90 degrees. With this initial tilt,
                // |gravity.x| never reaches the old 0.95 singular-cone threshold.
                float rollAngle = direction * index * rollSpeed / 250f;
                float roll = rollAngle * Mathf.Deg2Rad;
                Vector3 gravity = new Vector3(-Mathf.Cos(tilt) * Mathf.Sin(roll),
                    -Mathf.Cos(tilt) * Mathf.Cos(roll), -Mathf.Sin(tilt));
                double now = (index + 1) / 250.0;
                Assert.That(processor.ProcessSample(new GyroSample(SampleDelta,
                    Vector3.forward * (direction * rollSpeed), -gravity, gravity, now)), Is.True);
                processor.EndFrame(now);

                // Use the existing Mode A runtime contract: integrate callback-time stick area,
                // then apply the current 720-degree steering range (360 degrees each way).
                double delta = processor.ConsumeStickIntegral() * worldSpeed;
                double previousAngle = worldAngle;
                worldAngle = Math.Max(-360.0, Math.Min(360.0, worldAngle + delta));
                double signedStep = direction * (worldAngle - previousAngle);
                minimumSignedStep = Math.Min(minimumSignedStep, signedStep);
                Assert.That(signedStep, Is.GreaterThanOrEqualTo(-1e-9),
                    $"Mode A reversed at sample {index}, hand roll {rollAngle:F3} degrees, world angle {worldAngle:F6} degrees.");
                sampledSideOn |= index == 500;
                ++samples;
            }

            Assert.That(sampledSideOn, Is.True, "The regression must cross the exact side-on sample.");
            Assert.That(worldAngle, Is.EqualTo(direction * 360.0).Within(0.0001),
                "Continuous input must reach the steering limit instead of folding back or stalling.");
            TestContext.WriteLine($"Mode A direction={direction}: world={worldAngle:F6} degrees, samples={samples}, minimum signed step={minimumSignedStep:F9} degrees.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SlowConstantRoll_WithLowGyroVarianceIsNotStationaryCalibration(bool manual)
        {
            var calibration = new GyroCalibration();
            if (manual) calibration.BeginManualCalibration();
            for (int sample = 1; sample <= 1500; ++sample)
            {
                double now = sample / 250.0;
                // Below the absolute speed threshold, but gravity proves the device is moving.
                float angle = (float)now * 1.5f;
                calibration.Process(Sample(1.5f, GravityAt(angle), now));
            }

            Assert.That(calibration.IsCalibrated, Is.False);
            Assert.That(calibration.Bias, Is.EqualTo(Vector3.zero));
            if (manual) Assert.That(calibration.IsManualCalibration, Is.True);
            TestContext.WriteLine($"Constant 1.5 degrees/s over 6 s, manual={manual}: calibrated={calibration.IsCalibrated}, bias={calibration.Bias}.");
        }

        [Test]
        public void SingularCone_CrossingOutAndBackKeepsAxisAndRateContinuous()
        {
            var extractor = new SteeringExtractor();
            Vector3 previousAxis = extractor.Extract(Vector3.forward * 30f, GravityAt(65f)).Axis;
            float maximumRateError = 0f;
            for (int index = 0; index <= 2000; ++index)
            {
                float angle = index <= 1000 ? 65f + index * 0.05f : 115f - (index - 1000) * 0.05f;
                var reading = extractor.Extract(Vector3.forward * 30f, GravityAt(angle));
                Assert.That(Vector3.Dot(previousAxis, reading.Axis), Is.GreaterThan(0.999f));
                maximumRateError = Mathf.Max(maximumRateError, Mathf.Abs(reading.AngularSpeed - 30f));
                Assert.That(float.IsNaN(reading.Angle) || float.IsInfinity(reading.Angle), Is.False);
                previousAxis = reading.Axis;
            }
            Assert.That(maximumRateError, Is.LessThan(0.001f));
            TestContext.WriteLine($"65 to 115 to 65 degrees: max wheel rate error={maximumRateError:F9} degrees/s.");
        }

        [Test]
        public void InvalidGravityAndRates_AreRejectedWithoutPoisoningTheNextSample()
        {
            var processor = new GyroProcessor(new GyroSettings());
            var extractor = new SteeringExtractor();
            extractor.Extract(Vector3.forward * 30f, GravityAt(25f));
            Vector3[] invalidGravity = { Vector3.zero, new Vector3(float.NaN, -1f, 0f),
                new Vector3(0f, float.PositiveInfinity, 0f) };
            foreach (Vector3 gravity in invalidGravity)
            {
                Assert.That(processor.ProcessSample(new GyroSample(SampleDelta, Vector3.forward * 30f,
                    Vector3.up, gravity, 1)), Is.False);
                SteeringReading reading = extractor.Extract(Vector3.forward * 30f, gravity);
                Assert.That(reading.AngularSpeed, Is.Zero);
                Assert.That(reading.Angle, Is.EqualTo(25f).Within(0.001f));
            }
            Assert.That(processor.ProcessSample(new GyroSample(SampleDelta,
                new Vector3(float.NaN, 0f, 0f), Vector3.up, Vector3.down, 1)), Is.False);
            Assert.That(processor.ProcessedSamples, Is.Zero);
            Assert.That(processor.ConsumeStickIntegral(), Is.Zero);
            Assert.That(processor.ProcessSample(Sample(90f, Vector3.down, 2)), Is.True);
            Assert.That(processor.EndFrame(2), Is.EqualTo(1f));
            Assert.That(processor.ConsumeStickIntegral(), Is.EqualTo(SampleDelta).Within(1e-9));
        }

        private static GyroSample Sample(float wheelSpeed, Vector3 gravity, double timestamp) =>
            new GyroSample(SampleDelta, Vector3.forward * wheelSpeed, -gravity.normalized, gravity, timestamp);

        private static Vector3 GravityAt(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(radians), -Mathf.Cos(radians), 0f);
        }
    }
}
