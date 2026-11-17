using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Media;

namespace EasyShot.Config;

internal sealed class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Dictionary<string, string?> Hotkeys { get; set; } = DefaultHotkeys();

    public string SaveDirectory { get; set; } = DefaultSaveDirectory();

    public bool AutoStart { get; set; }

    public bool CopyOnSave { get; set; }
    public string SelectionBorderColor { get; set; } = "#FF3B30";
    public string PinBorderColor { get; set; } = "#007AFF";

    public Color GetSelectionBorderColor()
    {
        try { return (Color)ColorConverter.ConvertFromString(SelectionBorderColor); }
        catch { return Color.FromRgb(255, 59, 48); }
    }

    public Color GetPinBorderColor()
    {
        try { return (Color)ColorConverter.ConvertFromString(PinBorderColor); }
        catch { return Color.FromRgb(0, 122, 255); }
    }

    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyShot");
    public static string FilePath => Path.Combine(DirectoryPath, "config.json");
    private static string LegacyFilePath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppConfig Load(out bool needsWriteBack)
    {
        needsWriteBack = false;

        if (!File.Exists(FilePath))
        {
            needsWriteBack = true;
            // Preserve existing portable settings when upgrading to Program Files.
            if (!string.Equals(FilePath, LegacyFilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(LegacyFilePath))
            {
                try
                {
                    var legacy = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(LegacyFilePath), JsonOptions);
                    if (legacy is not null)
                    {
                        legacy.Normalize();
                        return legacy;
                    }
                }
                catch
                {
                    // Invalid legacy config falls back to defaults below.
                }
            }
            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (loaded is null)
            {
                needsWriteBack = true;
                return new AppConfig();
            }

            loaded.Normalize();
            return loaded;
        }
        catch
        {
            needsWriteBack = true;
            return new AppConfig();
        }
    }

    public bool TrySave(out string error)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"无法写入当前用户配置文件：{FilePath}\n请检查当前用户对 LocalAppData 目录的访问权限。\n\n{ex.Message}";
            return false;
        }
    }

    private void Normalize()
    {
        if (Hotkeys is null || Hotkeys.Count == 0)
        {
            Hotkeys = DefaultHotkeys();
        }
        else
        {
            foreach (var pair in DefaultHotkeys())
            {
                if (!Hotkeys.ContainsKey(pair.Key))
                {
                    Hotkeys[pair.Key] = pair.Value;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            SaveDirectory = DefaultSaveDirectory();
        }
    }

    private static Dictionary<string, string?> DefaultHotkeys() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["region"] = "Alt+S",
        ["fullscreen"] = null
    };

    private static string DefaultSaveDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "EasyShot");
}
