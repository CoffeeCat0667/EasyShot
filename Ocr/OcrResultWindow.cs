using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyShot.Ocr;

internal sealed class OcrResultWindow : Window
{
    private readonly Func<BitmapSource> _snapshot;
    private readonly OcrEngine _engine = OcrEngine.Shared;
    private readonly TextBox _text = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 5 };
    private readonly Button _recognize = new() { Content = "重识别", Margin = new Thickness(4) };
    private readonly Button _copy = new() { Content = "复制", Margin = new Thickness(4) };
    private readonly Button _cancel = new() { Content = "取消识别", Margin = new Thickness(4) };
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private bool _closeRequested;

    public OcrResultWindow(Func<BitmapSource> snapshot)
    {
        _snapshot = snapshot;
        Title = "EasyShot 文字识别";
        Width = 650;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("ShotBackground");
        Foreground = (Brush)FindResource("ShotText");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Resources.Add(typeof(ScrollBar), FindResource("ShotScrollBar"));
        _status.Foreground = (Brush)FindResource("ShotMuted");
        _text.Style = (Style)FindResource("ShotTextBox");
        _text.VerticalContentAlignment = VerticalAlignment.Top;
        _text.MinHeight = 280;
        _progress.Background = new SolidColorBrush(Color.FromRgb(53, 66, 88));
        _progress.Foreground = (Brush)FindResource("ShotAccent");
        _recognize.Style = (Style)FindResource("ShotButton");
        _copy.Style = (Style)FindResource("ShotButton");
        _cancel.Style = (Style)FindResource("ShotButton");
        var layout = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel();
        var heading = new TextBlock { Text = "识别结果", FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("ShotText"), Margin = new Thickness(0, 0, 0, 2) };
        top.Children.Add(heading);
        top.Children.Add(_status);
        top.Children.Add(_progress);
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_copy);
        buttons.Children.Add(_recognize);
        buttons.Children.Add(_cancel);
        var close = new Button { Content = "关闭", Margin = new Thickness(4), Style = (Style)FindResource("ShotButton") };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(_text);
        Content = layout;
        _copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_text.Text); }
            catch (Exception ex) { MessageBox.Show(this, $"复制文字失败：{ex.Message}", "EasyShot"); }
        };
        _recognize.Click += async (_, _) => await Recognize();
        _cancel.Click += (_, _) => { _cancellation?.Cancel(); _status.Text = "正在取消，请稍候…"; };
        Loaded += async (_, _) => await Recognize();
        Closing += OnClosing;
    }

    private async Task Recognize()
    {
        if (_busy) return;
        _busy = true;
        _recognize.IsEnabled = false;
        _copy.IsEnabled = false;
        _cancel.IsEnabled = true;
        _progress.Visibility = Visibility.Visible;
        _text.IsReadOnly = true;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        try
        {
            var image = _snapshot();
            var progress = new Progress<string>(message => { if (!cancellation.IsCancellationRequested) _status.Text = message; });
            var result = await Task.Run(() => _engine.Recognize(image, progress, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            _text.Text = result;
            _status.Text = result.Length == 0 ? "未识别到文字，可重新识别或关闭后调整选区。" : "识别完成，文字可编辑。";
        }
        catch (OperationCanceledException) { _status.Text = "识别已取消。"; }
        catch (Exception ex) { _status.Text = $"识别失败：{ex.Message}"; }
        finally
        {
            _cancellation = null;
            _busy = false;
            _recognize.IsEnabled = true;
            _copy.IsEnabled = !string.IsNullOrEmpty(_text.Text);
            _cancel.IsEnabled = false;
            _text.IsReadOnly = false;
            _progress.Visibility = Visibility.Collapsed;
            if (_closeRequested) Close();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_busy) return;
        e.Cancel = true;
        _closeRequested = true;
        _cancellation?.Cancel();
        _status.Text = "正在结束识别，请稍候…";
    }
}
