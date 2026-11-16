using System.Windows;
using System.Windows.Controls;
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
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(_status, Dock.Top);
        panel.Children.Add(_status);
        var close = new Button { Content = "关闭", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 4, 12, 4) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
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
                    var entry = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                    entry.Children.Add(new TextBlock { Text = $"{i + 1}. {code.Format}" });
                    entry.Children.Add(new TextBox { Text = code.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                        AcceptsReturn = true, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                    var copy = new Button { Content = "复制", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
                    copy.Click += (_, _) =>
                    {
                        try { Clipboard.SetText(code.Text); }
                        catch (Exception ex) { MessageBox.Show(this, $"复制识别结果失败：{ex.Message}", "EasyShot"); }
                    };
                    entry.Children.Add(copy);
                    _results.Children.Add(entry);
                }
            }
            catch (Exception ex) { if (IsVisible) _status.Text = $"识别失败：{ex.Message}"; }
        };
    }
}
