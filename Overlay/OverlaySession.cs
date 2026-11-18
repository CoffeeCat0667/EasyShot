using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using EasyShot.Capture;
using EasyShot.Config;
using EasyShot.Core;
using EasyShot.Ocr;
using EasyShot.Qr;

namespace EasyShot.Overlay;

internal sealed class OverlaySession
{
    private const int MinSelectionSize = 8;

    private BitmapSource _screen;
    private readonly Int32Rect _virtualScreen;
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly List<OverlayWindow> _windows = new();

    private ToolbarWindow? _toolbar;
    private Int32Rect _selection;
    private bool _hasSelection;
    private DragMode _mode = DragMode.None;
    private int _handle = -1;
    private Point _dragStart;
    private Int32Rect _dragOrigin;
    private bool _closed;
    private readonly List<Annotation> _annotations = new();
    private readonly UndoStack _history = new();
    private Annotation? _selectedAnnotation;
    private Annotation? _draft;
    private Annotation? _beforeEdit;
    private Point _annotationDragStart;
    private bool _outputInProgress;
    private bool _windowPicking;
    private IReadOnlyList<CaptureWindowInfo> _captureWindows = Array.Empty<CaptureWindowInfo>();
    private CaptureWindowInfo? _targetWindow;
    private int _nextNumber = 1;
    private bool _longImage;
    private bool _regionCapture;

    internal bool CanStartScrollCapture => _regionCapture && !_longImage;

    internal AnnotationTool Tool { get; private set; } = AnnotationTool.Select;
    internal Color Color { get; private set; } = Color.FromRgb(0xFF, 0x3B, 0x30);
    internal double Thickness { get; private set; } = 4;
    internal double TextSize { get; set; } = 24;
    internal Int32Rect? Selection => _hasSelection ? _selection : null;
    internal bool CanUndo => _history.CanUndo;
    internal bool CanRedo => _history.CanRedo;

    private OverlaySession(BitmapSource screen, Int32Rect virtualScreen, IReadOnlyList<MonitorInfo> monitors)
    {
        _screen = screen;
        _virtualScreen = virtualScreen;
        _monitors = monitors;
    }

    public static OverlaySession? Current { get; private set; }

    public static void Start()
    {
        if (ScrollCaptureWindow.Current is not null) return;
        if (Current is not null)
        {
            Current.ActivateInitial();
            return;
        }

        var monitors = ScreenInfo.GetMonitors();
        if (monitors.Count == 0)
        {
            MessageBox.Show("未检测到显示器。", "EasyShot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (screen, virtualScreen) = ScreenCapture.CaptureVirtualScreen();
        var session = new OverlaySession(screen, virtualScreen, monitors)
        {
            _regionCapture = true,
            _windowPicking = true,
            _captureWindows = WindowEnumerator.GetWindows()
        };
        Current = session;
        session.ShowAll();
        session.OnMouseMove(ScreenInfo.GetCursorPosition());
    }

    public static void CloseCurrent() => Current?.Cancel();

    internal void BeginScrollCapture()
    {
        if (!CanStartScrollCapture || !_hasSelection || _outputInProgress || _mode != DragMode.None) return;
        var region = _selection;
        var center = new Point(region.X + region.Width / 2.0, region.Y + region.Height / 2.0);
        var target = _captureWindows.FirstOrDefault(w => Contains(w.Frame, center))?.Handle ?? IntPtr.Zero;
        Cancel();
        // 先让覆盖层关闭后的绘制完成，再开始抓取真实桌面。
        Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                try { ScrollCaptureWindow.Start(region, target); }
                catch (Exception ex) { MessageBox.Show($"启动滚动捕捉失败：{ex.Message}", "EasyShot"); }
            }));
    }

    internal static void EditLongImage(BitmapSource image)
    {
        if (Current is not null) return;
        var bounds = new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
        var monitor = new MonitorInfo(IntPtr.Zero, bounds);
        var session = new OverlaySession(image, bounds, new[] { monitor })
        {
            _selection = bounds,
            _hasSelection = true,
            _longImage = true
        };
        Current = session;
        var editor = new OverlayWindow(session, monitor, image, bounds);
        session._windows.Add(editor);
        editor.Closed += (_, _) => { session._windows.Remove(editor); session.Cancel(); };
        session._toolbar = new ToolbarWindow(session);
        editor.ConfigureLongImage(session._toolbar.TakeContent(), image.PixelWidth, image.PixelHeight);
        editor.Show();
        session.Render();
        editor.ActivateForInput();
    }

    public static void StartFullScreen()
    {
        if (Current is not null || ScrollCaptureWindow.Current is not null) return;
        var monitors = ScreenInfo.GetMonitors();
        var cursor = ScreenInfo.GetCursorPosition();
        var monitor = monitors.FirstOrDefault(m => Contains(m.Bounds, cursor));
        if (monitor is null) throw new InvalidOperationException("无法找到鼠标所在的显示器。");
        var (screen, virtualScreen) = ScreenCapture.CaptureVirtualScreen();
        var session = new OverlaySession(screen, virtualScreen, monitors);
        session._selection = monitor.Bounds;
        session._hasSelection = true;
        Current = session;
        session.ShowAll();
        session.ShowToolbar();
        session.Render();
    }

    private void ShowAll()
    {
        foreach (var monitor in _monitors)
        {
            var window = new OverlayWindow(this, monitor, _screen, _virtualScreen);
            _windows.Add(window);
            window.Show();
            window.Place();
        }

        ActivateInitial();
        Render();
    }

    private void ActivateInitial()
    {
        var cursor = ScreenInfo.GetCursorPosition();
        var target = FindWindowAt(cursor) ?? _windows.FirstOrDefault();
        target?.ActivateForInput();
    }

    private OverlayWindow? FindWindowAt(Point physicalPoint) =>
        _windows.FirstOrDefault(w => Contains(w.Monitor.Bounds, physicalPoint)) ?? _windows.FirstOrDefault();

    internal void OnMouseDown(OverlayWindow window, Point physical, int clickCount)
    {
        if (_windowPicking)
        {
            OnMouseMove(physical);
            _dragStart = physical;
            _mode = DragMode.PendingSelection;
            return;
        }
        if (_hasSelection)
        {
            var annotationHandle = HitAnnotationHandle(physical);
            if (annotationHandle >= 0)
            {
                _mode = DragMode.ResizingAnnotation;
                _handle = annotationHandle;
                _beforeEdit = _selectedAnnotation!.Clone();
                _annotationDragStart = physical;
                return;
            }

            var handle = HitHandle(physical);
            if (handle >= 0)
            {
                _mode = DragMode.Resizing;
                _handle = handle;
                _dragStart = physical;
                _dragOrigin = _selection;
                return;
            }

            if (Contains(_selection, physical))
            {
                _selectedAnnotation = _annotations.LastOrDefault(a => a.HitTest(physical));
                if (_selectedAnnotation is not null)
                {
                    if (clickCount == 2 && _selectedAnnotation.Tool == AnnotationTool.Text)
                    {
                        window.BeginTextEdit(physical, _selectedAnnotation);
                        return;
                    }
                    _mode = DragMode.MovingAnnotation;
                    _beforeEdit = _selectedAnnotation.Clone();
                    _annotationDragStart = physical;
                    Render();
                    return;
                }

                if (Tool != AnnotationTool.Select)
                {
                    if (Tool == AnnotationTool.Text)
                    {
                        window.BeginTextEdit(physical, null);
                        return;
                    }
                    if (Tool == AnnotationTool.Number)
                    {
                        var number = new Annotation { Tool = Tool, Color = Color, Thickness = Thickness, Number = _nextNumber++ };
                        number.Points.Add(physical);
                        number.Points.Add(physical + new Vector(30, 30));
                        _annotations.Add(number);
                        _history.Record(() => { _annotations.Remove(number); _nextNumber--; }, () => { _annotations.Add(number); _nextNumber++; });
                        _selectedAnnotation = number;
                        Render();
                        return;
                    }
                    _draft = new Annotation { Tool = Tool, Color = Color, Thickness = Thickness };
                    _draft.Points.Add(physical);
                    _draft.Points.Add(physical);
                    _mode = DragMode.Drawing;
                    Render();
                    return;
                }
            }

            if (Contains(_selection, physical))
            {
                _mode = DragMode.Moving;
                _dragStart = physical;
                _dragOrigin = _selection;
                return;
            }

            // 已确定选区后，外部点击不再启动新选区。
            return;
        }

        _mode = DragMode.Creating;
        _handle = -1;
        _dragStart = physical;
        _dragOrigin = _selection;
        _hasSelection = false;
        _selection = new Int32Rect((int)physical.X, (int)physical.Y, 0, 0);
        HideToolbar();
        Render();
    }

    internal void OnMouseMove(Point physical)
    {
        if (_mode == DragMode.PendingSelection)
        {
            if (Math.Max(Math.Abs(physical.X - _dragStart.X), Math.Abs(physical.Y - _dragStart.Y)) <= 4) return;
            _windowPicking = false;
            _targetWindow = null;
            _mode = DragMode.Creating;
            _selection = NormalizeRect(_dragStart, physical);
            Render();
            return;
        }
        if (_windowPicking)
        {
            _selection = Int32Rect.Empty;
            _targetWindow = _captureWindows.FirstOrDefault(w => Contains(w.Frame, physical));
            if (_targetWindow is { } target)
                _selection = NormalizeRect(new Point(target.Frame.X, target.Frame.Y),
                    new Point(target.Frame.X + target.Frame.Width, target.Frame.Y + target.Frame.Height));
            Render();
            return;
        }
        switch (_mode)
        {
            case DragMode.ResizingAnnotation:
                ResizeAnnotationTo(physical);
                break;

            case DragMode.Drawing:
                if (_draft is not null)
                {
                    if (_draft.Tool == AnnotationTool.Pen)
                    {
                        if ((physical - _draft.Points[^1]).Length >= 1)
                        {
                            _draft.Points.Add(physical);
                        }
                    }
                    else
                    {
                        _draft.Points[^1] = physical;
                    }
                }
                break;

            case DragMode.MovingAnnotation:
                if (_selectedAnnotation is not null && _beforeEdit is not null)
                {
                    var delta = physical - _annotationDragStart;
                    _selectedAnnotation.Points.Clear();
                    _selectedAnnotation.Points.AddRange(_beforeEdit.Points.Select(p => p + delta));
                }
                break;

            case DragMode.Creating:
                _selection = NormalizeRect(_dragStart, physical);
                break;

            case DragMode.Moving:
                MoveTo(physical);
                break;

            case DragMode.Resizing:
                ResizeTo(physical);
                break;

            default:
                return;
        }

        Render();
    }

    internal void OnMouseUp()
    {
        if (_mode == DragMode.PendingSelection)
        {
            _mode = DragMode.None;
            if (_targetWindow is not null && _selection.Width >= MinSelectionSize && _selection.Height >= MinSelectionSize)
            {
                CaptureSelectedWindow();
                _windowPicking = false;
                _hasSelection = true;
                ShowToolbar();
            }
            Render();
            return;
        }
        if (_mode == DragMode.Drawing && _draft is not null)
        {
            var annotation = _draft;
            _draft = null;
            if (annotation.Bounds.Width > 1 || annotation.Bounds.Height > 1)
            {
                _annotations.Add(annotation);
                _history.Record(() => _annotations.Remove(annotation), () => _annotations.Add(annotation));
                _selectedAnnotation = annotation;
            }
        }
        else if ((_mode == DragMode.MovingAnnotation || _mode == DragMode.ResizingAnnotation)
                 && _selectedAnnotation is not null && _beforeEdit is not null)
        {
            var annotation = _selectedAnnotation;
            var before = _beforeEdit.Points.ToArray();
            var after = annotation.Points.ToArray();
            if (!before.SequenceEqual(after))
            {
                _history.Record(() => SetPoints(annotation, before), () => SetPoints(annotation, after));
            }

            _beforeEdit = null;
        }

        if (_mode == DragMode.Creating)
        {
            if (_selection.Width < MinSelectionSize || _selection.Height < MinSelectionSize)
            {
                Cancel();
                return;
            }

            _hasSelection = true;
            ShowToolbar();
        }

        _mode = DragMode.None;
        _handle = -1;
        Render();
    }

    private static void SetPoints(Annotation annotation, IEnumerable<Point> points)
    {
        annotation.Points.Clear();
        annotation.Points.AddRange(points);
    }

    internal void CommitText(Point position, Annotation? annotation, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei"), TextSize, Brushes.White, 1);
        if (annotation is null)
        {
            annotation = new Annotation { Tool = AnnotationTool.Text, Color = Color, Thickness = Thickness, Text = text };
            annotation.Points.Add(position);
            annotation.Points.Add(position + new Vector(Math.Max(1, formatted.WidthIncludingTrailingWhitespace), formatted.Height));
            var added = annotation;
            _annotations.Add(added);
            _history.Record(() => _annotations.Remove(added), () => _annotations.Add(added));
        }
        else
        {
            var edited = annotation;
            var oldText = edited.Text;
            var oldPoints = edited.Points.ToArray();
            edited.Text = text;
            edited.Points[1] = edited.Points[0] + new Vector(Math.Max(1, formatted.WidthIncludingTrailingWhitespace), formatted.Height);
            var newPoints = edited.Points.ToArray();
            _history.Record(() => { edited.Text = oldText; SetPoints(edited, oldPoints); },
                () => { edited.Text = text; SetPoints(edited, newPoints); });
        }
        _selectedAnnotation = annotation;
        Render();
    }

    private void ResizeAnnotationTo(Point physical)
    {
        if (_selectedAnnotation is null || _beforeEdit is null)
        {
            return;
        }

        var delta = physical - _annotationDragStart;
        var originalBounds = _beforeEdit.Bounds;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            var resized = ResizeBounds.Calculate(originalBounds, _handle, delta, 1, 1, true);
            var padding = _beforeEdit.Thickness / 2 + 4;
            resized.Inflate(padding, padding);
            _selectedAnnotation.ResizeFrom(_beforeEdit, resized);
            return;
        }
        var frame = _beforeEdit.SelectionBounds;
        var left = frame.Left;
        var right = frame.Right;
        var top = frame.Top;
        var bottom = frame.Bottom;
        var minimumSize = _beforeEdit.Thickness + 9;

        if (_handle is 0 or 6 or 7)
        {
            left = Math.Min(left + delta.X, right - minimumSize);
        }
        if (_handle is 2 or 3 or 4)
        {
            right = Math.Max(right + delta.X, left + minimumSize);
        }
        if (_handle is 0 or 1 or 2)
        {
            top = Math.Min(top + delta.Y, bottom - minimumSize);
        }
        if (_handle is 4 or 5 or 6)
        {
            bottom = Math.Max(bottom + delta.Y, top + minimumSize);
        }

        _selectedAnnotation.ResizeFrom(_beforeEdit, new Rect(new Point(left, top), new Point(right, bottom)));
    }

    internal void SetTool(AnnotationTool tool)
    {
        Tool = tool;
        _selectedAnnotation = null;
        Render();
    }

    internal void SetColor(Color color)
    {
        Color = color;
        if (_selectedAnnotation is { } annotation && annotation.Color != color)
        {
            var previous = annotation.Color;
            annotation.Color = color;
            _history.Record(() => annotation.Color = previous, () => annotation.Color = color);
        }

        Render();
    }

    internal void SetThickness(double thickness)
    {
        Thickness = thickness;
        if (_selectedAnnotation is { } annotation && annotation.Thickness != thickness)
        {
            var previous = annotation.Thickness;
            annotation.Thickness = thickness;
            _history.Record(() => annotation.Thickness = previous, () => annotation.Thickness = thickness);
        }

        Render();
    }

    internal void Undo()
    {
        _history.Undo();
        _selectedAnnotation = null;
        Render();
    }

    internal void Redo()
    {
        _history.Redo();
        _selectedAnnotation = null;
        Render();
    }

    internal void DeleteSelected()
    {
        if (_selectedAnnotation is not { } annotation)
        {
            return;
        }

        var index = _annotations.IndexOf(annotation);
        if (index < 0)
        {
            return;
        }

        _annotations.RemoveAt(index);
        _history.Record(() => _annotations.Insert(index, annotation), () => _annotations.Remove(annotation));
        _selectedAnnotation = null;
        Render();
    }

    internal void RenderAnnotations(DrawingContext dc, bool showSelection)
    {
        // 标注保存屏幕坐标；预览和导出统一转换到当前选区坐标。
        dc.PushTransform(new TranslateTransform(-_selection.X, -_selection.Y));
        foreach (var annotation in _annotations)
        {
            annotation.Render(dc, _screen, _virtualScreen);
        }

        _draft?.Render(dc, _screen, _virtualScreen);
        if (showSelection && _selectedAnnotation is { } selected && _annotations.Contains(selected))
        {
            var bounds = selected.SelectionBounds;
            var pen = new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(null, pen, bounds);
            var handlePen = new Pen(Brushes.DodgerBlue, 1);
            foreach (var center in selected.HandleCenters())
            {
                dc.DrawRectangle(Brushes.White, handlePen, new Rect(center.X - 4, center.Y - 4, 8, 8));
            }
        }

        dc.Pop();
    }

    internal bool CanCancelInitialRegion =>
        _regionCapture && !_hasSelection && !_outputInProgress && _mode == DragMode.None;

    internal void CancelInitialRegion()
    {
        if (CanCancelInitialRegion) Cancel();
    }

    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (_outputInProgress || _mode != DragMode.None)
        {
            if (key == Key.Escape && !_outputInProgress)
            {
                Cancel();
            }
            return true;
        }

        if (key == Key.Escape)
        {
            Cancel();
        }
        else if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            Copy();
        }
        else if (key == Key.S && modifiers == ModifierKeys.Control)
        {
            Save();
        }
        else if (key == Key.Z && modifiers == ModifierKeys.Control)
        {
            Undo();
        }
        else if ((key == Key.Y && modifiers == ModifierKeys.Control) ||
                 (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            Redo();
        }
        else if (key == Key.Delete && modifiers == ModifierKeys.None)
        {
            DeleteSelected();
        }
        else
        {
            return false;
        }

        return true;
    }

    private BitmapSource ComposeImage()
    {
        var crop = new CroppedBitmap(_screen, new Int32Rect(
            _selection.X - _virtualScreen.X, _selection.Y - _virtualScreen.Y,
            _selection.Width, _selection.Height));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(crop, new Rect(0, 0, _selection.Width, _selection.Height));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, _selection.Width, _selection.Height)));
            RenderAnnotations(dc, false);
            dc.Pop();
        }

        var image = new RenderTargetBitmap(_selection.Width, _selection.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    internal void RecognizeText() => ShowRecognition(() => new OcrResultWindow(ComposeImage));

    internal void RecognizeCodes() => ShowRecognition(() => new QrResultWindow(ComposeImage()));

    private void ShowRecognition(Func<Window> create)
    {
        if (!_hasSelection || _outputInProgress || _mode != DragMode.None) return;
        _outputInProgress = true;
        try
        {
            var result = create();
            result.Owner = OutputOwner;
            result.ShowDialog();
        }
        catch (Exception ex) { ShowOutputError($"无法打开识别窗口：{ex.Message}"); }
        finally
        {
            _outputInProgress = false;
            if (!_closed) { ActivateInitial(); Render(); }
        }
    }

    private void CaptureSelectedWindow()
    {
        var target = _targetWindow!;
        var captured = ScreenCapture.CaptureWindow(target);
        // 原屏幕由 BitBlt 在覆盖层显示前抓取；PrintWindow 失败时保留原冻结区域。
        var frozen = new CroppedBitmap(_screen, new Int32Rect(_selection.X - _virtualScreen.X,
            _selection.Y - _virtualScreen.Y, _selection.Width, _selection.Height));
        var capturedBlack = false;
        if (captured is not null)
        {
            var crop = new CroppedBitmap(captured, new Int32Rect(_selection.X - target.Bounds.X,
                _selection.Y - target.Bounds.Y, _selection.Width, _selection.Height));
            capturedBlack = ScreenCapture.IsNearlyBlack(crop);
        }
        if (captured is null || capturedBlack)
        {
            var frozenBlack = ScreenCapture.IsNearlyBlack(frozen);
            if (capturedBlack || frozenBlack)
            {
                MessageBox.Show(_windows[0], frozenBlack
                    ? "窗口截图及屏幕替代区域疑似全黑。可能是窗口保护、硬件加速或画面本身为黑色，无法确认已截取有效内容；请检查选区，或取消后重新框选。"
                    : "窗口专用抓取返回了疑似黑图，已保留屏幕上可见的冻结区域作为替代，请检查内容。被其他窗口遮挡的部分不会被还原。",
                    "EasyShot 截图提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                ActivateInitial();
            }
            return;
        }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(_screen, new Rect(0, 0, _screen.PixelWidth, _screen.PixelHeight));
            dc.PushClip(new RectangleGeometry(new Rect(_selection.X - _virtualScreen.X,
                _selection.Y - _virtualScreen.Y, _selection.Width, _selection.Height)));
            dc.DrawImage(captured, new Rect(target.Bounds.X - _virtualScreen.X,
                target.Bounds.Y - _virtualScreen.Y, target.Bounds.Width, target.Bounds.Height));
            dc.Pop();
        }
        var screen = new RenderTargetBitmap(_screen.PixelWidth, _screen.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        screen.Render(visual);
        screen.Freeze();
        _screen = screen;
        foreach (var window in _windows) window.SetScreen(screen, _virtualScreen);
    }

    internal void Copy()
    {
        if (!_hasSelection || _outputInProgress)
        {
            return;
        }

        _outputInProgress = true;
        try
        {
            var image = ComposeImage();
            ScreenshotOutput.Copy(image);
            Cancel();
        }
        catch (Exception ex)
        {
            ShowOutputError($"复制截图失败，选区已保留。\n{ex.Message}");
        }
        finally
        {
            _outputInProgress = false;
        }
    }

    internal void Pin()
    {
        if (!_hasSelection || _outputInProgress) return;
        try
        {
            var image = ComposeImage();
            var pin = new PinWindow(image, _selection);
            pin.Show();
            Cancel();
        }
        catch (Exception ex)
        {
            if (!_closed) ShowOutputError($"贴图失败：{ex.Message}");
            else MessageBox.Show($"贴图失败：{ex.Message}", "EasyShot");
        }
    }

    internal void Save()
    {
        if (!_hasSelection || _outputInProgress)
        {
            return;
        }

        _outputInProgress = true;
        try
        {
            var config = AppConfig.Load(out _);
            var dialog = new SaveFileDialog
            {
                Title = "保存截图",
                Filter = "PNG 图片 (*.png)|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = $"EasyShot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                InitialDirectory = Directory.Exists(config.SaveDirectory)
                    ? config.SaveDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (dialog.ShowDialog(OutputOwner) != true)
            {
                return;
            }

            var image = ComposeImage();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(dialog.FileName))
            {
                encoder.Save(file);
            }

            if (config.CopyOnSave)
            {
                try { ScreenshotOutput.Copy(image); }
                catch (Exception ex)
                {
                    ShowOutputError($"截图已保存至：{dialog.FileName}\n但复制到剪贴板失败，文件不受影响；选区已保留。\n{ex.Message}");
                    return;
                }
            }
            Cancel();
        }
        catch (Exception ex)
        {
            ShowOutputError($"保存截图失败，选区已保留。\n{ex.Message}");
        }
        finally
        {
            _outputInProgress = false;
            if (!_closed)
            {
                ActivateInitial();
                // 激活全屏覆盖层后，重新将工具栏置于其上，避免对话框关闭后被遮住。
                Render();
            }
        }
    }

    private void ShowOutputError(string message) =>
        MessageBox.Show(OutputOwner, message, "EasyShot",
            MessageBoxButton.OK, MessageBoxImage.Error);

    private Window OutputOwner => _longImage ? _windows[0] : _toolbar ?? (Window)_windows[0];

    private void MoveTo(Point physical)
    {
        var dx = (int)Math.Round(physical.X - _dragStart.X);
        var dy = (int)Math.Round(physical.Y - _dragStart.Y);

        var x = Math.Clamp(_dragOrigin.X + dx, _virtualScreen.X, _virtualScreen.X + _virtualScreen.Width - _dragOrigin.Width);
        var y = Math.Clamp(_dragOrigin.Y + dy, _virtualScreen.Y, _virtualScreen.Y + _virtualScreen.Height - _dragOrigin.Height);

        _selection = new Int32Rect(x, y, _dragOrigin.Width, _dragOrigin.Height);
    }

    private void ResizeTo(Point physical)
    {
        var dx = (int)Math.Round(physical.X - _dragStart.X);
        var dy = (int)Math.Round(physical.Y - _dragStart.Y);

        var left = _dragOrigin.X;
        var top = _dragOrigin.Y;
        var right = _dragOrigin.X + _dragOrigin.Width;
        var bottom = _dragOrigin.Y + _dragOrigin.Height;

        switch (_handle)
        {
            case 0:
                left += dx;
                top += dy;
                break;
            case 1:
                top += dy;
                break;
            case 2:
                right += dx;
                top += dy;
                break;
            case 3:
                right += dx;
                break;
            case 4:
                right += dx;
                bottom += dy;
                break;
            case 5:
                bottom += dy;
                break;
            case 6:
                left += dx;
                bottom += dy;
                break;
            case 7:
                left += dx;
                break;
        }

        var leftLimit = _virtualScreen.X;
        var topLimit = _virtualScreen.Y;
        var rightLimit = _virtualScreen.X + _virtualScreen.Width;
        var bottomLimit = _virtualScreen.Y + _virtualScreen.Height;

        left = Math.Max(leftLimit, Math.Min(left, right - MinSelectionSize));
        top = Math.Max(topLimit, Math.Min(top, bottom - MinSelectionSize));
        right = Math.Min(rightLimit, Math.Max(right, left + MinSelectionSize));
        bottom = Math.Min(bottomLimit, Math.Max(bottom, top + MinSelectionSize));

        _selection = new Int32Rect(left, top, right - left, bottom - top);
    }

    private Int32Rect NormalizeRect(Point a, Point b)
    {
        var x1 = (int)Math.Round(Math.Min(a.X, b.X));
        var y1 = (int)Math.Round(Math.Min(a.Y, b.Y));
        var x2 = (int)Math.Round(Math.Max(a.X, b.X));
        var y2 = (int)Math.Round(Math.Max(a.Y, b.Y));

        x1 = Math.Clamp(x1, _virtualScreen.X, _virtualScreen.X + _virtualScreen.Width);
        y1 = Math.Clamp(y1, _virtualScreen.Y, _virtualScreen.Y + _virtualScreen.Height);
        x2 = Math.Clamp(x2, _virtualScreen.X, _virtualScreen.X + _virtualScreen.Width);
        y2 = Math.Clamp(y2, _virtualScreen.Y, _virtualScreen.Y + _virtualScreen.Height);

        return new Int32Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    internal Cursor GetCursor(Point physical)
    {
        var handle = -1;
        if (_mode is DragMode.Resizing or DragMode.ResizingAnnotation)
        {
            handle = _handle;
        }
        else if (_mode == DragMode.None)
        {
            handle = HitAnnotationHandle(physical);
            if (handle < 0)
            {
                handle = HitHandle(physical);
            }
        }

        return handle switch
        {
            0 or 4 => Cursors.SizeNWSE,
            2 or 6 => Cursors.SizeNESW,
            1 or 5 => Cursors.SizeNS,
            3 or 7 => Cursors.SizeWE,
            _ when _mode is DragMode.Moving or DragMode.MovingAnnotation => Cursors.SizeAll,
            _ when _hasSelection && !Contains(_selection, physical) => Cursors.No,
            _ when _hasSelection && _mode == DragMode.Drawing => Cursors.Cross,
            _ when _hasSelection && _annotations.Any(a => a.HitTest(physical)) => Cursors.SizeAll,
            _ when _hasSelection && Tool == AnnotationTool.Text => Cursors.IBeam,
            _ when _hasSelection && Tool == AnnotationTool.Number => Cursors.Hand,
            _ when _hasSelection && Tool is AnnotationTool.Rectangle or AnnotationTool.Arrow or AnnotationTool.Pen or AnnotationTool.Mosaic => Cursors.Cross,
            _ when _hasSelection => Cursors.SizeAll,
            _ => Cursors.Cross
        };
    }

    private int HitAnnotationHandle(Point physical)
    {
        if (!_hasSelection || !Contains(_selection, physical) ||
            _selectedAnnotation is not { } selected || !_annotations.Contains(selected))
        {
            return -1;
        }

        var centers = selected.HandleCenters();
        for (var i = 0; i < centers.Length; i++)
        {
            if (Math.Abs(physical.X - centers[i].X) <= 6 && Math.Abs(physical.Y - centers[i].Y) <= 6)
            {
                return i;
            }
        }

        return -1;
    }

    private int HitHandle(Point physical)
    {
        if (!_hasSelection)
        {
            return -1;
        }

        const int tolerance = 6;
        var left = (double)_selection.X;
        var top = _selection.Y;
        var right = _selection.X + _selection.Width;
        var bottom = _selection.Y + _selection.Height;
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;

        var centers = new[]
        {
            new Point(left, top),
            new Point(centerX, top),
            new Point(right, top),
            new Point(right, centerY),
            new Point(right, bottom),
            new Point(centerX, bottom),
            new Point(left, bottom),
            new Point(left, centerY)
        };

        for (var i = 0; i < centers.Length; i++)
        {
            if (Math.Abs(physical.X - centers[i].X) <= tolerance && Math.Abs(physical.Y - centers[i].Y) <= tolerance)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Contains(Int32Rect rect, Point point) =>
        point.X >= rect.X && point.X <= rect.X + rect.Width &&
        point.Y >= rect.Y && point.Y <= rect.Y + rect.Height;

    private void Render()
    {
        if (_closed)
        {
            return;
        }

        Int32Rect? selection = _hasSelection || _mode == DragMode.Creating || _windowPicking ? _selection : null;
        foreach (var window in _windows)
        {
            window.UpdateVisual(selection);
        }

        if (_hasSelection && _toolbar is not null)
        {
            _toolbar.UpdateState();
            if (!_longImage) ShowToolbar();
        }
    }

    private void ShowToolbar()
    {
        if (_toolbar is null)
        {
            _toolbar = new ToolbarWindow(this);
            _toolbar.Show();
        }

        var monitor = _monitors.OrderByDescending(m =>
        {
            var intersection = Rect.Intersect(new Rect(m.Bounds.X, m.Bounds.Y, m.Bounds.Width, m.Bounds.Height),
                new Rect(_selection.X, _selection.Y, _selection.Width, _selection.Height));
            return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
        }).First();
        var scale = _windows.First(w => w.Monitor == monitor).Scale;
        _toolbar.Place(_selection, monitor, scale);
    }

    private void HideToolbar()
    {
        _toolbar?.Hide();
    }

    internal void Cancel()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _toolbar?.Close();
        _toolbar = null;

        foreach (var window in _windows.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
        Current = null;
    }

    private enum DragMode
    {
        None,
        PendingSelection,
        Creating,
        Moving,
        Resizing,
        Drawing,
        MovingAnnotation,
        ResizingAnnotation
    }
}
