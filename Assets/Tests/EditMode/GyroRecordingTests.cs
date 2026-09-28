using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Resource.Scripts.Gyro;
using UnityEngine;

namespace Resource.Tests.Gyro
{
    public sealed class GyroRecordingTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "GravityGameGyroRecordingTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void RecordedCsv_RoundTripsAllRawFloatsAndRebuildsRelativeCallbackTime()
        {
            var expected = new List<GyroSample>();
            string path;
            using (var recorder = new GyroRecorder(_directory))
            {
                path = recorder.Start();
                for (int i = 0; i < 511; ++i)
                {
                    var sample = new GyroSample(0.003f + i % 3 * 0.0001234567f,
                        new Vector3(0.12345679f + i, -19.876543f, 0.000001234567f),
                        new Vector3(0.2f, 0.99999994f, -0.1f), new Vector3(-0.3f, -0.9539392f, 0f), 5000 + i);
                    expected.Add(sample);
                    recorder.Append(sample);
                }
                Assert.That(recorder.IsRecording, Is.True);
                Assert.That(recorder.SampleCount, Is.EqualTo(expected.Count));
                recorder.Stop();
                Assert.That(recorder.IsRecording, Is.False);
                Assert.That(recorder.CurrentPath, Is.EqualTo(path));
            }
            GyroRecording recording = GyroRecording.Load(path);
            Assert.That(recording.Samples.Count, Is.EqualTo(expected.Count));
            Assert.That(recording.FilePath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(recording.InitialBias, Is.Null, "The original ten-column format remains supported.");
            double duration = 0;
            for (int i = 0; i < expected.Count; ++i)
            {
                duration += expected[i].DeltaTime;
                GyroSample actual = recording.Samples[i];
                Assert.That(actual.DeltaTime, Is.EqualTo(expected[i].DeltaTime));
                Assert.That(actual.Gyro, Is.EqualTo(expected[i].Gyro));
                Assert.That(actual.Acceleration, Is.EqualTo(expected[i].Acceleration));
                Assert.That(actual.Gravity, Is.EqualTo(expected[i].Gravity));
                Assert.That(actual.TimestampSeconds, Is.EqualTo(duration));
            }
            Assert.That(recording.Duration, Is.EqualTo(duration));
            TestContext.WriteLine($"CSV round trip: {expected.Count} samples, duration={duration:R}s, all 10 raw float columns preserved exactly.");
        }

        [Test]
        public void CurrentCultureWithDecimalComma_DoesNotChangeCsvFormatOrParsing()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                string path;
                using (var recorder = new GyroRecorder(_directory))
                {
                    path = recorder.Start();
                    recorder.Append(ValidSample());
                }
                string[] lines = File.ReadAllLines(path);
                Assert.That(lines[0], Is.EqualTo(GyroRecording.CsvHeader));
                Assert.That(lines[1].Split(',').Length, Is.EqualTo(10));
                Assert.That(lines[1], Does.StartWith("0.004,"));
                Assert.That(GyroRecording.Load(path).Samples[0].Gyro.x, Is.EqualTo(1.25f));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void RecordingConstructor_CopiesInputAndExposesReadOnlySamples()
        {
            GyroSample first = ValidSample();
            var input = new[] { first };
            var recording = new GyroRecording(input);
            input[0] = new GyroSample(0.01f, Vector3.zero, Vector3.up, Vector3.down);
            Assert.That(recording.Samples[0].Gyro, Is.EqualTo(first.Gyro));
            Assert.That(recording.Samples[0].TimestampSeconds, Is.EqualTo((double)first.DeltaTime));
            var list = recording.Samples as IList<GyroSample>;
            Assert.That(list, Is.Not.Null);
            Assert.That(list.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => list[0] = input[0]);
        }

        [TestCase("")]
        [TestCase(GyroRecording.CsvHeader + "\n")]
        public void EmptyRecordings_AreRejected(string content)
        {
            string path = WriteCsv(content);
            Assert.Throws<InvalidDataException>(() => GyroRecording.Load(path));
            Assert.Throws<InvalidDataException>(() => new GyroRecording(Array.Empty<GyroSample>()));
        }

        [TestCase("0.004,NaN,0,0,0,1,0,0,-1,0")]
        [TestCase("0.004,Infinity,0,0,0,1,0,0,-1,0")]
        [TestCase("0.004,1e100,0,0,0,1,0,0,-1,0")]
        [TestCase("0.004,3e38,0,0,0,1,0,0,-1,0")]
        [TestCase("0,1,0,0,0,1,0,0,-1,0")]
        [TestCase("-0.004,1,0,0,0,1,0,0,-1,0")]
        [TestCase("0.11,1,0,0,0,1,0,0,-1,0")]
        [TestCase("0.004,1,0,0,0,1,0,0,0,0")]
        [TestCase("0.004,1,0,0,0,1,0,0,-1")]
        [TestCase("0.004,1,0,0,0,1,0,0,-1,0,extra")]
        [TestCase("")]
        public void MalformedRow_IsRejectedWithItsLineNumberInsteadOfBeingDropped(string row)
        {
            string valid = "0.004,1,0,0,0,1,0,0,-1,0";
            string path = WriteCsv(GyroRecording.CsvHeader + "\n" + valid + "\n" + row + "\n");
            var error = Assert.Throws<InvalidDataException>(() => GyroRecording.Load(path));
            Assert.That(error.Message, Does.Contain("第 3 行"));
        }

        [Test]
        public void HeaderMismatchAndOversizedLine_AreRejected()
        {
            Assert.Throws<InvalidDataException>(() => GyroRecording.Load(WriteCsv("dt,gyro\n0.004,1\n")));
            string longLine = GyroRecording.CsvHeader + "\n" + new string('1', GyroRecording.MaximumLineCharacters + 1);
            var error = Assert.Throws<InvalidDataException>(() => GyroRecording.Load(WriteCsv(longLine)));
            Assert.That(error.Message, Does.Contain("1024"));
        }

        [Test]
        public void OversizedFileAndSampleCollection_AreRejectedBeforeLoading()
        {
            string path = Path.Combine(_directory, "oversized.csv");
            using (var stream = File.Create(path)) stream.SetLength(GyroRecording.MaximumFileBytes + 1);
            Assert.Throws<InvalidDataException>(() => GyroRecording.Load(path));
            Assert.Throws<InvalidDataException>(() => new GyroRecording(new OversizedSamples()));
        }

        [Test]
        public void RepeatedStartDoesNotOverwriteAndStopFlushesPartialBatch()
        {
            using (var recorder = new GyroRecorder(_directory))
            {
                string first = recorder.Start();
                recorder.Append(ValidSample());
                Assert.Throws<InvalidOperationException>(() => recorder.Start());
                Assert.That(recorder.IsRecording, Is.True);
                recorder.Stop();
                string oldContents = File.ReadAllText(first);
                string second = recorder.Start();
                Assert.That(second, Is.Not.EqualTo(first));
                Assert.That(recorder.SampleCount, Is.Zero);
                recorder.Append(ValidSample());
                recorder.Stop();
                recorder.Stop();
                Assert.That(File.ReadAllText(first), Is.EqualTo(oldContents));
                Assert.That(GyroRecording.Load(first).Samples.Count, Is.EqualTo(1));
                Assert.That(GyroRecording.Load(second).Samples.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void InvalidAppendDoesNotWriteACorruptRowAndDisposedRecorderCannotRestart()
        {
            var recorder = new GyroRecorder(_directory);
            string path = recorder.Start();
            Assert.Throws<InvalidDataException>(() => recorder.Append(
                new GyroSample(0.004f, new Vector3(float.NaN, 0, 0), Vector3.up, Vector3.down)));
            Assert.That(recorder.SampleCount, Is.Zero);
            Assert.That(recorder.IsRecording, Is.True);
            recorder.Append(ValidSample());
            recorder.Dispose();
            recorder.Dispose();
            Assert.That(recorder.IsRecording, Is.False);
            Assert.That(GyroRecording.Load(path).Samples.Count, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => recorder.Start());
            Assert.Throws<ObjectDisposedException>(() => recorder.Append(ValidSample()));
        }

        [Test]
        public void StartIoFailureDoesNotClaimToBeRecording()
        {
            string blockedDirectory = Path.Combine(_directory, "existing-file");
            File.WriteAllText(blockedDirectory, "a file cannot be used as a directory");
            using (var recorder = new GyroRecorder(blockedDirectory))
            {
                Assert.Throws<IOException>(() => recorder.Start());
                Assert.That(recorder.IsRecording, Is.False);
                Assert.That(recorder.SampleCount, Is.Zero);
            }
        }

        [Test]
        public void RecordingThatStartsDuringMotion_RestoresLiveBiasAndMatchesLiveRotation()
        {
            var settings = new GyroSettings();
            var live = new GyroProcessor(settings);
            for (int i = 0; i < 300; ++i)
                live.ProcessSample(new GyroSample(0.004f, Vector3.forward * 2f, Vector3.up, Vector3.down));
            Assert.That(live.Calibration.IsCalibrated, Is.True);
            Assert.That(live.Calibration.Bias.z, Is.EqualTo(2f).Within(0.00001f));
            live.ConsumeStickIntegral();
            string path;
            using (var recorder = new GyroRecorder(_directory))
            {
                path = recorder.Start(live.Calibration.Bias);
                for (int i = 1; i <= 2500; ++i)
                {
                    float radians = 20f * i * 0.004f * Mathf.Deg2Rad;
                    Vector3 gravity = new Vector3(-Mathf.Sin(radians), -Mathf.Cos(radians), 0f);
                    var raw = new GyroSample(0.004f, Vector3.forward * 22f, -gravity, gravity);
                    recorder.Append(raw);
                    live.ProcessSample(raw);
                }
            }
            GyroRecording recording = GyroRecording.Load(path);
            Assert.That(recording.InitialBias.HasValue, Is.True);
            Assert.That(recording.InitialBias.Value, Is.EqualTo(live.Calibration.Bias));
            var replay = new GyroProcessor(settings.Clone());
            replay.Calibration.SetInitialBias(recording.InitialBias.Value);
            foreach (GyroSample sample in recording.Samples) replay.ProcessSample(sample);
            double liveAngle = live.ConsumeStickIntegral() * 90;
            double replayAngle = replay.ConsumeStickIntegral() * 90;
            Assert.That(replayAngle, Is.EqualTo(liveAngle).Within(0.000001));
            TestContext.WriteLine($"Motion-start CSV with 2°/s initial bias: live={liveAngle:F9}°, replay={replayAngle:F9}°, delta={replayAngle - liveAngle:F9}° over 10 s.");
        }

        [TestCase("#initialBias,NaN,0,0")]
        [TestCase("#initialBias,0,Infinity,0")]
        [TestCase("#initialBias,3e38,0,0")]
        [TestCase("#initialBias,0,0")]
        public void InvalidCalibrationMetadata_IsRejected(string metadata)
        {
            string path = WriteCsv(GyroRecording.CsvHeader + "\n" + metadata + "\n0.004,1,0,0,0,1,0,0,-1,0\n");
            Assert.Throws<InvalidDataException>(() => GyroRecording.Load(path));
        }

        [Test]
        public void CalibrationMetadataMustPrecedeSamplesAndInitialBiasRejectsNonfiniteValues()
        {
            string path = WriteCsv(GyroRecording.CsvHeader + "\n0.004,1,0,0,0,1,0,0,-1,0\n#initialBias,0,0,0\n");
            Assert.Throws<InvalidDataException>(() => GyroRecording.Load(path));
            var calibration = new GyroCalibration();
            calibration.SetInitialBias(new Vector3(0.1f, 0.2f, 0.3f));
            Assert.That(calibration.IsCalibrated, Is.True);
            Assert.That(calibration.StillSeconds, Is.Zero);
            Assert.Throws<ArgumentException>(() => calibration.SetInitialBias(new Vector3(float.NaN, 0, 0)));
        }

        [Test]
        public void RecordingAfterCrossing90Degrees_RestoresWheelHemisphereAndMotionSign()
        {
            var settings = new GyroSettings();
            var live = new GyroProcessor(settings);
            live.Calibration.SetInitialBias(Vector3.forward * 0.5f);
            for (int i = 0; i <= 500; ++i)
            {
                float radians = (65f + 50f * i / 500f) * Mathf.Deg2Rad;
                Vector3 gravity = new Vector3(-Mathf.Sin(radians), -Mathf.Cos(radians), 0f);
                live.ProcessSample(new GyroSample(0.004f, Vector3.forward * 25.5f, -gravity, gravity));
            }
            Assert.That(live.SteeringCrossedDegeneracy, Is.True);
            Assert.That(live.SteeringAxis.HasValue, Is.True);
            Assert.That(Vector3.Dot(live.SteeringAxis.Value, Vector3.forward), Is.GreaterThan(0.99f));
            live.ConsumeStickIntegral();
            string path;
            using (var recorder = new GyroRecorder(_directory))
            {
                path = recorder.Start(live.Calibration.Bias, live.SteeringAxis, live.SteeringCrossedDegeneracy);
                for (int i = 1; i <= 250; ++i)
                {
                    float radians = (115f + 10f * i / 250f) * Mathf.Deg2Rad;
                    Vector3 gravity = new Vector3(-Mathf.Sin(radians), -Mathf.Cos(radians), 0f);
                    var raw = new GyroSample(0.004f, Vector3.forward * 10.5f, -gravity, gravity);
                    recorder.Append(raw);
                    live.ProcessSample(raw);
                }
            }
            GyroRecording recording = GyroRecording.Load(path);
            Assert.That(recording.InitialAxisCrossedDegeneracy, Is.True);
            Assert.That(recording.InitialAxis.HasValue, Is.True);
            var replay = new GyroProcessor(settings.Clone());
            replay.Calibration.SetInitialBias(recording.InitialBias.Value);
            replay.SetInitialSteeringAxis(recording.InitialAxis.Value, recording.InitialAxisCrossedDegeneracy);
            var withoutSnapshot = new GyroProcessor(settings.Clone());
            withoutSnapshot.Calibration.SetInitialBias(recording.InitialBias.Value);
            foreach (GyroSample sample in recording.Samples)
            {
                replay.ProcessSample(sample);
                withoutSnapshot.ProcessSample(sample);
            }
            double liveAngle = live.ConsumeStickIntegral() * 90;
            double replayAngle = replay.ConsumeStickIntegral() * 90;
            double unseededAngle = withoutSnapshot.ConsumeStickIntegral() * 90;
            Assert.That(liveAngle, Is.GreaterThan(0));
            Assert.That(replayAngle, Is.EqualTo(liveAngle).Within(0.000001));
            Assert.That(unseededAngle, Is.LessThan(0), "Fixture must expose the sign reversal when the initial axis is omitted.");
            TestContext.WriteLine($"CSV started at 115° after crossing the singular cone: live={liveAngle:F9}°, restored replay={replayAngle:F9}°, missing-axis replay={unseededAngle:F9}°.");
        }

        [TestCase("#initialAxis,0,0,0,0")]
        [TestCase("#initialAxis,NaN,0,1,0")]
        [TestCase("#initialAxis,3e38,0,1,0")]
        [TestCase("#initialAxis,0,0,1,2")]
        [TestCase("#initialAxis,0,0,1,true")]
        [TestCase("#initialAxis,0,0,1")]
        [TestCase("#initialAxis,0,0,1,1\n#initialAxis,0,0,1,1")]
        [TestCase("#initialBias,0,0,0\n#initialBias,0,0,0")]
        public void InvalidOrDuplicateSnapshotMetadata_IsRejected(string metadata)
        {
            string path = WriteCsv(GyroRecording.CsvHeader + "\n" + metadata + "\n0.004,1,0,0,0,1,0,0,-1,0\n");
            Assert.Throws<InvalidDataException>(() => GyroRecording.Load(path));
        }

        [Test]
        public void SnapshotMetadataCanUseEitherOrderButCannotFollowSamples()
        {
            string path = WriteCsv(GyroRecording.CsvHeader + "\n#initialAxis,0,0,2,1\n#initialBias,0,0,0.5\n0.004,1,0,0,0,1,0,0,-1,0\n");
            GyroRecording recording = GyroRecording.Load(path);
            Assert.That(recording.InitialAxis.Value, Is.EqualTo(Vector3.forward));
            Assert.That(recording.InitialAxisCrossedDegeneracy, Is.True);
            Assert.That(recording.InitialBias.Value.z, Is.EqualTo(0.5f));
            string lateAxis = WriteCsv(GyroRecording.CsvHeader + "\n0.004,1,0,0,0,1,0,0,-1,0\n#initialAxis,0,0,1,1\n");
            Assert.Throws<InvalidDataException>(() => GyroRecording.Load(lateAxis));
            Assert.Throws<ArgumentException>(() => new SteeringExtractor().SetInitialAxis(Vector3.zero, false));
            Assert.Throws<ArgumentException>(() => new SteeringExtractor().SetInitialAxis(new Vector3(float.NaN, 0, 1), false));
        }

        private string WriteCsv(string content)
        {
            string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        private static GyroSample ValidSample() => new GyroSample(0.004f,
            new Vector3(1.25f, -0.5f, 20.75f), Vector3.up, Vector3.down, 123456.7);

        private sealed class OversizedSamples : IReadOnlyList<GyroSample>
        {
            public int Count => GyroRecording.MaximumSamples + 1;
            public GyroSample this[int index] => throw new InvalidOperationException("Size must be rejected before reading samples.");
            public IEnumerator<GyroSample> GetEnumerator() => throw new NotSupportedException();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
