using System.Diagnostics;
using Mountly.Core.Storage;
using Mountly.Windows;

namespace Mountly.App.Services;

/// <summary>Explorer menus, start-with-Windows and opening things in Explorer or the browser.</summary>
public static class DesktopIntegration
{
    public static string? ExePath => Environment.ProcessPath;

    /// <summary>Applies the Explorer and sign-in settings (both per-user, no administrator rights).</summary>
    public static void Apply(AppSettings settings)
    {
        if (!OperatingSystem.IsWindows() || ExePath is not { } exe) return;
        try
        {
            if (settings.ShellIntegration) ShellIntegration.Register(exe);
            else if (ShellIntegration.IsRegistered()) ShellIntegration.Unregister();
            Autostart.Set(settings.StartWithWindows, exe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Trace.WriteLine($"[integration] {ex.Message}");
        }
    }

    /// <summary>Velopack update hook: re-point the registrations at the updated exe.</summary>
    public static void RefreshAfterUpdate()
    {
        var settings = new SettingsStore().Current;
        Apply(settings);
    }

    /// <summary>Velopack uninstall hook: leave nothing behind in the registry.</summary>
    public static void RemoveAll()
    {
        if (!OperatingSystem.IsWindows() || ExePath is not { } exe) return;
        try
        {
            ShellIntegration.Unregister();
            Autostart.Set(false, exe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Trace.WriteLine($"[integration] {ex.Message}");
        }
    }

    /// <summary>Opens a drive or folder in Explorer.</summary>
    public static void Open(string path) => Start("explorer.exe", path);

    /// <summary>Opens Explorer with the file selected.</summary>
    public static void Reveal(string path) => Start("explorer.exe", $"/select,\"{path}\"");

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static void Start(string exe, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"[desktop] {ex.Message}");
        }
    }
}
