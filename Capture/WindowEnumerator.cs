using System.Runtime.InteropServices;
using System.Windows;
using EasyShot.Core;

namespace EasyShot.Capture;

internal sealed record CaptureWindowInfo(IntPtr Handle, Int32Rect Bounds, Int32Rect Frame);

internal static class WindowEnumerator
{
    // 在覆盖层显示前保存 Z 序和窗口边界，不让覆盖层参与目标选择。
    public static IReadOnlyList<CaptureWindowInfo> GetWindows()
    {
        var windows = new List<CaptureWindowInfo>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)Environment.ProcessId || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return true;
            if (NativeMethods.DwmGetWindowIntAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var bounds) || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return true;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, 9, out var frame, Marshal.SizeOf<NativeMethods.Rect>()) != 0)
                frame = bounds;
            if (frame.Right <= frame.Left || frame.Bottom <= frame.Top) return true;
            windows.Add(new CaptureWindowInfo(hwnd, ToRect(bounds), ToRect(frame)));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static Int32Rect ToRect(NativeMethods.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
