using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using EasyShot.Core;
using EasyShot.Config;

namespace EasyShot.Capture;

// 只绘制实时桌面外侧遮罩及边框，不显示冻结截图，不拦截鼠标。
internal sealed class ScrollCaptureOverlay : Window
{
    private readonly Int32Rect _monitor;
    private readonly Int32Rect _region;
    private double _scale = 1;
    private readonly Brush _borderBrush = new SolidColorBrush(AppConfig.Load(out _).GetSelectionBorderColor());

    public ScrollCaptureOverlay(Int32Rect monitor, Int32Rect region)
    {
        _monitor = monitor;
        _region = region;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        IsHitTestVisible = false;
    }

    public void ShowOverlay()
    {
        new WindowInteropHelper(this).EnsureHandle();
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, -20, new IntPtr(style | 0x20 | 0x08000000 | 0x80));
        Show();
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, _monitor.X, _monitor.Y,
            _monitor.Width, _monitor.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _scale = Math.Max(1, NativeMethods.GetDpiForWindow(hwnd)) / 96.0;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            NativeMethods.SetWindowDisplayAffinity(hwnd, 0x11);
        Opacity = 1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var full = new Rect(0, 0, _monitor.Width / _scale, _monitor.Height / _scale);
        var region = new Rect((_region.X - _monitor.X) / _scale, (_region.Y - _monitor.Y) / _scale,
            _region.Width / _scale, _region.Height / _scale);
        dc.PushClip(new RectangleGeometry(full));
        var mask = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(full), new RectangleGeometry(region));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, mask);
        // 描边全部位于捕捉范围之外，旧系统不支持排除窗口时也不污染原图。
        region.Inflate(2 / _scale, 2 / _scale);
        dc.DrawRectangle(null, new Pen(_borderBrush, 2 / _scale), region);
        dc.Pop();
    }
}
