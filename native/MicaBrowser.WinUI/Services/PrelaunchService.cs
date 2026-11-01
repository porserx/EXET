using Microsoft.Win32;

namespace MicaBrowser.Services;

public static class PrelaunchService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EXET Prelaunch";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Sync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe)) return;
                key.SetValue(ValueName, $"\"{exe}\" --preload");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // Autostart is best-effort; the browser must still start.
        }
    }
}
