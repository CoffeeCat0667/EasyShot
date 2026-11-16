using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasyShot.Core;
using EasyShot.Overlay;

namespace EasyShot.Capture;

internal sealed class ScrollCaptureWindow : Window
{
    public static ScrollCaptureWindow? Current { get; private set; }
    private readonly Int32Rect _region;
    private ScrollStitcher? _stitcher;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stable = Stopwatch.StartNew();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image _preview = new() { Width = 210, Height = 220, Stretch = System.Windows.Media.Stretch.Uniform };
    private BitmapSource? _candidate;
    private byte[]? _candidatePixels;
    private bool _paused;
    private bool _busy;
    private bool _closed;
    private bool _finishRequested;
    private bool _escapeDown;
    private bool _waitingForOverlap;
    private readonly List<ScrollCaptureOverlay> _overlays = new();

    private ScrollCaptureWindow(Int32Rect region)
    {
        _region = region;
        Title = "EasyShot 滚动捕捉";
        Width = 260;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(_status);
        panel.Children.Add(_preview);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var finish = new Button { Content = "完成", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(3) };
        var cancel = new Button { Content = "取消", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(3) };
        finish.Click += (_, _) => RequestFinish();
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(finish);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            foreach (var overlay in _overlays) overlay.Close();
            _overlays.Clear();
            if (Current == this) Current = null;
        };
        _status.Text = "正在等待首个稳定画面，请暂时不要滚动；显示累计尺寸后再开始。";
    }

    public static void Start(Int32Rect region, IntPtr targetWindow)
    {
        if (Current is not null) { Current.Activate(); return; }
        if (targetWindow != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(targetWindow);
            NativeMethods.DwmFlush();
        }
        var window = new ScrollCaptureWindow(region);
        Current = window;
        try
        {
            foreach (var monitor in ScreenInfo.GetMonitors())
            {
                var overlay = new ScrollCaptureOverlay(monitor.Bounds, region);
                window._overlays.Add(overlay);
                overlay.ShowOverlay();
            }
            window.Show();
            var hwnd = new WindowInteropHelper(window).Handle;
            // 控制窗不进入屏幕捕捉，避免遮挡区域时把自身拼进去（Windows 10 2004+）。
            var excluded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                NativeMethods.SetWindowDisplayAffinity(hwnd, 0x11);
            window.PlaceOutside(region, excluded);
            if (targetWindow != IntPtr.Zero) NativeMethods.SetForegroundWindow(targetWindow);
            window._escapeDown = NativeMethods.GetAsyncKeyState(0x1B) < 0;
            window._timer.Start();
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private void PlaceOutside(Int32Rect region, bool excluded)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.GetWindowRect(hwnd, out var bounds);
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var monitors = ScreenInfo.GetMonitors();
        foreach (var monitor in monitors)
        {
            var m = monitor.Bounds;
            foreach (var point in new[]
                     {
                         new Point(region.X + region.Width + 8, region.Y),
                         new Point(region.X - width - 8, region.Y),
                         new Point(region.X, region.Y - height - 8),
                         new Point(region.X, region.Y + region.Height + 8)
                     })
            {
                var rect = new Rect(point, new Size(width, height));
                if (new Rect(m.X, m.Y, m.Width, m.Height).Contains(rect) &&
                    !rect.IntersectsWith(new Rect(region.X, region.Y, region.Width, region.Height)))
                {
                    NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, (int)point.X, (int)point.Y,
                        width, height, NativeMethods.SwpNoActivate);
                    return;
                }
            }
        }
        if (!excluded) throw new InvalidOperationException("控制栏无法避开捕捉区域，且系统不支持排除控制窗；请缩小捕捉范围。");
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        var down = NativeMethods.GetAsyncKeyState(0x1B) < 0;
        if (down && !_escapeDown) RequestFinish();
        _escapeDown = down;
        if (_closed || _busy || _paused) return;
        try
        {
            var frame = ScrollStitcher.ToBgra(ScreenCapture.CaptureRegion(_region));
            var pixels = ScrollStitcher.Pixels(frame);
            if (_stitcher is null)
            {
                if (_candidatePixels is null || !ScrollStitcher.IsStable(_candidatePixels, pixels))
                {
                    _candidate = frame;
                    _candidatePixels = pixels;
                    _stable.Restart();
                    return;
                }
                if (_stable.ElapsedMilliseconds < 100 || _candidate is null) return;
                _stitcher = new ScrollStitcher(_candidate);
                _candidate = null;
                RefreshPreview();
                if (_finishRequested) Finish();
                return;
            }
            // 采样变化后的新画面，不反复计算同一失败帧，也不要求精确滚回原帧。
            if (_candidatePixels is not null && ScrollStitcher.IsStable(_candidatePixels, pixels))
            {
                if (_finishRequested) Finish();
                return;
            }
            _candidatePixels = pixels;
            _busy = true;
            var result = await Task.Run(() => _stitcher.Append(frame));
            _busy = false;
            if (_closed) return;
            if (result == StitchResult.Appended || (result == StitchResult.Unchanged && _waitingForOverlap))
            {
                _waitingForOverlap = false;
                RefreshPreview();
            }
            else if (result == StitchResult.Failed)
            {
                _waitingForOverlap = true;
                _status.Text = $"等待重叠：{_stitcher.FailureReason}。\n已保留 {_stitcher.Width} × {_stitcher.Height}，请回滚一些，找到可匹配范围后自动恢复；也可直接完成。";
            }
            else if (result == StitchResult.HeightLimit)
            {
                _paused = true;
                RefreshPreview();
                _status.Text = "已达到 20000 像素高度上限，捕捉停止；点击完成进入编辑。";
            }
            if (_finishRequested) Finish();
        }
        catch (Exception ex)
        {
            _busy = false;
            if (_closed) return;
            _paused = true;
            _status.Text = $"捕捉失败，已保留拼接部分：{ex.Message}。请完成或取消。";
            if (_finishRequested) Finish();
        }
    }

    private void RefreshPreview()
    {
        if (_stitcher is null) return;
        _preview.Source = _stitcher.Compose();
        _status.Text = $"请在选区中手动向上或向下滚动，Esc 完成。\n累计：{_stitcher.Width} × {_stitcher.Height}";
    }

    private void RequestFinish()
    {
        if (_finishRequested) return;
        _finishRequested = true;
        if (!_busy && (_paused || _waitingForOverlap)) Finish();
        else if (!_busy)
        {
            // 完成前再采样一次，把可匹配的最后一段加入；无重叠则保留已确认部分。
            _candidate = null;
            _candidatePixels = null;
        }
    }

    private void Finish()
    {
        if (_closed) return;
        if (_stitcher is null)
        {
            MessageBox.Show(this, "尚未获得稳定的初始画面，请取消后重新选择捕捉范围。", "EasyShot");
            return;
        }
        try
        {
            var image = _stitcher.Compose();
            Close();
            OverlaySession.EditLongImage(image);
        }
        catch (Exception ex) { MessageBox.Show($"打开长图编辑失败：{ex.Message}", "EasyShot"); }
    }
}
