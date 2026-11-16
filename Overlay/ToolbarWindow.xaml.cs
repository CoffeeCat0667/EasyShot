using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EasyShot.Core;

namespace EasyShot.Overlay;

public partial class ToolbarWindow : Window
{
    private readonly OverlaySession _session;
    private IntPtr _placedMonitor;
    private Point? _manualPosition;
    private Point _dragCursor;
    private Point _dragWindow;
    private Int32Rect _lastSelection;
    private MonitorInfo? _lastMonitor;

    internal ToolbarWindow(OverlaySession session)
    {
        _session = session;
        InitializeComponent();
        PreviewKeyDown += (_, e) => e.Handled = _session.HandleKey(e.Key, Keyboard.Modifiers);
        UpdateState();
    }

    internal void Place(Int32Rect selection, MonitorInfo monitor, double scale)
    {
        _lastSelection = selection;
        _lastMonitor = monitor;
        if (_manualPosition is { } position)
        {
            var handle = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = (int)position.X, Y = (int)position.Y }, NativeMethods.MonitorDefaultToNearest);
            monitor = ScreenInfo.GetMonitors().FirstOrDefault(m => m.Handle == handle) ?? monitor;
        }
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var info = new NativeMethods.MonitorInfoEx { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
        var work = monitor.Bounds;
        if (NativeMethods.GetMonitorInfo(monitor.Handle, ref info))
            work = new Int32Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);

        if (_placedMonitor != monitor.Handle)
        {
            // 先移动至目标屏，让 WPF 接收该屏 DPI，再约束布局宽高。
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, work.X, work.Y,
                Math.Min(work.Width, Math.Max(1, (int)Math.Ceiling(ActualWidth * scale))),
                Math.Min(work.Height, Math.Max(1, (int)Math.Ceiling(ActualHeight * scale))), NativeMethods.SwpNoActivate);
            _placedMonitor = monitor.Handle;
        }
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi > 0) scale = dpi / 96.0;
        MaxWidth = work.Width / scale;
        MaxHeight = work.Height / scale;
        MinHeight = Math.Min(40, MaxHeight);
        var content = (FrameworkElement)Content;
        content.Measure(new Size(MaxWidth, MaxHeight));
        UpdateLayout();

        var width = Math.Clamp((int)Math.Ceiling(content.DesiredSize.Width * scale), 1, work.Width);
        var height = Math.Clamp((int)Math.Ceiling(Math.Max(MinHeight, content.DesiredSize.Height) * scale), 1, work.Height);
        var gap = (int)Math.Round(8 * scale);

        var x = selection.X;
        var y = selection.Y + selection.Height + gap;

        if (y + height > work.Y + work.Height)
        {
            y = selection.Y - height - gap;
        }

        if (y < work.Y)
        {
            y = selection.Y + gap;
        }

        if (_manualPosition is { } manual)
        {
            x = (int)Math.Round(manual.X);
            y = (int)Math.Round(manual.Y);
        }

        x = Math.Clamp(x, work.X, work.X + work.Width - width);
        y = Math.Clamp(y, work.Y, work.Y + work.Height - height);

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, x, y, width, height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);

        Opacity = 1;
        Show();
    }

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        NativeMethods.GetCursorPos(out var cursor);
        NativeMethods.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        _dragCursor = new Point(cursor.X, cursor.Y);
        _dragWindow = new Point(rect.Left, rect.Top);
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        NativeMethods.GetCursorPos(out var cursor);
        _manualPosition = _dragWindow + (new Point(cursor.X, cursor.Y) - _dragCursor);
        if (_lastMonitor is not null) Place(_lastSelection, _lastMonitor, 1);
    }

    internal void UpdateState()
    {
        StartScrollButton.Visibility = _session.CanStartScrollCapture ? Visibility.Visible : Visibility.Collapsed;
        UndoButton.IsEnabled = _session.CanUndo;
        RedoButton.IsEnabled = _session.CanRedo;
        foreach (var button in new[] { SelectButton, RectangleButton, ArrowButton, PenButton, TextButton, NumberButton, MosaicButton })
        {
            button.Background = new SolidColorBrush(button.Tag.ToString() == _session.Tool.ToString()
                ? Color.FromRgb(0x28, 0x53, 0x7C) : System.Windows.Media.Colors.Transparent);
            button.BorderBrush = button.Tag.ToString() == _session.Tool.ToString()
                ? new SolidColorBrush(Color.FromRgb(0x69, 0xA9, 0xFF)) : Brushes.Transparent;
        }
    }

    private void OnToolClick(object sender, RoutedEventArgs e) =>
        _session.SetTool(Enum.Parse<AnnotationTool>(((Button)sender).Tag.ToString()!));

    private void OnColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((ComboBox)sender).SelectedItem is ComboBoxItem item)
        {
            _session.SetColor((Color)ColorConverter.ConvertFromString(item.Tag.ToString()!));
        }
    }

    private void OnThicknessChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((ComboBox)sender).SelectedItem is ComboBoxItem item)
        {
            _session.SetThickness(double.Parse(item.Tag.ToString()!));
        }
    }

    private void OnTextSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((ComboBox)sender).SelectedItem is ComboBoxItem item)
            _session.TextSize = double.Parse(item.Tag.ToString()!);
    }

    private void OnUndo(object sender, RoutedEventArgs e) => _session.Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => _session.Redo();
    private void OnCopy(object sender, RoutedEventArgs e) => _session.Copy();
    private void OnSave(object sender, RoutedEventArgs e) => _session.Save();
    private void OnPin(object sender, RoutedEventArgs e) => _session.Pin();
    private void OnClose(object sender, RoutedEventArgs e) => _session.Cancel();
    private void OnStartScroll(object sender, RoutedEventArgs e) => _session.BeginScrollCapture();
    private void OnOcr(object sender, RoutedEventArgs e) => _session.RecognizeText();
    private void OnQr(object sender, RoutedEventArgs e) => _session.RecognizeCodes();

    internal UIElement TakeContent()
    {
        DragGrip.Visibility = Visibility.Collapsed;
        var content = (UIElement)Content;
        Content = null;
        return content;
    }
}
