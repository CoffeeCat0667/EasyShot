using Microsoft.Win32;

namespace EasyShot.Core;

internal static class AutoStart
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValue("EasyShot") as string;
    }

    public static void Write(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, true)
            ?? throw new InvalidOperationException("无法打开开机自启注册表项。");
        if (command is null) key.DeleteValue("EasyShot", false);
        else key.SetValue("EasyShot", command);
    }

    public static string Command => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("无法获取程序路径。 ")}\"";
}
