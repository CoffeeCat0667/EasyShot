using System.Windows;
using EasyShot.Core;

namespace EasyShot.Core;

internal sealed record MonitorInfo(IntPtr Handle, Int32Rect Bounds);

internal static class ScreenInfo
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfoEx { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                var r = info.Monitor;
                monitors.Add(new MonitorInfo(monitor, new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)));
            }

            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    public static Int32Rect GetVirtualScreen()
    {
        var x = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var w = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var h = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        return new Int32Rect(x, y, w, h);
    }

    public static Point GetCursorPosition()
    {
        NativeMethods.GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }
}
