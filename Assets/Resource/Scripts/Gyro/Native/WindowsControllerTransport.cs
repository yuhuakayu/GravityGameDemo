using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Resource.Scripts.Gyro
{
    /// <summary>
    /// JSL v3.0 has no public connection-transport getter. Read the Windows HID devnode's
    /// actual parent bus, without opening reports or interfering with Unity/JSL handles.
    /// When multiple matching controllers have different buses, report ambiguity honestly.
    /// </summary>
    internal static class WindowsControllerTransport
    {
        internal static string Find(int controllerType)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try { return FindWindows(controllerType); }
            catch (Exception) { return "未知（Windows 设备信息不可用）"; }
#else
            return "未知";
#endif
        }

        private static string FindWindows(int controllerType)
        {
            var hidClass = new Guid("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
            IntPtr devices = SetupDiGetClassDevsW(ref hidClass, null, IntPtr.Zero, 2); // DIGCF_PRESENT
            if (devices == new IntPtr(-1)) return "未知";
            try
            {
                var buses = new HashSet<string>();
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf(typeof(DeviceInfo)) };
                for (uint index = 0; SetupDiEnumDeviceInfo(devices, index, ref info); index++)
                {
                    string id = GetDeviceId(info.DevInst);
                    if (!MatchesSonyController(id, controllerType)) continue;
                    if (CM_Get_DevNode_Status(out uint status, out uint problem, info.DevInst, 0) != 0
                        || (status & 0x8) == 0 || (status & 0x400) != 0) continue; // DN_STARTED, DN_HAS_PROBLEM
                    uint node = info.DevInst;
                    // Stop at the first physical bus; a Bluetooth adapter can itself sit on USB.
                    for (int depth = 0; depth < 12; depth++)
                    {
                        string ancestor = GetDeviceId(node);
                        if (ancestor.StartsWith("BTH", StringComparison.OrdinalIgnoreCase))
                        {
                            buses.Add("蓝牙");
                            break;
                        }
                        if (ancestor.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
                        {
                            buses.Add("USB");
                            break;
                        }
                        if (CM_Get_Parent(out uint parent, node, 0) != 0) break;
                        node = parent;
                    }
                }
                if (buses.Count > 1) return "未知（同型号 USB / 蓝牙设备并存）";
                foreach (string bus in buses) return bus;
                return "未知（JSL 3.0 未提供设备路径）";
            }
            finally { SetupDiDestroyDeviceInfoList(devices); }
        }

        private static bool MatchesSonyController(string id, int type)
        {
            string upper = id.ToUpperInvariant();
            if (!upper.Contains("VID_054C") && !upper.Contains("VID&0002054C")) return false;
            string[] products = type == JslNative.DualSense
                ? new[] { "0CE6", "0DF2" } : new[] { "05C4", "09CC", "0BA0" };
            foreach (string product in products)
                if (upper.Contains("PID_" + product) || upper.Contains("PID&" + product)) return true;
            return false;
        }

        private static string GetDeviceId(uint node)
        {
            var buffer = new StringBuilder(512);
            return CM_Get_Device_IDW(node, buffer, buffer.Capacity, 0) == 0 ? buffer.ToString() : string.Empty;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInfo
        {
            internal uint Size;
            internal Guid ClassGuid;
            internal uint DevInst;
            internal UIntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string enumerator, IntPtr window, uint flags);
        [DllImport("setupapi.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint index, ref DeviceInfo data);
        [DllImport("setupapi.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint CM_Get_Device_IDW(uint node, StringBuilder buffer, int capacity, uint flags);
    }
}
