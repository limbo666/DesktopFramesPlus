using Desktop_Frames.Localization;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace Desktop_Frames.Layouts
{
    internal static class LayoutDisplays
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice data, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        internal static List<LayoutMonitor> Read()
        {
            double scale;
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                scale = graphics.DpiX / 96.0;
            var result = new List<LayoutMonitor>();
            foreach (var screen in Screen.AllScreens)
            {
                var monitor = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                string id = "";
                string hardware = "";
                string name = Strings.LayoutMonitor;
                for (uint i = 0; EnumDisplayDevices(screen.DeviceName, i, ref monitor, 1); i++)
                {
                    if ((monitor.StateFlags & 1) == 0)
                        continue;
                    id = monitor.DeviceId;
                    name = monitor.DeviceString;
                    // EDD_GET_DEVICE_INTERFACE_NAME gives a physical device interface, not DISPLAY1/2.
                    var parts = id.Split('#');
                    if (parts.Length >= 3)
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
                        if (key?.GetValue("EDID") is byte[] edid && edid.Length >= 128)
                        {
                            hardware = Convert.ToHexString(SHA256.HashData(edid));
                            for (int offset = 54; offset <= 108; offset += 18)
                                if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xfc)
                                    name = Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\n', '\r', ' ');
                        }
                    }
                    break;
                }
                // An identity query can fail during a driver reset. Do not adopt an unstable display number.
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException(Strings.LayoutMonitorIdentityPending);
                var area = screen.WorkingArea;
                result.Add(new LayoutMonitor
                {
                    Id = id,
                    HardwareId = hardware,
                    Name = name,
                    Left = area.Left,
                    Top = area.Top,
                    Width = area.Width,
                    Height = area.Height,
                    Scale = scale
                });
            }
            if (result.Count == 0 || result.Select(m => m.Id).Distinct().Count() != result.Count)
                throw new InvalidOperationException(Strings.LayoutMonitorsNotReady);
            return result;
        }

        internal static LayoutMonitor Nearest(IReadOnlyList<LayoutMonitor> monitors, double x, double y) =>
            monitors.OrderBy(m => Math.Pow(x - Math.Clamp(x, m.Left, m.Left + m.Width), 2) +
                Math.Pow(y - Math.Clamp(y, m.Top, m.Top + m.Height), 2)).First();
    }
}
