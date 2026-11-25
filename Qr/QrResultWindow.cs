using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyShot.Qr;

internal sealed class QrResultWindow : Window
{
    private readonly BitmapSource _image;
    private readonly StackPanel _results = new();
    private readonly TextBlock _status = new() { Text = "正在识别二维码和条形码…", Margin = new Thickness(0, 0, 0, 8) };

    public QrResultWindow(BitmapSource image)
    {
        _image = image;
        Title = "EasyShot 二维码 / 条形码识别";
        Width = 620;
        Height = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)FindResource("ShotBackground");
        Foreground = (Brush)FindResource("ShotText");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Resources.Add(typeof(ScrollBar), FindResource("ShotScrollBar"));
        _status.Foreground = (Brush)FindResource("ShotMuted");
        _status.Margin = new Thickness(0, 0, 0, 12);
        var panel = new DockPanel { Margin = new Thickness(16) };
        var heading = new TextBlock { Text = "识别结果", FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("ShotText"), Margin = new Thickness(0, 0, 0, 3) };
        var top = new StackPanel();
        top.Children.Add(heading);
        top.Children.Add(_status);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(new ScrollViewer { Content = _results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = panel;
        Loaded += async (_, _) =>
        {
            try
            {
                var codes = await Task.Run(() => QrDecoder.Decode(_image));
                if (!IsVisible) return;
                _status.Text = codes.Count == 0 ? "未识别到二维码或条形码。" : $"识别到 {codes.Count} 个码：";
                for (var i = 0; i < codes.Count; i++)
                {
                    var code = codes[i];
                    var entry = new Border { Background = (Brush)FindResource("ShotSurface"),
                        BorderBrush = (Brush)FindResource("ShotBorder"), BorderThickness = new Thickness(1),
                        Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10) };
                    var entryPanel = new StackPanel();
                    var label = new TextBlock { Text = $"{i + 1}. {code.Format}", FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("ShotAccent"), Margin = new Thickness(0, 0, 0, 8) };
                    DockPanel.SetDock(label, Dock.Top);
                    entryPanel.Children.Add(label);
                    var value = new TextBox { Text = code.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                        Style = (Style)FindResource("ShotTextBox"),
                        VerticalContentAlignment = VerticalAlignment.Top, AcceptsReturn = true, MaxHeight = 160,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                    entryPanel.Children.Add(value);
                    var copy = new Button { Content = "复制", HorizontalAlignment = HorizontalAlignment.Right,
                        Style = (Style)FindResource("ShotButton"), Margin = new Thickness(0, 8, 0, 0) };
                    copy.Click += (_, _) =>
                    {
                        try { Clipboard.SetText(code.Text); }
                        catch (Exception ex) { MessageBox.Show(this, $"复制识别结果失败：{ex.Message}", "EasyShot"); }
                    };
                    entryPanel.Children.Add(copy);
                    entry.Child = entryPanel;
                    _results.Children.Add(entry);
                }
            }
            catch (Exception ex) { if (IsVisible) _status.Text = $"识别失败：{ex.Message}"; }
        };
    }
}
