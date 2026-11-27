using System.Runtime.Versioning;
using Microsoft.Win32;
using Mountly.Core.Images;

namespace Mountly.Windows;

/// <summary>
/// Per-user Explorer integration (HKCU only, no administrator rights): "Mount with Mountly" and friends on
/// image files, "Create ISO with Mountly" on folders, and Mountly in each image type's "Open with" list.
/// Verbs live under SystemFileAssociations, so they never take over the user's default app for .iso.
/// On Windows 11 they appear under "Show more options".
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShellIntegration
{
    public const string MountArgument = AppArguments.Mount;
    public const string ConvertArgument = AppArguments.Convert;
    public const string VerifyArgument = AppArguments.Verify;
    public const string CreateIsoArgument = AppArguments.CreateIso;

    private const string Classes = @"Software\Classes";
    private const string ProgId = "Mountly.Image";
    private const string VerbPrefix = "Mountly.";

    private const int ShcneAssocChanged = 0x08000000;

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{Classes}\SystemFileAssociations\.iso\shell\{VerbPrefix}Mount");
        return key is not null;
    }

    public static void Register(string exePath)
    {
        var icon = $"\"{exePath}\",0";
        foreach (var ext in ImageFormats.ShellExtensions)
        {
            var shell = $@"{Classes}\SystemFileAssociations\{ext}\shell";
            AddVerb(shell, "Mount", "Mount with Mountly", icon, $"\"{exePath}\" {MountArgument} \"%1\"");
            if (ext != ".iso" && ImageFormats.Detect("x" + ext).IsOptical())
                AddVerb(shell, "Convert", "Convert to ISO with Mountly", icon, $"\"{exePath}\" {ConvertArgument} \"%1\"");
            AddVerb(shell, "Verify", "Verify checksum with Mountly", icon, $"\"{exePath}\" {VerifyArgument} \"%1\"");

            using var openWith = Registry.CurrentUser.CreateSubKey($@"{Classes}\{ext}\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        AddVerb($@"{Classes}\Directory\shell", "CreateIso", "Create ISO with Mountly", icon, $"\"{exePath}\" {CreateIsoArgument} \"%1\"");

        using (var progId = Registry.CurrentUser.CreateSubKey($@"{Classes}\{ProgId}"))
        {
            progId.SetValue("", "Disc image");
            progId.SetValue("FriendlyTypeName", "Disc image");
            using var defaultIcon = progId.CreateSubKey("DefaultIcon");
            defaultIcon.SetValue("", icon);
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{exePath}\" {MountArgument} \"%1\"");
        }

        Notify();
    }

    public static void Unregister()
    {
        foreach (var ext in ImageFormats.ShellExtensions)
        {
            foreach (var verb in new[] { "Mount", "Convert", "Verify" })
                Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\SystemFileAssociations\{ext}\shell\{VerbPrefix}{verb}", throwOnMissingSubKey: false);
            using var openWith = Registry.CurrentUser.OpenSubKey($@"{Classes}\{ext}\OpenWithProgids", writable: true);
            openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\Directory\shell\{VerbPrefix}CreateIso", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{ProgId}", throwOnMissingSubKey: false);
        Notify();
    }

    private static void AddVerb(string shellKey, string verb, string label, string icon, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{shellKey}\{VerbPrefix}{verb}");
        key.SetValue("", label);
        key.SetValue("MUIVerb", label);
        key.SetValue("Icon", icon);
        using var cmd = key.CreateSubKey("command");
        cmd.SetValue("", command);
    }

    private static void Notify() => Native.SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
}

/// <summary>Command-line arguments of the app that the Explorer verbs and the Run key use.</summary>
public static class AppArguments
{
    public const string Mount = "--mount";
    public const string Convert = "--convert";
    public const string Verify = "--verify";
    public const string CreateIso = "--create-iso";
    public const string Tray = "--tray";
}

/// <summary>Starts Mountly in the tray when the user signs in (HKCU Run key).</summary>
[SupportedOSPlatform("windows")]
public static class Autostart
{
    public const string TrayArgument = AppArguments.Tray;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Mountly";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\" {TrayArgument}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
