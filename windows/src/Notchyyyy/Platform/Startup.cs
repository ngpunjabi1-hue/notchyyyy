namespace Notchyyyy.Platform;

/// <summary>
/// "Launch at login": the per-user startup list (HKCU\Software\Microsoft\Windows\CurrentVersion\Run), the same list
/// Settings › Apps › Startup shows. No administrator rights needed. On the Mac preview it only remembers the choice.
/// </summary>
public static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "Notchyyyy";
    private static bool _previewOn;

    /// The command Windows runs at login: this exe, quoted.
    public static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsOn
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return _previewOn;
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) is string v && v.Length > 0;
        }
    }

    public static void Set(bool on)
    {
        if (!OperatingSystem.IsWindows()) { _previewOn = on; return; }
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key, writable: true);
        if (on) k.SetValue(Name, Command);
        else if (k.GetValue(Name) is not null) k.DeleteValue(Name);
    }
}
