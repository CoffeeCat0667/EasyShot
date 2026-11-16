using System.Windows;
using EasyShot.Config;
using EasyShot.Core;
using EasyShot.Overlay;
using EasyShot.Settings;
using EasyShot.Capture;
using EasyShot.Ocr;

namespace EasyShot;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private TrayIconService? _tray;
    private HotkeyManager? _hotkeys;
    private AppConfig _config = new();
    private SettingsWindow? _settings;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "EasyShot", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _singleInstance = SingleInstance.TryCreate(() => Dispatcher.Invoke(OverlaySession.Start));
        if (_singleInstance is null)
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        _config = AppConfig.Load(out var writeBack);
        if (writeBack && !_config.TrySave(out var configError))
        {
            MessageBox.Show(configError, "EasyShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _tray = new TrayIconService(OverlaySession.Start, OverlaySession.StartFullScreen, OpenSettings, ExitApplication);
        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += OnHotkeyPressed;
        _hotkeys.Captured += gesture => _settings?.CaptureRegisteredHotkey(gesture);
        RegisterConfiguredHotkeys();
        PreloadOcr();
    }

    private async void PreloadOcr()
    {
        try { await OcrEngine.Shared.StartLoading(); }
        catch (Exception ex)
        {
            if (!_exiting)
                MessageBox.Show($"OCR 模型预加载失败，截图功能仍可使用。\n{ex.Message}",
                    "EasyShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RegisterConfiguredHotkeys()
    {
        var registered = new Dictionary<string, string?>();
        var errors = new List<string>();
        foreach (var action in new[] { "region", "fullscreen" })
        {
            registered[action] = _config.Hotkeys.GetValueOrDefault(action);
            var error = _hotkeys!.Apply(registered, () => null);
            if (error is null) continue;
            registered.Remove(action);
            errors.Add(error);
        }
        if (errors.Count > 0) MessageBox.Show(string.Join("\n", errors), "EasyShot", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnHotkeyPressed(string action)
    {
        if (action == "region") OverlaySession.Start();
        else if (action == "fullscreen") OverlaySession.StartFullScreen();
    }

    private void OpenSettings()
    {
        if (_settings is not null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(_config, ApplySettings);
        _settings.Activated += (_, _) => _hotkeys!.Suspended = true;
        _settings.Deactivated += (_, _) => _hotkeys!.Suspended = false;
        _settings.Closed += (_, _) => { _settings = null; _hotkeys!.Suspended = false; };
        _settings.Show();
    }

    private string? ApplySettings(AppConfig next)
    {
        try
        {
            var error = _hotkeys!.Apply(next.Hotkeys, () =>
            {
                var previousCommand = AutoStart.Read();
                var command = next.AutoStart ? AutoStart.Command : null;
                var changed = previousCommand != command;
                if (changed) AutoStart.Write(command);
                if (next.TrySave(out var saveError)) return null;
                if (changed)
                {
                    try { AutoStart.Write(previousCommand); }
                    catch (Exception ex) { return $"{saveError}\n恢复自启设置失败：{ex.Message}"; }
                }
                return saveError;
            });
            if (error is not null) return error;
            _config = next;
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    private void ExitApplication()
    {
        OverlaySession.CloseCurrent();
        ScrollCaptureWindow.Current?.Close();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        OcrEngine.Shared.Dispose();
        base.OnExit(e);
    }
}
