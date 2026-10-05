using System;
using UnityEngine;

namespace Resource.Scripts.Gyro
{
    public enum GyroControlMode { Speed, Angle }
    public enum GyroInputSource { Gyro, RightStick, Combined }
    public enum GyroRecenterButton { LeftStick, RightStick, Touchpad, South, North, LeftShoulder, RightShoulder }

    [Serializable]
    public sealed class GyroSettings
    {
        public int version = 1;
        public GyroControlMode mode = GyroControlMode.Speed;
        public GyroInputSource inputSource = GyroInputSource.Gyro;
        public float sensitivity = 5.25f;
        public float minInputSpeed = 0f;
        public float maxInputSpeed = 180f;
        public float minOutput = 0.071f;
        public float maxOutput = 1f;
        public float outputCurve = 1f;
        public float speedDeadzone = 1f;
        public float precisionSpeed = 0f;
        public bool shakeReject = true;
        public float purityLow = 0.5f;
        public float purityHigh = 0.8f;
        public float shakeHoldTime = 0.12f;
        public float offAxisTrigger = 30f;
        public bool invertDirection;
        public float angleMultiplier = 1f;
        public float angleSmoothTime = 0.05f;
        public float angleDeadzone = 1f;
        public GyroRecenterButton recenterButton = GyroRecenterButton.LeftStick;
        public float uiScale = 2f;
        public float consoleScreenFraction = 0.7f;
        public bool consoleWindowCustomized;
        public Rect consoleWindowNormalized = new Rect(0f, 0f, 0.7f, 0.7f);
        public float timeScale = 1f;
        public int selectedTab;
        public bool pauseOnOpen;
        public bool skipPreview;

        public static GyroSettings Defaults() => new GyroSettings();
        public GyroSettings Clone() => (GyroSettings)MemberwiseClone();

        public void Sanitize()
        {
            if (!Enum.IsDefined(typeof(GyroControlMode), mode)) mode = GyroControlMode.Speed;
            if (!Enum.IsDefined(typeof(GyroInputSource), inputSource)) inputSource = GyroInputSource.Gyro;
            if (!Enum.IsDefined(typeof(GyroRecenterButton), recenterButton)) recenterButton = GyroRecenterButton.LeftStick;
            sensitivity = Clamp(sensitivity, 0f, 20f, 5.25f);
            minInputSpeed = Clamp(minInputSpeed, 0f, 1999f, 0f);
            maxInputSpeed = Clamp(maxInputSpeed, minInputSpeed + 0.01f, 2000f, 180f);
            minOutput = Clamp(minOutput, 0f, 1f, 0.071f);
            maxOutput = Clamp(maxOutput, minOutput, 1f, 1f);
            outputCurve = Clamp(outputCurve, 0.1f, 4f, 1f);
            speedDeadzone = Clamp(speedDeadzone, 0f, 20f, 1f);
            precisionSpeed = Clamp(precisionSpeed, 0f, 2000f, 0f);
            purityLow = Clamp(purityLow, 0.2f, 0.9f, 0.5f);
            purityHigh = Clamp(purityHigh, purityLow + 0.05f, 1f, 0.8f);
            shakeHoldTime = Clamp(shakeHoldTime, 0f, 0.3f, 0.12f);
            offAxisTrigger = Clamp(offAxisTrigger, 5f, 100f, 30f);
            angleMultiplier = Clamp(angleMultiplier, 0.5f, 4f, 1f);
            angleSmoothTime = Clamp(angleSmoothTime, 0f, 1f, 0.05f);
            angleDeadzone = Clamp(angleDeadzone, 0f, 20f, 1f);
            uiScale = Clamp(uiScale, 0.75f, 3f, 2f);
            consoleScreenFraction = Clamp(consoleScreenFraction, 0.4f, 1f, 0.7f);
            float windowWidth = Clamp(consoleWindowNormalized.width, 0.001f, 1f, 0.7f);
            float windowHeight = Clamp(consoleWindowNormalized.height, 0.001f, 1f, 0.7f);
            consoleWindowNormalized = new Rect(
                Clamp(consoleWindowNormalized.x, 0f, 1f - windowWidth, 0f),
                Clamp(consoleWindowNormalized.y, 0f, 1f - windowHeight, 0f),
                windowWidth, windowHeight);
            timeScale = Clamp(timeScale, 0f, 4f, 1f);
            selectedTab = Mathf.Clamp(selectedTab, 0, 3);
        }

        public void Validate() => Sanitize();

        private static float Clamp(float value, float min, float max, float fallback) =>
            Mathf.Clamp(GyroSample.Finite(value) ? value : fallback, min, max);
    }
}
