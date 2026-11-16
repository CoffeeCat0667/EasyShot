using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using Microsoft.Win32;
using EasyShot.Config;
using EasyShot.Core;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using EasyShot.UI;

namespace EasyShot.Settings;

internal sealed class SettingsWindow : Window
{
    private readonly Func<AppConfig, string?> _apply;
    private readonly Dictionary<string, TextBox> _keys = new();
    private readonly TextBox _directory;
    private readonly CheckBox _autostart;
    private readonly CheckBox _copy;
    private readonly ComboBox _borderColor;
    private readonly ComboBox _pinColor;
    private readonly TextBox _pinColorHex;

    public SettingsWindow(AppConfig config, Func<AppConfig, string?> apply)
    {
        _apply = apply;
        Title = "EasyShot 设置";
        Width = 620;
        Height = Math.Min(760, SystemParameters.WorkArea.Height - 24);
        MinWidth = 500;
        MinHeight = 500;
        Background = (Brush)FindResource("ShotBackground");
        Foreground = (Brush)FindResource("ShotText");
        FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), FindResource("ShotScrollBar"));
        var layout = new DockPanel { Margin = new Thickness(20) };
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        var note = new TextBlock { Text = "更改将在保存后生效", Foreground = (Brush)FindResource("ShotMuted"), VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(note);
        var save = new Button { Content = "保存设置", Style = (Style)FindResource("ShotButton"), Background = new SolidColorBrush(Color.FromRgb(44, 104, 183)),
            HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 112 };
        save.Click += Save;
        footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        var panel = new StackPanel();
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) });
        Content = layout;
        panel.Children.Add(new TextBlock { Text = "偏好设置", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "EasyShot · 让截图更顺手", Foreground = (Brush)FindResource("ShotMuted"), Margin = new Thickness(0, 3, 0, 12) });
        var shortcuts = Section(panel, "快捷键", "点击输入框后按组合键；清空即不绑定。");
        foreach (var (key, label) in new[] { ("region", "区域截图（含窗口）"), ("fullscreen", "全屏截图") })
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Width = 152, VerticalAlignment = VerticalAlignment.Center });
            var box = new TextBox { Style = (Style)FindResource("ShotTextBox"), Padding = new Thickness(9, 6, 9, 6), Text = config.Hotkeys.GetValueOrDefault(key) ?? "", IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"), ToolTip = "按组合键设置，Backspace 清除" };
            var clear = new Button { Content = ShotIconExtension.Create("Close"), Style = (Style)FindResource("ShotButton"), ToolTip = "清除快捷键", Margin = new Thickness(7, 0, 0, 0), Padding = new Thickness(8) };
            DockPanel.SetDock(clear, Dock.Right);
            clear.Click += (_, _) => box.Text = "";
            row.Children.Add(clear);
            box.PreviewKeyDown += CaptureKey;
            row.Children.Add(box);
            shortcuts.Children.Add(row);
            _keys[key] = box;
        }
        var appearance = Section(panel, "截图外观", "框线颜色独立于标注颜色，用于选区、窗口高亮和长截图。");
        var colorRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        colorRow.Children.Add(new TextBlock { Text = "截图框线颜色", Width = 152, VerticalAlignment = VerticalAlignment.Center });
        _borderColor = new ComboBox { Style = (Style)FindResource("ShotCombo"), MinWidth = 180 };
        foreach (var (label, color) in new[] { ("珊瑚红", "#FF3B30"), ("天空蓝", "#007AFF"), ("薄荷绿", "#34C759"),
                     ("琥珀橙", "#FF9500"), ("明黄色", "#FFCC00"), ("紫罗兰", "#AF7AFF"), ("纯白", "#FFFFFF") })
            AddBorderColor(label, color);
        var selected = _borderColor.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            ((SolidColorBrush)new BrushConverter().ConvertFromString(item.Tag.ToString()!)!).Color == config.GetSelectionBorderColor());
        if (selected is null)
        {
            AddBorderColor("当前颜色", config.GetSelectionBorderColor().ToString());
            selected = (ComboBoxItem)_borderColor.Items[^1];
        }
        _borderColor.SelectedItem = selected;
        colorRow.Children.Add(_borderColor);
        appearance.Children.Add(colorRow);

        var pin = Section(panel, "贴图外观", "贴图线框独立配色，无缩放手柄；可选择预设色或输入 #RRGGBB。");
        var pinRow = new DockPanel();
        pinRow.Children.Add(new TextBlock { Text = "贴图框线颜色", Width = 152, VerticalAlignment = VerticalAlignment.Center });
        _pinColor = new ComboBox { Style = (Style)FindResource("ShotCombo"), Width = 130, Margin = new Thickness(0, 0, 8, 0) };
        _pinColorHex = new TextBox { Style = (Style)FindResource("ShotTextBox"), Text = "#" + config.GetPinBorderColor().ToString()[3..],
            FontFamily = new FontFamily("Consolas"), MinWidth = 105, MaxLength = 7, ToolTip = "十六进制 RGB 颜色，例如 #007AFF" };
        foreach (var (label, color) in new[] { ("天空蓝", "#007AFF"), ("珊瑚红", "#FF3B30"), ("薄荷绿", "#34C759"),
                     ("琥珀橙", "#FF9500"), ("明黄色", "#FFCC00"), ("紫罗兰", "#AF7AFF"), ("纯白", "#FFFFFF") })
        {
            var sample = new StackPanel { Orientation = Orientation.Horizontal };
            sample.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = (Brush)new BrushConverter().ConvertFromString(color)!, Margin = new Thickness(0, 0, 8, 0) });
            sample.Children.Add(new TextBlock { Text = label });
            _pinColor.Items.Add(new ComboBoxItem { Content = sample, Tag = color });
        }
        _pinColor.SelectedItem = _pinColor.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag.ToString(), _pinColorHex.Text, StringComparison.OrdinalIgnoreCase));
        _pinColor.SelectionChanged += (_, _) =>
        {
            if (_pinColor.SelectedItem is ComboBoxItem item) _pinColorHex.Text = item.Tag.ToString()!;
        };
        pinRow.Children.Add(_pinColor);
        pinRow.Children.Add(_pinColorHex);
        pin.Children.Add(pinRow);

        var output = Section(panel, "保存与启动", "截图保持本地处理，偏好设置随程序保存。");
        output.Children.Add(new TextBlock { Text = "保存对话框初始目录", Foreground = (Brush)FindResource("ShotMuted"), Margin = new Thickness(0, 0, 0, 7) });
        var directoryRow = new DockPanel();
        _directory = new TextBox { Text = config.SaveDirectory, Style = (Style)FindResource("ShotTextBox") };
        var browse = new Button { Content = ShotIconExtension.Create("Folder"), Style = (Style)FindResource("ShotButton"), ToolTip = "选择目录", Margin = new Thickness(7, 0, 0, 0), Padding = new Thickness(9) };
        DockPanel.SetDock(browse, Dock.Right);
        directoryRow.Children.Add(browse);
        directoryRow.Children.Add(_directory);
        output.Children.Add(directoryRow);
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "选择保存对话框的初始目录" };
            if (Directory.Exists(_directory.Text)) dialog.InitialDirectory = _directory.Text;
            if (dialog.ShowDialog(this) == true) _directory.Text = dialog.FolderName;
        };
        _autostart = new CheckBox { Style = (Style)FindResource("ShotSwitch"), Content = "登录 Windows 后启动 EasyShot", IsChecked = config.AutoStart, Margin = new Thickness(0, 12, 0, 0) };
        _copy = new CheckBox { Style = (Style)FindResource("ShotSwitch"), Content = "保存截图时同时复制到剪贴板", IsChecked = config.CopyOnSave, Margin = new Thickness(0, 12, 0, 2) };
        output.Children.Add(_autostart);
        output.Children.Add(_copy);
    }

    private StackPanel Section(StackPanel parent, string title, string subtitle)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, FontSize = 11,
            Foreground = (Brush)FindResource("ShotMuted"), Margin = new Thickness(0, 3, 0, 8) });
        parent.Children.Add(new Border { Background = (Brush)FindResource("ShotSurface"), BorderBrush = (Brush)FindResource("ShotBorder"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(13), Margin = new Thickness(0, 0, 0, 9), Child = body });
        return body;
    }

    private void AddBorderColor(string label, string color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = (Brush)new BrushConverter().ConvertFromString(color)!, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(new TextBlock { Text = label });
        _borderColor.Items.Add(new ComboBoxItem { Tag = color, Content = row });
    }

    private static void CaptureKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (key == Key.Back) { ((TextBox)sender).Text = ""; return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        var name = key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString();
        parts.Add(name);
        var text = string.Join("+", parts);
        if (HotkeyGesture.TryParse(text, out var gesture)) ((TextBox)sender).Text = gesture!.Display;
        else MessageBox.Show("该按键暂不支持，请使用字母、数字、F1–F24 或常用功能键。", "EasyShot");
    }

    internal void CaptureRegisteredHotkey(HotkeyGesture gesture)
    {
        var box = _keys.Values.FirstOrDefault(b => b.IsKeyboardFocusWithin && b.IsEnabled);
        if (box is not null) box.Text = gesture.Display;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        var next = new AppConfig { SaveDirectory = _directory.Text.Trim(), AutoStart = _autostart.IsChecked == true, CopyOnSave = _copy.IsChecked == true,
            SelectionBorderColor = ((ComboBoxItem)_borderColor.SelectedItem).Tag.ToString()!,
            PinBorderColor = _pinColorHex.Text.Trim().ToUpperInvariant(),
            Hotkeys = _keys.ToDictionary(x => x.Key, x => string.IsNullOrWhiteSpace(x.Value.Text) ? null : x.Value.Text) };
        if (string.IsNullOrWhiteSpace(next.SaveDirectory)) { MessageBox.Show(this, "请输入保存目录。"); return; }
        if (next.PinBorderColor.Length != 7 || next.PinBorderColor[0] != '#' ||
            !int.TryParse(next.PinBorderColor[1..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        { MessageBox.Show(this, "贴图框线颜色应为 #RRGGBB，例如 #007AFF。", "颜色无效"); return; }
        try
        {
            if (!Path.IsPathFullyQualified(next.SaveDirectory)) { MessageBox.Show(this, "保存目录必须是完整路径。"); return; }
            next.SaveDirectory = Path.GetFullPath(next.SaveDirectory);
        }
        catch (Exception ex) { MessageBox.Show(this, $"保存目录无效：{ex.Message}"); return; }
        var gestures = new HashSet<HotkeyGesture>();
        foreach (var entry in next.Hotkeys)
        {
            if (entry.Value is null) continue;
            if (!HotkeyGesture.TryParse(entry.Value, out var gesture) || gesture is null || !gestures.Add(gesture))
            { MessageBox.Show(this, "快捷键无效或重复。"); return; }
        }
        var error = _apply(next);
        if (error is not null) { MessageBox.Show(this, error, "设置失败"); return; }
        Close();
    }
}
