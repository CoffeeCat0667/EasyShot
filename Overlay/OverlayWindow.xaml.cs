using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using EasyShot.Core;
using EasyShot.Config;

namespace EasyShot.Overlay;

public partial class OverlayWindow : Window
{
    private readonly Brush Accent = new SolidColorBrush(AppConfig.Load(out _).GetSelectionBorderColor());
    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));

    private readonly OverlaySession _session;
    private readonly MonitorInfo _monitor;
    private CroppedBitmap _slice;
    private readonly List<Rectangle> _handles = new();

    private Image? _image;
    private System.Windows.Shapes.Path? _dim;
    private Rectangle? _border;
    private Border? _label;
    private TextBlock? _labelText;
    private AnnotationLayer? _annotationLayer;
    private double _scale = 1.0;
    private TextBox? _textEditor;
    private bool _longImage;
    private bool _rightCancelPending;
    private Int32Rect? _visualSelection;
    private bool _visualSelectionReady;
    private bool _visualLocked;
    private double _visualWidth = -1;
    private double _visualHeight = -1;

    internal OverlayWindow(OverlaySession session, MonitorInfo monitor, BitmapSource screen, Int32Rect virtualScreen)
    {
        InitializeComponent();

        _session = session;
        _monitor = monitor;
        _slice = new CroppedBitmap(screen, new Int32Rect(
            monitor.Bounds.X - virtualScreen.X,
            monitor.Bounds.Y - virtualScreen.Y,
            monitor.Bounds.Width,
            monitor.Bounds.Height));

        PreviewKeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseDown;
        MouseRightButtonDown += OnRightMouseDown;
        MouseRightButtonUp += OnRightMouseUp;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
    }

    internal MonitorInfo Monitor => _monitor;

    internal double Scale => _scale;

    internal void ConfigureLongImage(UIElement toolbar, int width, int height)
    {
        _longImage = true;
        AllowsTransparency = false;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        Title = "EasyShot 长图标注";
        ShowInTaskbar = true;
        Topmost = false;
        Width = Math.Min(1200, SystemParameters.WorkArea.Width);
        Height = Math.Min(850, SystemParameters.WorkArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = null;
        Root.Width = width;
        Root.Height = height;
        var layout = new DockPanel();
        var toolbarScroll = new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(toolbarScroll, Dock.Top);
        layout.Children.Add(toolbarScroll);
        layout.Children.Add(new ScrollViewer { Content = Root, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        MouseLeftButtonDown -= OnMouseDown;
        MouseRightButtonDown -= OnRightMouseDown;
        MouseRightButtonUp -= OnRightMouseUp;
        MouseMove -= OnMouseMove;
        MouseLeftButtonUp -= OnMouseUp;
        Root.MouseLeftButtonDown += OnMouseDown;
        Root.MouseMove += OnMouseMove;
        Root.MouseLeftButtonUp += OnMouseUp;
        BuildVisuals();
        Opacity = 1;
    }

    internal void SetScreen(BitmapSource screen, Int32Rect virtualScreen)
    {
        _slice = new CroppedBitmap(screen, new Int32Rect(_monitor.Bounds.X - virtualScreen.X,
            _monitor.Bounds.Y - virtualScreen.Y, _monitor.Bounds.Width, _monitor.Bounds.Height));
    }

    internal void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            hwnd = new WindowInteropHelper(this).EnsureHandle();
        }

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost,
            _monitor.Bounds.X, _monitor.Bounds.Y, _monitor.Bounds.Width, _monitor.Bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);

        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        _scale = dpi > 0 ? dpi / 96.0 : 1.0;

        UpdateLayout();
        BuildVisuals();
        Opacity = 1;
    }

    internal void ActivateForInput()
    {
        Activate();
        Root.Focus();
        Keyboard.Focus(Root);
    }

    internal Point ToPhysical(Point dipPoint) =>
        new(_monitor.Bounds.X + dipPoint.X * _scale, _monitor.Bounds.Y + dipPoint.Y * _scale);

    internal Point ToLocalDip(Int32Rect physical)
    {
        var dipX = (physical.X - _monitor.Bounds.X) / _scale;
        var dipY = (physical.Y - _monitor.Bounds.Y) / _scale;
        return new Point(dipX, dipY);
    }

    internal void UpdateVisual(Int32Rect? selection)
    {
        if (_image is null || _dim is null || _border is null || _label is null || _labelText is null)
        {
            return;
        }

        if (!ReferenceEquals(_image.Source, _slice)) _image.Source = _slice;
        var width = _longImage ? Root.Width : ActualWidth;
        var height = _longImage ? Root.Height : ActualHeight;
        if (_image.Width != width) _image.Width = width;
        if (_image.Height != height) _image.Height = height;

        // 标注拖动/绘制时选区不变，无需重建遮罩、边框、抓手和尺寸标签。
        var locked = _session.Selection is not null;
        if (!_visualSelectionReady || _visualSelection != selection || _visualLocked != locked ||
            _visualWidth != width || _visualHeight != height)
        {
            var full = new Rect(0, 0, width, height);
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(new RectangleGeometry(full));

            if (selection is { } sel && sel.Width > 0 && sel.Height > 0)
            {
                var origin = ToLocalDip(sel);
                var rect = new Rect(origin.X, origin.Y, sel.Width / _scale, sel.Height / _scale);
                group.Children.Add(new RectangleGeometry(rect));

                _border.Visibility = Visibility.Visible;
                _border.Width = rect.Width;
                _border.Height = rect.Height;
                Canvas.SetLeft(_border, rect.X);
                Canvas.SetTop(_border, rect.Y);

                PlaceHandles(rect);
                if (_session.Selection is null)
                {
                    foreach (var handle in _handles) handle.Visibility = Visibility.Collapsed;
                }
                PlaceLabel(rect, sel);
            }
            else
            {
                _border.Visibility = Visibility.Collapsed;
                foreach (var handle in _handles)
                {
                    handle.Visibility = Visibility.Collapsed;
                }

                _label.Visibility = Visibility.Collapsed;
            }

            _dim.Data = group;
            _visualSelection = selection;
            _visualSelectionReady = true;
            _visualLocked = locked;
            _visualWidth = width;
            _visualHeight = height;
        }

        if (_annotationLayer is not null)
        {
            _annotationLayer.Width = width;
            _annotationLayer.Height = height;
            _annotationLayer.InvalidateVisual();
        }
    }

    private void PlaceHandles(Rect selection)
    {
        var size = 8.0 / _scale;
        var half = size / 2;
        var centerX = selection.X + selection.Width / 2;
        var centerY = selection.Y + selection.Height / 2;

        var centers = new[]
        {
            new Point(selection.Left, selection.Top),
            new Point(centerX, selection.Top),
            new Point(selection.Right, selection.Top),
            new Point(selection.Right, centerY),
            new Point(selection.Right, selection.Bottom),
            new Point(centerX, selection.Bottom),
            new Point(selection.Left, selection.Bottom),
            new Point(selection.Left, centerY)
        };

        for (var i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            handle.Visibility = Visibility.Visible;
            handle.Width = size;
            handle.Height = size;
            Canvas.SetLeft(handle, centers[i].X - half);
            Canvas.SetTop(handle, centers[i].Y - half);
        }
    }

    private void PlaceLabel(Rect selection, Int32Rect physical)
    {
        if (_label is null || _labelText is null)
        {
            return;
        }

        _labelText.Text = $"{physical.Width} × {physical.Height}";
        _label.Visibility = Visibility.Visible;

        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _label.DesiredSize;

        var x = selection.X + 2;
        var y = selection.Y - size.Height - 4;
        if (y < 0)
        {
            y = selection.Y + 4;
        }

        var maxX = Math.Max(0, ActualWidth - size.Width);
        var maxY = Math.Max(0, ActualHeight - size.Height);
        x = Math.Clamp(x, 0, maxX);
        y = Math.Clamp(y, 0, maxY);

        Canvas.SetLeft(_label, x);
        Canvas.SetTop(_label, y);
    }

    private void BuildVisuals()
    {
        if (_image is not null)
        {
            return;
        }

        _image = new Image
        {
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        Root.Children.Add(_image);

        _dim = new System.Windows.Shapes.Path { Fill = DimBrush, IsHitTestVisible = false };
        Root.Children.Add(_dim);

        _annotationLayer = new AnnotationLayer(_session, this);
        Root.Children.Add(_annotationLayer);

        _border = new Rectangle
        {
            Stroke = Accent,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Root.Children.Add(_border);

        for (var i = 0; i < 8; i++)
        {
            var handle = new Rectangle
            {
                Fill = Brushes.White,
                Stroke = Accent,
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            _handles.Add(handle);
            Root.Children.Add(handle);
        }

        _labelText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.White
        };
        _label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            Child = _labelText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        Root.Children.Add(_label);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_textEditor is not null)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                var editor = _textEditor;
                var annotation = (Annotation?)editor.Tag;
                var position = ToPhysical(new Point(Canvas.GetLeft(editor), Canvas.GetTop(editor)));
                _textEditor = null;
                Root.Children.Remove(editor);
                if (e.Key == Key.Enter) _session.CommitText(position, annotation, editor.Text);
                Root.Focus();
                e.Handled = true;
            }
            return;
        }
        e.Handled = _session.HandleKey(e.Key, Keyboard.Modifiers);
    }

    internal void BeginTextEdit(Point physical, Annotation? annotation)
    {
        ReleaseMouseCapture();
        Root.ReleaseMouseCapture();
        var position = annotation?.Points[0] ?? physical;
        var color = annotation?.Color ?? _session.Color;
        var textBrush = new SolidColorBrush(color);
        _textEditor = new TextBox { Text = annotation?.Text ?? "", Tag = annotation, MinWidth = 120,
            FontSize = _session.TextSize / _scale, FontFamily = new FontFamily("Microsoft YaHei"),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = textBrush,
            CaretBrush = textBrush, FocusVisualStyle = null, Padding = new Thickness(2, 0, 2, 0) };
        Canvas.SetLeft(_textEditor, (position.X - _monitor.Bounds.X) / _scale);
        Canvas.SetTop(_textEditor, (position.Y - _monitor.Bounds.Y) / _scale);
        Root.Children.Add(_textEditor);
        _textEditor.Focus();
        _textEditor.SelectAll();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_textEditor is not null) return;
        var physical = ToPhysical(e.GetPosition(Root));
        Cursor = _session.GetCursor(physical);
        if (Cursor == Cursors.No)
        {
            e.Handled = true;
            return;
        }

        ActivateForInput();
        if (_longImage) Root.CaptureMouse();
        else CaptureMouse();
        _session.OnMouseDown(this, ToPhysical(e.GetPosition(Root)), e.ClickCount);
        Cursor = _session.GetCursor(ToPhysical(e.GetPosition(Root)));
        e.Handled = true;
    }

    private void OnRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_session.CanCancelInitialRegion) return;
        _rightCancelPending = true;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnRightMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_rightCancelPending) return;
        _rightCancelPending = false;
        ReleaseMouseCapture();
        Root.ReleaseMouseCapture();
        e.Handled = true;
        _session.CancelInitialRegion();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _session.OnMouseMove(ToPhysical(e.GetPosition(Root)));
        Cursor = _session.GetCursor(ToPhysical(e.GetPosition(Root)));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured && !Root.IsMouseCaptured)
        {
            e.Handled = true;
            return;
        }

        _session.OnMouseMove(ToPhysical(e.GetPosition(Root)));
        ReleaseMouseCapture();
        Root.ReleaseMouseCapture();
        _session.OnMouseUp();
        Cursor = _session.GetCursor(ToPhysical(e.GetPosition(Root)));
        e.Handled = true;
    }
}
