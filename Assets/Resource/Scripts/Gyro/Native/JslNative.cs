using System;
using System.Runtime.InteropServices;

namespace Resource.Scripts.Gyro
{
    /// <summary>ABI from the header bundled with official JSL v3.0, not its older C# example.</summary>
    internal static class JslNative
    {
        private const string Library = "JoyShockLibrary";
        internal const int DualShock4 = 4;
        internal const int DualSense = 5;

        [StructLayout(LayoutKind.Sequential)]
        internal struct SimpleState
        {
            public int Buttons;
            public float LeftTrigger, RightTrigger, LeftX, LeftY, RightX, RightY;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ImuState
        {
            public float AccelX, AccelY, AccelZ, GyroX, GyroY, GyroZ;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MotionState
        {
            public float QuatW, QuatX, QuatY, QuatZ;
            public float AccelX, AccelY, AccelZ, GravityX, GravityY, GravityZ;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void StateCallback(int deviceId, SimpleState current, SimpleState previous,
            ImuState imu, ImuState previousImu, float deltaTime);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DisconnectCallback(int deviceId, [MarshalAs(UnmanagedType.I1)] bool timedOut);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int JslConnectDevices();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int JslGetConnectedDeviceHandles([Out] int[] deviceHandleArray, int size);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JslDisconnectAndDisposeAll();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool JslStillConnected(int deviceId);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern MotionState JslGetMotionState(int deviceId);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int JslGetControllerType(int deviceId);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JslSetGyroSpace(int deviceId, int gyroSpace);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JslPauseContinuousCalibration(int deviceId);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JslSetAutomaticCalibration(int deviceId, [MarshalAs(UnmanagedType.I1)] bool enabled);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JslSetCalibrationOffset(int deviceId, float xOffset, float yOffset, float zOffset);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JslSetCallback(StateCallback callback);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JslSetDisconnectCallback(DisconnectCallback callback);
    }

    /// <summary>JSL v3.0 header bit positions; names do not depend on Nintendo/Xbox lettering.</summary>
    [Flags]
    public enum GyroButtons
    {
        None = 0, Up = 0x00001, Down = 0x00002, Left = 0x00004, Right = 0x00008,
        Options = 0x00010, Create = 0x00020, LeftStick = 0x00040, RightStick = 0x00080,
        LeftShoulder = 0x00100, RightShoulder = 0x00200,
        LeftTrigger = 0x00400, RightTrigger = 0x00800,
        South = 0x01000, East = 0x02000, West = 0x04000, North = 0x08000,
        Home = 0x10000, Touchpad = 0x20000, Microphone = 0x40000
    }
}
