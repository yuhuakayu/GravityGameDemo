using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Resource.Scripts.Gyro
{
    /// <summary>Streaming raw CSV writer. Call from the same main-thread sample path used by live gameplay.</summary>
    public sealed class GyroRecorder : IDisposable
    {
        private const int FlushEverySamples = 250;
        private readonly string _directory;
        private StreamWriter _writer;
        private bool _disposed;

        public bool IsRecording => _writer != null;
        public string CurrentPath { get; private set; } = "";
        public int SampleCount { get; private set; }
        public double Duration { get; private set; }

        public GyroRecorder(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("录制目录为空。", nameof(directory));
            _directory = Path.GetFullPath(directory);
        }

        public string Start(Vector3? initialBias = null, Vector3? initialAxis = null, bool initialAxisCrossedDegeneracy = false)
        {
            ThrowIfDisposed();
            if (IsRecording) throw new InvalidOperationException("录制已经开始，请先停止当前录制。");
            if (initialBias.HasValue) GyroRecording.ValidateInitialBias(initialBias.Value, "录制初始零点偏移");
            if (initialAxis.HasValue) initialAxis = GyroRecording.NormalizeInitialAxis(initialAxis.Value, "录制初始方向盘轴");
            else if (initialAxisCrossedDegeneracy) throw new InvalidDataException("跨退化区状态需要同时提供初始方向盘轴。");
            Directory.CreateDirectory(_directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            FileStream stream = null;
            for (int attempt = 0; attempt < 1000; ++attempt)
            {
                string suffix = attempt == 0 ? "" : "_" + attempt.ToString("D3", CultureInfo.InvariantCulture);
                string path = Path.Combine(_directory, stamp + suffix + ".csv");
                try
                {
                    stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 16384);
                    CurrentPath = path;
                    break;
                }
                catch (IOException) when (File.Exists(path)) { }
            }
            if (stream == null) throw new IOException("无法创建唯一的录制文件名，现有文件未被覆盖。");

            StreamWriter writer = null;
            try
            {
                writer = new StreamWriter(stream, new UTF8Encoding(false), 16384);
                writer.WriteLine(GyroRecording.CsvHeader);
                if (initialBias.HasValue)
                {
                    Vector3 bias = initialBias.Value;
                    writer.WriteLine("#initialBias," + Number(bias.x) + "," + Number(bias.y) + "," + Number(bias.z));
                }
                if (initialAxis.HasValue)
                {
                    Vector3 axis = initialAxis.Value;
                    writer.WriteLine("#initialAxis," + Number(axis.x) + "," + Number(axis.y) + "," + Number(axis.z) +
                        "," + (initialAxisCrossedDegeneracy ? "1" : "0"));
                }
                writer.Flush();
                SampleCount = 0;
                Duration = 0;
                _writer = writer;
                return CurrentPath;
            }
            catch
            {
                try { if (writer != null) writer.Dispose(); else stream.Dispose(); } catch { }
                throw;
            }
        }

        public void Append(GyroSample sample)
        {
            ThrowIfDisposed();
            if (_writer == null) throw new InvalidOperationException("尚未开始录制。");
            GyroRecording.ValidateSample(sample, "待录制样本");
            if (SampleCount >= GyroRecording.MaximumSamples)
            {
                Stop();
                throw new InvalidDataException("录制已达到 100 万个样本上限，已停止并保存现有数据。");
            }
            string line = string.Join(",", Number(sample.DeltaTime), Number(sample.Gyro.x), Number(sample.Gyro.y),
                Number(sample.Gyro.z), Number(sample.Acceleration.x), Number(sample.Acceleration.y), Number(sample.Acceleration.z),
                Number(sample.Gravity.x), Number(sample.Gravity.y), Number(sample.Gravity.z));
            try
            {
                _writer.WriteLine(line);
                SampleCount++;
                Duration += sample.DeltaTime;
                if (SampleCount % FlushEverySamples == 0) _writer.Flush();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ObjectDisposedException)
            {
                StreamWriter failed = _writer;
                _writer = null;
                try { failed?.Dispose(); } catch { }
                throw;
            }
        }

        public void Stop()
        {
            // Clear state first, so a failed final flush cannot leave a falsely active recording.
            StreamWriter writer = _writer;
            _writer = null;
            writer?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GyroRecorder));
        }

        private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
