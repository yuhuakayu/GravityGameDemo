using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Resource.Scripts.Gyro
{
    /// <summary>Immutable raw sensor recording with a relative, callback-delta timeline.</summary>
    public sealed class GyroRecording
    {
        public const string CsvHeader = "deltaTime,gyroX,gyroY,gyroZ,accelX,accelY,accelZ,gravityX,gravityY,gravityZ";
        public const long MaximumFileBytes = 128L * 1024L * 1024L;
        public const int MaximumSamples = 1000000;
        public const int MaximumLineCharacters = 1024;
        private readonly ReadOnlyCollection<GyroSample> _samples;

        public IReadOnlyList<GyroSample> Samples => _samples;
        public double Duration { get; }
        public string FilePath { get; }
        public Vector3? InitialBias { get; }
        public Vector3? InitialAxis { get; }
        public bool InitialAxisCrossedDegeneracy { get; }

        public GyroRecording(IReadOnlyList<GyroSample> samples, string path = "", Vector3? initialBias = null,
            Vector3? initialAxis = null, bool initialAxisCrossedDegeneracy = false)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (initialBias.HasValue) ValidateInitialBias(initialBias.Value, "录制初始零点偏移");
            if (initialAxis.HasValue) initialAxis = NormalizeInitialAxis(initialAxis.Value, "录制初始方向盘轴");
            else if (initialAxisCrossedDegeneracy) throw new InvalidDataException("跨退化区状态需要同时提供初始方向盘轴。");
            ValidateCount(samples.Count);
            var copy = new GyroSample[samples.Count];
            double elapsed = 0;
            for (int i = 0; i < samples.Count; ++i)
            {
                GyroSample sample = samples[i];
                ValidateSample(sample, "样本 " + (i + 1));
                elapsed += sample.DeltaTime;
                copy[i] = new GyroSample(sample.DeltaTime, sample.Gyro, sample.Acceleration, sample.Gravity, elapsed);
            }
            _samples = Array.AsReadOnly(copy);
            Duration = elapsed;
            FilePath = string.IsNullOrEmpty(path) ? "" : Path.GetFullPath(path);
            InitialBias = initialBias;
            InitialAxis = initialAxis;
            InitialAxisCrossedDegeneracy = initialAxisCrossedDegeneracy;
        }

        private GyroRecording(GyroSample[] ownedSamples, string fullPath, double duration, Vector3? initialBias,
            Vector3? initialAxis, bool initialAxisCrossedDegeneracy)
        {
            _samples = Array.AsReadOnly(ownedSamples);
            FilePath = fullPath;
            Duration = duration;
            InitialBias = initialBias;
            InitialAxis = initialAxis;
            InitialAxisCrossedDegeneracy = initialAxisCrossedDegeneracy;
        }

        public static GyroRecording Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("录制文件路径为空。", nameof(path));
            string fullPath = Path.GetFullPath(path);
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length == 0) throw new InvalidDataException("录制文件为空。");
                if (stream.Length > MaximumFileBytes)
                    throw new InvalidDataException("录制文件超过 128 MiB，不能载入。");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096))
                {
                    string header = ReadBoundedLine(reader, 1);
                    if (header != CsvHeader) throw new InvalidDataException("录制文件第 1 行必须是标准的 10 列 CSV 表头。");
                    int capacity = Math.Min(MaximumSamples, Math.Max(16, (int)(stream.Length / 120)));
                    var samples = new List<GyroSample>(capacity);
                    double duration = 0;
                    Vector3? initialBias = null;
                    Vector3? initialAxis = null;
                    bool initialAxisCrossedDegeneracy = false;
                    int lineNumber = 1;
                    string line;
                    while ((line = ReadBoundedLine(reader, ++lineNumber)) != null)
                    {
                        if (samples.Count >= MaximumSamples)
                            throw new InvalidDataException("录制文件超过 100 万个样本，不能载入。");
                        string location = "录制文件第 " + lineNumber + " 行";
                        string[] columns = line.Split(',');
                        if (columns[0] == "#initialBias")
                        {
                            if (samples.Count != 0 || columns.Length != 4 || initialBias.HasValue)
                                throw new InvalidDataException(location + "的初始校准元数据必须仅出现一次、位于所有样本之前且恰好有 4 列。");
                            var biasValues = new float[3];
                            for (int column = 0; column < 3; ++column)
                            {
                                if (!float.TryParse(columns[column + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out biasValues[column]))
                                    throw new InvalidDataException(location + "的初始零点偏移不是数字。");
                            }
                            var bias = new Vector3(biasValues[0], biasValues[1], biasValues[2]);
                            ValidateInitialBias(bias, location);
                            initialBias = bias;
                            continue;
                        }
                        if (columns[0] == "#initialAxis")
                        {
                            if (samples.Count != 0 || columns.Length != 5 || initialAxis.HasValue)
                                throw new InvalidDataException(location + "的初始方向盘轴必须仅出现一次、位于所有样本之前且恰好有 5 列。");
                            var axisValues = new float[3];
                            for (int column = 0; column < 3; ++column)
                            {
                                if (!float.TryParse(columns[column + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out axisValues[column]))
                                    throw new InvalidDataException(location + "的初始方向盘轴不是数字。");
                            }
                            string crossed = columns[4].Trim();
                            if (crossed != "0" && crossed != "1")
                                throw new InvalidDataException(location + "的方向盘跨退化区标记必须为 0 或 1。");
                            initialAxis = NormalizeInitialAxis(new Vector3(axisValues[0], axisValues[1], axisValues[2]), location);
                            initialAxisCrossedDegeneracy = crossed == "1";
                            continue;
                        }
                        if (columns.Length != 10) throw new InvalidDataException(location + "必须恰好有 10 列，空行也不允许。");
                        var values = new float[10];
                        for (int column = 0; column < values.Length; ++column)
                        {
                            if (!float.TryParse(columns[column], NumberStyles.Float, CultureInfo.InvariantCulture, out values[column]) ||
                                !GyroSample.Finite(values[column]))
                                throw new InvalidDataException(location + "第 " + (column + 1) + " 列不是有限数字。");
                        }
                        var sample = new GyroSample(values[0], new Vector3(values[1], values[2], values[3]),
                            new Vector3(values[4], values[5], values[6]), new Vector3(values[7], values[8], values[9]));
                        ValidateSample(sample, location);
                        duration += sample.DeltaTime;
                        samples.Add(new GyroSample(sample.DeltaTime, sample.Gyro, sample.Acceleration, sample.Gravity, duration));
                    }
                    ValidateCount(samples.Count);
                    return new GyroRecording(samples.ToArray(), fullPath, duration, initialBias, initialAxis, initialAxisCrossedDegeneracy);
                }
            }
        }

        internal static void ValidateSample(GyroSample sample, string location)
        {
            if (!sample.IsValid || !GyroSample.Finite(sample.Gyro.sqrMagnitude) ||
                !GyroSample.Finite(sample.Acceleration.sqrMagnitude) || !GyroSample.Finite(sample.Gravity.sqrMagnitude))
                throw new InvalidDataException(location + "无效：需要有限向量、非零重力以及 0 < deltaTime ≤ 0.1 秒。");
        }

        internal static void ValidateInitialBias(Vector3 bias, string location)
        {
            if (!GyroSample.Finite(bias) || !GyroSample.Finite(bias.sqrMagnitude))
                throw new InvalidDataException(location + "必须是有限的三轴零点偏移。");
        }

        internal static Vector3 NormalizeInitialAxis(Vector3 axis, string location)
        {
            if (!GyroSample.Finite(axis) || !GyroSample.Finite(axis.sqrMagnitude) || axis.sqrMagnitude < 0.000001f)
                throw new InvalidDataException(location + "必须是有限、非零的方向盘轴。");
            return axis.normalized;
        }

        private static void ValidateCount(int count)
        {
            if (count <= 0) throw new InvalidDataException("录制没有任何传感器样本。");
            if (count > MaximumSamples) throw new InvalidDataException("录制超过 100 万个样本。");
        }

        private static string ReadBoundedLine(StreamReader reader, int lineNumber)
        {
            // Stop before allocating an arbitrarily long malformed line.
            var line = new StringBuilder(128);
            int character;
            while ((character = reader.Read()) != -1)
            {
                if (character == '\n') return line.ToString();
                if (character == '\r')
                {
                    if (reader.Peek() == '\n') reader.Read();
                    return line.ToString();
                }
                if (line.Length >= MaximumLineCharacters)
                    throw new InvalidDataException("录制文件第 " + lineNumber + " 行超过 1024 个字符。");
                line.Append((char)character);
            }
            return line.Length == 0 ? null : line.ToString();
        }
    }
}
