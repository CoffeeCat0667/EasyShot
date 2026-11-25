using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyShot.Ocr;

internal sealed class OcrResultWindow : Window
{
    private readonly BitmapSource _image;
    private readonly OcrEngine _engine = OcrEngine.Shared;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBox _text = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { Text = "正在准备识别…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 5, Visibility = Visibility.Collapsed };
    private readonly Button _copy = new() { Content = "复制", MinWidth = 88,
        ToolTip = "复制全部识别结果", IsEnabled = false };
    private CancellationTokenSource? _cancellation;
    private bool _busy;

    public OcrResultWindow(BitmapSource image)
    {
        _image = image;
        Title = "EasyShot 文字识别";
        Width = 1000;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)FindResource("ShotBackground");
        Foreground = (Brush)FindResource("ShotText");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Resources.Add(typeof(ScrollBar), FindResource("ShotScrollBar"));
        _status.Foreground = (Brush)FindResource("ShotMuted");
        _text.Style = (Style)FindResource("ShotTextBox");
        _text.VerticalContentAlignment = VerticalAlignment.Top;
        _preview.Source = image;
        _progress.Background = new SolidColorBrush(Color.FromRgb(53, 66, 88));
        _progress.Foreground = (Brush)FindResource("ShotAccent");

        var layout = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "文字识别", FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("ShotText") });
        header.Children.Add(_status);
        header.Children.Add(_progress);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        _copy.Style = (Style)FindResource("ShotButton");
        buttons.Children.Add(_copy);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);

        var main = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });
        var previewFrame = new Border { Background = (Brush)FindResource("ShotSurface"),
            BorderBrush = (Brush)FindResource("ShotBorder"), BorderThickness = new Thickness(1),
            Padding = new Thickness(8), Margin = new Thickness(0, 0, 12, 0), Child = _preview };
        Grid.SetColumn(previewFrame, 0);
        main.Children.Add(previewFrame);
        Grid.SetColumn(_text, 1);
        main.Children.Add(_text);
        layout.Children.Add(main);
        Content = layout;

        _copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_text.Text); }
            catch (Exception ex) { MessageBox.Show(this, $"复制文字失败：{ex.Message}", "EasyShot"); }
        };
        Loaded += async (_, _) => await Recognize();
        Closing += (_, _) => _cancellation?.Cancel();
    }

    private async Task Recognize()
    {
        if (_busy) return;
        _busy = true;
        _progress.Visibility = Visibility.Visible;
        _text.IsReadOnly = true;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        try
        {
            var progress = new Progress<string>(message =>
            {
                if (!cancellation.IsCancellationRequested && IsVisible) _status.Text = message;
            });
            var result = await Task.Run(() => _engine.Recognize(_image, progress, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsVisible) return;
            _text.Text = result;
            _status.Text = result.Length == 0 ? "未识别到文字，可关闭后调整选区。" : "识别完成，文字可编辑。";
        }
        catch (OperationCanceledException) { if (IsVisible) _status.Text = "识别已取消。"; }
        catch (Exception ex) { if (IsVisible) _status.Text = $"识别失败：{ex.Message}"; }
        finally
        {
            _cancellation = null;
            _busy = false;
            _copy.IsEnabled = !string.IsNullOrEmpty(_text.Text);
            _text.IsReadOnly = false;
            _progress.Visibility = Visibility.Collapsed;
        }
    }
}
