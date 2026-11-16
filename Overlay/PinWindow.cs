using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using EasyShot.Config;
using EasyShot.Capture;
using EasyShot.Core;
using EasyShot.UI;

namespace EasyShot.Overlay;

internal sealed class PinWindow : Window
{
    private readonly PinViewport _view;
    private readonly Int32Rect _location;
    private readonly MenuItem _topmost;
    private readonly TextBlock _opacityLabel;
    private readonly TextBlock _zoomLabel;
    private Point? _panStart;
    private Vector _panOrigin;
    private HwndSource? _source;
    private Rect _resizeOrigin;

    public PinWindow(BitmapSource image, Int32Rect location)
    {
        _location = location;
        Title = "EasyShot 贴图";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        MinWidth = 48;
        MinHeight = 48;
        Width = Math.Max(MinWidth, image.PixelWidth + 6);
        Height = Math.Max(MinHeight, image.PixelHeight + 6);
        _view = new PinViewport(image);
        var config = AppConfig.Load(out _);
        Content = new Border
        {
            BorderBrush = new SolidColorBrush(config.GetPinBorderColor()), BorderThickness = new Thickness(3),
            Background = Brushes.Transparent, Child = _view, SnapsToDevicePixels = true
        };

        var menu = new ContextMenu { Style = (Style)FindResource("ShotMenu"), MinWidth = 290 };
        _zoomLabel = new TextBlock { Foreground = (Brush)FindResource("ShotMuted"), Margin = new Thickness(12, 8, 12, 4), FontSize = 11 };
        menu.Items.Add(new MenuItem { Header = _zoomLabel, IsEnabled = false });
        AddAction(menu, "整体放大", "Expand", "窗口 +", () => ScaleWindow(1.1));
        AddAction(menu, "整体缩小", "Shrink", "窗口 −", () => ScaleWindow(1 / 1.1));
        menu.Items.Add(new Separator());
        AddAction(menu, "局部放大", "ZoomIn", "滚轮 ↑", () => Zoom(1.1, Center()));
        AddAction(menu, "局部缩小", "ZoomOut", "滚轮 ↓", () => Zoom(1 / 1.1, Center()));
        menu.Items.Add(new Separator());
        _topmost = new MenuItem { Header = "置顶显示", Icon = ShotIconExtension.Create("Pin"), IsCheckable = true, IsChecked = true };
        _topmost.Click += (_, _) => { Topmost = _topmost.IsChecked; UpdateMenu(); };
        menu.Items.Add(_topmost);

        var opacityPanel = new StackPanel { Width = 238, Margin = new Thickness(0, 3, 0, 4) };
        _opacityLabel = new TextBlock { Text = "不透明度  100%", Foreground = (Brush)FindResource("ShotText"), Margin = new Thickness(0, 0, 0, 6) };
        opacityPanel.Children.Add(_opacityLabel);
        var slider = new Slider
        {
            Style = (Style)FindResource("ShotSlider"), Minimum = 0, Maximum = 100, Value = 100,
            TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10,
            ToolTip = "0% 完全透明 · 100% 不透明；框线及菜单保持可操作"
        };
        slider.ValueChanged += (_, e) =>
        {
            _view.ImageOpacity = e.NewValue / 100;
            _view.InvalidateVisual();
            _opacityLabel.Text = $"不透明度  {e.NewValue:0}%";
        };
        opacityPanel.Children.Add(slider);
        menu.Items.Add(new MenuItem { Header = opacityPanel, StaysOpenOnClick = true, Focusable = false });
        menu.Items.Add(new Separator());
        AddAction(menu, "确认并复制", "Confirm", "Enter", Confirm);
        AddAction(menu, "保存当前视图…", "Save", "Ctrl+S", SaveImage);
        AddAction(menu, "取消贴图", "Close", "Esc", Close);
        ContextMenu = menu;
        menu.Opened += (_, _) => UpdateMenu();

        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WindowProc);
        };
        Closed += (_, _) => _source?.RemoveHook(WindowProc);

        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
            var frame = (int)Math.Ceiling(6 * scale);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, _location.X - frame / 2, _location.Y - frame / 2,
                Math.Max((int)Math.Ceiling(MinWidth * scale), image.PixelWidth + frame),
                Math.Max((int)Math.Ceiling(MinHeight * scale), image.PixelHeight + frame), NativeMethods.SwpShowWindow);
        };
        PreviewMouseDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseUp += OnMouseUp;
        LostMouseCapture += (_, _) => _panStart = null;
        MouseWheel += (_, e) =>
        {
            var point = e.GetPosition(_view);
            if (point.X >= 0 && point.Y >= 0 && point.X <= _view.ActualWidth && point.Y <= _view.ActualHeight)
                Zoom(e.Delta > 0 ? 1.1 : 1 / 1.1, point);
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Confirm();
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) SaveImage();
            else return;
            e.Handled = true;
        };
    }

    private static void AddAction(ContextMenu menu, string text, string icon, string shortcut, Action action)
    {
        var item = new MenuItem { Header = text, Icon = ShotIconExtension.Create(icon), InputGestureText = shortcut };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private Point Center() => new(_view.ActualWidth / 2, _view.ActualHeight / 2);

    private void Zoom(double factor, Point anchor)
    {
        _view.ZoomAt(factor, anchor);
        UpdateMenu();
    }

    private void UpdateMenu()
    {
        _zoomLabel.Text = $"当前视图  {_view.Zoom * 100:0}% · Shift 自由缩放";
        _topmost.Header = Topmost ? "取消置顶" : "置顶显示";
        _topmost.IsChecked = Topmost;
    }

    private void ScaleWindow(double factor)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(hwnd, out var bounds)) return;
        var dpi = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        factor = Math.Max(factor, Math.Max(MinWidth * dpi / (bounds.Right - bounds.Left), MinHeight * dpi / (bounds.Bottom - bounds.Top)));
        var width = (int)Math.Round((bounds.Right - bounds.Left) * factor);
        var height = (int)Math.Round((bounds.Bottom - bounds.Top) * factor);
        NativeMethods.SetWindowPos(hwnd, Topmost ? NativeMethods.HwndTopmost : new IntPtr(-2), bounds.Left, bounds.Top,
            width, height, NativeMethods.SwpNoActivate);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0231 && NativeMethods.GetWindowRect(hwnd, out var original)) // WM_ENTERSIZEMOVE
            _resizeOrigin = new Rect(original.Left, original.Top, original.Right - original.Left, original.Bottom - original.Top);
        if (message != 0x0214 || NativeMethods.GetAsyncKeyState(0x10) < 0 || _resizeOrigin.Width <= 0) return IntPtr.Zero;
        var handle = wParam.ToInt32() switch { 1 => 7, 2 => 3, 3 => 1, 4 => 0, 5 => 2, 6 => 5, 7 => 6, 8 => 4, _ => -1 };
        if (handle < 0) return IntPtr.Zero;
        var request = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.Rect>(lParam);
        var dx = handle is 0 or 6 or 7 ? request.Left - _resizeOrigin.Left : request.Right - _resizeOrigin.Right;
        var dy = handle is 0 or 1 or 2 ? request.Top - _resizeOrigin.Top : request.Bottom - _resizeOrigin.Bottom;
        var dpi = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        var rect = ResizeBounds.Calculate(_resizeOrigin, handle, new Vector(dx, dy), MinWidth * dpi, MinHeight * dpi, true);
        request.Left = (int)Math.Round(rect.Left);
        request.Top = (int)Math.Round(rect.Top);
        request.Right = (int)Math.Round(rect.Right);
        request.Bottom = (int)Math.Round(rect.Bottom);
        System.Runtime.InteropServices.Marshal.StructureToPtr(request, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    // Win32 边框命中码；只提供线框，不绘制缩放抓手。
    private int EdgeAt(Point point)
    {
        const double margin = 6;
        var left = point.X < margin;
        var right = point.X > ActualWidth - margin;
        var top = point.Y < margin;
        var bottom = point.Y > ActualHeight - margin;
        if (top && left) return 13;
        if (top && right) return 14;
        if (bottom && left) return 16;
        if (bottom && right) return 17;
        if (left) return 10;
        if (right) return 11;
        if (top) return 12;
        if (bottom) return 15;
        return 0;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panStart = e.GetPosition(_view);
            _panOrigin = _view.Pan;
            _view.CaptureMouse();
            Cursor = Cursors.ScrollAll;
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            e.Handled = true;
            var edge = EdgeAt(e.GetPosition(this));
            if (edge != 0)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.GetCursorPos(out var point);
                var packed = (point.X & 0xFFFF) | ((point.Y & 0xFFFF) << 16);
                NativeMethods.SendMessage(new WindowInteropHelper(this).Handle, 0xA1, new IntPtr(edge), new IntPtr(packed));
            }
            else DragMove();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart is { } start && e.MiddleButton == MouseButtonState.Pressed)
        {
            _view.PanBy(_panOrigin, e.GetPosition(_view) - start);
            return;
        }
        Cursor = EdgeAt(e.GetPosition(this)) switch
        {
            10 or 11 => Cursors.SizeWE, 12 or 15 => Cursors.SizeNS,
            13 or 17 => Cursors.SizeNWSE, 14 or 16 => Cursors.SizeNESW, _ => Cursors.SizeAll
        };
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _panStart = null;
        _view.ReleaseMouseCapture();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private BitmapSource Snapshot()
    {
        var scale = VisualTreeHelper.GetDpi(_view);
        var width = Math.Max(1, (int)Math.Round(_view.ActualWidth * scale.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(_view.ActualHeight * scale.DpiScaleY));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale.DpiScaleX, scale.DpiScaleY));
            _view.DrawImage(dc, false);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void Confirm()
    {
        try { ScreenshotOutput.Copy(Snapshot()); Close(); }
        catch (Exception ex) { MessageBox.Show(this, $"复制当前贴图视图失败：{ex.Message}", "EasyShot"); }
    }

    private void SaveImage()
    {
        try
        {
            var config = AppConfig.Load(out _);
            var dialog = new SaveFileDialog { Filter = "PNG 图片 (*.png)|*.png", DefaultExt = ".png", AddExtension = true,
                FileName = $"EasyShot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                InitialDirectory = Directory.Exists(config.SaveDirectory) ? config.SaveDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
            if (dialog.ShowDialog(this) != true) return;
            var image = Snapshot();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(dialog.FileName)) encoder.Save(file);
            if (config.CopyOnSave)
            {
                try { ScreenshotOutput.Copy(image); }
                catch (Exception ex) { MessageBox.Show(this, $"图片已保存至：{dialog.FileName}\n复制失败，文件不受影响：{ex.Message}", "EasyShot"); }
            }
        }
        catch (Exception ex) { MessageBox.Show(this, $"保存贴图失败：{ex.Message}", "EasyShot"); }
    }
}

internal sealed class PinViewport : FrameworkElement
{
    private readonly BitmapSource _image;
    public double Zoom { get; private set; } = 1;
    public Vector Pan { get; private set; }
    public double ImageOpacity { get; set; } = 1;

    public PinViewport(BitmapSource image)
    {
        _image = image;
        ClipToBounds = true;
    }

    public void ZoomAt(double factor, Point anchor)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var next = Math.Clamp(Zoom * factor, 1, 8);
        var source = new Point(anchor.X * _image.PixelWidth / ActualWidth, anchor.Y * _image.PixelHeight / ActualHeight);
        var contentPoint = (new Vector(source.X, source.Y) - Pan) / Zoom;
        Pan = new Vector(source.X, source.Y) - contentPoint * next;
        Zoom = next;
        ConstrainPan();
        InvalidateVisual();
    }

    public void PanBy(Vector origin, Vector delta)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        Pan = origin + new Vector(delta.X * _image.PixelWidth / ActualWidth, delta.Y * _image.PixelHeight / ActualHeight);
        ConstrainPan();
        InvalidateVisual();
    }

    private void ConstrainPan()
    {
        Pan = new Vector(Math.Clamp(Pan.X, _image.PixelWidth * (1 - Zoom), 0),
            Math.Clamp(Pan.Y, _image.PixelHeight * (1 - Zoom), 0));
    }

    protected override void OnRender(DrawingContext dc)
    {
        // 极低透明度底层维持 0% 图像时的命中区域，线框与窗口本身不被隐藏。
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, new Rect(RenderSize));
        DrawImage(dc, true);
    }

    internal void DrawImage(DrawingContext dc, bool displayOpacity)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.PushOpacity(displayOpacity ? ImageOpacity : 1);
        dc.DrawImage(_image, new Rect(Pan.X * ActualWidth / _image.PixelWidth, Pan.Y * ActualHeight / _image.PixelHeight,
            ActualWidth * Zoom, ActualHeight * Zoom));
        dc.Pop();
        dc.Pop();
    }
}
