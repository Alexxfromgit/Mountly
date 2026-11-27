using System.Security.Cryptography;
using System.Text;

namespace Mountly.Core.Storage;

/// <summary>Where Mountly keeps its files. Tests point <see cref="OverrideRoot"/> at a temporary folder.</summary>
public static class AppPaths
{
    /// <summary>When set, every path below lives under this folder (tests, portable mode).</summary>
    public static string? OverrideRoot { get; set; } = Environment.GetEnvironmentVariable("MOUNTLY_HOME");

    /// <summary>Settings, drive slots and the image catalog (%APPDATA%\Mountly).</summary>
    public static string ConfigDirectory => Ensure(OverrideRoot is { } root
        ? Path.Combine(root, "config")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mountly"));

    /// <summary>Converted images and mount state (%LOCALAPPDATA%\Mountly).</summary>
    public static string LocalDirectory => Ensure(OverrideRoot is { } root
        ? Path.Combine(root, "local")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mountly"));

    /// <summary>ISOs converted from BIN/CUE, NRG, MDS and CloneCD images while they are mounted.</summary>
    public static string CacheDirectory => Ensure(Path.Combine(LocalDirectory, "cache"));

    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");
    public static string SlotsFile => Path.Combine(ConfigDirectory, "drives.json");
    public static string CatalogFile => Path.Combine(ConfigDirectory, "catalog.json");
    public static string MountStateFile => Path.Combine(LocalDirectory, "mounts.json");

    /// <summary>
    /// A stable cache file for a source image: the same source always converts to the same file, so a
    /// remount reuses the ISO while the source is unchanged.
    /// </summary>
    public static string CachedIsoFor(string sourcePath)
    {
        var full = Path.GetFullPath(sourcePath).ToUpperInvariant();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16];
        var name = new string(Path.GetFileNameWithoutExtension(sourcePath)
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').Take(48).ToArray());
        return Path.Combine(CacheDirectory, $"{name}-{hash}.iso");
    }

    private static string Ensure(string directory)
    {
        Directory.CreateDirectory(directory);
        return directory;
    }
}
