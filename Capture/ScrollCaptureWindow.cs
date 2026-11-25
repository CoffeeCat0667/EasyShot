using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasyShot.Core;
using EasyShot.Overlay;
using EasyShot.UI;

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
    private const int MaxPendingFrames = 4;
    private readonly Queue<BitmapSource> _pendingFrames = new();
    private readonly List<ScrollCaptureOverlay> _overlays = new();

    private ScrollCaptureWindow(Int32Rect region)
    {
        _region = region;
        Title = "EasyShot 滚动捕捉";
        WindowStyle = WindowStyle.None;
        Width = 260;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = (Brush)FindResource("ShotBackground");
        Foreground = (Brush)FindResource("ShotText");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        Resources.Add(typeof(ScrollBar), FindResource("ShotScrollBar"));
        _status.Foreground = (Brush)FindResource("ShotMuted");
        _status.Margin = new Thickness(0, 0, 0, 10);
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(_status);
        var previewFrame = new Border { Background = (Brush)FindResource("ShotSurface"),
            BorderBrush = (Brush)FindResource("ShotBorder"), BorderThickness = new Thickness(1),
            Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Center };
        previewFrame.Child = _preview;
        panel.Children.Add(previewFrame);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = ShotIconExtension.Create("Close"), Style = (Style)FindResource("ShotButton"),
            MinWidth = 88, ToolTip = "取消并退出", Margin = new Thickness(3, 12, 3, 0) };
        var finish = new Button { Content = ShotIconExtension.Create("Confirm"), Style = (Style)FindResource("ShotButton"),
            MinWidth = 88, ToolTip = "复制到剪贴板并退出", Margin = new Thickness(3, 12, 3, 0) };
        finish.Click += (_, _) => RequestFinish();
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);
        buttons.Children.Add(finish);
        panel.Children.Add(buttons);
        Content = panel;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _pendingFrames.Clear();
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
        if (_closed || _paused) return;
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
                if (_finishRequested && !_busy && _pendingFrames.Count == 0) Finish();
                return;
            }
            _candidatePixels = pixels;

            // 匹配较慢时继续缓存滚动过程中的画面，避免只取最后一帧而整段跳过。
            if (_busy)
            {
                if (!_finishRequested)
                {
                    if (_pendingFrames.Count >= MaxPendingFrames) _pendingFrames.Dequeue();
                    _pendingFrames.Enqueue(frame);
                }
                return;
            }

            _pendingFrames.Enqueue(frame);
            _busy = true;
            try
            {
                while (_pendingFrames.Count > 0 && !_closed && !_paused)
                {
                    var pending = _pendingFrames.Dequeue();
                    var result = await Task.Run(() => _stitcher.Append(pending));
                    if (_closed) return;
                    ApplyResult(result);
                }
            }
            finally
            {
                _busy = false;
            }

            if (_finishRequested && !_closed && !_paused && _pendingFrames.Count == 0) Finish();
        }
        catch (Exception ex)
        {
            _busy = false;
            _pendingFrames.Clear();
            if (_closed) return;
            _paused = true;
            _status.Text = $"捕捉失败，已保留拼接部分：{ex.Message}。点击 √ 复制已有部分，或 X 取消。";
            if (_finishRequested) Finish();
        }
    }

    private void ApplyResult(StitchResult result)
    {
        if (result == StitchResult.Appended || (result == StitchResult.Unchanged && _waitingForOverlap))
        {
            _waitingForOverlap = false;
            RefreshPreview();
        }
        else if (result == StitchResult.Failed)
        {
            _waitingForOverlap = true;
            _status.Text = $"等待重叠：{_stitcher!.FailureReason}。\n已保留 {_stitcher.Width} × {_stitcher.Height}，请回滚一些，找到可匹配范围后自动恢复；也可点击 √ 直接复制已有部分。";
        }
        else if (result == StitchResult.HeightLimit)
        {
            _paused = true;
            _pendingFrames.Clear();
            RefreshPreview();
            _status.Text = "已达到 20000 像素高度上限，捕捉停止；点击 √ 复制已有部分并退出。";
        }
    }

    private void RefreshPreview()
    {
        if (_stitcher is null) return;
        _preview.Source = _stitcher.Compose();
        _status.Text = $"请在选区中手动向上或向下滚动。\n点击 √ 复制并退出，X 取消。\n累计：{_stitcher.Width} × {_stitcher.Height}";
    }

    private void RequestFinish()
    {
        if (_finishRequested) return;
        _finishRequested = true;
        if (!_busy && (_paused || _waitingForOverlap)) Finish();
        else if (!_busy)
        {
            // 复制前再采样一次，把可匹配的最后一段加入；无重叠则保留已确认部分。
            _candidate = null;
            _candidatePixels = null;
        }
    }

    private void Finish()
    {
        if (_closed) return;
        if (_stitcher is null)
        {
            _finishRequested = false;
            MessageBox.Show(this, "尚未获得稳定的初始画面，请取消后重新选择捕捉范围。", "EasyShot");
            return;
        }
        try
        {
            var image = _stitcher.Compose();
            ScreenshotOutput.Copy(image);
            Close();
        }
        catch (Exception ex)
        {
            _finishRequested = false;
            MessageBox.Show(this, $"复制长截图失败：{ex.Message}\n可重试或取消。", "EasyShot");
        }
    }
}
