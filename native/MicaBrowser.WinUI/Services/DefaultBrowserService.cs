using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MicaBrowser.Services;

/// <summary>
/// Registers EXET as a per-user browser candidate so Windows can offer it in
/// Default apps. Windows deliberately does not allow an app to silently take
/// over http/https; the actual choice must be confirmed in the system UI.
/// </summary>
public static class DefaultBrowserService
{
    private const string AppName = "EXET";
    private const string CapabilitiesPath = @"Software\EXET\Capabilities";

    public static void EnsureRegistered()
    {
        try
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "EXET.exe");
            if (!File.Exists(exe)) return;

            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            if (classes is null) return;

            CreateProgId(classes, "EXET", "EXET HTML Document", exe);
            AddOpenWith(classes, "http", "EXET");
            AddOpenWith(classes, "https", "EXET");
            AddOpenWith(classes, ".html", "EXET");
            AddOpenWith(classes, ".htm", "EXET");

            using (var app = classes.CreateSubKey(@"Applications\EXET.exe"))
            {
                app?.SetValue(null, "EXET");
                using var command = app?.CreateSubKey(@"shell\open\command");
                command?.SetValue(null, $"\"{exe}\" \"%1\"");
            }

            using var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesPath);
            if (capabilities is not null)
            {
                capabilities.SetValue("ApplicationName", "EXET");
                capabilities.SetValue("ApplicationDescription", "EXET 浏览器");
                capabilities.SetValue("ApplicationIcon", exe + ",0");
                using var urls = capabilities.CreateSubKey("URLAssociations");
                urls?.SetValue("http", "EXET");
                urls?.SetValue("https", "EXET");
                using var files = capabilities.CreateSubKey("FileAssociations");
                files?.SetValue(".html", "EXET");
                files?.SetValue(".htm", "EXET");
            }

            using var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
            registered?.SetValue("EXET", CapabilitiesPath);
            Logger.Info("Default browser registration updated.");
        }
        catch (Exception ex)
        {
            Logger.Warn("Default browser registration failed.", ex);
        }
    }

    public static async Task OpenDefaultAppsAsync()
    {
        EnsureRegistered();

        // Windows' documented association UI, filtered to this app. It is
        // available on Windows 10/11 and avoids dumping the user on a generic
        // Settings search page.
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("1968106d-f3b5-44cf-890e-116fcb9ecef1"));
            if (type is not null &&
                Activator.CreateInstance(type) is IApplicationAssociationRegistrationUI ui)
            {
                ui.LaunchAdvancedAssociationUI(AppName);
                Logger.Info("Opened default apps association UI for EXET.");
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Association UI unavailable; falling back to Settings.", ex);
        }

        await Windows.System.Launcher.LaunchUriAsync(
            new Uri("ms-settings:defaultapps?registeredAppUser=EXET"));
    }

    private static void CreateProgId(RegistryKey classes, string progId, string description, string exe)
    {
        using var key = classes.CreateSubKey(progId);
        if (key is null) return;
        key.SetValue(null, description);
        key.SetValue("FriendlyTypeName", description);
        using (var icon = key.CreateSubKey("DefaultIcon"))
        {
            icon?.SetValue(null, exe + ",0");
        }
        using var command = key.CreateSubKey(@"shell\open\command");
        command?.SetValue(null, $"\"{exe}\" \"%1\"");
    }

    private static void AddOpenWith(RegistryKey classes, string extension, string progId)
    {
        using var key = classes.CreateSubKey(extension + @"\OpenWithProgids");
        key?.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
    }

    [ComImport]
    [Guid("1f76a169-f994-40ac-8fc8-0959e8874710")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationAssociationRegistrationUI
    {
        void LaunchAdvancedAssociationUI([MarshalAs(UnmanagedType.LPWStr)] string applicationName);
    }
}
