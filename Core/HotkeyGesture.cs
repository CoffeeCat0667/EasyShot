using System.Text;

namespace EasyShot.Core;

internal sealed class HotkeyGesture : IEquatable<HotkeyGesture>
{
    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PrintScreen"] = 0x2C,
        ["Pause"] = 0x13,
        ["ScrollLock"] = 0x91,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Backspace"] = 0x08,
        ["Tab"] = 0x09,
        ["Space"] = 0x20
    };

    private HotkeyGesture(uint modifiers, uint virtualKey, string keyName)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        Display = BuildDisplay(modifiers, keyName);
    }

    public uint Modifiers { get; }

    public uint VirtualKey { get; }

    public string Display { get; }

    public static bool TryParse(string text, out HotkeyGesture? gesture)
    {
        gesture = null;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        uint modifiers = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.ModControl;
                    break;
                case "alt":
                    modifiers |= NativeMethods.ModAlt;
                    break;
                case "shift":
                    modifiers |= NativeMethods.ModShift;
                    break;
                case "win":
                    modifiers |= NativeMethods.ModWin;
                    break;
                default:
                    return false;
            }
        }

        var keyToken = parts[^1];
        if (!TryParseKey(keyToken, out var virtualKey, out var keyName))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey, keyName);
        return true;
    }

    private static bool TryParseKey(string token, out uint virtualKey, out string keyName)
    {
        virtualKey = 0;
        keyName = string.Empty;

        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = c;
                keyName = c.ToString();
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = c;
                keyName = c.ToString();
                return true;
            }

            return false;
        }

        if (token.Length is 2 or 3 && (token[0] is 'F' or 'f') && int.TryParse(token[1..], out var fn) && fn is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + fn - 1);
            keyName = $"F{fn}";
            return true;
        }

        foreach (var pair in NamedKeys)
        {
            if (string.Equals(pair.Key, token, StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = pair.Value;
                keyName = pair.Key;
                return true;
            }
        }

        return false;
    }

    private static string BuildDisplay(uint modifiers, string keyName)
    {
        var sb = new StringBuilder();
        if ((modifiers & NativeMethods.ModControl) != 0)
        {
            sb.Append("Ctrl+");
        }

        if ((modifiers & NativeMethods.ModAlt) != 0)
        {
            sb.Append("Alt+");
        }

        if ((modifiers & NativeMethods.ModShift) != 0)
        {
            sb.Append("Shift+");
        }

        if ((modifiers & NativeMethods.ModWin) != 0)
        {
            sb.Append("Win+");
        }

        sb.Append(keyName);
        return sb.ToString();
    }

    public bool Equals(HotkeyGesture? other) =>
        other is not null && other.Modifiers == Modifiers && other.VirtualKey == VirtualKey;

    public override bool Equals(object? obj) => Equals(obj as HotkeyGesture);

    public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);
}
