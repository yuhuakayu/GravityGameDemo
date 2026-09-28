using System;
using NUnit.Framework;
using Resource.Scripts.Gyro;
using UnityEngine;

namespace Resource.Tests.Gyro
{
    public sealed class GyroTests
    {
        private const float SampleDt = 1f / 250f;

        [Test]
        public void T1_Wheel90Degrees_HasPositiveStickAndGravityDerived90DegreeTarget()
        {
            var settings = new GyroSettings { angleSmoothTime = 0f };
            var processor = new GyroProcessor(settings);
            processor.AngleMapper.EnterAngleMode(0f, 0f);
            for (int i = 1; i <= 250; ++i)
            {
                float angle = 90f * i / 250f;
                processor.ProcessSample(Sample(Vector3.forward * 90f, GravityForWheel(angle), i * SampleDt));
                Assert.That(processor.EndFrame(i * SampleDt), Is.GreaterThan(0f));
            }
            Assert.That(processor.LastSteering.Angle, Is.EqualTo(90f).Within(1f));
            Assert.That(processor.AngleMapper.OutputAngle, Is.EqualTo(90f).Within(1f));
            TestContext.WriteLine($"T1: A={processor.StickOutput:F6}; gravity theta={processor.LastSteering.Angle:F6}°; B={processor.AngleMapper.OutputAngle:F6}° (smoothing=0).");

            // The default 50 ms filter is tested after a 0.5 s settling period, not disguised as an instant target.
            settings.angleSmoothTime = 0.05f;
            var smoothed = new WheelAngleMapper(settings);
            smoothed.EnterAngleMode(0f, 0f);
            for (int i = 1; i <= 250; ++i) smoothed.Evaluate(90f * i / 250f, SampleDt);
            float atOneSecond = smoothed.OutputAngle;
            for (int i = 0; i < 125; ++i) smoothed.Evaluate(90f, SampleDt);
            Assert.That(smoothed.OutputAngle, Is.EqualTo(90f).Within(1f));
            TestContext.WriteLine($"T1 default smoothing: moving endpoint={atOneSecond:F6}°; after 0.5 s stationary={smoothed.OutputAngle:F6}°.");
        }

        [Test]
        public void T2_Pitch60DegreesPerSecondFor2Seconds_IsZero()
        {
            var extractor = new SteeringExtractor();
            var mapper = new GyroStickMapper();
            var settings = new GyroSettings();
            float peak = 0;
            for (int i = 0; i < 500; ++i)
            {
                Vector3 g = Quaternion.AngleAxis(-60f * i * SampleDt, Vector3.right) * Vector3.down;
                var reading = extractor.Extract(Vector3.right * 60f, g);
                peak = Mathf.Max(peak, Mathf.Abs(mapper.Map(reading.AngularSpeed, settings)));
            }
            Assert.That(peak, Is.Zero);
            TestContext.WriteLine($"T2: maximum A output over 2 s={peak:F9}.");
        }

        [Test]
        public void T3_Yaw60DegreesPerSecondFor2Seconds_IsZero()
        {
            var extractor = new SteeringExtractor();
            var mapper = new GyroStickMapper();
            var settings = new GyroSettings();
            float peak = 0;
            Vector3 g = new Vector3(0.3f, -0.7f, -0.6f).normalized;
            for (int i = 0; i < 500; ++i)
            {
                var reading = extractor.Extract(g * 60f, g);
                peak = Mathf.Max(peak, Mathf.Abs(mapper.Map(reading.AngularSpeed, settings)));
            }
            Assert.That(peak, Is.Zero);
            TestContext.WriteLine($"T3: maximum A output over 2 s={peak:F9}.");
        }

        [Test]
        public void T4_Wheel30PlusPitch100_MatchesPureWheelWithin2Percent()
        {
            var pure = new SteeringExtractor();
            var mixed = new SteeringExtractor();
            float peakRelativeError = 0;
            for (int i = 0; i < 250; ++i)
            {
                Vector3 g = Quaternion.AngleAxis(-100f * i * SampleDt, Vector3.right) * GravityForWheel(30f * i * SampleDt);
                Vector3 f = Vector3.Cross(g, Vector3.right).normalized;
                float expected = pure.Extract(f * 30f, g).AngularSpeed;
                float actual = mixed.Extract(f * 30f + Vector3.right * 100f, g).AngularSpeed;
                peakRelativeError = Mathf.Max(peakRelativeError, Mathf.Abs(actual - expected) / 30f);
            }
            Assert.That(peakRelativeError, Is.LessThan(0.02f));
            TestContext.WriteLine($"T4: max mixed/pure wheel rate error={peakRelativeError * 100:F9}%.");
        }

        [Test]
        public void T5_FlatUprightAnd45DegreeGrip_MatchWithin2Percent()
        {
            float[] tilts = { 0f, 45f, 90f };
            float[] integrals = new float[3];
            for (int pose = 0; pose < tilts.Length; ++pose)
            {
                var extractor = new SteeringExtractor();
                var mapper = new GyroStickMapper();
                var settings = new GyroSettings();
                for (int i = 0; i < 250; ++i)
                {
                    Vector3 g = Quaternion.AngleAxis(tilts[pose], Vector3.right) * GravityForWheel(30f * i * SampleDt);
                    Vector3 f = Vector3.Cross(g, Vector3.right).normalized;
                    var reading = extractor.Extract(f * 30f, g);
                    Assert.That(reading.Angle, Is.EqualTo(30f * i * SampleDt).Within(0.001f));
                    integrals[pose] += mapper.Map(reading.AngularSpeed, settings) * SampleDt;
                }
            }
            float error = (Mathf.Max(integrals) - Mathf.Min(integrals)) / integrals[0];
            Assert.That(error, Is.LessThan(0.02f));
            TestContext.WriteLine($"T5: pose stick-seconds={integrals[0]:F9}, {integrals[1]:F9}, {integrals[2]:F9}; spread={error * 100:F9}%.");
        }

        [Test]
        public void T6_250HzSamplesAt60_144_600RenderFps_WorldAnglesWithin1Percent()
        {
            const double duration = 24;
            var data = new GyroSample[6000];
            for (int i = 0; i < data.Length; ++i)
            {
                double time = (i + 1) / 250.0;
                // Includes sign reversals, a changing amplitude and a second frequency, sampled slower than 600 FPS.
                float rate = 8f + 20f * Mathf.Sin((float)(time * Math.PI * 2 / 3)) +
                    4f * Mathf.Sin((float)(time * Math.PI * 2 * 7));
                data[i] = Sample(Vector3.forward * rate, Vector3.down, time);
            }
            double[] angles = { SimulateFrames(data, duration, 60), SimulateFrames(data, duration, 144),
                SimulateFrames(data, duration, 600) };
            double min = Math.Min(angles[0], Math.Min(angles[1], angles[2]));
            double max = Math.Max(angles[0], Math.Max(angles[1], angles[2]));
            double error = (max - min) / Math.Abs(angles[2]);
            Assert.That(error, Is.LessThan(0.01));
            TestContext.WriteLine($"T6: world angle at 60/144/600 FPS={angles[0]:F9}°, {angles[1]:F9}°, {angles[2]:F9}°; max spread={error * 100:F6}% (250 Hz sensor, 24 s, speed=90°/s; world consumes sample-time area).");
        }

        [Test]
        public void T7_HalfDegreeBiasPlusNoise_Calibrated5MinuteDriftBelow2Degrees()
        {
            var calibration = new GyroCalibration();
            var random = new System.Random(4081);
            double drift = 0;
            const int warmup = 300;
            const int fiveMinutes = 75000;
            for (int i = 0; i < warmup + fiveMinutes; ++i)
            {
                Vector3 raw = new Vector3(Noise(random), Noise(random), 0.5f + Noise(random));
                Vector3 corrected = calibration.Process(Sample(raw, Vector3.down, (i + 1) / 250.0));
                if (i >= warmup) drift += corrected.z * SampleDt;
            }
            Assert.That(calibration.IsCalibrated, Is.True);
            Assert.That(Math.Abs(drift), Is.LessThan(2));
            Assert.That(calibration.Bias.z, Is.EqualTo(0.5f).Within(0.01f));
            TestContext.WriteLine($"T7: 300 s unthresholded corrected wheel drift={drift:F9}°; bias={calibration.Bias.ToString("F6")}; seeded uniform noise ±0.03°/s, warmup=1.2 s.");
        }

        [Test]
        public void T8_StoppedSamples_ReturnZeroWithin50Milliseconds()
        {
            var processor = new GyroProcessor(new GyroSettings());
            processor.ProcessSample(Sample(Vector3.forward * 30f, Vector3.down, 1));
            Assert.That(processor.EndFrame(1), Is.GreaterThan(0));
            Assert.That(processor.EndFrame(1.019), Is.GreaterThan(0));
            Assert.That(processor.EndFrame(1.021), Is.Zero);
            Assert.That(processor.EndFrame(1.050), Is.Zero);
            Assert.DoesNotThrow(() => processor.EndFrame(20));
            TestContext.WriteLine("T8: output held at 19 ms, zero at 21 ms and 50 ms; no exception on prolonged disconnect.");
        }

        [Test]
        public void T9_NearParallelGravity_EnteringCrossingAndLeavingConeHasNoNaNOrRateJump()
        {
            var extractor = new SteeringExtractor();
            float previous = extractor.Extract(Vector3.forward * 30f, GravityForWheel(65f)).AngularSpeed;
            float maxJump = 0f;
            for (int i = 0; i <= 1000; ++i)
            {
                float angle = 65f + 50f * i / 1000f;
                Vector3 g = GravityForWheel(angle);
                if (angle > 80f && angle < 100f) g.z += Mathf.Sin(i * 2.3f) * 0.00001f;
                var reading = extractor.Extract(Vector3.forward * 30f, g);
                Assert.That(float.IsNaN(reading.Angle) || float.IsInfinity(reading.Angle), Is.False);
                Assert.That(float.IsNaN(reading.AngularSpeed) || float.IsInfinity(reading.AngularSpeed), Is.False);
                maxJump = Mathf.Max(maxJump, Mathf.Abs(reading.AngularSpeed - previous));
                previous = reading.AngularSpeed;
            }
            Assert.That(maxJump, Is.LessThan(0.01f));
            var singularStart = new SteeringExtractor().Extract(Vector3.forward * 30f, -Vector3.right);
            Assert.That(singularStart.AngularSpeed, Is.Zero);
            Assert.That(singularStart.Angle, Is.EqualTo(90f).Within(0.001f));
            TestContext.WriteLine($"T9: wheel 65→115°, noisy singular cone; maximum rate step={maxJump:F9}°/s; singular-start output=0, theta=90°.");
        }

        [Test]
        public void T10_ModeSwitchAndPulseResume_PreserveCurrentWorldAngle()
        {
            var settings = new GyroSettings { angleSmoothTime = 0f, angleMultiplier = 2f };
            var mapper = new WheelAngleMapper(settings);
            mapper.EnterAngleMode(31f, 137f);
            float afterSwitch = mapper.Evaluate(31f, SampleDt);
            Assert.That(afterSwitch, Is.EqualTo(137f).Within(0.0001f));
            Assert.That(mapper.Evaluate(36f, SampleDt), Is.EqualTo(147f).Within(0.0001f));
            mapper.SetBlocked(true, 36f, 147f);
            Assert.That(mapper.Evaluate(-42f, SampleDt), Is.EqualTo(147f).Within(0.0001f));
            mapper.SetBlocked(false, -42f, 147f);
            float afterPulse = mapper.Evaluate(-42f, SampleDt);
            Assert.That(afterPulse, Is.EqualTo(147f).Within(0.0001f));
            Assert.That(mapper.Evaluate(-37f, SampleDt), Is.EqualTo(157f).Within(0.0001f));
            mapper.Recenter(-37f, 157f);
            Assert.That(mapper.Evaluate(-37f, SampleDt), Is.EqualTo(157f).Within(0.0001f));
            TestContext.WriteLine($"T10: mode-switch snap={afterSwitch - 137f:F9}°; blocked movement=0°; pulse-resume snap={afterPulse - 147f:F9}°; new 5° wheel movement gives 10° world movement.");
        }

        [Test]
        public void MappingOrder_DeadzoneSensitivityPrecisionCurveAndInvertMatchSpecification()
        {
            var settings = new GyroSettings { sensitivity = 2f, speedDeadzone = 1f, precisionSpeed = 40f,
                minInputSpeed = 0f, maxInputSpeed = 100f, minOutput = 0.1f, maxOutput = 0.9f, outputCurve = 2f };
            var mapper = new GyroStickMapper();
            Assert.That(mapper.Map(0.9f, settings), Is.Zero);
            // 10*2=20; precision gives 20*(20/40)=10; t=.1; square=.01; output=.108.
            Assert.That(mapper.Map(10f, settings), Is.EqualTo(0.108f).Within(0.000001f));
            settings.invertDirection = true;
            Assert.That(mapper.Map(10f, settings), Is.EqualTo(-0.108f).Within(0.000001f));
            settings.speedDeadzone = 0f;
            Assert.That(mapper.Map(0f, settings), Is.Zero);
        }

        [Test]
        public void FrameOutput_UsesSampleTimeWeightsAndRejectsInvalidSamples()
        {
            var settings = new GyroSettings { sensitivity = 1f, minOutput = 0f, maxInputSpeed = 100f };
            var processor = new GyroProcessor(settings);
            processor.ProcessSample(new GyroSample(0.002f, Vector3.forward * 10f, Vector3.up, Vector3.down, 1));
            processor.ProcessSample(new GyroSample(0.006f, Vector3.forward * 30f, Vector3.up, Vector3.down, 1.006));
            Assert.That(processor.EndFrame(1.006), Is.EqualTo(0.25f).Within(0.000001f));
            Assert.That(processor.ProcessSample(new GyroSample(float.NaN, Vector3.zero, Vector3.zero, Vector3.down)), Is.False);
            Assert.That(processor.ProcessSample(new GyroSample(0.5f, Vector3.zero, Vector3.zero, Vector3.down)), Is.False);
            Assert.That(processor.ProcessSample(new GyroSample(SampleDt, Vector3.zero, Vector3.zero, Vector3.zero)), Is.False);
            processor.Disconnect();
            Assert.That(processor.EndFrame(1.007), Is.Zero);
        }

        [Test]
        public void Calibration_RequiresStationarySecondsAndManualThreeSeconds()
        {
            var calibration = new GyroCalibration();
            for (int i = 1; i <= 250; ++i)
                calibration.Process(Sample(Vector3.forward * 30f, GravityForWheel(30f * i * SampleDt), i * SampleDt));
            Assert.That(calibration.IsCalibrated, Is.False, "A constant-rate turn is not a stationary bias.");
            calibration.BeginManualCalibration();
            for (int i = 0; i < 700; ++i)
                calibration.Process(Sample(Vector3.forward * 0.5f, Vector3.down, i * SampleDt));
            Assert.That(calibration.IsManualCalibration, Is.True);
            Assert.That(calibration.Progress, Is.InRange(0.92f, 0.95f));
            for (int i = 0; i < 55; ++i)
                calibration.Process(Sample(Vector3.forward * 0.5f, Vector3.down, i * SampleDt));
            Assert.That(calibration.IsManualCalibration, Is.False);
            Assert.That(calibration.Bias.z, Is.EqualTo(0.5f).Within(0.000001f));
        }

        private static GyroSample Sample(Vector3 gyro, Vector3 gravity, double timestamp) =>
            new GyroSample(SampleDt, gyro, -gravity.normalized, gravity, timestamp);

        private static Vector3 GravityForWheel(float clockwiseDegrees)
        {
            float radians = clockwiseDegrees * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(radians), -Mathf.Cos(radians), 0f);
        }

        private static float Noise(System.Random random) => (float)(random.NextDouble() * 0.06 - 0.03);

        private static double SimulateFrames(GyroSample[] samples, double duration, int fps)
        {
            var processor = new GyroProcessor(new GyroSettings());
            int next = 0;
            double worldAngle = 0;
            double naiveFrameAngle = 0;
            double previousTime = 0;
            for (int frame = 1; frame <= (int)(duration * fps); ++frame)
            {
                double now = frame / (double)fps;
                while (next < samples.Length && samples[next].TimestampSeconds <= now + 0.000000001)
                    processor.ProcessSample(samples[next++]);
                naiveFrameAngle += processor.EndFrame(now) * (now - previousTime) * 90.0;
                worldAngle += processor.ConsumeStickIntegral() * 90.0;
                previousTime = now;
            }
            Assert.That(next, Is.EqualTo(samples.Length));
            Assert.That(worldAngle, Is.EqualTo(processor.IntegratedStickSeconds * 90).Within(0.000001));
            TestContext.WriteLine($"T6 {fps} FPS: sample-derived reference={processor.IntegratedStickSeconds * 90:F9}°; actual consumed world delta={worldAngle:F9}°; naive frame-clock readout integration (not used for world)={naiveFrameAngle:F9}°.");
            return worldAngle;
        }
    }
}
