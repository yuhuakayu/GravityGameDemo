using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Resource.Scripts.Gyro
{
    /// <summary>Reads and calibrates a physical wheel without changing gyro controller selection.</summary>
    public sealed class PhysicalWheelInput : IDisposable
    {
        private const float CalibrationMovement = 0.05f;
        private readonly List<CalibrationAxis> _candidates = new List<CalibrationAxis>();
        private AxisControl _axis;
        private string _deviceMatch;
        private string _axisPath;
        private bool _bindingDirty = true;
        private bool _initialized;
        private float _left, _center, _right;
        private bool _calibrated;
        private string _calibrationMessage;

        private sealed class CalibrationAxis
        {
            public AxisControl Axis;
            public float Start, Min, Max;
            public int FirstDirection;
        }

        [Serializable]
        private sealed class XInputIdentity
        {
            public int userIndex = -1;
        }

        public bool HasInput { get; private set; }
        public bool IsCalibrating { get; private set; }
        public float RawValue { get; private set; }
        public float Angle { get; private set; }
        public int Revision { get; private set; }
        public string DeviceName { get; private set; } = "未绑定";
        public string Status { get; private set; } = "尚未校准方向盘";

        public PhysicalWheelInput() => InputSystem.onDeviceChange += OnDeviceChange;

        public static string DeviceMatch(InputDevice device)
        {
            var description = device.description;
            string match = string.Join("|", description.interfaceName, description.manufacturer,
                description.product, description.serial, device.layout);
            if (string.Equals(description.interfaceName, "XInput", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(description.capabilities))
            {
                var identity = new XInputIdentity();
                JsonUtility.FromJsonOverwrite(description.capabilities, identity);
                if (identity.userIndex >= 0) match += "|userIndex=" + identity.userIndex;
            }
            return match;
        }

        public void Update(GyroSettings settings)
        {
            bool rebind = _bindingDirty || _deviceMatch != settings.wheelDeviceMatch ||
                _axisPath != settings.wheelAxisPath;
            bool calibrationChanged = !_initialized || !_left.Equals(settings.wheelLeft) ||
                !_center.Equals(settings.wheelCenter) || !_right.Equals(settings.wheelRight) ||
                _calibrated != settings.wheelCalibrated;
            if (rebind)
            {
                _deviceMatch = settings.wheelDeviceMatch;
                _axisPath = settings.wheelAxisPath;
                _axis = null;
                if (!string.IsNullOrEmpty(_deviceMatch) && !string.IsNullOrEmpty(_axisPath))
                    foreach (var device in InputSystem.devices)
                    {
                        if (!Available(device) || DeviceMatch(device) != _deviceMatch) continue;
                        var axis = device.TryGetChildControl<AxisControl>(_axisPath);
                        if (axis == null || axis.synthetic || axis is ButtonControl) continue;
                        _axis = axis;
                        break;
                    }
                _bindingDirty = false;
            }
            if (rebind || calibrationChanged) ++Revision;
            _initialized = true;
            _left = settings.wheelLeft;
            _center = settings.wheelCenter;
            _right = settings.wheelRight;
            _calibrated = settings.wheelCalibrated;

            HasInput = false;
            Angle = 0f;
            RawValue = 0f;
            if (_axis != null && Available(_axis.device))
            {
                DeviceName = _axis.device.displayName;
                RawValue = _axis.ReadUnprocessedValue();
                HasInput = settings.wheelCalibrated && Finite(RawValue) &&
                    PhysicalWheelMapper.IsValid(_left, _center, _right);
                if (HasInput)
                    Angle = PhysicalWheelMapper.MapAngle(RawValue, _left, _center, _right,
                        settings.invertDirection);
            }
            else DeviceName = "未绑定 / 未连接";

            if (IsCalibrating)
            {
                CaptureCalibration();
                Status = "校准中：先向左到底，再向右到底，最后回正";
            }
            else if (!string.IsNullOrEmpty(_calibrationMessage)) Status = _calibrationMessage;
            else if (!settings.wheelCalibrated) Status = "尚未校准方向盘";
            else if (_axis == null || !Available(_axis.device)) Status = "方向盘未连接，世界旋转已停止";
            else if (!HasInput) Status = "方向盘校准或输入无效，请重新校准";
            else Status = "方向盘已就绪：全行程对应世界 360°";
        }

        public void BeginCalibration()
        {
            _candidates.Clear();
            _calibrationMessage = null;
            foreach (var device in InputSystem.devices)
            {
                if (!Available(device)) continue;
                foreach (var control in device.allControls)
                {
                    if (!(control is AxisControl axis) || axis.synthetic || axis is ButtonControl) continue;
                    float value = axis.ReadUnprocessedValue();
                    if (!Finite(value)) continue;
                    _candidates.Add(new CalibrationAxis { Axis = axis, Start = value, Min = value, Max = value });
                }
            }
            IsCalibrating = _candidates.Count > 0;
            Status = IsCalibrating ? "校准中：先向左到底，再向右到底，最后回正" :
                "未检测到方向盘轴，请确认已连接";
            if (!IsCalibrating) _calibrationMessage = Status;
        }

        public bool FinishCalibration(GyroSettings settings)
        {
            if (!IsCalibrating) return false;
            CaptureCalibration();
            IsCalibrating = false;
            CalibrationAxis best = null;
            float bestRange = 0f;
            foreach (var candidate in _candidates)
            {
                float range = candidate.Max - candidate.Min;
                if (candidate.FirstDirection == 0 || !Available(candidate.Axis.device) || range <= bestRange) continue;
                best = candidate;
                bestRange = range;
            }
            if (best == null) return FailCalibration("校准失败：没有检测到明显转动，请先左到底再右到底");
            float center = best.Axis.ReadUnprocessedValue();
            float left = best.FirstDirection < 0 ? best.Min : best.Max;
            float right = best.FirstDirection < 0 ? best.Max : best.Min;
            if (!PhysicalWheelMapper.IsValid(left, center, right))
                return FailCalibration("校准失败：请先左右转到底，回到中间后再完成校准");

            settings.wheelDeviceMatch = DeviceMatch(best.Axis.device);
            settings.wheelAxisPath = best.Axis.path.Substring(best.Axis.device.path.Length + 1);
            settings.wheelLeft = left;
            settings.wheelCenter = center;
            settings.wheelRight = right;
            settings.wheelCalibrated = true;
            settings.mode = GyroControlMode.Wheel;
            _calibrationMessage = null;
            _bindingDirty = true;
            _candidates.Clear();
            Update(settings);
            return true;
        }

        private bool FailCalibration(string message)
        {
            _candidates.Clear();
            Status = _calibrationMessage = message;
            return false;
        }

        private void CaptureCalibration()
        {
            float largestRange = -1f;
            foreach (var candidate in _candidates)
            {
                if (!Available(candidate.Axis.device)) continue;
                float value = candidate.Axis.ReadUnprocessedValue();
                if (!Finite(value)) continue;
                candidate.Min = Mathf.Min(candidate.Min, value);
                candidate.Max = Mathf.Max(candidate.Max, value);
                if (candidate.FirstDirection == 0 && Mathf.Abs(value - candidate.Start) >= CalibrationMovement)
                    candidate.FirstDirection = value < candidate.Start ? -1 : 1;
                float range = candidate.Max - candidate.Min;
                if (range <= largestRange) continue;
                largestRange = range;
                DeviceName = candidate.Axis.device.displayName;
                RawValue = value;
            }
        }

        private static bool Available(InputDevice device) =>
            device.added && device.enabled && (device is Gamepad || device is Joystick);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if ((_axis != null && device == _axis.device) || DeviceMatch(device) == _deviceMatch)
                _bindingDirty = true;
        }

        public void Dispose()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            _candidates.Clear();
            _axis = null;
            HasInput = IsCalibrating = false;
        }
    }
}
