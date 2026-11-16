using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using EasyShot.UI;

namespace EasyShot.Core;

internal sealed class TrayIconService : IDisposable
{
    private static readonly Uri IconUri = new("pack://application:,,,/icon/easyshot.ico", UriKind.Absolute);

    private readonly TaskbarIcon _icon;
    private NativeMethods.Point _menuPoint;

    public TrayIconService(Action onRegionCapture, Action onFullscreen, Action onSettings, Action onExit)
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("ShotMenu") };
        menu.Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;
        menu.Opened += (_, _) => PositionMenu(menu);

        var regionItem = new MenuItem { Header = "区域截图", Icon = ShotIconExtension.Create("Region") };
        regionItem.Click += (_, _) => onRegionCapture();

        var exitItem = new MenuItem { Header = "退出", Icon = ShotIconExtension.Create("Close") };
        exitItem.Click += (_, _) => onExit();

        menu.Items.Add(regionItem);
        var fullscreen = new MenuItem { Header = "全屏截图", Icon = ShotIconExtension.Create("Fullscreen") };
        fullscreen.Click += (_, _) => onFullscreen();
        menu.Items.Add(fullscreen);
        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = "设置", Icon = ShotIconExtension.Create("Settings") };
        settings.Click += (_, _) => onSettings();
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _icon = new TaskbarIcon
        {
            ToolTipText = "EasyShot",
            Icon = LoadIcon(),
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.None,
            NoLeftClickDelay = true
        };

        _icon.TrayLeftMouseUp += (_, _) => onRegionCapture();
        _icon.TrayRightMouseUp += (_, _) => OpenMenu();
        _icon.TrayKeyboardContextMenu += (_, _) => OpenMenu();
        _icon.ForceCreate();
        menu.ApplyTemplate();
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    }

    private void OpenMenu()
    {
        NativeMethods.GetCursorPos(out _menuPoint);
        if (_icon.ContextMenu.IsOpen) { PositionMenu(_icon.ContextMenu); return; }
        // ShowContextMenu 内部使用 WPF 绝对坐标；打开后用原生物理坐标定位，避免 DPI 二次放大。
        _icon.ShowContextMenu(new System.Drawing.Point(0, 0));
        PositionMenu(_icon.ContextMenu);
    }

    private void PositionMenu(ContextMenu menu)
    {
        if (PresentationSource.FromVisual(menu) is not HwndSource source) return;
        var monitor = NativeMethods.MonitorFromPoint(_menuPoint, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfoEx { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info) || !NativeMethods.GetWindowRect(source.Handle, out var rect)) return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var x = Math.Clamp(_menuPoint.X, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        var y = _menuPoint.Y + height <= info.Work.Bottom ? _menuPoint.Y : _menuPoint.Y - height;
        y = Math.Clamp(y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        NativeMethods.SetWindowPos(source.Handle, NativeMethods.HwndTopmost, x, y, width, height, NativeMethods.SwpShowWindow);
        NativeMethods.SetForegroundWindow(source.Handle);
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(IconUri)
            ?? throw new InvalidOperationException("缺少托盘图标资源。");

        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream);
    }

    public void Dispose()
    {
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }

}
