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

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppConfig Load(out bool needsWriteBack)
    {
        needsWriteBack = false;

        if (!File.Exists(FilePath))
        {
            needsWriteBack = true;
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"无法写入配置文件：{FilePath}\n请将 EasyShot.exe 放到可写目录后再运行。\n\n{ex.Message}";
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
